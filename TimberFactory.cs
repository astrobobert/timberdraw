using System;
using System.Collections.Generic;
using System.Text.Json;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace TimberDraw
{
    // -----------------------------------------------------------------------
    // TimberFactory -- Phase 2 parametric regeneration dispatcher
    //
    // Regenerate() reads the timber's stored DrawContext + xdata, erases the
    // old solid (and its tracked tenons/pegs), redraws with the new dimensions,
    // and marks all connected timbers as stale for the user to resolve.
    //
    // Dispatch uses the "class" key in DrawContext first, then falls back to
    // the Type xdata field for drawings made before the class key was added.
    //
    // Adding Regenerate support for a new member type:
    //   1. At end of the member's Draw(): call Module1.PersistPegHandles() and
    //      Module1.SaveDrawContext(TimberId, BuildContextJson()) where
    //      BuildContextJson captures all Module1 globals + member-specific fields.
    //      Include "class":"MemberClassName" as the first JSON key.
    //   2. Add a private struct XxxContext and ParseXxxContext(string json) below.
    //   3. Add a private RegenerateXxx() method following the BentGirt pattern.
    //   4. Add a case to the switch in Regenerate() below.
    // -----------------------------------------------------------------------
    public static class TimberFactory
    {
        // ObjectId of the solid drawn by the most recent successful Regenerate() call.
        // Set inside ApplyJointTypes() so every RegenerateXxx() method is covered
        // automatically.  Read by Commands.TRegenTimber to write NOM["TRegenResult"]
        // so TimberTag can refresh its display with the new entity's handle.
        internal static ObjectId LastRegeneratedId = ObjectId.Null;

        // True while ApplyCascade is running inner Regenerate() calls.
        // Prevents recursive cascading: inner calls see _isCascading=true,
        // skip capturing connHandles, and skip the ApplyCascade call themselves.
        private static bool _isCascading = false;

        // Bbox mortise records (DrawElement-only after JF migration) saved before erase.
        // Used as fallback re-cut for oblique/legacy mortises with no JF params.
        private static Extents3d[] _pendingIncomingMortises = new Extents3d[0];

        // Exact JF mortise params (keyed by giver handle) saved before erase.
        // Used for exact re-cut when the receiver is directly regenned.
        private static (Handle GiverHandle, JointParams Params)[] _pendingIncomingJoints
            = System.Array.Empty<(Handle, JointParams)>();

        // JointNearParams / JointFarParams saved from the old entity before it is erased.
        // Re-written to the new entity by ApplyJointTypes so user-edited joint params
        // survive a regeneration.
        private static string _pendingNearParams = "";
        private static string _pendingFarParams  = "";

        // Accumulated (oldHandle → newHandle) pairs for the current outermost Regenerate()
        // call, including primary and all cascade-regenerated members.  Written to
        // NOM["TRegenMap"] after cascade so TimberTag can update every stale SS entry.
        // Cleared at the start of each outermost call.
        private static readonly List<(Handle Old, Handle New)> _regenMap
            = new List<(Handle, Handle)>();

        // Session-scoped regen map: persists across all TRegenTimber calls for the lifetime
        // of the drawing session.  Records every oldHandle->newHandle pair so that
        // RebuildKPStrutConns can resolve a stale ConnHandle (from a previous regen cycle)
        // to the current live entity even when UpdateConnectionHandle propagation missed it.
        private static readonly Dictionary<Handle, Handle> _sessionRegenMap
            = new Dictionary<Handle, Handle>();

        // Follows the _sessionRegenMap chain until it reaches a live handle.
        // A->B->C resolves to C even if A and B are erased.
        private static Handle ResolveHandle(Handle h)
        {
            var visited = new System.Collections.Generic.HashSet<Handle>();
            while (_sessionRegenMap.TryGetValue(h, out Handle next) && !visited.Contains(next))
            {
                visited.Add(h);
                h = next;
            }
            return h;
        }

        // Deserializes joint parameter JSON string into a dictionary.
        // Format: {"key":value,...} e.g. {"tenonWidth":2.0,"tenonRelish":1.5}
        // Returns empty dict if json is null/empty/invalid.
        private static Dictionary<string, double> DeserializeJointParams(string json)
        {
            var dict = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(json) || json.Trim() == "{}") return dict;
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(json))
                {
                    foreach (JsonProperty prop in doc.RootElement.EnumerateObject())
                    {
                        if (prop.Value.TryGetDouble(out double val))
                            dict[prop.Name] = val;
                    }
                }
            }
            catch { }  // invalid JSON: return empty dict
            return dict;
        }

        // Entry point. Reads the timber's class/type and dispatches regeneration.
        //
        // newWidth / newDepth: new cross-section. Pass 0 to keep existing value.
        // newJointNear / newJointFar: leave empty to keep existing values.
        public static ObjectId Regenerate(Handle timberHandle,
            double newWidth = 0, double newDepth = 0,
            string newJointNear = "", string newJointFar = "")
        {
            LastRegeneratedId = ObjectId.Null;   // reset before dispatch
            if (!_isCascading) _regenMap.Clear();  // fresh map for this outermost call
            ObjectId timberId = Module1.GetObjectIdFromHandle(timberHandle);
            if (timberId.IsNull || timberId.IsErased)
                throw new ArgumentException("Timber handle not found in drawing: " + timberHandle);

            Module1.DataStructure data = Module1.GetXdata(timberId);
            if (string.IsNullOrEmpty(data.Type))
                throw new InvalidOperationException(
                    "Entity at " + timberHandle + " has no TimberDraw xdata (Type field empty).");


            // Merge caller values with stored values
            double w  = (newWidth  > 0)                     ? newWidth     : data.Width;
            double d  = (newDepth  > 0)                     ? newDepth     : data.Depth;
            string jn = !string.IsNullOrEmpty(newJointNear) ? newJointNear : data.JointNear;
            string jf = !string.IsNullOrEmpty(newJointFar)  ? newJointFar  : data.JointFar;

            // No-change guard (outermost calls only -- cascade calls must always run).
            //
            // When dimensions, joint types, and params all match what was last physically
            // drawn, the entity's geometry is already correct.  Skip the erase-redraw
            // cycle entirely.  This prevents:
            //   (a) DrawElement brace/strut mortises being re-cut as rectangular bbox
            //       approximations when the original oblique shape was already correct.
            //   (b) Cascade side effects (e.g. ApplyCascadeGeometric regenerating the
            //       king post and failing to re-apply shoulder mortises) when the joint
            //       geometry did not actually change.
            //
            // JointNearParamsDrawn / JointFarParamsDrawn are null on old drawings (written
            // only since Phase 4).  ParamsUnchanged returns false for null drawn snapshot
            // so old drawings always fall through to full regen (safe).
            if (!_isCascading &&
                Math.Abs(w - data.Width) < 0.001 &&
                Math.Abs(d - data.Depth) < 0.001 &&
                string.Equals(jn, data.JointNear, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(jf, data.JointFar,  StringComparison.OrdinalIgnoreCase) &&
                ParamsUnchanged(data.JointNearParams, data.JointNearParamsDrawn) &&
                ParamsUnchanged(data.JointFarParams,  data.JointFarParamsDrawn))
            {
                LastRegeneratedId = timberId;
                return timberId;
            }

            string ctx = Module1.LoadDrawContext(timberId);

            // Prefer "class" from DrawContext; fall back to xdata Type for old drawings
            string memberClass = GetMemberClass(ctx);
            string dispatchKey = !string.IsNullOrEmpty(memberClass) ? memberClass : data.Type;

            // Capture connection data before dispatch erases the old entity.
            // Inner calls (during ApplyCascade) skip this -- they don't cascade.
            // savedConns: rich Connection records (new format) used to re-write
            //             Connections xrecord on the regenerated timber so ApplyCascade
            //             can route directly to the correct post via alive tenon solids.
            // connHandles: flat handle list from ConnectedMembers for stale marking +
            //              geometric cascade fallback on old drawings.
            Handle[] connHandles = _isCascading
                ? Array.Empty<Handle>()
                : Module1.GetConnectedHandles(timberId);
            Module1.Connection[] savedConns = _isCascading
                ? Array.Empty<Module1.Connection>()
                : Module1.GetConnections(timberId);

            switch (dispatchKey)
            {
                case "BentGirt":
                    RegenerateBentGirt(timberId, data, ctx, w, d, jn, jf, savedConns);
                    break;
                case "FloorBentGirt":
                    RegenerateFloorBentGirt(timberId, data, ctx, w, d, jn, jf, savedConns);
                    break;
                case "PostLeft":
                    RegeneratePostLeft(timberId, data, ctx, w, d, jn, jf);
                    break;
                case "PostRight":
                    RegeneratePostRight(timberId, data, ctx, w, d, jn, jf);
                    break;
                case "KPost":
                    RegenerateKPost(timberId, data, ctx, w, d, jn, jf, savedConns);
                    break;
                case "BentBrace":
                    RegenerateBentBrace(timberId, data, ctx, w, d, jn, jf, savedConns);
                    break;
                case "BayBrace":
                    RegenerateBayBrace(timberId, data, ctx, w, d, jn, jf);
                    break;
                case "KPRafterLeft":
                    RegenerateKPRafterLeft(timberId, data, ctx, w, d, jn, jf, savedConns);
                    break;
                case "KPRafterRight":
                    RegenerateKPRafterRight(timberId, data, ctx, w, d, jn, jf, savedConns);
                    break;
                case "KPStrutLeft":
                    RegenerateKPStrutLeft(timberId, data, ctx, w, d, jn, jf, savedConns);
                    break;
                case "KPStrutRight":
                    RegenerateKPStrutRight(timberId, data, ctx, w, d, jn, jf, savedConns);
                    break;
                case "KPVertStrutLeft":
                    RegenerateKPVertStrutLeft(timberId, data, ctx, w, d, jn, jf, savedConns);
                    break;
                case "KPVertStrutRight":
                    RegenerateKPVertStrutRight(timberId, data, ctx, w, d, jn, jf, savedConns);
                    break;
                // ---- Qpost ----
                case "QPPostLeft":    RegenerateQPPostLeft   (timberId, data, ctx, w, d, jn, jf, savedConns); break;
                case "QPPostRight":   RegenerateQPPostRight  (timberId, data, ctx, w, d, jn, jf, savedConns); break;
                case "QPRafterLeft":  RegenerateQPRafterLeft (timberId, data, ctx, w, d, jn, jf, savedConns); break;
                case "QPRafterRight": RegenerateQPRafterRight(timberId, data, ctx, w, d, jn, jf, savedConns); break;
                case "QPStrutLeft":   RegenerateQPStrutLeft  (timberId, data, ctx, w, d, jn, jf, savedConns); break;
                case "QPStrutRight":  RegenerateQPStrutRight (timberId, data, ctx, w, d, jn, jf, savedConns); break;
                case "QPUpperGirt":   RegenerateQPUpperGirt  (timberId, data, ctx, w, d, jn, jf, savedConns); break;
                // ---- Hbeam ----
                case "HBeamLeft":     RegenerateHBeamLeft    (timberId, data, ctx, w, d, jn, jf, savedConns); break;
                case "HBeamRight":    RegenerateHBeamRight   (timberId, data, ctx, w, d, jn, jf, savedConns); break;
                case "HBGirt":        RegenerateHBGirt       (timberId, data, ctx, w, d, jn, jf, savedConns); break;
                case "HBKpost":       RegenerateHBKpost      (timberId, data, ctx, w, d, jn, jf, savedConns); break;
                case "HPostLeft":     RegenerateHPostLeft    (timberId, data, ctx, w, d, jn, jf, savedConns); break;
                case "HPostRight":    RegenerateHPostRight   (timberId, data, ctx, w, d, jn, jf, savedConns); break;
                case "HBBayGirt":     RegenerateHBBayGirt    (timberId, data, ctx, w, d, jn, jf); break;
                // ---- KpostTruss ----
                case "KPTPost":         RegenerateKPTPost        (timberId, data, ctx, w, d, jn, jf); break;
                case "KPTRafterLeft":   RegenerateKPTRafterLeft  (timberId, data, ctx, w, d, jn, jf); break;
                case "KPTRafterRight":  RegenerateKPTRafterRight (timberId, data, ctx, w, d, jn, jf); break;
                case "KPTStrutLeft":    RegenerateKPTStrutLeft   (timberId, data, ctx, w, d, jn, jf); break;
                case "KPTStrutRight":   RegenerateKPTStrutRight  (timberId, data, ctx, w, d, jn, jf); break;
                case "KPTVertStrutLeft":  RegenerateKPTVertStrutLeft (timberId, data, ctx, w, d, jn, jf); break;
                case "KPTVertStrutRight": RegenerateKPTVertStrutRight(timberId, data, ctx, w, d, jn, jf); break;
                // ---- QpostTruss ----
                case "QPTPostLeft":     RegenerateQPTPostLeft  (timberId, data, ctx, w, d, jn, jf); break;
                case "QPTPostRight":    RegenerateQPTPostRight (timberId, data, ctx, w, d, jn, jf); break;
                case "QPTRafterLeft":   RegenerateQPTRafterLeft(timberId, data, ctx, w, d, jn, jf); break;
                case "QPTRafterRight":  RegenerateQPTRafterRight(timberId, data, ctx, w, d, jn, jf); break;
                case "QPTStrutLeft":    RegenerateQPTStrutLeft (timberId, data, ctx, w, d, jn, jf); break;
                case "QPTStrutRight":   RegenerateQPTStrutRight(timberId, data, ctx, w, d, jn, jf); break;
                case "QPTUpperGirt":    RegenerateQPTUpperGirt (timberId, data, ctx, w, d, jn, jf); break;
                // ---- Other root members ----
                case "TrussGirt": RegenerateTrussGirt(timberId, data, ctx, w, d, jn, jf); break;
                case "Ridge":     RegenerateRidge    (timberId, data, ctx, w, d, jn, jf); break;
                // Complex multi-entity members: each Draw() produces two timbers.
                // DrawContext is stored (class key prevents fallthrough to BentGirt "Girt" alias).
                // Full regeneration needs separate per-side logic -- Phase 3 TODO.
                case "EaveGirt":
                case "FloorBayGirt":
                    throw new NotImplementedException(
                        $"Regenerate() for '{dispatchKey}' is not yet implemented. " +
                        "It draws two timbers per call (left + right side) and requires " +
                        "side-specific regeneration logic not yet added to TimberFactory.");
                default:
                    throw new NotImplementedException(
                        $"Regenerate() not yet implemented for '{dispatchKey}'. " +
                        "See TimberFactory.cs to add a RegenerateXxx() method.");
            }

            // Cascade: regenerate directly connected timbers and re-cut their mortises.
            // New format: ApplyCascade reads alive tenon solids from Connections xrecord.
            // Old format fallback: ApplyCascadeGeometric uses JointFactory geometric match.
            // Only fires for the outermost Regenerate() call; inner calls skip this.
            //
            // Save/restore pattern:
            //   ApplyJointTypes always writes LastRegeneratedId so inner cascade calls
            //   return the correct new-entity ID to ApplyCascade (needed for BoolSubtract).
            //   We save the primary ID here before cascade overwrites it, then restore
            //   it after so Commands.TRegenTimber receives the primary entity's handle.
            if (!_isCascading && !LastRegeneratedId.IsNull)
            {
                ObjectId primaryId = LastRegeneratedId;   // primary drawn by dispatch above

                // Record primary mapping before cascade (cascade overwrites LastRegeneratedId).
                _regenMap.Add((timberHandle, primaryId.Handle));

                // Determine which ends actually changed so the cascade can skip connections
                // on unchanged ends (prevents KPost shoulder loss when only jf changes).
                // w/d change affects all ends; joint-type or params change is end-specific.
                bool wdChanged   = Math.Abs(w - data.Width) > 0.001 || Math.Abs(d - data.Depth) > 0.001;
                bool nearChanged = wdChanged
                    || !string.Equals(jn, data.JointNear, StringComparison.OrdinalIgnoreCase)
                    || !ParamsUnchanged(data.JointNearParams, data.JointNearParamsDrawn);
                bool farChanged  = wdChanged
                    || !string.Equals(jf, data.JointFar, StringComparison.OrdinalIgnoreCase)
                    || !ParamsUnchanged(data.JointFarParams, data.JointFarParamsDrawn);

                _isCascading = true;
                HashSet<Handle> deltaUpdated = new HashSet<Handle>();
                // JointNearParamsDrawn = params that were physically drawn last time.
                // JointNearParams may already be the user's NEW values (TimberTag writes
                // them before triggering Regen), so we use the Drawn snapshot for old params.
                string oldNearJson = !string.IsNullOrEmpty(data.JointNearParamsDrawn)
                    ? data.JointNearParamsDrawn : data.JointNearParams;
                string oldFarJson  = !string.IsNullOrEmpty(data.JointFarParamsDrawn)
                    ? data.JointFarParamsDrawn  : data.JointFarParams;
                // Also pass the old joint types so ApplyCascade can skip the fill step
                // when old was "Butt" (no void existed -- fill would corrupt the receiver).
                string oldNearType = data.JointNear ?? "";
                string oldFarType  = data.JointFar  ?? "";
                try   { deltaUpdated = ApplyCascade(primaryId, connHandles, timberHandle, oldNearJson, oldFarJson, nearChanged, farChanged, oldNearType, oldFarType); }
                finally { _isCascading = false; }
                LastRegeneratedId = primaryId;            // restore: cascade calls overwrote it

                // Write the full (oldHandle → newHandle) map to NOM["TRegenMap"] so
                // TimberTag can update every stale SS entry -- primary and all cascade results.
                // Delta-updated receivers keep their existing handles (no entry needed).
                Commands.WriteRegenMap(HostApplicationServices.WorkingDatabase, _regenMap);

                // Accumulate into the session-scoped map so RebuildKPStrutConns can resolve
                // stale ConnHandles from previous regen cycles (UpdateConnectionHandle misses).
                foreach (var (old, newH) in _regenMap)
                    _sessionRegenMap[old] = newH;

                // Erase any standalone tenon solids on the primary that the cascade
                // did not consume.  Normally the cascade erases every TenonHandle it
                // processes; orphans only occur when old-drawing Connections xrecords
                // have stale ThisEnd values (e.g. brace pre-AddConnectionFull: both
                // connections stored as Near=0, so the Far tenon was never assigned to
                // a connection and the cascade never saw it to erase it).
                {
                    var ed2 = Autodesk.AutoCAD.ApplicationServices.Application
                        .DocumentManager.MdiActiveDocument?.Editor;
                    Handle[] remaining = Module1.GetHandlesFromField(primaryId, "TenonHandles");
                    foreach (Handle th in remaining)
                    {
                        ObjectId tenonId = Module1.GetObjectIdFromHandle(th);
                        if (!tenonId.IsNull && !tenonId.IsErased)
                        {
                            ed2?.WriteMessage("\nOrphan TenonHandle " + th.ToString()
                                + " on " + primaryId.Handle.ToString() + " -- erasing.");
                            Module1.EraseEntity(tenonId);
                        }
                    }
                }

                // Propagate the primary's new handle into every live neighbor's Connections
                // xrecord so subsequent regens of neighbors find the current entity.
                // Previously done inside ApplyCascadeGeometric after each inner Regenerate();
                // now handled here since the geometric fallback is gone.
                foreach (Handle h in connHandles)
                {
                    ObjectId neighborId = Module1.GetObjectIdFromHandle(h);
                    if (!neighborId.IsNull && !neighborId.IsErased)
                        Module1.UpdateConnectionHandle(neighborId, timberHandle, primaryId.Handle);
                }

                // Mark stale only for connections cascade did NOT handle.
                // Delta-updated receivers: their joint is current -- skip stale mark.
                // Cascade-erased receivers: IsErased == true -- skipped by the null check.
                // Missed receivers (not wired via AddConnectionFull): alive, get marked stale.
                foreach (Handle h in connHandles)
                {
                    if (deltaUpdated.Contains(h)) continue;   // delta-updated: joint is current
                    ObjectId id = Module1.GetObjectIdFromHandle(h);
                    if (!id.IsNull && !id.IsErased) Module1.MarkStale(id);
                }
            }

            return LastRegeneratedId;
        }

        // -----------------------------------------------------------------------
        // Shared erase helper used by all member regen methods.
        // Does NOT mark connected timbers stale -- Regenerate() does that after
        // cascade, so only timbers that cascade missed are flagged.
        //
        // Phase 3: loads IncomingMortises from the old entity BEFORE erasing it
        // (the extension dictionary is destroyed with the entity).  Stored in
        // _pendingIncomingMortises for consumption by ApplyJointTypes.  During
        // cascade (_isCascading=true) the tenon-giver cuts fresh mortises, so
        // we skip the save -- the cascade re-cut path takes over.
        // -----------------------------------------------------------------------
        private static void EraseAndMarkStale(ObjectId timberId)
        {
            _pendingIncomingMortises = _isCascading
                ? new Extents3d[0]
                : Module1.LoadIncomingMortises(timberId);

            _pendingIncomingJoints = _isCascading
                ? System.Array.Empty<(Handle, JointParams)>()
                : Module1.LoadAllIncomingJoints(timberId);

            // Preserve user-edited joint params across the erase/redraw cycle.
            var oldData = Module1.GetXdata(timberId);
            _pendingNearParams = oldData.JointNearParams ?? "";
            _pendingFarParams  = oldData.JointFarParams  ?? "";

            Module1.EraseEntities(Module1.GetHandlesFromField(timberId, "TenonHandles"));
            Module1.EraseEntities(Module1.GetHandlesFromField(timberId, "PegHandles"));
            Module1.EraseEntities(Module1.GetHandlesFromField(timberId, "EndMarkerHandles"));
            Module1.EraseEntity(timberId);
        }

        // Updates JointNear/Far on a freshly-drawn timber if the caller supplied new values.
        // Also records the new entity's ObjectId in LastRegeneratedId so the enclosing
        // Regenerate() call can return it.  Always written -- inner cascade calls also
        // set this so ApplyCascade receives the correct new-entity ObjectId as newConnId.
        // The outer Regenerate() saves LastRegeneratedId before ApplyCascade and restores
        // it after, so Commands.TRegenTimber always receives the primary entity's handle.
        //
        // nearTenonId / farTenonId: optional handles of the standalone tenon solids that
        // Draw() just produced at each end.  Used when the requested joint type differs
        // from the member's default so geometry can be adjusted (e.g. erase a tenon when
        // changing Tenon->Butt).  Pass default(ObjectId) when an end has no tenon.
        //
        // Phase 3: consumes _pendingIncomingMortises set by EraseAndMarkStale.
        // Re-cuts all stored mortises into the freshly drawn entity, then clears
        // the field.  AddJoint(Mortise) inside ApplyIncomingMortises automatically
        // re-writes the IncomingMortises xrecord on the new entity.
        private static void ApplyJointTypes(ObjectId newId, string jn, string jf,
            ObjectId nearTenonId = default, ObjectId farTenonId = default)
        {
            LastRegeneratedId = newId;

            bool changeJn = !string.IsNullOrEmpty(jn);
            bool changeJf = !string.IsNullOrEmpty(jf);

            if (changeJn || changeJf)
            {
                // Read the defaults Draw() stamped via DrawElement/DrawBrace so we can
                // detect an actual type change and adjust geometry accordingly.
                Module1.DataStructure d = Module1.GetXdata(newId);

                if (changeJn && jn != d.JointNear)
                    ApplyJointTypeChange(Module1.End.Near, d.JointNear, jn, nearTenonId);
                if (changeJf && jf != d.JointFar)
                    ApplyJointTypeChange(Module1.End.Far, d.JointFar, jf, farTenonId);

                if (changeJn) d.JointNear = jn;
                if (changeJf) d.JointFar  = jf;
                Module1.SetXdata(newId, d);
            }

            // For outermost regens (!_isCascading) always call ApplyIncomingJointsAndMortises
            // so NetworkManager.ReapplyIncoming fires even when both pending arrays are empty.
            // This is required for members (like rafters) where incoming Polygon joints live
            // only in BentNetwork -- no PrepareIncomingJointRecord, no bbox -- so the arrays
            // are empty but the network still has edges to re-cut.
            // For inner cascade calls (_isCascading=true) EraseAndMarkStale already clears
            // both arrays; the original condition keeps the no-op fast path.
            if (_pendingIncomingJoints.Length > 0 || _pendingIncomingMortises.Length > 0
                || !_isCascading)
            {
                ApplyIncomingJointsAndMortises(newId, _pendingIncomingJoints, _pendingIncomingMortises);
                _pendingIncomingJoints   = System.Array.Empty<(Handle, JointParams)>();
                _pendingIncomingMortises = new Extents3d[0];
            }

            // Re-write joint param strings so user edits survive the regen.
            if (!string.IsNullOrEmpty(_pendingNearParams) || !string.IsNullOrEmpty(_pendingFarParams))
            {
                var d = Module1.GetXdata(newId);
                d.JointNearParams = _pendingNearParams;
                d.JointFarParams  = _pendingFarParams;
                Module1.SetXdata(newId, d);
                _pendingNearParams = "";
                _pendingFarParams  = "";
            }
        }

        // Applies the geometry consequence of changing a joint type at one end.
        //
        // "Tenon -> Butt": erases the standalone tenon solid at that end.
        //   Note: associated peg solids are tracked in PegHandles without per-end
        //   metadata; they are not automatically erased -- the user is notified.
        //
        // All other transitions: reports "not yet implemented" on the AutoCAD
        //   command line and updates xdata only (geometry unchanged).
        private static void ApplyJointTypeChange(short end,
            string fromType, string toType, ObjectId tenonId)
        {
            string endLabel = (end == Module1.End.Near) ? "Near" : "Far";
            var ed = Autodesk.AutoCAD.ApplicationServices.Application
                .DocumentManager.MdiActiveDocument?.Editor;

            if ((fromType == "Tenon" || fromType == "Shoulder") && toType == "Butt")
            {
                if (!tenonId.IsNull && !tenonId.IsErased)
                    Module1.EraseEntity(tenonId);
                ed?.WriteMessage(
                    "\n" + endLabel + " joint erased (" + fromType + " -> Butt). " +
                    "Remove associated peg manually if present.");
                return;
            }

            // All other transitions: xdata updated by caller; geometry not changed.
            ed?.WriteMessage(
                "\nJoint geometry change " + fromType + " -> " + toType +
                " at " + endLabel + " end is not yet implemented. " +
                "XData updated; redraw manually if geometry needs correction.");
        }

        // -----------------------------------------------------------------------
        // Phase 3 helpers: receiver-driven mortise re-cut
        // -----------------------------------------------------------------------

        // Re-cuts each stored mortise into a freshly drawn timber by creating a
        // temporary axis-aligned box from the stored bounding box and BoolSubtracting
        // it into the timber.  AddJoint(Mortise) is the cut path -- it also writes
        // each box back to IncomingMortises on the new entity, so the xrecord
        // stays current after every direct-update regen cycle.
        // Re-cuts all incoming mortises into a freshly drawn timber.
        // JF entries are re-cut exactly using JointFactory.Create.
        // Bbox entries are the DrawElement/legacy fallback (oblique struts, braces,
        // and members not yet covered by PrepareIncomingJointRecord).
        // Bbox writes are suppressed for JF re-cuts so IncomingMortises stays
        // DrawElement-only after this call.
        // Re-cuts all incoming mortises into a freshly drawn receiver.
        //
        // Priority order:
        //   1. BentNetwork (Phase B) -- exact params for ALL registered bents.
        //      Works for any member type once its orchestrator calls BentNetwork.RegisterEdge.
        //   2. _pendingIncomingJoints (per-entity IncomingJoints xrecord) -- covers KP bent
        //      members using PrepareIncomingJointRecord; also serves as fallback for old
        //      drawings that predate the BentNetwork.
        //   3. Bbox fallback -- DrawElement-based mortises (oblique struts, braces) that
        //      have no JF params.
        private static void ApplyIncomingJointsAndMortises(
            ObjectId newTimberId,
            (Handle GiverHandle, JointParams Params)[] jfJoints,
            Extents3d[] bboxes)
        {
            // Phase B: BentNetwork provides exact re-cuts for all network-registered joints.
            // Returns the set of giver handles it handled so duplicates are skipped below.
            System.Collections.Generic.HashSet<Handle> networkHandled
                = NetworkManager.ReapplyIncoming(newTimberId);

            // Existing per-entity JF re-cuts: skip any giver already handled by the network.
            foreach (var (giverHandle, jp) in jfJoints)
            {
                if (networkHandled.Contains(giverHandle)) continue;
                if (jp.JointType == Module1.JointType.None) continue;
                var recut = jp;
                recut.GeneratePegs = false;
                Module1.SuppressNextMortiseBbox();
                ObjectId mortise = JointFactory.Create(recut.JointType, recut);
                if (mortise.IsNull) continue;
                try   { Module1.AddJoint(newTimberId, mortise, Module1.Joint.Mortise); }
                catch { }
                try   { Module1.DeleteJoint(mortise); }
                catch { }
                Module1.SaveIncomingJoint(newTimberId, giverHandle, jp);
            }

            // Bbox fallback for DrawElement mortises (oblique struts, braces, unmigrated).
            ApplyIncomingMortises(newTimberId, bboxes);
        }

        private static void ApplyIncomingMortises(ObjectId newTimberId, Extents3d[] mortises)
        {
            foreach (Extents3d ext in mortises)
            {
                ObjectId boxId = CreateBoxSolid(ext);
                if (boxId.IsNull) continue;
                try   { Module1.AddJoint(newTimberId, boxId, Module1.Joint.Mortise); }
                catch { }
                try   { Module1.DeleteJoint(boxId); }
                catch { }
            }
        }

        // Creates a temporary axis-aligned Solid3d box matching the supplied bounding box.
        // Used to reconstruct mortise-cutting geometry from stored Extents3d records.
        private static ObjectId CreateBoxSolid(Extents3d ext)
        {
            double dx = ext.MaxPoint.X - ext.MinPoint.X;
            double dy = ext.MaxPoint.Y - ext.MinPoint.Y;
            double dz = ext.MaxPoint.Z - ext.MinPoint.Z;
            if (dx <= 0 || dy <= 0 || dz <= 0) return ObjectId.Null;

            Point3d center = new Point3d(
                (ext.MinPoint.X + ext.MaxPoint.X) / 2.0,
                (ext.MinPoint.Y + ext.MaxPoint.Y) / 2.0,
                (ext.MinPoint.Z + ext.MaxPoint.Z) / 2.0);

            Database db = HostApplicationServices.WorkingDatabase;
            Document doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            ObjectId boxId = ObjectId.Null;
            using (doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTableRecord btr = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
                Solid3d box = new Solid3d();
                box.CreateBox(dx, dy, dz);
                box.TransformBy(Matrix3d.Displacement(center.GetAsVector()));
                boxId = btr.AppendEntity(box);
                tr.AddNewlyCreatedDBObject(box, true);
                tr.Commit();
            }
            return boxId;
        }

        // -----------------------------------------------------------------------
        // Cascade helpers
        // -----------------------------------------------------------------------

        // Cascade using alive standalone tenon solids stored in the Connections xrecord.
        // All member Regenerate methods write a fresh Connections xrecord after Draw()
        // so that TenonHandles contains the handles of the new tenon solids.
        // Returns the set of receiver handles that were delta-updated (not erased/redrawn).
        // Callers exclude these from the stale-marking pass since their joints are current.
        // Members without a Connections xrecord (not yet wired via AddConnectionFull)
        // return an empty set -- all receivers are stale-marked and must be manually regenned.
        // oldNearJson/oldFarJson: the giver's JointNearParamsDrawn/JointFarParamsDrawn.
        // Captured in Regenerate() before dispatch so they represent the existing void.
        private static HashSet<Handle> ApplyCascade(ObjectId newPrimaryId,
            Handle[] connHandles, Handle oldPrimaryHandle = default,
            string oldNearJson = "", string oldFarJson = "",
            bool nearChanged = true, bool farChanged = true,
            string oldNearType = "", string oldFarType = "")
        {
            var deltaUpdated = new HashSet<Handle>();
            Module1.Connection[] conns = Module1.GetConnections(newPrimaryId);

            // Include Tenon connections even with empty TenonHandles -- needed so the
            // "became Butt" fill path can run for ends whose new tenon solid is null.
            var tenonConns = Array.FindAll(conns, c =>
                c.ThisJoint != Module1.JointType.None &&
                c.ThisJoint != Module1.JointType.Mortise);

            if (tenonConns.Length == 0)
            {
                // No Connections xrecord: member not yet wired via AddConnectionFull.
                // All receivers are stale-marked by the caller; user regens them manually.
                return deltaUpdated;
            }

            // Reconstruct BEFORE and AFTER tenon params using the same giver geometry but
            // different scalar JSON -- exact JointFactory solids for fill and cut.
            // checkButt:false so old params are always returned (the old void existed even
            // if the new joint type is now Butt and would otherwise return an empty array).
            JointParams[] oldParamsArr = GetTenonParamsGeometric(newPrimaryId, oldNearJson, oldFarJson, checkButt: false);

            // (nearBecameButt/farBecameButt are no longer used; fill now fires for any
            // changed end with no live tenon, not only when the new type is "Butt".
            // E.g. Shoulder->Tenon on a rafter near end produces no solid either.)

            // Delta path: fill old void + cut new void; receiver NOT erased/redrawn.
            foreach (Module1.Connection c in tenonConns)
            {
                // Unchanged ends: erase unused standalone tenon solids (they were drawn
                // by this regen's Draw() but are not needed since the receiver is current),
                // then add to deltaUpdated to prevent spurious stale marking.
                if (c.ThisEnd == Module1.End.Near && !nearChanged)
                {
                    foreach (Handle th in c.TenonHandles) { ObjectId t = Module1.GetObjectIdFromHandle(th); if (!t.IsNull && !t.IsErased) Module1.EraseEntity(t); }
                    deltaUpdated.Add(c.ConnHandle); continue;
                }
                if (c.ThisEnd == Module1.End.Far && !farChanged)
                {
                    foreach (Handle th in c.TenonHandles) { ObjectId t = Module1.GetObjectIdFromHandle(th); if (!t.IsNull && !t.IsErased) Module1.EraseEntity(t); }
                    deltaUpdated.Add(c.ConnHandle); continue;
                }

                ObjectId cid = Module1.GetObjectIdFromHandle(c.ConnHandle);
                if (cid.IsNull || cid.IsErased) continue;

                // Slot selection: near=0, far=1.
                // If GetTenonParamsGeometric only returned near params (length==1) and this is
                // a far-end connection, there are no old far params -- use default so the fill
                // guard (oldP.Origin == default) skips the fill instead of misapplying near
                // params to the wrong receiver (which corrupts geometry and leaves orphan solids).
                int idx = (c.ThisEnd == Module1.End.Near) ? 0
                         : (oldParamsArr.Length > 1) ? 1 : -1;
                JointParams oldP = (idx >= 0 && idx < oldParamsArr.Length)
                    ? oldParamsArr[idx] : default;

                // Skip the fill step when the old joint was "Butt" -- there was no void in
                // the receiver to fill.  Trying to BoolUnite a fill solid into an already-
                // solid region of a pentagonal KPost can corrupt its topology and cause the
                // subsequent BoolSubtract (cut) to fail silently.
                bool oldEndWasButt = (c.ThisEnd == Module1.End.Near)
                    ? string.Equals(oldNearType, "Butt", StringComparison.OrdinalIgnoreCase)
                    : string.Equals(oldFarType,  "Butt", StringComparison.OrdinalIgnoreCase);

                bool liveTenonFound = false;
                foreach (Handle th in c.TenonHandles)
                {
                    ObjectId tenonId = Module1.GetObjectIdFromHandle(th);
                    if (tenonId.IsNull || tenonId.IsErased) continue;
                    DeltaSwapJoint(cid, oldP, tenonId, skipFill: oldEndWasButt);
                    Module1.EraseEntity(tenonId);
                    liveTenonFound = true;
                }

                // No live tenon for this changed end: fill the old void.
                // Fires for any transition that produces no solid at this end --
                // Shoulder->Butt, Shoulder->Tenon (no near-tenon impl on rafter), etc.
                // CustomPts check covers Polygon joints whose Origin is always (0,0,0).
                bool hasOldParams = oldP.Origin != default(Point3d) || oldP.CustomPts != null;
                if (!liveTenonFound && hasOldParams && Module1.Make3D)
                {
                    ObjectId fillId = JointFactory.Create(oldP.JointType, oldP);
                    if (!fillId.IsNull)
                    {
                        try   { Module1.AddJoint(cid, fillId, Module1.Joint.Fill); }
                        catch { }
                        Module1.EraseEntity(fillId);
                    }
                }

                deltaUpdated.Add(c.ConnHandle);
            }

            return deltaUpdated;
        }

        // Rebuild the Connections xrecord on a freshly regenerated entity using the
        // connection records saved from the old (now-erased) entity before erase.
        // Uses empty TenonHandles for all connections -- the cascade fill path then
        // falls to GetTenonParamsGeometric to reconstruct old joint geometry.
        // Used by QP regen methods whose tenon solids are deleted or handled via BentNetwork.
        private static void RebuildConnectionsEmpty(ObjectId newId, Module1.Connection[] savedConns)
        {
            foreach (Module1.Connection sc in savedConns)
            {
                if (sc.ThisJoint == Module1.JointType.Mortise ||
                    sc.ThisJoint == Module1.JointType.None) continue;
                Handle connHandle = sc.ConnHandle;
                ObjectId connId   = Module1.GetObjectIdFromHandle(connHandle);
                if (connId.IsNull || connId.IsErased)
                {
                    connHandle = ResolveHandle(connHandle);
                    connId     = Module1.GetObjectIdFromHandle(connHandle);
                    if (connId.IsNull || connId.IsErased) continue;
                }
                Module1.AddConnectionFull(newId, new Module1.Connection {
                    ConnHandle   = connHandle,
                    ThisEnd      = sc.ThisEnd,
                    OtherEnd     = sc.OtherEnd,
                    ThisJoint    = sc.ThisJoint,
                    TenonHandles = System.Array.Empty<Handle>()
                });
            }
        }

        // Reconstructs JointParams from DrawContext for fill and cut solids used by
        // ApplyCascade (DeltaSwapJoint / no-live-tenon fill).
        // nearOverride/farOverride: when non-null, replace the stored JointNearParams/
        // JointFarParams so the caller can reconstruct OLD tenon geometry (same position,
        // old scalar params) alongside NEW geometry (same position, current stored params).
        // Returns empty array for member types with no outgoing tenon connections.
        private static JointParams[] GetTenonParamsGeometric(ObjectId timberId,
            string nearOverride = null, string farOverride = null, bool checkButt = true)
        {
            string ctxJson = Module1.LoadDrawContext(timberId);
            string cls     = GetMemberClass(ctxJson);
            Module1.DataStructure xd = Module1.GetXdata(timberId);

            switch (cls)
            {
                case "BentGirt":
                {
                    BentGirtContext c = ParseBentGirtContext(ctxJson);
                    double tenonZ = c.Make3D ? c.StartZ + (xd.Width - 2.0) / 2.0 : c.StartZ;
                    double lx = c.PostDepth;
                    double rx = c.Span - c.PostDepth;
                    ReadScalars(xd, out double nTw, out double nRel, out double nHd,
                                    out double fTw, out double fRel, out double fHd,
                                    nearOverride, farOverride);
                    return new[]
                    {
                        new JointParams(Module1.JointType.Tenon,
                            new Point3d(lx, c.BOG, tenonZ),
                            new Vector3d(-1, 0, 0), new Vector3d(0, 1, 0),
                            xd.Width, xd.Depth, nTw, xd.BentNumber, xd.Designation,
                            nRel, 0.0, false, nHd),
                        new JointParams(Module1.JointType.Tenon,
                            new Point3d(rx, c.BOG, tenonZ),
                            new Vector3d( 1, 0, 0), new Vector3d(0, 1, 0),
                            xd.Width, xd.Depth, fTw, xd.BentNumber, xd.Designation,
                            fRel, 0.0, false, fHd),
                    };
                }
                case "FloorBentGirt":
                {
                    FloorBentGirtContext c = ParseFloorBentGirtContext(ctxJson);
                    double tenonZ = c.Make3D ? c.StartZ + (xd.Width - 2.0) / 2.0 : c.StartZ;
                    ReadScalars(xd, out double nTw, out double nRel, out double nHd,
                                    out double fTw, out double fRel, out double fHd,
                                    nearOverride, farOverride);
                    return new[]
                    {
                        new JointParams(Module1.JointType.Tenon,
                            new Point3d(c.PostDepth, c.LocalStartY, tenonZ),
                            new Vector3d(-1, 0, 0), new Vector3d(0, 1, 0),
                            xd.Width, xd.Depth, nTw, xd.BentNumber, xd.Designation,
                            nRel, 0.0, false, nHd),
                        new JointParams(Module1.JointType.Tenon,
                            new Point3d(c.Span - c.PostDepth, c.LocalStartY, tenonZ),
                            new Vector3d( 1, 0, 0), new Vector3d(0, 1, 0),
                            xd.Width, xd.Depth, fTw, xd.BentNumber, xd.Designation,
                            fRel, 0.0, false, fHd),
                    };
                }
                case "KPost":
                {
                    KPostContext c = ParseKPostContext(ctxJson);
                    double tenonZ = c.Make3D ? c.StartZ + (xd.Width - 2.0) / 2.0 : 0.0;
                    double originX = c.Span / 2.0 - xd.Depth / 2.0;
                    ReadScalars(xd, out double nTw, out _, out double nHd, out _, out _, out _,
                        nearOverride, farOverride);
                    double nRel2 = ReadNearScalar(xd, "tenonRelish", 0.0, nearOverride);
                    return new[]
                    {
                        new JointParams(Module1.JointType.Tenon,
                            new Point3d(originX, c.TOG, tenonZ),
                            new Vector3d(0, -1, 0), new Vector3d(1, 0, 0),
                            xd.Width, xd.Depth, nTw, xd.BentNumber, xd.Designation,
                            nRel2, 0.0, false, nHd),
                    };
                }
                case "HBKpost":
                {
                    HBKpostContext c = ParseHBKpostContext(ctxJson);
                    double tenonZ = c.Make3D ? c.StartZ + (xd.Width - 2.0) / 2.0 : 0.0;
                    ReadScalars(xd, out double nTw, out double nRel, out double nHd, out _, out _, out _,
                        nearOverride, farOverride);
                    return new[]
                    {
                        new JointParams(Module1.JointType.Tenon,
                            new Point3d(c.StartX, c.StartY, tenonZ),
                            new Vector3d(0, -1, 0), new Vector3d(1, 0, 0),
                            xd.Width, xd.Depth, nTw, xd.BentNumber, xd.Designation,
                            nRel, 0.0, false, nHd),
                    };
                }
                case "KPRafterLeft":
                {
                    KPRafterContext c = ParseKPRafterContext(ctxJson);
                    double tenonZ = c.Make3D ? c.StartZ + (xd.Width - 2.0) / 2.0 : c.StartZ;
                    double plumb  = xd.Depth / Math.Cos(c.Beta);
                    double xFoot  = c.PostDepth;
                    double xPeak  = c.Span / 2.0 - c.KPostDepth / 2.0;
                    double yBotFoot = c.EaveHt + xFoot * c.Pitch - plumb;
                    double yBotPeak = c.EaveHt + xPeak * c.Pitch - plumb;
                    ReadScalars(xd, out _, out double nRel, out _, out double fTw, out double fRel, out double fHd,
                        nearOverride, farOverride);
                    bool nearButt = checkButt && string.Equals(xd.JointNear, "Butt", StringComparison.OrdinalIgnoreCase);
                    bool farButt  = checkButt && string.Equals(xd.JointFar,  "Butt", StringComparison.OrdinalIgnoreCase);
                    var result = new List<JointParams>();
                    // near = peak/kingpost: Shoulder (TopRelish = stored relish, default 2")
                    if (!nearButt)
                        result.Add(new JointParams(Module1.JointType.Shoulder,
                            new Point3d(xPeak, yBotPeak, tenonZ),
                            new Vector3d(Math.Cos(c.Beta), Math.Sin(c.Beta), 0),
                            new Vector3d(0, 1, 0),
                            xd.Width, plumb, 2.0, xd.BentNumber, xd.Designation,
                            nRel, c.KpostRafterSitDepth));
                    // far = foot/eave: Tenon with housing into left post
                    if (!farButt)
                        result.Add(new JointParams(Module1.JointType.Tenon,
                            new Point3d(xFoot, yBotFoot, tenonZ),
                            new Vector3d(-1, 0, 0), new Vector3d(0, 1, 0),
                            xd.Width, plumb, fTw, xd.BentNumber, xd.Designation,
                            fRel, 0.0, false, fHd, c.Pitch));
                    return result.ToArray();
                }
                case "KPRafterRight":
                {
                    KPRafterContext c = ParseKPRafterContext(ctxJson);
                    double tenonZ = c.Make3D ? c.StartZ + (xd.Width - 2.0) / 2.0 : c.StartZ;
                    double plumb  = xd.Depth / Math.Cos(c.Beta);
                    double xFoot  = c.Span - c.PostDepth;
                    double xPeak  = c.Span / 2.0 + c.KPostDepth / 2.0;
                    double yBotFoot = c.EaveHt + c.PostDepth * c.Pitch - plumb;
                    double yBotPeak = c.EaveHt + (c.Span / 2.0 - c.KPostDepth / 2.0) * c.Pitch - plumb;
                    ReadScalars(xd, out _, out double nRel, out _, out double fTw, out double fRel, out double fHd,
                        nearOverride, farOverride);
                    bool nearButt = checkButt && string.Equals(xd.JointNear, "Butt", StringComparison.OrdinalIgnoreCase);
                    bool farButt  = checkButt && string.Equals(xd.JointFar,  "Butt", StringComparison.OrdinalIgnoreCase);
                    var result = new List<JointParams>();
                    // near = peak/kingpost: Shoulder (right side)
                    if (!nearButt)
                        result.Add(new JointParams(Module1.JointType.Shoulder,
                            new Point3d(xPeak, yBotPeak, tenonZ),
                            new Vector3d(-Math.Cos(c.Beta), Math.Sin(c.Beta), 0),
                            new Vector3d(0, 1, 0),
                            xd.Width, plumb, 2.0, xd.BentNumber, xd.Designation,
                            nRel, c.KpostRafterSitDepth));
                    // far = foot/eave: Tenon with housing into right post
                    if (!farButt)
                        result.Add(new JointParams(Module1.JointType.Tenon,
                            new Point3d(xFoot, yBotFoot, tenonZ),
                            new Vector3d( 1, 0, 0), new Vector3d(0, 1, 0),
                            xd.Width, plumb, fTw, xd.BentNumber, xd.Designation,
                            fRel, 0.0, false, fHd, c.Pitch));
                    return result.ToArray();
                }
                // ---- KPVertStrut: near = foot tenon (JF), far = rafter contact (Polygon) ----
                case "KPVertStrutLeft":
                {
                    KPStrutContext c = ParseKPStrutContext(ctxJson);
                    bool nearButt = checkButt
                        && string.Equals(xd.JointNear, "Butt", StringComparison.OrdinalIgnoreCase);
                    bool farButt  = checkButt
                        && string.Equals(xd.JointFar,  "Butt", StringComparison.OrdinalIgnoreCase);
                    double z = 0;
                    if (c.Make3D) {
                        if      (c.OffsetType == Module1.Centered) z = (c.KPostWidth - xd.Width) / 2;
                        else if (c.OffsetType == Module1.Front)    z =  c.KPostWidth - xd.Width;
                    }
                    double tenonZ = c.Make3D ? z + (xd.Width - 2.0) / 2.0 : 0.0;
                    ReadScalars(xd, out double nTw, out double nRel, out double nHd,
                                    out double fTwVL, out _, out _, nearOverride, farOverride);
                    JointParams nearVL = nearButt ? default : new JointParams(Module1.JointType.Tenon,
                        new Point3d(c.StartX, c.StartY, tenonZ),
                        new Vector3d(0, -1, 0), new Vector3d(1, 0, 0),
                        xd.Width, xd.Depth, nTw, xd.BentNumber, xd.Designation,
                        nRel, 0.0, false, nHd);
                    JointParams farVL = default;
                    if (!farButt) {
                        double hs = c.Span / 2.0;
                        var fp0 = Module1.AtPoint(new Point3d(c.StartX, c.StartY, tenonZ), 0,
                            ((((hs - (c.PostDepth + 5)) / 2) - xd.Depth) * c.Pitch) + 6, 0);
                        var fp1 = Module1.AtPoint(fp0, xd.Depth, xd.Depth * c.Pitch, 0);
                        var fp2 = Module1.PolarPoint(fp1, c.Beta + Math.PI / 2, 4);
                        var fp3 = Module1.PolarPoint(fp0, Math.PI / 2, 4 / Math.Cos(c.Beta));
                        farVL = JointParams.ForPolygon(new[] { fp0, fp1, fp2, fp3 },
                            fTwVL, xd.BentNumber, xd.Designation);
                    }
                    return new[] { nearVL, farVL };
                }
                case "KPVertStrutRight":
                {
                    KPStrutContext c = ParseKPStrutContext(ctxJson);
                    bool nearButt = checkButt
                        && string.Equals(xd.JointNear, "Butt", StringComparison.OrdinalIgnoreCase);
                    bool farButt  = checkButt
                        && string.Equals(xd.JointFar,  "Butt", StringComparison.OrdinalIgnoreCase);
                    double z = 0;
                    if (c.Make3D) {
                        if      (c.OffsetType == Module1.Centered) z = (c.KPostWidth - xd.Width) / 2;
                        else if (c.OffsetType == Module1.Front)    z =  c.KPostWidth - xd.Width;
                    }
                    double tenonZ = c.Make3D ? z + (xd.Width - 2.0) / 2.0 : 0.0;
                    ReadScalars(xd, out double nTwVR, out double nRelVR, out double nHdVR,
                                    out double fTwVR, out _, out _, nearOverride, farOverride);
                    JointParams nearVR = nearButt ? default : new JointParams(Module1.JointType.Tenon,
                        new Point3d(c.StartX, c.StartY, tenonZ),
                        new Vector3d(0, -1, 0), new Vector3d(1, 0, 0),
                        xd.Width, xd.Depth, nTwVR, xd.BentNumber, xd.Designation,
                        nRelVR, 0.0, false, nHdVR);
                    JointParams farVR = default;
                    if (!farButt) {
                        double hs = c.Span / 2.0;
                        var fp0 = Module1.AtPoint(new Point3d(c.StartX, c.StartY, tenonZ), 0,
                            (((hs - (c.PostDepth + 5)) / 2) * c.Pitch) + 6, 0);
                        var fp1 = Module1.AtPoint(fp0, xd.Depth, -(xd.Depth * c.Pitch), 0);
                        var fp2 = Module1.PolarPoint(fp1, Math.PI / 2, 4 / Math.Cos(c.Beta));
                        var fp3 = Module1.PolarPoint(fp0, Math.Atan(c.Prun / c.Prise), 4);
                        farVR = JointParams.ForPolygon(new[] { fp0, fp1, fp2, fp3 },
                            fTwVR, xd.BentNumber, xd.Designation);
                    }
                    return new[] { nearVR, farVR };
                }
                // ---- KPStrut: near = rafter contact (Polygon), far = KPost face tenon (JF) ----
                case "KPStrutLeft":
                {
                    KPStrutContext c = ParseKPStrutContext(ctxJson);
                    double halfSpanL = c.Span / 2.0;
                    bool nearButt = checkButt
                        && string.Equals(xd.JointNear, "Butt", StringComparison.OrdinalIgnoreCase);
                    bool farButt  = checkButt
                        && string.Equals(xd.JointFar,  "Butt", StringComparison.OrdinalIgnoreCase);
                    double zL = 0;
                    if (c.Make3D) {
                        if      (c.OffsetType == Module1.Centered) zL = (c.KPostWidth - xd.Width) / 2;
                        else if (c.OffsetType == Module1.Front)    zL =  c.KPostWidth - xd.Width;
                    }
                    double tenonZL = c.Make3D ? zL + (xd.Width - 2.0) / 2.0 : 0.0;
                    ReadScalars(xd, out double nTwL, out _, out _, out double fTwL, out double fRelL, out _,
                        nearOverride, farOverride);
                    // [0] near = rafter contact: Polygon joint (oblique, reconstructed from context)
                    JointParams nearPL = default;
                    if (!nearButt) {
                        double hPi = Math.PI / 2;
                        var np0 = new Point3d(c.StartX, c.StartY, tenonZL);
                        var np1 = Module1.PolarPoint(np0, c.Beta, xd.Depth / Math.Cos(hPi - c.Beta * 2));
                        var np2 = Module1.PolarPoint(np1, c.Beta + hPi + (hPi - c.Beta * 2),
                                      4 / Math.Cos(hPi - c.Beta * 2));
                        var np3 = Module1.PolarPoint(np0, c.Beta + hPi, 4);
                        nearPL = JointParams.ForPolygon(new[] { np0, np1, np2, np3 },
                            nTwL, xd.BentNumber, xd.Designation);
                    }
                    // [1] far  = KPost left face: tenon extends rightward (+X) with roof-pitch top
                    JointParams farPL = farButt ? default : new JointParams(Module1.JointType.Tenon,
                        new Point3d(halfSpanL - c.KPostDepth / 2.0, c.TOH, tenonZL),
                        new Vector3d(1, 0, 0), new Vector3d(0, 1, 0),
                        xd.Width, xd.Depth / Math.Cos(c.Beta),
                        fTwL, xd.BentNumber, xd.Designation,
                        fRelL, 0.0, false, 0.0, c.Pitch);
                    return new[] { nearPL, farPL };
                }
                case "KPStrutRight":
                {
                    KPStrutContext c = ParseKPStrutContext(ctxJson);
                    double halfSpanR = c.Span / 2.0;
                    bool nearButt = checkButt
                        && string.Equals(xd.JointNear, "Butt", StringComparison.OrdinalIgnoreCase);
                    bool farButt  = checkButt
                        && string.Equals(xd.JointFar,  "Butt", StringComparison.OrdinalIgnoreCase);
                    double zR = 0;
                    if (c.Make3D) {
                        if      (c.OffsetType == Module1.Centered) zR = (c.KPostWidth - xd.Width) / 2;
                        else if (c.OffsetType == Module1.Front)    zR =  c.KPostWidth - xd.Width;
                    }
                    double tenonZR = c.Make3D ? zR + (xd.Width - 2.0) / 2.0 : 0.0;
                    ReadScalars(xd, out double nTwR, out _, out _, out double fTwR, out double fRelR, out _,
                        nearOverride, farOverride);
                    // [0] near = rafter contact: Polygon joint (oblique, reconstructed from context)
                    JointParams nearPR = default;
                    if (!nearButt) {
                        var np0 = new Point3d(c.StartX, c.StartY, tenonZR);
                        var np1 = Module1.PolarPoint(np0, Math.Atan(c.Prun / c.Prise), 4);
                        var np2 = Module1.PolarPoint(np1, Math.Atan(c.Prun / c.Prise) + Math.PI / 2,
                                      xd.Depth / Math.Cos(Math.PI / 2 - c.Beta * 2)
                                      - 4 * Math.Tan(Math.PI / 2 - c.Beta * 2));
                        var np3 = Module1.PolarPoint(np0, Math.PI - c.Beta,
                                      xd.Depth / Math.Cos(Math.PI / 2 - c.Beta * 2));
                        nearPR = JointParams.ForPolygon(new[] { np0, np1, np2, np3 },
                            nTwR, xd.BentNumber, xd.Designation);
                    }
                    // [1] far  = KPost right face: tenon extends leftward (-X) with roof-pitch top
                    JointParams farPR = farButt ? default : new JointParams(Module1.JointType.Tenon,
                        new Point3d(halfSpanR + c.KPostDepth / 2.0, c.TOH, tenonZR),
                        new Vector3d(-1, 0, 0), new Vector3d(0, 1, 0),
                        xd.Width, xd.Depth / Math.Cos(c.Beta),
                        fTwR, xd.BentNumber, xd.Designation,
                        fRelR, 0.0, false, 0.0, c.Pitch);
                    return new[] { nearPR, farPR };
                }
                case "QPRafterLeft":
                {
                    // Near (peak/center) is always Butt -- placeholder at index 0.
                    // Far (foot/eave): Tenon with housing into left post -- at index 1.
                    // Two-element array so slot selection (Far=index 1) works correctly.
                    QPRafterContext c = ParseQPRafterContext(ctxJson);
                    bool farButt = checkButt && string.Equals(xd.JointFar, "Butt", StringComparison.OrdinalIgnoreCase);
                    if (farButt) return Array.Empty<JointParams>();
                    double tenonZ   = c.Make3D ? c.StartZ + (xd.Width - 2.0) / 2.0 : c.StartZ;
                    double plumb    = xd.Depth / Math.Cos(c.Beta);
                    double xFoot    = c.PostDepth;
                    double yBotFoot = c.EaveHt + xFoot * c.Pitch - plumb;
                    ReadScalars(xd, out _, out _, out _, out double fTw, out double fRel, out double fHd,
                        nearOverride, farOverride);
                    return new JointParams[]
                    {
                        default,   // [0] near placeholder (near is always Butt, no void to fill)
                        new JointParams(Module1.JointType.Tenon,
                            new Point3d(xFoot, yBotFoot, tenonZ),
                            new Vector3d(-1, 0, 0), new Vector3d(0, 1, 0),
                            xd.Width, plumb, fTw, xd.BentNumber, xd.Designation,
                            fRel, 0.0, false, fHd, c.Pitch),
                    };
                }
                case "QPRafterRight":
                {
                    // Near (foot/eave): Tenon with housing into right post.
                    // Far (peak/ridge): Shoulder into QPRafterLeft (ridge bearing seat).
                    QPRafterContext c = ParseQPRafterContext(ctxJson);
                    bool nearButt = checkButt && string.Equals(xd.JointNear, "Butt", StringComparison.OrdinalIgnoreCase);
                    bool farButt  = checkButt && string.Equals(xd.JointFar,  "Butt", StringComparison.OrdinalIgnoreCase);
                    double tenonZ   = c.Make3D ? c.StartZ + (xd.Width - 2.0) / 2.0 : c.StartZ;
                    double plumb    = xd.Depth / Math.Cos(c.Beta);
                    double xFoot    = c.Span - c.PostDepth;
                    double xPeak    = c.Span / 2.0;
                    double yBotFoot = c.EaveHt + c.PostDepth * c.Pitch - plumb;
                    double yBotPeak = c.EaveHt + xPeak * c.Pitch - plumb;
                    ReadScalars(xd, out double nTw, out double nRel, out double nHd, out _, out _, out _,
                        nearOverride, farOverride);
                    var result = new List<JointParams>();
                    if (!nearButt)
                        result.Add(new JointParams(Module1.JointType.Tenon,
                            new Point3d(xFoot, yBotFoot, tenonZ),
                            new Vector3d(1, 0, 0), new Vector3d(0, 1, 0),
                            xd.Width, plumb, nTw, xd.BentNumber, xd.Designation,
                            nRel, 0.0, false, nHd, c.Pitch));
                    if (!farButt)
                    {
                        // Housing polygon: same 4-vertex computation as QPRafterRight.Draw().
                        double hd    = c.SitDepth;   // housing perpendicular depth (default 1.0")
                        double lapX  = plumb / (2.0 * c.Pitch);
                        double sinB  = Math.Sin(c.Beta), cosB = Math.Cos(c.Beta);
                        // Full-width housing: use timber base Z, not tenon center Z.
                        double baseZ = c.Make3D ? c.StartZ : 0.0;
                        // yBot = right rafter peak-bottom (plumb face, same formula as left bottom at xPeak)
                        double yBot  = c.EaveHt + xPeak * c.Pitch - plumb;
                        Point3d pv0  = new Point3d(xPeak,           yBot,                         baseZ);
                        Point3d pv1  = new Point3d(xPeak - sinB*hd, yBot + cosB*hd,               baseZ);
                        double  xv3  = xPeak + lapX, yv3 = yBot + lapX * c.Pitch;
                        Point3d pv3  = new Point3d(xv3,             yv3,                          baseZ);
                        double  xv2  = (yv3 + c.Pitch*xv3 - pv1.Y + c.Pitch*pv1.X) / (2.0*c.Pitch);
                        Point3d pv2  = new Point3d(xv2,             pv1.Y + c.Pitch*(xv2-pv1.X), baseZ);
                        // Reverse order: pv0..pv3 is CW; DrawElement requires CCW for +Z extrusion.
                        result.Add(JointParams.ForPolygon(
                            new[] { pv3, pv2, pv1, pv0 }, xd.Width, xd.BentNumber, xd.Designation));
                    }
                    return result.ToArray();
                }
                // ---- QPStrut ----
                case "QPStrutLeft":
                {
                    QPStrutContext c = ParseQPStrutContext(ctxJson);
                    bool nearButt = checkButt && string.Equals(xd.JointNear, "Butt", StringComparison.OrdinalIgnoreCase);
                    bool farButt  = checkButt && string.Equals(xd.JointFar,  "Butt", StringComparison.OrdinalIgnoreCase);
                    double z = 0;
                    if (c.Make3D) { switch (c.OffsetType) { case Module1.Back: z=0; break; case Module1.Centered: z=(c.QPRafterWidth-xd.Width)/2; break; default: z=(c.QPRafterWidth-xd.Width); break; } }
                    double tenonZ  = c.Make3D ? z + (xd.Width - 2.0) / 2.0 : 0;
                    double thirdSpan = c.Span / 3.0;
                    var result = new List<JointParams>();
                    // Near (index 0) = TenonDown, QPPost end
                    if (!nearButt)
                    {
                        Point3d n0 = new Point3d(thirdSpan, c.TOH, tenonZ);
                        Point3d n1 = new Point3d(n0.X + 4, n0.Y, n0.Z);
                        Point3d n2 = new Point3d(thirdSpan + 4, c.TOH + 8.4853 - 4, tenonZ);
                        Point3d n3 = new Point3d(n2.X - 4, n2.Y + 4, n2.Z);
                        result.Add(JointParams.ForPolygon(new[] { n0, n1, n2, n3 }, 2.0, xd.BentNumber, xd.Designation));
                    }
                    // Far (index 1) = TenonUp, rafter end
                    if (!farButt)
                    {
                        Point3d f0 = new Point3d(c.StartX, c.StartY, tenonZ);
                        Point3d f1 = Module1.PolarPoint(f0, c.Beta, xd.Depth / Math.Cos((Math.PI/4) - c.Beta));
                        Point3d f2 = Module1.PolarPoint(f1, Math.PI * 0.75, 4 / Math.Cos((Math.PI/2) - (c.Beta * 2)));
                        Point3d f3 = Module1.PolarPoint(f0, c.Beta + (Math.PI/2), 4);
                        result.Add(JointParams.ForPolygon(new[] { f0, f1, f2, f3 }, 2.0, xd.BentNumber, xd.Designation));
                    }
                    return result.ToArray();
                }
                case "QPStrutRight":
                {
                    QPStrutContext c = ParseQPStrutContext(ctxJson);
                    bool nearButt = checkButt && string.Equals(xd.JointNear, "Butt", StringComparison.OrdinalIgnoreCase);
                    bool farButt  = checkButt && string.Equals(xd.JointFar,  "Butt", StringComparison.OrdinalIgnoreCase);
                    double z = 0;
                    if (c.Make3D) { switch (c.OffsetType) { case Module1.Back: z=0; break; case Module1.Centered: z=(c.QPRafterWidth-xd.Width)/2; break; default: z=(c.QPRafterWidth-xd.Width); break; } }
                    double tenonZ  = c.Make3D ? z + (xd.Width - 2.0) / 2.0 : 0;
                    // Parser now reads Span correctly (backward-compat fallback to PostDepth for old entities).
                    double thirdSpan = c.Span / 3.0;
                    var result = new List<JointParams>();
                    // Near (index 0) = TenonDown, QPPost end (StartPoint area)
                    if (!nearButt)
                    {
                        Point3d n0 = new Point3d(c.StartX, c.StartY, tenonZ);
                        Point3d n1 = Module1.PolarPoint(n0, Math.PI / 2, xd.Depth / Math.Cos(Math.PI / 4));
                        Point3d n2 = Module1.PolarPoint(n1, Math.PI * 1.25, 4 / Math.Cos(Math.PI / 4));
                        Point3d n3 = Module1.PolarPoint(n0, Math.PI, 4);
                        result.Add(JointParams.ForPolygon(new[] { n0, n1, n2, n3 }, 2.0, xd.BentNumber, xd.Designation));
                    }
                    // Far (index 1) = TenonUp, rafter end
                    if (!farButt)
                    {
                        double sinPitch = Math.Sin(Math.Atan(c.Pitch));
                        double sin45    = Math.Sin(Math.Atan(1));
                        double bodyY    = c.TOH + c.B + sinPitch * (2*(8.4853/(2*Math.Sin(Math.PI-(Math.Atan(c.Pitch)+Math.Atan(1)))))) * sin45;
                        Point3d f0 = new Point3d(((2*thirdSpan)+c.B) - (8.4853 - ((c.B*8.4853)/(thirdSpan-10))), bodyY, tenonZ);
                        Point3d f1 = new Point3d((2*thirdSpan)+c.B, c.TOH+c.B, tenonZ);
                        Point3d f2 = Module1.PolarPoint(f1, (Math.PI/2) - c.Beta, 4);
                        Point3d f3 = Module1.PolarPoint(f0, Math.PI/4, 4/Math.Cos(((Math.PI/2)-(c.Beta*2))/2));
                        result.Add(JointParams.ForPolygon(new[] { f0, f1, f2, f3 }, 2.0, xd.BentNumber, xd.Designation));
                    }
                    return result.ToArray();
                }
                // ---- QPPost ----
                case "QPPostLeft":
                {
                    QPPostContext c = ParseQPPostContext(ctxJson);
                    bool nearButt = checkButt && string.Equals(xd.JointNear, "Butt", StringComparison.OrdinalIgnoreCase);
                    bool farButt  = checkButt && string.Equals(xd.JointFar,  "Butt", StringComparison.OrdinalIgnoreCase);
                    double z = 0;
                    if (c.Make3D) { switch (c.OffsetType) { case Module1.Back: z=0; break; case Module1.Centered: z=(c.GirtWidth-xd.Width)/2; break; default: z=(c.GirtWidth-xd.Width); break; } }
                    double tenonZ  = c.Make3D ? z + (xd.Width - 2.0) / 2.0 : 0;
                    double thirdSpan = c.Span / 3.0;
                    ReadScalars(xd, out double nTw, out double nRel, out double nHd, out double fTw, out _, out _,
                        nearOverride, farOverride);
                    var result = new List<JointParams>();
                    // Near (index 0) = TenonDownId: standard foot tenon projects -Y into tie beam
                    if (!nearButt)
                        result.Add(new JointParams(Module1.JointType.Tenon,
                            new Point3d(c.StartX, c.StartY, tenonZ),
                            new Vector3d(0, -1, 0), new Vector3d(1, 0, 0),
                            xd.Width, xd.Depth, nTw, xd.BentNumber, xd.Designation,
                            nRel, 0.0, false, nHd));
                    // Far (index 1) = TenonUpId: oblique rafter-contact polygon
                    if (!farButt)
                    {
                        Point3d pf0 = new Point3d(thirdSpan, c.TOH + ((thirdSpan - c.PostDepth) * c.Pitch), tenonZ);
                        Point3d pf1 = new Point3d(pf0.X + xd.Depth, pf0.Y + xd.Depth * c.Pitch, pf0.Z);
                        Point3d pf2 = Module1.PolarPoint(pf1, c.Beta + (Math.PI/2), 4);
                        Point3d pf3 = Module1.PolarPoint(pf0, Math.PI/2, 4/Math.Cos(c.Beta));
                        result.Add(JointParams.ForPolygon(new[] { pf0, pf1, pf2, pf3 }, fTw > 0 ? fTw : 2.0, xd.BentNumber, xd.Designation));
                    }
                    return result.ToArray();
                }
                case "QPPostRight":
                {
                    QPPostContext c = ParseQPPostContext(ctxJson);
                    bool nearButt = checkButt && string.Equals(xd.JointNear, "Butt", StringComparison.OrdinalIgnoreCase);
                    bool farButt  = checkButt && string.Equals(xd.JointFar,  "Butt", StringComparison.OrdinalIgnoreCase);
                    double z = 0;
                    if (c.Make3D) { switch (c.OffsetType) { case Module1.Back: z=0; break; case Module1.Centered: z=(c.GirtWidth-xd.Width)/2; break; default: z=(c.GirtWidth-xd.Width); break; } }
                    double tenonZ  = c.Make3D ? z + (xd.Width - 2.0) / 2.0 : 0;
                    double thirdSpan = c.Span / 3.0;
                    ReadScalars(xd, out double nTw, out double nRel, out double nHd, out double fTw, out _, out _,
                        nearOverride, farOverride);
                    var result = new List<JointParams>();
                    if (!nearButt)
                        result.Add(new JointParams(Module1.JointType.Tenon,
                            new Point3d(c.StartX, c.StartY, tenonZ),
                            new Vector3d(0, -1, 0), new Vector3d(1, 0, 0),
                            xd.Width, xd.Depth, nTw, xd.BentNumber, xd.Designation,
                            nRel, 0.0, false, nHd));
                    if (!farButt)
                    {
                        Point3d pf0 = new Point3d((thirdSpan*2) - xd.Depth, c.TOH + ((thirdSpan - c.PostDepth)*c.Pitch) + (xd.Depth*c.Pitch), tenonZ);
                        Point3d pf1 = new Point3d(pf0.X + xd.Depth, pf0.Y - xd.Depth*c.Pitch, pf0.Z);
                        Point3d pf2 = Module1.PolarPoint(pf1, Math.PI/2, 4/Math.Cos(c.Beta));
                        Point3d pf3 = Module1.PolarPoint(pf0, (Math.PI/2) - c.Beta, 4);
                        result.Add(JointParams.ForPolygon(new[] { pf0, pf1, pf2, pf3 }, fTw > 0 ? fTw : 2.0, xd.BentNumber, xd.Designation));
                    }
                    return result.ToArray();
                }
                // ---- QPUpperGirt ----
                case "QPUpperGirt":
                {
                    QPUpperGirtContext c = ParseQPUpperGirtContext(ctxJson);
                    bool nearButt = checkButt && string.Equals(xd.JointNear, "Butt", StringComparison.OrdinalIgnoreCase);
                    bool farButt  = checkButt && string.Equals(xd.JointFar,  "Butt", StringComparison.OrdinalIgnoreCase);
                    double z = 0;
                    if (c.Make3D) { switch (c.OffsetType) { case Module1.Back: z=0; break; case Module1.Centered: z=(c.QPRafterWidth-xd.Width)/2; break; default: z=(c.QPRafterWidth-xd.Width); break; } }
                    double tenonZ    = c.Make3D ? z + (xd.Width - 2.0) / 2.0 : 0;
                    double thirdSpan = c.Span / 3.0;
                    const double tw  = 2.0;  // QPUpperGirt TenonWidth is always 2 (private field)
                    var result = new List<JointParams>();
                    // Near (index 0) = TenonLeft, into left queen post (-X)
                    if (!nearButt)
                        result.Add(new JointParams(Module1.JointType.Tenon,
                            new Point3d(c.StartX, c.StartY, tenonZ),
                            new Vector3d(-1, 0, 0), new Vector3d(0, 1, 0),
                            xd.Width, xd.Depth, tw, xd.BentNumber, xd.Designation));
                    // Far (index 1) = TenonRight, into right queen post (+X)
                    if (!farButt)
                    {
                        double rightX = (thirdSpan * 2) - c.QPQpostDepth;
                        double rightY = c.TOH + ((thirdSpan - c.PostDepth + c.QPQpostDepth) * c.Pitch) - (6 + xd.Depth);
                        result.Add(new JointParams(Module1.JointType.Tenon,
                            new Point3d(rightX, rightY, tenonZ),
                            new Vector3d(1, 0, 0), new Vector3d(0, 1, 0),
                            xd.Width, xd.Depth, tw, xd.BentNumber, xd.Designation));
                    }
                    return result.ToArray();
                }
                // ---- HBeam ----
                case "HBeamLeft":
                {
                    // Near always Butt; Far = standard Tenon into left wall post.
                    // Two-element array so slot selection (Far=index 1) works correctly.
                    HBeamContext c = ParseHBeamContext(ctxJson);
                    bool farButt = checkButt && string.Equals(xd.JointFar, "Butt", StringComparison.OrdinalIgnoreCase);
                    if (farButt) return Array.Empty<JointParams>();
                    double tenonZ = c.Make3D ? c.StartZ + (xd.Width - 2.0) / 2.0 : 0;
                    return new JointParams[] {
                        default,  // [0] near placeholder
                        new JointParams(Module1.JointType.Tenon,
                            new Point3d(c.StartX, c.StartY, tenonZ),
                            new Vector3d(-1, 0, 0), new Vector3d(0, 1, 0),
                            xd.Width, xd.Depth, 2.0, xd.BentNumber, xd.Designation)
                    };
                }
                case "HBeamRight":
                {
                    HBeamContext c = ParseHBeamContext(ctxJson);
                    bool farButt = checkButt && string.Equals(xd.JointFar, "Butt", StringComparison.OrdinalIgnoreCase);
                    if (farButt) return Array.Empty<JointParams>();
                    double tenonZ = c.Make3D ? c.StartZ + (xd.Width - 2.0) / 2.0 : 0;
                    return new JointParams[] {
                        default,
                        new JointParams(Module1.JointType.Tenon,
                            new Point3d(c.StartX, c.StartY, tenonZ),
                            new Vector3d(1, 0, 0), new Vector3d(0, 1, 0),
                            xd.Width, xd.Depth, 2.0, xd.BentNumber, xd.Designation)
                    };
                }
                case "HBGirt":
                {
                    HBGirtContext c = ParseHBGirtContext(ctxJson);
                    bool nearButt = checkButt && string.Equals(xd.JointNear, "Butt", StringComparison.OrdinalIgnoreCase);
                    bool farButt  = checkButt && string.Equals(xd.JointFar,  "Butt", StringComparison.OrdinalIgnoreCase);
                    double tenonZ     = c.Make3D ? c.StartZ + (xd.Width - 2.0) / 2.0 : 0;
                    double hbLen      = ((c.Span - ((c.PostDepth * 2) + c.KpostDepth)) / c.HBDivisor * 2) + c.KpostDepth;
                    var result        = new System.Collections.Generic.List<JointParams>();
                    if (!nearButt)
                        result.Add(new JointParams(Module1.JointType.Tenon,
                            new Point3d(c.StartX, c.StartY, tenonZ),
                            new Vector3d(-1, 0, 0), new Vector3d(0, 1, 0),
                            xd.Width, xd.Depth, 2.0, xd.BentNumber, xd.Designation));
                    if (!farButt)
                        result.Add(new JointParams(Module1.JointType.Tenon,
                            new Point3d(c.StartX + hbLen, c.StartY, tenonZ),
                            new Vector3d(1, 0, 0), new Vector3d(0, 1, 0),
                            xd.Width, xd.Depth, 2.0, xd.BentNumber, xd.Designation));
                    return result.ToArray();
                }
                case "HPostLeft":
                {
                    HPostContext c = ParseHPostContext(ctxJson);
                    bool nearButt = checkButt && string.Equals(xd.JointNear, "Butt", StringComparison.OrdinalIgnoreCase);
                    bool farButt  = checkButt && string.Equals(xd.JointFar,  "Butt", StringComparison.OrdinalIgnoreCase);
                    double tenonZ    = c.Make3D ? c.StartZ + (xd.Width - 2.0) / 2.0 : 0;
                    double hbLength  = (c.Span - (c.PostDepth * 2 + c.KpostDepth)) / c.HBDivisor;
                    double hpLenShrt = (hbLength - xd.Depth) * c.Pitch;
                    var result       = new System.Collections.Generic.List<JointParams>();
                    // Near (index 0) = TenonDown: foot tenon projects -Y into HBeamLeft
                    if (!nearButt)
                        result.Add(new JointParams(Module1.JointType.Tenon,
                            new Point3d(c.StartX, c.StartY, tenonZ),
                            new Vector3d(0, -1, 0), new Vector3d(-1, 0, 0),
                            xd.Width, xd.Depth, 2.0, xd.BentNumber, xd.Designation));
                    // Far (index 1) = TenonUp: oblique rafter-contact polygon
                    if (!farButt)
                    {
                        Point3d fp0 = new Point3d(c.StartX - xd.Depth, c.StartY + hpLenShrt + 6, tenonZ);
                        Point3d fp1 = new Point3d(fp0.X + xd.Depth, fp0.Y + xd.Depth * c.Pitch, fp0.Z);
                        Point3d fp2 = Module1.PolarPoint(fp1, c.Beta + (Math.PI / 2), 4);
                        Point3d fp3 = Module1.PolarPoint(fp0, Math.PI / 2, 4 / Math.Cos(c.Beta));
                        result.Add(JointParams.ForPolygon(new[] { fp0, fp1, fp2, fp3 }, 2.0, xd.BentNumber, xd.Designation));
                    }
                    return result.ToArray();
                }
                case "HPostRight":
                {
                    HPostContext c = ParseHPostContext(ctxJson);
                    bool nearButt = checkButt && string.Equals(xd.JointNear, "Butt", StringComparison.OrdinalIgnoreCase);
                    bool farButt  = checkButt && string.Equals(xd.JointFar,  "Butt", StringComparison.OrdinalIgnoreCase);
                    double tenonZ    = c.Make3D ? c.StartZ + (xd.Width - 2.0) / 2.0 : 0;
                    double hbLength  = (c.Span - (c.PostDepth * 2 + c.KpostDepth)) / c.HBDivisor;
                    double hpLenShrt = (hbLength - xd.Depth) * c.Pitch;
                    var result       = new System.Collections.Generic.List<JointParams>();
                    if (!nearButt)
                        result.Add(new JointParams(Module1.JointType.Tenon,
                            new Point3d(c.StartX, c.StartY, tenonZ),
                            new Vector3d(0, -1, 0), new Vector3d(1, 0, 0),
                            xd.Width, xd.Depth, 2.0, xd.BentNumber, xd.Designation));
                    if (!farButt)
                    {
                        // HPostRight.Draw() TenonUp pts (symmetric with HPostLeft but +X)
                        Point3d fp0 = new Point3d(c.StartX, c.StartY + hpLenShrt + hbLength * c.Pitch + 6, tenonZ);
                        Point3d fp1 = new Point3d(fp0.X + xd.Depth, fp0.Y - xd.Depth * c.Pitch, fp0.Z);
                        Point3d fp2 = Module1.PolarPoint(fp1, Math.PI / 2, 4 / Math.Cos(c.Beta));
                        Point3d fp3 = Module1.PolarPoint(fp0, Math.PI / 2 - c.Beta, 4);
                        result.Add(JointParams.ForPolygon(new[] { fp0, fp1, fp2, fp3 }, 2.0, xd.BentNumber, xd.Designation));
                    }
                    return result.ToArray();
                }
                default:
                    return Array.Empty<JointParams>();
            }
        }

        // Reads near- and far-end tenonWidth / tenonRelish / housingDepth.
        // nearJson/farJson override the stored XData JSON strings -- used by ApplyCascade
        // to reconstruct OLD tenon geometry using the pre-regen scalar params.
        private static void ReadScalars(Module1.DataStructure xd,
            out double nTw, out double nRel, out double nHd,
            out double fTw, out double fRel, out double fHd,
            string nearJson = null, string farJson = null)
        {
            var np = DeserializeJointParams(nearJson ?? xd.JointNearParams);
            var fp = DeserializeJointParams(farJson  ?? xd.JointFarParams);
            nTw  = np.TryGetValue("tenonWidth",   out var v) ? v : 2.0;
            nRel = np.TryGetValue("tenonRelish",  out v)     ? v : 0.0;
            nHd  = np.TryGetValue("housingDepth", out v)     ? v : 0.0;
            fTw  = fp.TryGetValue("tenonWidth",   out v)     ? v : 2.0;
            fRel = fp.TryGetValue("tenonRelish",  out v)     ? v : 0.0;
            fHd  = fp.TryGetValue("housingDepth", out v)     ? v : 0.0;
        }

        private static double ReadNearScalar(Module1.DataStructure xd, string key, double def,
            string nearJson = null)
        {
            var np = DeserializeJointParams(nearJson ?? xd.JointNearParams);
            return np.TryGetValue(key, out var v) ? v : def;
        }

        // -----------------------------------------------------------------------
        // DeltaSwapJoint
        // Fills the old mortise void using JointFactory + Joint.Fill, then cuts the
        // new mortise using the actual standalone tenon solid + Joint.Mortise.
        // Also saves the newParams to the receiver's IncomingJoints xrecord so the
        // next delta-swap has accurate old params to fill with.
        //
        // oldParams.Origin == default(Point3d) means no stored params for this giver
        // (first regen, old drawing): fill step is skipped; only the cut is applied.
        // -----------------------------------------------------------------------
        // Fills the old mortise void (BoolUnite) then cuts the new one (BoolSubtract).
        // oldParams: reconstructed from the giver's JointNearParamsDrawn (the params that
        // physically created the current void).  JointFactory.Create(oldParams) produces
        // the exact solid that matches the existing void.
        // newTenonId: the actual new standalone tenon solid from the giver's fresh Draw().
        // If oldParams.Origin == default the fill step is skipped (should not happen when
        // JointNearParamsDrawn is properly written by TimberDraw on every draw).
        private static void DeltaSwapJoint(ObjectId receiverId,
            JointParams oldParams, ObjectId newTenonId, bool skipFill = false)
        {
            // 1. Fill old void using JointFactory -- exact geometry from stored params.
            // Skipped when the old joint was "Butt": no void existed in the receiver,
            // and BoolUnite of a Shoulder solid into an already-solid pentagonal body
            // can corrupt its topology, causing the subsequent BoolSubtract to fail.
            // CustomPts check covers Polygon joints whose Origin is always (0,0,0).
            bool hasOldGeom = oldParams.Origin != default(Point3d) || oldParams.CustomPts != null;
            if (!skipFill && hasOldGeom && Module1.Make3D)
            {
                ObjectId fillId = JointFactory.Create(oldParams.JointType, oldParams);
                if (!fillId.IsNull && !fillId.IsErased)
                {
                    try   { Module1.AddJoint(receiverId, fillId, Module1.Joint.Fill); }
                    catch { }
                    Module1.EraseEntity(fillId);
                }
            }

            // 2. Cut new mortise with the actual tenon solid (exact geometry).
            // Opens the tool solid ForRead (not ForWrite) to avoid an AutoCAD transaction
            // conflict that Module1.AddJoint(Mortise) triggers when both solids are opened
            // ForWrite in the same transaction -- causing the BoolSubtract to silently no-op.
            if (!newTenonId.IsNull && !newTenonId.IsErased)
            {
                try
                {
                    var _db = Autodesk.AutoCAD.DatabaseServices.HostApplicationServices.WorkingDatabase;
                    using var tr = _db.TransactionManager.StartTransaction();
                    var rcvr = (Autodesk.AutoCAD.DatabaseServices.Solid3d)
                        tr.GetObject(receiverId, Autodesk.AutoCAD.DatabaseServices.OpenMode.ForWrite);
                    var tnon = (Autodesk.AutoCAD.DatabaseServices.Solid3d)
                        tr.GetObject(newTenonId, Autodesk.AutoCAD.DatabaseServices.OpenMode.ForRead);
                    rcvr.BooleanOperation(
                        Autodesk.AutoCAD.DatabaseServices.BooleanOperationType.BoolSubtract,
                        (Autodesk.AutoCAD.DatabaseServices.Solid3d)tnon.Clone());
                    tr.Commit();
                }
                catch { }
                Module1.DeleteJoint(newTenonId);
            }
        }

        // -----------------------------------------------------------------------
        // BentGirt
        // -----------------------------------------------------------------------
        private static void RegenerateBentGirt(ObjectId timberId, Module1.DataStructure data,
            string contextJson, double w, double d, string jn, string jf,
            Module1.Connection[] savedConns)
        {
            BentGirtContext ctx = ParseBentGirtContext(contextJson);
            EraseAndMarkStale(timberId);

            Module1.StartPoint  = new Point3d(ctx.StartX, ctx.StartY, ctx.StartZ);
            Module1.Span        = ctx.Span;
            Module1.BOG         = ctx.BOG;
            Module1.Make3D      = ctx.Make3D;

            var bg = new BentGirt
            {
                Width         = w,
                Depth         = d,
                postDepth     = ctx.PostDepth,
                StartPoint    = new Point3d(ctx.PostDepth, ctx.BOG, ctx.StartZ),
                Type          = data.Type,
                BentNumber    = data.BentNumber,
                Designation   = data.Designation,
                NearJointType = string.IsNullOrEmpty(jn) ? "Tenon" : jn,
                FarJointType  = string.IsNullOrEmpty(jf) ? "Tenon" : jf,
                NearParams    = DeserializeJointParams(data.JointNearParams),
                FarParams     = DeserializeJointParams(data.JointFarParams)
            };
            bg.Draw();
            ApplyJointTypes(bg.TimberId, jn, jf, bg.TenonLeftId, bg.TenonRightId);

            // Re-write Connections xrecord using saved per-end conn handles + new tenon solids.
            // Near end (thisEnd=0) -> left tenon solid; Far end (thisEnd=1) -> right tenon solid.
            // Only processes connections that were written with AddConnectionFull (tenonHandles set).
            // Rebuild the Connections xrecord on the new entity using saved per-end handles
            // and the freshly drawn tenon solids.  Skip only non-Tenon connections (those
            // were written by AddConnection, not AddConnectionFull, and have no tenon handle).
            // Empty TenonHandles in savedConns means the end was Butt on the last draw;
            // we still rebuild the connection with the NEW TenonId (which may now be live).
            // Resolve stale ConnHandles via _sessionRegenMap when a receiver was re-regened
            // and UpdateConnectionHandle missed this member.
            foreach (Module1.Connection sc in savedConns)
            {
                if (sc.ThisJoint != Module1.JointType.Tenon)
                    continue;
                Handle connHandle = sc.ConnHandle;
                ObjectId connId   = Module1.GetObjectIdFromHandle(connHandle);
                if (connId.IsNull || connId.IsErased)
                {
                    connHandle = ResolveHandle(connHandle);
                    connId     = Module1.GetObjectIdFromHandle(connHandle);
                    if (connId.IsNull || connId.IsErased) continue;
                }
                ObjectId newTenonId = (sc.ThisEnd == Module1.End.Near)
                    ? bg.TenonLeftId : bg.TenonRightId;
                Handle[] tenonHandles = newTenonId.IsNull
                    ? System.Array.Empty<Handle>() : new[] { newTenonId.Handle };
                Module1.AddConnectionFull(bg.TimberId, new Module1.Connection {
                    ConnHandle   = connHandle,
                    ThisEnd      = sc.ThisEnd,
                    OtherEnd     = sc.OtherEnd,
                    ThisJoint    = Module1.JointType.Tenon,
                    TenonHandles = tenonHandles
                });
            }
        }

        // -----------------------------------------------------------------------
        // FloorBentGirt
        // -----------------------------------------------------------------------
        private static void RegenerateFloorBentGirt(ObjectId timberId, Module1.DataStructure data,
            string contextJson, double w, double d, string jn, string jf,
            Module1.Connection[] savedConns)
        {
            FloorBentGirtContext ctx = ParseFloorBentGirtContext(contextJson);
            EraseAndMarkStale(timberId);

            Module1.StartPoint  = new Point3d(ctx.StartX, ctx.StartY, ctx.StartZ);
            Module1.Span        = ctx.Span;
            Module1.Make3D      = ctx.Make3D;

            var fg = new FloorBentGirt
            {
                Width         = w,
                Depth         = d,
                StartPoint    = new Point3d(ctx.PostDepth, ctx.LocalStartY, ctx.StartZ),
                Type          = data.Type,
                BentNumber    = data.BentNumber,
                Designation   = data.Designation,
                NearJointType = string.IsNullOrEmpty(jn) ? "Tenon" : jn,
                FarJointType  = string.IsNullOrEmpty(jf) ? "Tenon" : jf,
                NearParams    = DeserializeJointParams(data.JointNearParams),
                FarParams     = DeserializeJointParams(data.JointFarParams)
            };
            fg.Draw(ctx.PostDepth);
            ApplyJointTypes(fg.TimberId, jn, jf, fg.TenonLeftId, fg.TenonRightId);

            // Rebuild the Connections xrecord using the new tenon solids.
            // Skip only non-Tenon connections (AddConnection without TenonHandles).
            // Empty TenonHandles in savedConns = end was Butt; rebuild with new TenonId.
            // Resolve stale ConnHandles via _sessionRegenMap.
            foreach (Module1.Connection sc in savedConns)
            {
                if (sc.ThisJoint != Module1.JointType.Tenon)
                    continue;
                Handle connHandle = sc.ConnHandle;
                ObjectId connId   = Module1.GetObjectIdFromHandle(connHandle);
                if (connId.IsNull || connId.IsErased)
                {
                    connHandle = ResolveHandle(connHandle);
                    connId     = Module1.GetObjectIdFromHandle(connHandle);
                    if (connId.IsNull || connId.IsErased) continue;
                }
                ObjectId newTenonId = (sc.ThisEnd == Module1.End.Near)
                    ? fg.TenonLeftId : fg.TenonRightId;
                Handle[] tenonHandles = newTenonId.IsNull
                    ? System.Array.Empty<Handle>() : new[] { newTenonId.Handle };
                Module1.AddConnectionFull(fg.TimberId, new Module1.Connection {
                    ConnHandle   = connHandle,
                    ThisEnd      = sc.ThisEnd,
                    OtherEnd     = sc.OtherEnd,
                    ThisJoint    = Module1.JointType.Tenon,
                    TenonHandles = tenonHandles
                });
            }
        }

        // -----------------------------------------------------------------------
        // PostLeft
        // -----------------------------------------------------------------------
        private static void RegeneratePostLeft(ObjectId timberId, Module1.DataStructure data,
            string contextJson, double w, double d, string jn, string jf)
        {
            PostContext ctx = ParsePostContext(contextJson);
            EraseAndMarkStale(timberId);

            Module1.StartPoint  = new Point3d(ctx.StartX, ctx.StartY, ctx.StartZ);
            Module1.EaveHt      = ctx.EaveHt;
            Module1.Pitch       = ctx.Pitch;
            Module1.BOG         = ctx.BOG;
            Module1.TOG         = ctx.TOG;
            Module1.TOH         = ctx.TOH;
            Module1.Make3D      = ctx.Make3D;

            var pl = new PostLeft
            {
                StartPoint    = Module1.StartPoint,
                Width         = w,
                Depth         = d,
                BentNumber    = data.BentNumber,
                Designation   = data.Designation,
                HasFlrGirt    = ctx.HasFlrGirt,
                FlrGirtHt     = ctx.FlrGirtHt,
                FlrGirtDepth  = ctx.FlrGirtDepth,
                NearJointType = jn,
                FarJointType  = jf,
                NearParams    = DeserializeJointParams(data.JointNearParams),
                FarParams     = DeserializeJointParams(data.JointFarParams)
            };
            pl.Draw();
            ApplyJointTypes(pl.TimberId, jn, jf, pl.TenonNearId, pl.TenonFarId);
        }

        // -----------------------------------------------------------------------
        // PostRight
        // -----------------------------------------------------------------------
        private static void RegeneratePostRight(ObjectId timberId, Module1.DataStructure data,
            string contextJson, double w, double d, string jn, string jf)
        {
            PostContext ctx = ParsePostContext(contextJson);
            EraseAndMarkStale(timberId);

            Module1.StartPoint  = new Point3d(ctx.StartX, ctx.StartY, ctx.StartZ);
            Module1.Span        = ctx.Span;
            Module1.EaveHt      = ctx.EaveHt;
            Module1.Pitch       = ctx.Pitch;
            Module1.BOG         = ctx.BOG;
            Module1.TOG         = ctx.TOG;
            Module1.TOH         = ctx.TOH;
            Module1.Make3D      = ctx.Make3D;

            var pr = new PostRight
            {
                StartPoint    = Module1.StartPoint,
                Width         = w,
                Depth         = d,
                BentNumber    = data.BentNumber,
                Designation   = data.Designation,
                HasFlrGirt    = ctx.HasFlrGirt,
                FlrGirtHt     = ctx.FlrGirtHt,
                FlrGirtDepth  = ctx.FlrGirtDepth,
                NearJointType = jn,
                FarJointType  = jf,
                NearParams    = DeserializeJointParams(data.JointNearParams),
                FarParams     = DeserializeJointParams(data.JointFarParams)
            };
            pr.Draw();
            ApplyJointTypes(pr.TimberId, jn, jf, pr.TenonNearId, pr.TenonFarId);
        }

        // -----------------------------------------------------------------------
        // KPost
        // -----------------------------------------------------------------------
        private static void RegenerateKPost(ObjectId timberId, Module1.DataStructure data,
            string contextJson, double w, double d, string jn, string jf,
            Module1.Connection[] savedConns = null)
        {
            KPostContext ctx = ParseKPostContext(contextJson);
            EraseAndMarkStale(timberId);

            // ctx.StartX/Y/Z is the KPost's local origin (halfSpan - Depth/2, TOG, z),
            // not the bent's global origin.  Module1.StartPoint is the bent global;
            // for KPBent it is always (0, 0, z) in 2D mode.
            Module1.StartPoint = new Point3d(0, 0, ctx.StartZ);
            Module1.Span       = ctx.Span;
            Module1.EaveHt     = ctx.EaveHt;
            Module1.Pitch      = ctx.Pitch;
            Module1.Beta       = ctx.Beta;
            Module1.TOG        = ctx.TOG;
            Module1.TOH        = ctx.TOH;
            Module1.Make3D     = ctx.Make3D;

            var kp = new KPost
            {
                Width               = w,
                Depth               = d,
                postDepth           = ctx.PostDepth,
                KpostRafterSitDepth = ctx.KpostRafterSitDepth,
                StartPoint          = new Point3d(ctx.StartX, ctx.TOG, ctx.StartZ),
                Type                = data.Type,
                BentNumber          = data.BentNumber,
                Designation         = data.Designation,
                NearJointType       = string.IsNullOrEmpty(jn) ? "Tenon" : jn,
                NearParams          = DeserializeJointParams(data.JointNearParams),
                FarParams           = DeserializeJointParams(data.JointFarParams)
            };
            kp.Draw();
            ApplyJointTypes(kp.TimberId, jn, jf, nearTenonId: kp.Tenon);

            // Rebuild Connections xrecord so ApplyCascade's rich path can fill/cut
            // the BGirt void when the near joint type changes (Tenon <-> Butt).
            foreach (Module1.Connection sc in savedConns ?? System.Array.Empty<Module1.Connection>())
            {
                if (sc.ThisJoint != Module1.JointType.Tenon) continue;
                // KPost only has a near tenon (foot into BGirt).
                Handle[] th = kp.Tenon.IsNull
                    ? System.Array.Empty<Handle>() : new[] { kp.Tenon.Handle };
                Module1.AddConnectionFull(kp.TimberId, new Module1.Connection {
                    ConnHandle   = sc.ConnHandle,
                    ThisEnd      = sc.ThisEnd,
                    OtherEnd     = sc.OtherEnd,
                    ThisJoint    = Module1.JointType.Tenon,
                    TenonHandles = th
                });
            }
        }

        // -----------------------------------------------------------------------
        // KP Struts (4 classes sharing one context struct; all fields stored even
        // if a specific class doesn't use TOG, Prun, etc. -- unused values = 0)
        // -----------------------------------------------------------------------
        // Fill the rafter void when KPStrutLeft near end transitions Tenon->Butt.
        // TenonUpId (rafter contact oblique tenon) was erased by KPBent.AddMortise, so
        // we recreate the geometry from ctx+data (identical to KPStrutLeft.Draw() near block).
        private static void FillObliqueStrutLeft(
            KPStrutContext ctx, Module1.DataStructure data,
            string newJn, Module1.Connection[] savedConns)
        {
            if (!Module1.Make3D) return;
            if (!string.Equals(newJn, "Butt", StringComparison.OrdinalIgnoreCase)) return;
            if (string.Equals(data.JointNear, "Butt", StringComparison.OrdinalIgnoreCase)) return;

            Handle receiverHandle = default;
            if (savedConns != null)
                foreach (var sc in savedConns)
                    if (sc.ThisJoint == Module1.JointType.Tenon && sc.ThisEnd == Module1.End.Near)
                    { receiverHandle = sc.ConnHandle; break; }
            if (receiverHandle == default) return;
            receiverHandle = ResolveHandle(receiverHandle);
            ObjectId receiverId = Module1.GetObjectIdFromHandle(receiverHandle);
            if (receiverId.IsNull || receiverId.IsErased) return;

            double z = 0;
            if (ctx.Make3D) {
                switch (ctx.OffsetType) {
                    case Module1.Centered: z = (ctx.KPostWidth - data.Width) / 2; break;
                    case Module1.Front:    z =  ctx.KPostWidth - data.Width;      break;
                }
            }
            double tenonZ = ctx.Make3D ? z + (data.Width - 2) / 2 : 0;
            var nearP = DeserializeJointParams(data.JointNearParams);
            double nearTW = nearP.TryGetValue("tenonWidth", out var ntwSL) ? ntwSL : 2.0;

            // Reconstruct pts identical to KPStrutLeft.Draw() near (Up) tenon block.
            var pts = new Point3dCollection();
            pts.Add(new Point3d(ctx.StartX, ctx.StartY, tenonZ));
            pts.Add(Module1.PolarPoint(pts[0], Module1.Beta,
                data.Depth / Math.Cos((Math.PI / 2) - (Module1.Beta * 2))));
            pts.Add(Module1.PolarPoint(pts[1],
                Module1.Beta + (Math.PI / 2) + ((Math.PI / 2) - (Module1.Beta * 2)),
                4 / Math.Cos((Math.PI / 2) - (Module1.Beta * 2))));
            pts.Add(Module1.PolarPoint(pts[0], Module1.Beta + (Math.PI / 2), 4));

            ObjectId fillId = Module1.DrawElement(pts, nearTW, "Tenon", "5", "");
            if (!fillId.IsNull) {
                try   { Module1.AddJoint(receiverId, fillId, Module1.Joint.Fill); }
                catch { }
                Module1.EraseEntity(fillId);
            }
        }

        // Fill the rafter void when KPStrutRight near end transitions Tenon->Butt.
        private static void FillObliqueStrutRight(
            KPStrutContext ctx, Module1.DataStructure data,
            string newJn, Module1.Connection[] savedConns)
        {
            if (!Module1.Make3D) return;
            if (!string.Equals(newJn, "Butt", StringComparison.OrdinalIgnoreCase)) return;
            if (string.Equals(data.JointNear, "Butt", StringComparison.OrdinalIgnoreCase)) return;

            Handle receiverHandle = default;
            if (savedConns != null)
                foreach (var sc in savedConns)
                    if (sc.ThisJoint == Module1.JointType.Tenon && sc.ThisEnd == Module1.End.Near)
                    { receiverHandle = sc.ConnHandle; break; }
            if (receiverHandle == default) return;
            receiverHandle = ResolveHandle(receiverHandle);
            ObjectId receiverId = Module1.GetObjectIdFromHandle(receiverHandle);
            if (receiverId.IsNull || receiverId.IsErased) return;

            double z = 0;
            if (ctx.Make3D) {
                switch (ctx.OffsetType) {
                    case Module1.Centered: z = (ctx.KPostWidth - data.Width) / 2; break;
                    case Module1.Front:    z =  ctx.KPostWidth - data.Width;      break;
                }
            }
            double tenonZ = ctx.Make3D ? z + (data.Width - 2) / 2 : 0;
            var nearP = DeserializeJointParams(data.JointNearParams);
            double nearTW = nearP.TryGetValue("tenonWidth", out var ntwSR) ? ntwSR : 2.0;

            // Reconstruct pts identical to KPStrutRight.Draw() near (Up) tenon block.
            var pts = new Point3dCollection();
            pts.Add(new Point3d(ctx.StartX, ctx.StartY, tenonZ));
            pts.Add(Module1.PolarPoint(pts[0], Math.Atan(Module1.Prun / Module1.Prise), 4));
            pts.Add(Module1.PolarPoint(pts[1],
                Math.Atan(Module1.Prun / Module1.Prise) + (Math.PI / 2),
                data.Depth / Math.Cos((Math.PI / 2) - (Module1.Beta * 2))
                    - 4 * Math.Tan((Math.PI / 2) - (Module1.Beta * 2))));
            pts.Add(Module1.PolarPoint(pts[0], Math.PI - Module1.Beta,
                data.Depth / Math.Cos((Math.PI / 2) - (Module1.Beta * 2))));

            ObjectId fillId = Module1.DrawElement(pts, nearTW, "Tenon", "3", "");
            if (!fillId.IsNull) {
                try   { Module1.AddJoint(receiverId, fillId, Module1.Joint.Fill); }
                catch { }
                Module1.EraseEntity(fillId);
            }
        }

        private static void RegenerateKPStrutLeft(ObjectId timberId, Module1.DataStructure data,
            string contextJson, double w, double d, string jn, string jf,
            Module1.Connection[] savedConns = null)
        {
            KPStrutContext ctx = ParseKPStrutContext(contextJson);
            // Set globals BEFORE fill so the reconstructed pts use the correct Beta/Pitch.
            // TenonUpId (rafter contact) was erased by AddMortise in KPBent so we cannot
            // reuse the stored solid -- FillObliqueStrutLeft recreates the geometry from ctx.
            Module1.Make3D = ctx.Make3D;  Module1.OffsetType = ctx.OffsetType;
            Module1.TOH    = ctx.TOH;     Module1.Pitch       = ctx.Pitch;
            Module1.Beta   = ctx.Beta;
            FillObliqueStrutLeft(ctx, data, jn, savedConns);
            EraseAndMarkStale(timberId);
            var s = new KPStrutLeft {
                Width = w, Depth = d, KPostWidth = ctx.KPostWidth, KPostDepth = ctx.KPostDepth,
                postDepth = ctx.PostDepth, Span = ctx.Span, KpostRafterSitDepth = ctx.KpostRafterSitDepth,
                StartPoint    = new Point3d(ctx.StartX, ctx.StartY, 0),
                Type          = data.Type, BentNumber = data.BentNumber, Designation = data.Designation,
                NearJointType = string.IsNullOrEmpty(jn) ? "Tenon" : jn,
                FarJointType  = string.IsNullOrEmpty(jf) ? "Tenon" : jf,
                NearParams    = DeserializeJointParams(data.JointNearParams),
                FarParams     = DeserializeJointParams(data.JointFarParams)
            };
            // near=rafter=TenonUpId, far=kpost=TenonDownId
            s.Draw();  ApplyJointTypes(s.TimberId, jn, jf, s.TenonUpId, s.TenonDownId);
            RebuildKPStrutConns(s.TimberId, savedConns, s.TenonUpId, s.TenonDownId);
        }

        private static void RegenerateKPStrutRight(ObjectId timberId, Module1.DataStructure data,
            string contextJson, double w, double d, string jn, string jf,
            Module1.Connection[] savedConns = null)
        {
            KPStrutContext ctx = ParseKPStrutContext(contextJson);
            Module1.Make3D = ctx.Make3D;  Module1.OffsetType = ctx.OffsetType;
            Module1.TOH    = ctx.TOH;     Module1.Pitch       = ctx.Pitch;
            Module1.Beta   = ctx.Beta;    Module1.Prun        = ctx.Prun;
            Module1.Prise  = ctx.Prise;
            FillObliqueStrutRight(ctx, data, jn, savedConns);
            EraseAndMarkStale(timberId);
            var s = new KPStrutRight {
                Width = w, Depth = d, KPostWidth = ctx.KPostWidth, KPostDepth = ctx.KPostDepth,
                postDepth = ctx.PostDepth, Span = ctx.Span, KpostRafterSitDepth = ctx.KpostRafterSitDepth,
                StartPoint    = new Point3d(ctx.StartX, ctx.StartY, 0),
                Type          = data.Type, BentNumber = data.BentNumber, Designation = data.Designation,
                NearJointType = string.IsNullOrEmpty(jn) ? "Tenon" : jn,
                FarJointType  = string.IsNullOrEmpty(jf) ? "Tenon" : jf,
                NearParams    = DeserializeJointParams(data.JointNearParams),
                FarParams     = DeserializeJointParams(data.JointFarParams)
            };
            s.Draw();  ApplyJointTypes(s.TimberId, jn, jf, s.TenonUpId, s.TenonDownId);
            RebuildKPStrutConns(s.TimberId, savedConns, s.TenonUpId, s.TenonDownId);
        }

        private static void RegenerateKPVertStrutLeft(ObjectId timberId, Module1.DataStructure data,
            string contextJson, double w, double d, string jn, string jf,
            Module1.Connection[] savedConns = null)
        {
            KPStrutContext ctx = ParseKPStrutContext(contextJson);
            // Fill of old rafter void (Tenon->Butt) is now handled by ApplyCascade via
            // GetTenonParamsGeometric KPVertStrutLeft index-1 returning ForPolygon params.
            Module1.Make3D = ctx.Make3D;  Module1.OffsetType = ctx.OffsetType;
            Module1.TOH    = ctx.TOH;     Module1.TOG        = ctx.TOH - 6;
            Module1.Pitch  = ctx.Pitch;   Module1.Beta       = ctx.Beta;
            EraseAndMarkStale(timberId);
            var s = new KPVertStrutLeft {
                Width         = w, Depth = d, KPostWidth = ctx.KPostWidth, KPostDepth = ctx.KPostDepth,
                postDepth     = ctx.PostDepth, Span = ctx.Span, KpostRafterSitDepth = ctx.KpostRafterSitDepth,
                StartPoint    = new Point3d(ctx.StartX, ctx.StartY, 0),
                Type          = data.Type, BentNumber = data.BentNumber, Designation = data.Designation,
                NearJointType = string.IsNullOrEmpty(jn) ? "Tenon" : jn,
                FarJointType  = string.IsNullOrEmpty(jf) ? "Tenon" : jf,
                NearParams    = DeserializeJointParams(data.JointNearParams),
                FarParams     = DeserializeJointParams(data.JointFarParams)
            };
            // near=foot/girt=TenonDownId, far=rafter=TenonUpId
            s.Draw();  ApplyJointTypes(s.TimberId, jn, jf, s.TenonDownId, s.TenonUpId);
            RebuildKPStrutConns(s.TimberId, savedConns, s.TenonDownId, s.TenonUpId);
        }

        private static void RegenerateKPVertStrutRight(ObjectId timberId, Module1.DataStructure data,
            string contextJson, double w, double d, string jn, string jf,
            Module1.Connection[] savedConns = null)
        {
            KPStrutContext ctx = ParseKPStrutContext(contextJson);
            // Fill of old rafter void now handled by ApplyCascade via ForPolygon params.
            Module1.Make3D  = ctx.Make3D;  Module1.OffsetType = ctx.OffsetType;
            Module1.TOH     = ctx.TOH;     Module1.TOG        = ctx.TOH - 6;
            Module1.Pitch   = ctx.Pitch;   Module1.Beta       = ctx.Beta;
            Module1.Prun    = ctx.Prun;    Module1.Prise      = ctx.Prise;
            EraseAndMarkStale(timberId);
            var s = new KPVertStrutRight {
                Width         = w, Depth = d, KPostWidth = ctx.KPostWidth, KPostDepth = ctx.KPostDepth,
                postDepth     = ctx.PostDepth, Span = ctx.Span, KpostRafterSitDepth = ctx.KpostRafterSitDepth,
                StartPoint    = new Point3d(ctx.StartX, ctx.StartY, 0),
                Type          = data.Type, BentNumber = data.BentNumber, Designation = data.Designation,
                NearJointType = string.IsNullOrEmpty(jn) ? "Tenon" : jn,
                FarJointType  = string.IsNullOrEmpty(jf) ? "Tenon" : jf,
                NearParams    = DeserializeJointParams(data.JointNearParams),
                FarParams     = DeserializeJointParams(data.JointFarParams)
            };
            s.Draw();  ApplyJointTypes(s.TimberId, jn, jf, s.TenonDownId, s.TenonUpId);
            RebuildKPStrutConns(s.TimberId, savedConns, s.TenonDownId, s.TenonUpId);
        }

        // Shared Connections-xrecord rebuild for KPStrut* and KPVertStrut* regen.
        // nearTenonId = the end-0 (near) tenon solid just drawn; farTenonId = end-1 (far).
        // Resolves stale ConnHandles via _sessionRegenMap so receivers regenerated earlier
        // in the session are still findable even if UpdateConnectionHandle missed them.
        private static void RebuildKPStrutConns(ObjectId newId,
            Module1.Connection[] savedConns, ObjectId nearTenonId, ObjectId farTenonId)
        {
            foreach (Module1.Connection sc in savedConns ?? System.Array.Empty<Module1.Connection>())
            {
                if (sc.ThisJoint != Module1.JointType.Tenon) continue;

                // Resolve the receiver handle: follow the session regen map if it's stale.
                Handle connHandle = sc.ConnHandle;
                ObjectId connId   = Module1.GetObjectIdFromHandle(connHandle);
                if (connId.IsNull || connId.IsErased)
                {
                    connHandle = ResolveHandle(connHandle);
                    connId     = Module1.GetObjectIdFromHandle(connHandle);
                    if (connId.IsNull || connId.IsErased) continue;   // truly gone
                }

                ObjectId t = (sc.ThisEnd == Module1.End.Near) ? nearTenonId : farTenonId;
                Handle[] th = t.IsNull ? System.Array.Empty<Handle>() : new[] { t.Handle };
                Module1.AddConnectionFull(newId, new Module1.Connection {
                    ConnHandle = connHandle, ThisEnd = sc.ThisEnd,
                    OtherEnd = sc.OtherEnd, ThisJoint = Module1.JointType.Tenon,
                    TenonHandles = th
                });
            }
        }

        // FillObliqueVStrutLeft and FillObliqueVStrutRight have been removed.
        // Rafter void fill for VStrut Tenon->Butt transitions is now handled by
        // ApplyCascade using GetTenonParamsGeometric KPVertStrutLeft/Right index-1
        // (ForPolygon params), which fires the standard "no live tenon fill" path.

        // -----------------------------------------------------------------------
        // KPRafterLeft / KPRafterRight  (share one context struct)
        // -----------------------------------------------------------------------
        private static void RegenerateKPRafterLeft(ObjectId timberId, Module1.DataStructure data,
            string contextJson, double w, double d, string jn, string jf,
            Module1.Connection[] savedConns = null)
        {
            KPRafterContext ctx = ParseKPRafterContext(contextJson);
            EraseAndMarkStale(timberId);

            Module1.Span        = ctx.Span;   Module1.EaveHt = ctx.EaveHt;
            Module1.Pitch       = ctx.Pitch;  Module1.Beta   = ctx.Beta;
            Module1.TOH         = ctx.TOH;    Module1.Make3D = ctx.Make3D;
            Module1.PlumbLength = d / Math.Cos(ctx.Beta);

            var kr = new KPRafterLeft
            {
                Width               = w,
                Depth               = d,
                KPostDepth          = ctx.KPostDepth,
                postDepth           = ctx.PostDepth,
                KpostRafterSitDepth = ctx.KpostRafterSitDepth,
                StartPoint          = new Point3d(ctx.StartX, ctx.StartY, ctx.StartZ),
                Type                = data.Type,
                BentNumber          = data.BentNumber,
                Designation         = data.Designation,
                NearJointType       = string.IsNullOrEmpty(jn) ? "Shoulder" : jn,
                FarJointType        = string.IsNullOrEmpty(jf) ? "Tenon"    : jf,
                NearParams          = DeserializeJointParams(data.JointNearParams),
                FarParams           = DeserializeJointParams(data.JointFarParams)
            };
            kr.Draw();
            // Sync BentNetwork: the new entity has a new handle; update stored ReceiverHandle
            // so NetworkManager.ReapplyIncoming (called inside ApplyJointTypes) can find
            // the incoming Polygon edges (strut/vstrut→rafter) for this new entity.
            BentNetwork.UpdateReceiverHandle(timberId.Handle, kr.TimberId.Handle);
            ApplyJointTypes(kr.TimberId, jn, jf, nearTenonId: kr.SeatPeakId, farTenonId: kr.Tenon);
            // near=KPost shoulder (SeatPeakId), far=post foot tenon (Tenon)
            RebuildKPStrutConns(kr.TimberId, savedConns, kr.SeatPeakId, kr.Tenon);
        }

        private static void RegenerateKPRafterRight(ObjectId timberId, Module1.DataStructure data,
            string contextJson, double w, double d, string jn, string jf,
            Module1.Connection[] savedConns = null)
        {
            KPRafterContext ctx = ParseKPRafterContext(contextJson);
            EraseAndMarkStale(timberId);

            Module1.Span        = ctx.Span;   Module1.EaveHt = ctx.EaveHt;
            Module1.Pitch       = ctx.Pitch;  Module1.Beta   = ctx.Beta;
            Module1.TOH         = ctx.TOH;    Module1.Make3D = ctx.Make3D;
            Module1.PlumbLength = d / Math.Cos(ctx.Beta);

            var kr = new KPRafterRight
            {
                Width               = w,
                Depth               = d,
                KPostDepth          = ctx.KPostDepth,
                postDepth           = ctx.PostDepth,
                KpostRafterSitDepth = ctx.KpostRafterSitDepth,
                StartPoint          = new Point3d(ctx.StartX, ctx.StartY, ctx.StartZ),
                Type                = data.Type,
                BentNumber          = data.BentNumber,
                Designation         = data.Designation,
                NearJointType       = string.IsNullOrEmpty(jn) ? "Shoulder" : jn,
                FarJointType        = string.IsNullOrEmpty(jf) ? "Tenon"    : jf,
                NearParams          = DeserializeJointParams(data.JointNearParams),
                FarParams           = DeserializeJointParams(data.JointFarParams)
            };
            kr.Draw();
            BentNetwork.UpdateReceiverHandle(timberId.Handle, kr.TimberId.Handle);
            ApplyJointTypes(kr.TimberId, jn, jf, nearTenonId: kr.SeatPeakId, farTenonId: kr.Tenon);
            RebuildKPStrutConns(kr.TimberId, savedConns, kr.SeatPeakId, kr.Tenon);
        }

        // -----------------------------------------------------------------------
        // KPRafter oblique-strut mortise re-cut
        // -----------------------------------------------------------------------
        // Handled by NetworkManager.ReapplyIncoming via BentNetwork Polygon edges
        // registered in KPBent.Draw().  These per-type helpers have been removed.

        // -----------------------------------------------------------------------
        // BentBrace
        // -----------------------------------------------------------------------
        // Common setup for brace fill helpers: compute z, angles, tenonZ.
        private static void BraceZ(BentBraceContext ctx, double dataWidth,
            out double z, out double zAngle, out double yAngle, out double tenonZ)
        {
            z = 0; zAngle = ctx.ZAngle; yAngle = ctx.YAngle;
            if (ctx.Make3D) {
                switch (ctx.OffsetType) {
                    case Module1.Centered: z = (ctx.PostWidth - dataWidth) / 2; break;
                    case Module1.Front:    z = ctx.PostWidth; zAngle += 90; yAngle += 180; break;
                }
            }
            tenonZ = ctx.Make3D ? 1.5 : 0;
        }

        // Fill the Near=post receiver when NearJointType transitions Tenon->Butt.
        // SwapEnds=false: post=TenonDown -> use pt0 geometry.
        // SwapEnds=true : post=TenonUp   -> use pt4 geometry.
        private static void FillObliqueBraceNear(
            BentBraceContext ctx, Module1.DataStructure data,
            string newJn, Module1.Connection[] savedConns)
        {
            var _edBN = Autodesk.AutoCAD.ApplicationServices.Application
                .DocumentManager.MdiActiveDocument?.Editor;
            if (!Module1.Make3D) { _edBN?.WriteMessage("\nFillBraceNear: Make3D=false, skip."); return; }
            if (!string.Equals(newJn, "Butt", StringComparison.OrdinalIgnoreCase)) return;
            if (string.Equals(data.JointNear, "Butt", StringComparison.OrdinalIgnoreCase))
            { _edBN?.WriteMessage("\nFillBraceNear: JointNear already Butt, skip."); return; }
            _edBN?.WriteMessage("\nFillBraceNear: SwapEnds=" + ctx.SwapEnds
                + " conns=" + (savedConns?.Length ?? 0));

            Handle receiverHandle = default;
            if (savedConns != null)
                foreach (var sc in savedConns)
                    if (sc.ThisJoint == Module1.JointType.Tenon && sc.ThisEnd == Module1.End.Near)
                    { receiverHandle = sc.ConnHandle; break; }
            if (receiverHandle == default)
            { _edBN?.WriteMessage("\nFillBraceNear: no Near conn found."); return; }
            receiverHandle = ResolveHandle(receiverHandle);
            ObjectId receiverId = Module1.GetObjectIdFromHandle(receiverHandle);
            if (receiverId.IsNull || receiverId.IsErased) return;

            BraceZ(ctx, data.Width, out double z, out double zAngle, out double yAngle, out double tenonZ);
            const double sinBeta = 0.7071067811865476;

            Point3dCollection pts = new();
            string label; double tw;
            if (!ctx.SwapEnds) {
                // NearJointType controls TenonDown (pt0 = post end for this brace)
                var pt0 = new Point3d(ctx.StartX, ctx.StartY - ctx.Length, z);
                pts.Add(Module1.AtPoint(pt0, 0, 0, tenonZ));
                pts.Add(Module1.AtPoint(pts[0], 0, ctx.Depth / sinBeta, 0));
                pts.Add(Module1.AtPoint(pts[1], -4, -4, 0));
                pts.Add(Module1.AtPoint(pts[0], -4, 0, 0));
                label = "Down";
                var nearP = DeserializeJointParams(data.JointNearParams);
                tw = nearP.TryGetValue("tenonWidth", out var n1) ? n1 : 1.5;
            } else {
                // NearJointType controls TenonUp (pt4 = post end for this brace)
                var pt4 = new Point3d(ctx.StartX + ctx.Length - ctx.Depth / sinBeta, ctx.StartY, z);
                pts.Add(Module1.AtPoint(pt4, 0, 0, tenonZ * 2));
                pts.Add(Module1.AtPoint(pts[0], 4, 4, 0));
                pts.Add(Module1.AtPoint(pts[0], ctx.Depth / sinBeta, 4, 0));
                pts.Add(Module1.AtPoint(pts[0], ctx.Depth / sinBeta, 0, 0));
                label = "Up";
                var nearP = DeserializeJointParams(data.JointNearParams);
                tw = nearP.TryGetValue("tenonWidth", out var n2) ? n2 : 1.5;
            }
            ObjectId fillId = Module1.DrawElement(pts, tw, "Tenon", label, "", "",
                yAngle, zAngle, ctx.StartX, ctx.StartY, z);
            _edBN?.WriteMessage("\nFillBraceNear: fillId=" + (fillId.IsNull ? "null" : fillId.Handle.ToString())
                + " receiver=" + receiverId.Handle.ToString());
            if (!fillId.IsNull) {
                try   { Module1.AddJoint(receiverId, fillId, Module1.Joint.Fill); _edBN?.WriteMessage(" AddJoint ok"); }
                catch (System.Exception ex) { _edBN?.WriteMessage(" AddJoint FAIL: " + ex.Message); }
                Module1.EraseEntity(fillId);
                // Force viewport refresh in a separate transaction AFTER the BoolUnite committed.
                // RecordGraphicsModified inside the BoolUnite transaction causes it not to persist.
                try {
                    var _db2 = Autodesk.AutoCAD.DatabaseServices.HostApplicationServices.WorkingDatabase;
                    using var trR = _db2.TransactionManager.StartTransaction();
                    var rcvrRefresh = (Autodesk.AutoCAD.DatabaseServices.Entity)trR.GetObject(
                        receiverId, Autodesk.AutoCAD.DatabaseServices.OpenMode.ForWrite);
                    rcvrRefresh.RecordGraphicsModified(true);
                    trR.Commit();
                } catch { }
            }
        }

        // Fill the Far=girt receiver when FarJointType transitions Tenon->Butt.
        // SwapEnds=false: girt=TenonUp   -> use pt4 geometry.
        // SwapEnds=true : girt=TenonDown -> use pt0 geometry.
        private static void FillObliqueBraceFar(
            BentBraceContext ctx, Module1.DataStructure data,
            string newJf, Module1.Connection[] savedConns)
        {
            if (!Module1.Make3D) return;
            if (!string.Equals(newJf, "Butt", StringComparison.OrdinalIgnoreCase)) return;
            if (string.Equals(data.JointFar, "Butt", StringComparison.OrdinalIgnoreCase)) return;

            Handle receiverHandle = default;
            if (savedConns != null)
                foreach (var sc in savedConns)
                    if (sc.ThisJoint == Module1.JointType.Tenon && sc.ThisEnd == Module1.End.Far)
                    { receiverHandle = sc.ConnHandle; break; }
            if (receiverHandle == default) return;
            receiverHandle = ResolveHandle(receiverHandle);
            ObjectId receiverId = Module1.GetObjectIdFromHandle(receiverHandle);
            if (receiverId.IsNull || receiverId.IsErased) return;

            BraceZ(ctx, data.Width, out double z, out double zAngle, out double yAngle, out double tenonZ);
            const double sinBeta = 0.7071067811865476;

            Point3dCollection pts = new();
            string label; double tw;
            if (!ctx.SwapEnds) {
                // FarJointType controls TenonUp (pt4 = girt end for this brace)
                var pt4 = new Point3d(ctx.StartX + ctx.Length - ctx.Depth / sinBeta, ctx.StartY, z);
                pts.Add(Module1.AtPoint(pt4, 0, 0, tenonZ * 2));
                pts.Add(Module1.AtPoint(pts[0], 4, 4, 0));
                pts.Add(Module1.AtPoint(pts[0], ctx.Depth / sinBeta, 4, 0));
                pts.Add(Module1.AtPoint(pts[0], ctx.Depth / sinBeta, 0, 0));
                label = "Up";
                var farP = DeserializeJointParams(data.JointFarParams);
                tw = farP.TryGetValue("tenonWidth", out var f1) ? f1 : 1.5;
            } else {
                // FarJointType controls TenonDown (pt0 = girt end for this brace)
                var pt0 = new Point3d(ctx.StartX, ctx.StartY - ctx.Length, z);
                pts.Add(Module1.AtPoint(pt0, 0, 0, tenonZ));
                pts.Add(Module1.AtPoint(pts[0], 0, ctx.Depth / sinBeta, 0));
                pts.Add(Module1.AtPoint(pts[1], -4, -4, 0));
                pts.Add(Module1.AtPoint(pts[0], -4, 0, 0));
                label = "Down";
                var farP = DeserializeJointParams(data.JointFarParams);
                tw = farP.TryGetValue("tenonWidth", out var f2) ? f2 : 1.5;
            }
            ObjectId fillId = Module1.DrawElement(pts, tw, "Tenon", label, "", "",
                yAngle, zAngle, ctx.StartX, ctx.StartY, z);
            var _edBF = Autodesk.AutoCAD.ApplicationServices.Application
                .DocumentManager.MdiActiveDocument?.Editor;
            _edBF?.WriteMessage("\nFillBraceFar: fillId=" + (fillId.IsNull ? "null" : fillId.Handle.ToString())
                + " receiver=" + receiverId.Handle.ToString() + " SwapEnds=" + ctx.SwapEnds);
            if (!fillId.IsNull) {
                try   { Module1.AddJoint(receiverId, fillId, Module1.Joint.Fill); _edBF?.WriteMessage(" ok"); }
                catch (System.Exception ex) { _edBF?.WriteMessage(" FAIL: " + ex.Message); }
                Module1.EraseEntity(fillId);
                try {
                    var _db3 = Autodesk.AutoCAD.DatabaseServices.HostApplicationServices.WorkingDatabase;
                    using var trR2 = _db3.TransactionManager.StartTransaction();
                    var rcvrRefresh2 = (Autodesk.AutoCAD.DatabaseServices.Entity)trR2.GetObject(
                        receiverId, Autodesk.AutoCAD.DatabaseServices.OpenMode.ForWrite);
                    rcvrRefresh2.RecordGraphicsModified(true);
                    trR2.Commit();
                } catch { }
            }
        }

        private static void RegenerateBentBrace(ObjectId timberId, Module1.DataStructure data,
            string contextJson, double w, double d, string jn, string jf,
            Module1.Connection[] savedConns = null)
        {
            BentBraceContext ctx = ParseBentBraceContext(contextJson);
            // Set globals BEFORE fills so geometry uses correct Make3D/OffsetType.
            Module1.Make3D     = ctx.Make3D;
            Module1.OffsetType = ctx.OffsetType;
            // Fill oblique voids before erasing (both tenons are erased by AddMortise).
            // Near=post, Far=girt in all cases (SwapEnds governs which pts formula applies).
            FillObliqueBraceNear(ctx, data, jn, savedConns);
            FillObliqueBraceFar(ctx, data, jf, savedConns);
            EraseAndMarkStale(timberId);

            var bb = new BentBrace
            {
                Width         = w,
                Depth         = d,
                Length        = ctx.Length,
                postWidth     = ctx.PostWidth,
                ZAngle        = ctx.ZAngle,
                YAngle        = ctx.YAngle,
                XAngle        = ctx.XAngle,
                StartPoint    = new Point3d(ctx.StartX, ctx.StartY, 0),
                SwapEnds      = ctx.SwapEnds,
                NearJointType = string.IsNullOrEmpty(jn) ? "Tenon" : jn,
                FarJointType  = string.IsNullOrEmpty(jf) ? "Tenon" : jf,
                NearParams    = DeserializeJointParams(data.JointNearParams),
                FarParams     = DeserializeJointParams(data.JointFarParams)
            };
            bb.Draw();
            ApplyJointTypes(bb.TimberId, jn, jf, bb.TenonDown, bb.TenonUp);
            // Rebuild Connections xrecord. Near=post receiver, Far=girt receiver.
            // SwapEnds=false: post=TenonDown, girt=TenonUp (normal RebuildKPStrutConns order).
            // SwapEnds=true:  post=TenonUp,   girt=TenonDown (swap the two tenon args).
            if (!ctx.SwapEnds)
                RebuildKPStrutConns(bb.TimberId, savedConns, bb.TenonDown, bb.TenonUp);
            else
                RebuildKPStrutConns(bb.TimberId, savedConns, bb.TenonUp, bb.TenonDown);
        }

        // -----------------------------------------------------------------------
        // BayBrace
        // -----------------------------------------------------------------------
        private static void RegenerateBayBrace(ObjectId timberId, Module1.DataStructure data,
            string contextJson, double w, double d, string jn, string jf)
        {
            BayBraceContext ctx = ParseBayBraceContext(contextJson);
            EraseAndMarkStale(timberId);

            Module1.Make3D = ctx.Make3D;

            var bb = new BayBrace
            {
                Width       = w,
                Depth       = d,
                Length      = ctx.Length,
                Peg1Length  = ctx.Peg1Length,
                Peg1Z       = ctx.Peg1Z,
                Peg2Length  = ctx.Peg2Length,
                Peg2Z       = ctx.Peg2Z,
                ZAngle      = ctx.ZAngle,
                YAngle      = ctx.YAngle,
                XAngle      = ctx.XAngle,
                Designation = ctx.Designation,
                StartPoint  = new Point3d(ctx.StartX, ctx.StartY, ctx.StartZ)
            };
            bb.Draw();
            ApplyJointTypes(bb.Timber, jn, jf, bb.TenonUp, bb.TenonDown);
        }

        // -----------------------------------------------------------------------
        // Context structs and parsers
        // -----------------------------------------------------------------------
        private struct BentGirtContext
        {
            public double StartX, StartY, StartZ;
            public double Span, BOG;
            public bool Make3D;
            public double PostDepth;
        }
        private static BentGirtContext ParseBentGirtContext(string json)
        {
            AssertHasContext(json, "BentGirt");
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement r = doc.RootElement;
            return new BentGirtContext {
                StartX      = D(r, "startX"), StartY = D(r, "startY"), StartZ = D(r, "startZ"),
                Span        = D(r, "span"),   BOG    = D(r, "bog"),
                Make3D      = B(r, "make3D"),
                PostDepth   = D(r, "postDepth")
            };
        }

        private struct FloorBentGirtContext
        {
            public double StartX, StartY, StartZ;
            public double LocalStartY, Span;
            public bool Make3D;
            public double PostDepth;
        }
        private static FloorBentGirtContext ParseFloorBentGirtContext(string json)
        {
            AssertHasContext(json, "FloorBentGirt");
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement r = doc.RootElement;
            return new FloorBentGirtContext {
                StartX      = D(r, "startX"), StartY = D(r, "startY"), StartZ = D(r, "startZ"),
                LocalStartY = D(r, "localStartY"), Span = D(r, "span"),
                Make3D      = B(r, "make3D"),
                PostDepth   = D(r, "postDepth")
            };
        }

        private struct PostContext
        {
            public double StartX, StartY, StartZ;
            public double Span, EaveHt, Pitch, BOG, TOG, TOH;
            public bool Make3D, HasFlrGirt;
            public double FlrGirtHt, FlrGirtDepth;
        }
        private static PostContext ParsePostContext(string json)
        {
            AssertHasContext(json, "Post");
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement r = doc.RootElement;
            return new PostContext {
                StartX       = D(r, "startX"), StartY = D(r, "startY"), StartZ = D(r, "startZ"),
                Span         = D(r, "span"),
                EaveHt       = D(r, "eaveHt"),  Pitch  = D(r, "pitch"),
                BOG          = D(r, "bog"),      TOG   = D(r, "tog"),    TOH    = D(r, "toh"),
                Make3D       = B(r, "make3D"),
                HasFlrGirt   = B(r, "hasFlrGirt"),
                FlrGirtHt    = D(r, "flrGirtHt"), FlrGirtDepth = D(r, "flrGirtDepth")
            };
        }

        private struct KPostContext
        {
            public double StartX, StartY, StartZ;
            public double Span, EaveHt, Pitch, Beta, TOG, TOH;
            public bool Make3D;
            public double PostDepth, KpostRafterSitDepth;
        }
        private static KPostContext ParseKPostContext(string json)
        {
            AssertHasContext(json, "KPost");
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement r = doc.RootElement;
            return new KPostContext {
                StartX              = D(r, "startX"), StartY = D(r, "startY"), StartZ = D(r, "startZ"),
                Span                = D(r, "span"),   EaveHt = D(r, "eaveHt"), Pitch  = D(r, "pitch"),
                Beta                = D(r, "beta"),   TOG    = D(r, "tog"),    TOH    = D(r, "toh"),
                Make3D              = B(r, "make3D"),
                PostDepth           = D(r, "postDepth"),
                KpostRafterSitDepth = D(r, "kpostRafterSitDepth")
            };
        }

        // -----------------------------------------------------------------------
        // KPStrut (shared by all 4 strut classes; unused fields default to 0)
        // -----------------------------------------------------------------------
        private struct KPStrutContext
        {
            public double StartX, StartY;
            public double KPostWidth, KPostDepth, PostDepth, Span, KpostRafterSitDepth;
            public bool Make3D;
            public int OffsetType;
            public double TOH, Pitch, Beta, Prun, Prise;
        }
        private static KPStrutContext ParseKPStrutContext(string json)
        {
            AssertHasContext(json, "KPStrut");
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement r = doc.RootElement;
            return new KPStrutContext {
                StartX = D(r, "startX"), StartY = D(r, "startY"),
                KPostWidth = D(r, "kPostWidth"), KPostDepth = D(r, "kPostDepth"),
                PostDepth  = D(r, "postDepth"),  Span = D(r, "span"),
                KpostRafterSitDepth = D(r, "kpostRafterSitDepth"),
                Make3D    = B(r, "make3D"),
                OffsetType = (int)D(r, "offsetType"),
                TOH   = D(r, "toh"),   Pitch = D(r, "pitch"),
                Beta  = D(r, "beta"),  Prun  = D(r, "prun"), Prise = D(r, "prise")
            };
        }

        // -----------------------------------------------------------------------
        // KPRafter (shared by Left and Right)
        // -----------------------------------------------------------------------
        private struct KPRafterContext
        {
            public double StartX, StartY, StartZ;
            public double KPostDepth, PostDepth, KpostRafterSitDepth;
            public double Span, EaveHt, Pitch, Beta, TOH;
            public bool Make3D;
        }
        private static KPRafterContext ParseKPRafterContext(string json)
        {
            // Accepts class="KPRafterLeft" or class="KPRafterRight" -- fields identical
            AssertHasContext(json, "KPRafter");
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement r = doc.RootElement;
            return new KPRafterContext {
                StartX              = D(r, "startX"),     StartY = D(r, "startY"), StartZ = D(r, "startZ"),
                KPostDepth          = D(r, "kPostDepth"),
                PostDepth           = D(r, "postDepth"),
                KpostRafterSitDepth = D(r, "kpostRafterSitDepth"),
                Span   = D(r, "span"),   EaveHt = D(r, "eaveHt"),
                Pitch  = D(r, "pitch"),  Beta   = D(r, "beta"),   TOH = D(r, "toh"),
                Make3D = B(r, "make3D")
            };
        }

        // -----------------------------------------------------------------------
        // BentBrace / BayBrace context
        // -----------------------------------------------------------------------
        private struct BentBraceContext
        {
            public double StartX, StartY;
            public double Width, Depth, Length, PostWidth;
            public double ZAngle, YAngle, XAngle;
            public bool Make3D;
            public int OffsetType;
            public bool SwapEnds;  // true when NearJointType=TenonUp(post) and FarJointType=TenonDown(girt)
        }
        private static BentBraceContext ParseBentBraceContext(string json)
        {
            AssertHasContext(json, "BentBrace");
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement r = doc.RootElement;
            return new BentBraceContext {
                StartX    = D(r, "startX"),    StartY    = D(r, "startY"),
                Width     = D(r, "width"),     Depth     = D(r, "depth"),
                Length    = D(r, "length"),    PostWidth = D(r, "postWidth"),
                ZAngle    = D(r, "zAngle"),    YAngle    = D(r, "yAngle"),
                XAngle    = D(r, "xAngle"),
                Make3D    = B(r, "make3D"),
                OffsetType = (int)D(r, "offsetType"),
                SwapEnds  = B(r, "swapEnds")
            };
        }

        private struct BayBraceContext
        {
            public double StartX, StartY, StartZ;
            public double Width, Depth, Length;
            public double Peg1Length, Peg1Z, Peg2Length, Peg2Z;
            public double ZAngle, YAngle, XAngle;
            public string Designation;
            public bool Make3D;
        }
        private static BayBraceContext ParseBayBraceContext(string json)
        {
            AssertHasContext(json, "BayBrace");
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement r = doc.RootElement;
            string desig = r.TryGetProperty("designation", out JsonElement de)
                ? (de.GetString() ?? "") : "";
            return new BayBraceContext {
                StartX     = D(r, "startX"),    StartY = D(r, "startY"), StartZ = D(r, "startZ"),
                Width      = D(r, "width"),     Depth  = D(r, "depth"),  Length = D(r, "length"),
                Peg1Length = D(r, "peg1Length"), Peg1Z = D(r, "peg1Z"),
                Peg2Length = D(r, "peg2Length"), Peg2Z = D(r, "peg2Z"),
                ZAngle     = D(r, "zAngle"),    YAngle = D(r, "yAngle"), XAngle = D(r, "xAngle"),
                Designation = desig,
                Make3D     = B(r, "make3D")
            };
        }

        // -----------------------------------------------------------------------
        // Qpost members
        // -----------------------------------------------------------------------
        private static void RegenerateQPPostLeft(ObjectId timberId, Module1.DataStructure data, string j, double w, double d, string jn, string jf, Module1.Connection[] savedConns)
        {
            var ctx = ParseQPPostContext(j); EraseAndMarkStale(timberId);
            Module1.Span = ctx.Span; Module1.Make3D = ctx.Make3D; Module1.OffsetType = ctx.OffsetType;
            Module1.TOG = ctx.TOG; Module1.TOH = ctx.TOH; Module1.Pitch = ctx.Pitch; Module1.Beta = ctx.Beta;
            var s = new QPPostLeft { Width=w, Depth=d, GirtWidth=ctx.GirtWidth, postDepth=ctx.PostDepth,
                StartPoint=new Point3d(ctx.StartX, ctx.StartY, 0),
                Type=data.Type, BentNumber=data.BentNumber, Designation=data.Designation,
                NearJointType = string.IsNullOrEmpty(jn) ? "Tenon" : jn,
                NearParams    = DeserializeJointParams(data.JointNearParams),
                FarParams     = DeserializeJointParams(data.JointFarParams) };
            s.Draw(); RebuildConnectionsEmpty(s.TimberId, savedConns);
            ApplyJointTypes(s.TimberId, jn, jf, s.TenonDownId, s.TenonUpId);
        }

        private static void RegenerateQPPostRight(ObjectId timberId, Module1.DataStructure data, string j, double w, double d, string jn, string jf, Module1.Connection[] savedConns)
        {
            var ctx = ParseQPPostContext(j); EraseAndMarkStale(timberId);
            Module1.Span = ctx.Span; Module1.Make3D = ctx.Make3D; Module1.OffsetType = ctx.OffsetType;
            Module1.TOG = ctx.TOG; Module1.TOH = ctx.TOH; Module1.Pitch = ctx.Pitch; Module1.Beta = ctx.Beta;
            var s = new QPPostRight { Width=w, Depth=d, GirtWidth=ctx.GirtWidth, postDepth=ctx.PostDepth,
                StartPoint=new Point3d(ctx.StartX, ctx.StartY, 0),
                Type=data.Type, BentNumber=data.BentNumber, Designation=data.Designation,
                NearJointType = string.IsNullOrEmpty(jn) ? "Tenon" : jn,
                NearParams    = DeserializeJointParams(data.JointNearParams),
                FarParams     = DeserializeJointParams(data.JointFarParams) };
            s.Draw(); RebuildConnectionsEmpty(s.TimberId, savedConns);
            ApplyJointTypes(s.TimberId, jn, jf, s.TenonDownId, s.TenonUpId);
        }

        private static void RegenerateQPRafterLeft(ObjectId timberId, Module1.DataStructure data, string j, double w, double d, string jn, string jf, Module1.Connection[] savedConns)
        {
            var ctx = ParseQPRafterContext(j); EraseAndMarkStale(timberId);
            Module1.Span = ctx.Span; Module1.TOH = ctx.TOH; Module1.Pitch = ctx.Pitch;
            Module1.Beta = ctx.Beta; Module1.EaveHt = ctx.EaveHt; Module1.Make3D = ctx.Make3D;
            Module1.PlumbLength = d / Math.Cos(ctx.Beta);
            var s = new QPRafterLeft {
                Width=w, Depth=d, postDepth=ctx.PostDepth,
                StartPoint=new Point3d(ctx.StartX, ctx.StartY, ctx.StartZ),
                Type=data.Type, BentNumber=data.BentNumber, Designation=data.Designation,
                NearJointType = string.IsNullOrEmpty(jn) ? "Butt"   : jn,
                FarJointType  = string.IsNullOrEmpty(jf) ? "Tenon"  : jf,
                NearParams    = DeserializeJointParams(data.JointNearParams),
                FarParams     = DeserializeJointParams(data.JointFarParams) };
            s.Draw();
            RebuildConnectionsEmpty(s.TimberId, savedConns);
            BentNetwork.UpdateReceiverHandle(timberId.Handle, s.TimberId.Handle);
            ApplyJointTypes(s.TimberId, jn, jf, farTenonId: s.Tenon);
        }

        private static void RegenerateQPRafterRight(ObjectId timberId, Module1.DataStructure data, string j, double w, double d, string jn, string jf, Module1.Connection[] savedConns)
        {
            var ctx = ParseQPRafterContext(j); EraseAndMarkStale(timberId);
            Module1.Span = ctx.Span; Module1.TOH = ctx.TOH; Module1.Pitch = ctx.Pitch;
            Module1.Beta = ctx.Beta; Module1.EaveHt = ctx.EaveHt; Module1.Make3D = ctx.Make3D;
            Module1.PlumbLength = d / Math.Cos(ctx.Beta);
            var s = new QPRafterRight {
                Width=w, Depth=d, postDepth=ctx.PostDepth, sitDepth=ctx.SitDepth,
                StartPoint=new Point3d(ctx.StartX, ctx.StartY, ctx.StartZ),
                Type=data.Type, BentNumber=data.BentNumber, Designation=data.Designation,
                NearJointType = string.IsNullOrEmpty(jn) ? "Tenon"   : jn,
                FarJointType  = string.IsNullOrEmpty(jf) ? "Polygon" : jf,
                NearParams    = DeserializeJointParams(data.JointNearParams),
                FarParams     = DeserializeJointParams(data.JointFarParams) };
            s.Draw();
            RebuildConnectionsEmpty(s.TimberId, savedConns);
            BentNetwork.UpdateReceiverHandle(timberId.Handle, s.TimberId.Handle);
            ApplyJointTypes(s.TimberId, jn, jf, nearTenonId: s.Tenon);
        }

        private static void RegenerateQPStrutLeft(ObjectId timberId, Module1.DataStructure data, string j, double w, double d, string jn, string jf, Module1.Connection[] savedConns)
        {
            var ctx = ParseQPStrutContext(j); EraseAndMarkStale(timberId);
            Module1.Span = ctx.Span; Module1.Make3D = ctx.Make3D; Module1.OffsetType = ctx.OffsetType;
            Module1.TOH = ctx.TOH; Module1.Pitch = ctx.Pitch; Module1.Beta = ctx.Beta; Module1.B = ctx.B;
            var s = new QPStrutLeft { Width=w, Depth=d, QPRafterWidth=ctx.QPRafterWidth, postDepth=ctx.PostDepth,
                StartPoint=new Point3d(ctx.StartX, ctx.StartY, 0),
                Type=data.Type, BentNumber=data.BentNumber, Designation=data.Designation };
            s.Draw(); RebuildConnectionsEmpty(s.TimberId, savedConns);
            ApplyJointTypes(s.TimberId, jn, jf, s.TenonDown, s.TenonUp);
        }

        private static void RegenerateQPStrutRight(ObjectId timberId, Module1.DataStructure data, string j, double w, double d, string jn, string jf, Module1.Connection[] savedConns)
        {
            var ctx = ParseQPStrutContext(j); EraseAndMarkStale(timberId);
            Module1.Span = ctx.Span; Module1.Make3D = ctx.Make3D; Module1.OffsetType = ctx.OffsetType;
            Module1.TOH = ctx.TOH; Module1.Pitch = ctx.Pitch; Module1.Beta = ctx.Beta; Module1.B = ctx.B;
            var s = new QPStrutRight { Width=w, Depth=d, QPRafterWidth=ctx.QPRafterWidth,
                StartPoint=new Point3d(ctx.StartX, ctx.StartY, 0),
                Type=data.Type, BentNumber=data.BentNumber, Designation=data.Designation };
            s.Draw(); RebuildConnectionsEmpty(s.TimberId, savedConns);
            ApplyJointTypes(s.TimberId, jn, jf, s.TenonDown, s.TenonUp);
        }

        private static void RegenerateQPUpperGirt(ObjectId timberId, Module1.DataStructure data, string j, double w, double d, string jn, string jf, Module1.Connection[] savedConns)
        {
            var ctx = ParseQPUpperGirtContext(j); EraseAndMarkStale(timberId);
            Module1.Span = ctx.Span; Module1.Make3D = ctx.Make3D; Module1.OffsetType = ctx.OffsetType;
            Module1.TOH = ctx.TOH; Module1.Pitch = ctx.Pitch;
            var s = new QPUpperGirt { Width=w, Depth=d, QPRafterWidth=ctx.QPRafterWidth,
                QPQpostDepth=ctx.QPQpostDepth, postDepth=ctx.PostDepth,
                StartPoint=new Point3d(ctx.StartX, ctx.StartY, 0),
                Type=data.Type, BentNumber=data.BentNumber, Designation=data.Designation };
            s.Draw(); RebuildConnectionsEmpty(s.TimberId, savedConns);
            ApplyJointTypes(s.TimberId, jn, jf, s.TenonLeft, s.TenonRight);
        }

        // -----------------------------------------------------------------------
        // Hbeam members
        // -----------------------------------------------------------------------
        private static void RegenerateHBeamLeft(ObjectId timberId, Module1.DataStructure data, string j, double w, double d, string jn, string jf, Module1.Connection[] savedConns)
        {
            var ctx = ParseHBeamContext(j); EraseAndMarkStale(timberId);
            Module1.Make3D = ctx.Make3D;
            var s = new HBeamLeft { Width=w, Depth=d, hbLength=ctx.HbLength,
                postWidth=ctx.PostWidth, KpostDepth=ctx.KpostDepth, HBDivisor=ctx.HBDivisor,
                StartPoint=new Point3d(ctx.StartX, ctx.StartY, ctx.StartZ),
                Type=data.Type, BentNumber=data.BentNumber, Designation=data.Designation };
            s.Draw(); RebuildConnectionsEmpty(s.TimberId, savedConns);
            ApplyJointTypes(s.TimberId, jn, jf, farTenonId: s.Tenon);
        }

        private static void RegenerateHBeamRight(ObjectId timberId, Module1.DataStructure data, string j, double w, double d, string jn, string jf, Module1.Connection[] savedConns)
        {
            var ctx = ParseHBeamContext(j); EraseAndMarkStale(timberId);
            Module1.Make3D = ctx.Make3D;
            var s = new HBeamRight { Width=w, Depth=d, hbLength=ctx.HbLength,
                postWidth=ctx.PostWidth, KpostDepth=ctx.KpostDepth, HBDivisor=ctx.HBDivisor,
                StartPoint=new Point3d(ctx.StartX, ctx.StartY, ctx.StartZ),
                Type=data.Type, BentNumber=data.BentNumber, Designation=data.Designation };
            s.Draw(); RebuildConnectionsEmpty(s.TimberID, savedConns);
            ApplyJointTypes(s.TimberID, jn, jf, farTenonId: s.Tenon);
        }

        private static void RegenerateHBGirt(ObjectId timberId, Module1.DataStructure data, string j, double w, double d, string jn, string jf, Module1.Connection[] savedConns)
        {
            var ctx = ParseHBGirtContext(j); EraseAndMarkStale(timberId);
            Module1.Span = ctx.Span; Module1.Pitch = ctx.Pitch; Module1.Make3D = ctx.Make3D;
            var s = new HBGirt { Width=w, Depth=d, postDepth=ctx.PostDepth,
                postWidth=ctx.PostWidth, KpostDepth=ctx.KpostDepth, HBDivisor=ctx.HBDivisor,
                StartPoint=new Point3d(ctx.StartX, ctx.StartY, ctx.StartZ),
                Type=data.Type, BentNumber=data.BentNumber, Designation=data.Designation };
            s.Draw(); RebuildConnectionsEmpty(s.Timber, savedConns);
            ApplyJointTypes(s.Timber, jn, jf, s.TenonLeft, s.TenonRight);
        }

        private static void RegenerateHBKpost(ObjectId timberId, Module1.DataStructure data, string j, double w, double d, string jn, string jf, Module1.Connection[] savedConns)
        {
            var ctx = ParseHBKpostContext(j); EraseAndMarkStale(timberId);
            Module1.Span = ctx.Span; Module1.EaveHt = ctx.EaveHt; Module1.Pitch = ctx.Pitch;
            Module1.Beta = ctx.Beta; Module1.TOH = ctx.TOH; Module1.TOG = ctx.TOG; Module1.Make3D = ctx.Make3D;
            var s = new HBKpost { Width=w, Depth=d, postDepth=ctx.PostDepth,
                KpostRafterSeatDepth=ctx.KpostRafterSeatDepth, HBDivisor=ctx.HBDivisor,
                StartPoint=new Point3d(ctx.StartX, ctx.StartY, ctx.StartZ),
                Type=data.Type, BentNumber=data.BentNumber, Designation=data.Designation,
                NearParams=DeserializeJointParams(data.JointNearParams),
                FarParams=DeserializeJointParams(data.JointFarParams) };
            s.Draw(); RebuildConnectionsEmpty(s.TimberId, savedConns);
            ApplyJointTypes(s.TimberId, jn, jf, nearTenonId: s.Tenon);
        }

        private static void RegenerateHPostLeft(ObjectId timberId, Module1.DataStructure data, string j, double w, double d, string jn, string jf, Module1.Connection[] savedConns)
        {
            var ctx = ParseHPostContext(j); EraseAndMarkStale(timberId);
            Module1.Span = ctx.Span; Module1.Pitch = ctx.Pitch; Module1.Beta = ctx.Beta; Module1.Make3D = ctx.Make3D;
            var s = new HPostLeft { Width=w, Depth=d, postDepth=ctx.PostDepth,
                postWidth=ctx.PostWidth, KpostDepth=ctx.KpostDepth, RafterWidth=ctx.RafterWidth, HBDivisor=ctx.HBDivisor,
                StartPoint=new Point3d(ctx.StartX, ctx.StartY, ctx.StartZ),
                Type=data.Type, BentNumber=data.BentNumber, Designation=data.Designation };
            s.Draw(); RebuildConnectionsEmpty(s.TimberId, savedConns);
            ApplyJointTypes(s.TimberId, jn, jf, s.TenonDown, s.TenonUp);
        }

        private static void RegenerateHPostRight(ObjectId timberId, Module1.DataStructure data, string j, double w, double d, string jn, string jf, Module1.Connection[] savedConns)
        {
            var ctx = ParseHPostContext(j); EraseAndMarkStale(timberId);
            Module1.Span = ctx.Span; Module1.Pitch = ctx.Pitch; Module1.Beta = ctx.Beta; Module1.Make3D = ctx.Make3D;
            var s = new HPostRight { Width=w, Depth=d, postDepth=ctx.PostDepth,
                postWidth=ctx.PostWidth, KpostDepth=ctx.KpostDepth, RafterWidth=ctx.RafterWidth, HBDivisor=ctx.HBDivisor,
                StartPoint=new Point3d(ctx.StartX, ctx.StartY, ctx.StartZ),
                Type=data.Type, BentNumber=data.BentNumber, Designation=data.Designation };
            s.Draw(); RebuildConnectionsEmpty(s.TimberId, savedConns);
            ApplyJointTypes(s.TimberId, jn, jf, s.TenonDown, s.TenonUp);
        }

        private static void RegenerateHBBayGirt(ObjectId timberId, Module1.DataStructure data, string j, double w, double d, string jn, string jf)
        {
            var ctx = ParseHBBayGirtContext(j); EraseAndMarkStale(timberId);
            Module1.Make3D = ctx.Make3D;
            var s = new HBBayGirt { Width=w, Depth=d, Baywidth=ctx.Baywidth,
                HPostWidth=ctx.HPostWidth, HPostDepth=ctx.HPostDepth,
                Startpoint=new Point3d(ctx.StartX, ctx.StartY, 0),
                Type=data.Type, BentNumber=data.BentNumber, Designation=data.Designation };
            s.Draw((HBBayGirt.Sides)ctx.Side); ApplyJointTypes(s.Timber, jn, jf, s.TenonLeft, s.TenonRight);
        }

        // -----------------------------------------------------------------------
        // KpostTruss members
        // -----------------------------------------------------------------------
        private static void RegenerateKPTPost(ObjectId timberId, Module1.DataStructure data, string j, double w, double d, string jn, string jf)
        {
            var ctx = ParseKPTPostContext(j); EraseAndMarkStale(timberId);
            Module1.Span = ctx.Span; Module1.EaveHt = ctx.EaveHt; Module1.Pitch = ctx.Pitch;
            Module1.Beta = ctx.Beta; Module1.Make3D = ctx.Make3D;
            var s = new KPTPost { Width=w, Depth=d, KPostWidth=ctx.KPostWidth, KPostDepth=ctx.KPostDepth,
                KPRafterDepth=ctx.KPRafterDepth, postWidth=ctx.PostWidth,
                StartPoint=new Point3d(ctx.StartX, ctx.StartY, ctx.StartZ),
                Type=data.Type, BentNumber=data.BentNumber, Designation=data.Designation };
            s.Draw(); ApplyJointTypes(s.Timber, jn, jf, farTenonId: s.Tenon);
        }

        private static void RegenerateKPTRafterLeft(ObjectId timberId, Module1.DataStructure data, string j, double w, double d, string jn, string jf)
        {
            var ctx = ParseKPTRafterContext(j); EraseAndMarkStale(timberId);
            Module1.Span = ctx.Span; Module1.EaveHt = ctx.EaveHt; Module1.Pitch = ctx.Pitch;
            Module1.Beta = ctx.Beta; Module1.Make3D = ctx.Make3D;
            var s = new KPTRafterLeft { Width=w, Depth=d, KPostDepth=ctx.KPostDepth,
                postDepth=ctx.PostDepth, KpostRafterSitDepth=ctx.KpostRafterSitDepth,
                StartPoint=new Point3d(ctx.StartX, ctx.StartY, ctx.StartZ),
                Type=data.Type, BentNumber=data.BentNumber, Designation=data.Designation };
            s.Draw(); ApplyJointTypes(s.Timber, jn, jf, farTenonId: s.Tenon);
        }

        private static void RegenerateKPTRafterRight(ObjectId timberId, Module1.DataStructure data, string j, double w, double d, string jn, string jf)
        {
            var ctx = ParseKPTRafterContext(j); EraseAndMarkStale(timberId);
            Module1.Span = ctx.Span; Module1.EaveHt = ctx.EaveHt; Module1.Pitch = ctx.Pitch;
            Module1.Beta = ctx.Beta; Module1.Make3D = ctx.Make3D;
            var s = new KPTRafterRight { Width=w, Depth=d, KPostDepth=ctx.KPostDepth,
                postDepth=ctx.PostDepth, KpostRafterSitDepth=ctx.KpostRafterSitDepth,
                StartPoint=new Point3d(ctx.StartX, ctx.StartY, ctx.StartZ),
                Type=data.Type, BentNumber=data.BentNumber, Designation=data.Designation };
            s.Draw(); ApplyJointTypes(s.Timber, jn, jf, farTenonId: s.Tenon);
        }

        private static void RegenerateKPTStrutLeft(ObjectId timberId, Module1.DataStructure data, string j, double w, double d, string jn, string jf)
        {
            var ctx = ParseKPTStrutContext(j); EraseAndMarkStale(timberId);
            Module1.Span = ctx.Span; Module1.EaveHt = ctx.EaveHt; Module1.Pitch = ctx.Pitch;
            Module1.Beta = ctx.Beta; Module1.Make3D = ctx.Make3D; Module1.OffsetType = ctx.OffsetType;
            var s = new KPTStrutLeft { Width=w, Depth=d, KPostWidth=ctx.KPostWidth, KPostDepth=ctx.KPostDepth,
                KPRafterDepth=ctx.KPRafterDepth, postWidth=ctx.PostWidth,
                StartPoint=new Point3d(ctx.StartX, ctx.StartY, 0),
                Type=data.Type, BentNumber=data.BentNumber, Designation=data.Designation };
            s.Draw(); ApplyJointTypes(s.Timber, jn, jf, s.TenonDown, s.TenonUp);
        }

        private static void RegenerateKPTStrutRight(ObjectId timberId, Module1.DataStructure data, string j, double w, double d, string jn, string jf)
        {
            var ctx = ParseKPTStrutContext(j); EraseAndMarkStale(timberId);
            Module1.Span = ctx.Span; Module1.EaveHt = ctx.EaveHt; Module1.Pitch = ctx.Pitch;
            Module1.Beta = ctx.Beta; Module1.Make3D = ctx.Make3D; Module1.OffsetType = ctx.OffsetType;
            var s = new KPTStrutRight { Width=w, Depth=d, KPostWidth=ctx.KPostWidth, KPostDepth=ctx.KPostDepth,
                KPRafterDepth=ctx.KPRafterDepth, postWidth=ctx.PostWidth,
                StartPoint=new Point3d(ctx.StartX, ctx.StartY, 0),
                Type=data.Type, BentNumber=data.BentNumber, Designation=data.Designation };
            s.Draw(); ApplyJointTypes(s.Timber, jn, jf, s.TenonDown, s.TenonUp);
        }

        private static void RegenerateKPTVertStrutLeft(ObjectId timberId, Module1.DataStructure data, string j, double w, double d, string jn, string jf)
        {
            var ctx = ParseKPTStrutContext(j); EraseAndMarkStale(timberId);
            Module1.Span = ctx.Span; Module1.EaveHt = ctx.EaveHt; Module1.Pitch = ctx.Pitch;
            Module1.Beta = ctx.Beta; Module1.Make3D = ctx.Make3D; Module1.OffsetType = ctx.OffsetType;
            var s = new KPTVertStrutLeft { Width=w, Depth=d, KPostWidth=ctx.KPostWidth, KPostDepth=ctx.KPostDepth,
                KPRafterDepth=ctx.KPRafterDepth, postWidth=ctx.PostWidth,
                StartPoint=new Point3d(ctx.StartX, ctx.StartY, 0),
                Type=data.Type, BentNumber=data.BentNumber, Designation=data.Designation };
            s.Draw(); ApplyJointTypes(s.Timber, jn, jf, s.TenonDown, s.TenonUp);
        }

        private static void RegenerateKPTVertStrutRight(ObjectId timberId, Module1.DataStructure data, string j, double w, double d, string jn, string jf)
        {
            var ctx = ParseKPTStrutContext(j); EraseAndMarkStale(timberId);
            Module1.Span = ctx.Span; Module1.EaveHt = ctx.EaveHt; Module1.Pitch = ctx.Pitch;
            Module1.Beta = ctx.Beta; Module1.Make3D = ctx.Make3D; Module1.OffsetType = ctx.OffsetType;
            var s = new KPTVertStrutRight { Width=w, Depth=d, KPostWidth=ctx.KPostWidth, KPostDepth=ctx.KPostDepth,
                KPRafterDepth=ctx.KPRafterDepth, postWidth=ctx.PostWidth,
                StartPoint=new Point3d(ctx.StartX, ctx.StartY, 0),
                Type=data.Type, BentNumber=data.BentNumber, Designation=data.Designation };
            s.Draw(); ApplyJointTypes(s.Timber, jn, jf, s.TenonDown, s.TenonUp);
        }

        // -----------------------------------------------------------------------
        // QpostTruss members
        // -----------------------------------------------------------------------
        private static void RegenerateQPTPostLeft(ObjectId timberId, Module1.DataStructure data, string j, double w, double d, string jn, string jf)
        {
            var ctx = ParseQPTPostContext(j); EraseAndMarkStale(timberId);
            Module1.Span = ctx.Span; Module1.EaveHt = ctx.EaveHt; Module1.Pitch = ctx.Pitch;
            Module1.Beta = ctx.Beta; Module1.Make3D = ctx.Make3D; Module1.OffsetType = ctx.OffsetType;
            var s = new QPTPostLeft { Width=w, Depth=d, RafterDepth=ctx.RafterDepth, RafterWidth=ctx.RafterWidth,
                StartPoint=new Point3d(ctx.StartX, ctx.StartY, ctx.StartZ),
                Type=data.Type, BentNumber=data.BentNumber, Designation=data.Designation };
            s.Draw(); ApplyJointTypes(s.TimberId, jn, jf, s.TenonDownId, s.TenonUpId);
        }

        private static void RegenerateQPTPostRight(ObjectId timberId, Module1.DataStructure data, string j, double w, double d, string jn, string jf)
        {
            var ctx = ParseQPTPostContext(j); EraseAndMarkStale(timberId);
            Module1.Span = ctx.Span; Module1.EaveHt = ctx.EaveHt; Module1.Pitch = ctx.Pitch;
            Module1.Beta = ctx.Beta; Module1.Make3D = ctx.Make3D; Module1.OffsetType = ctx.OffsetType;
            var s = new QPTPostRight { Width=w, Depth=d, RafterDepth=ctx.RafterDepth, RafterWidth=ctx.RafterWidth,
                QpostDepth=ctx.QpostDepth,
                StartPoint=new Point3d(ctx.StartX, ctx.StartY, ctx.StartZ),
                Type=data.Type, BentNumber=data.BentNumber, Designation=data.Designation };
            s.Draw(); ApplyJointTypes(s.TimberId, jn, jf, s.TenonDownId, s.TenonUpId);
        }

        private static void RegenerateQPTRafterLeft(ObjectId timberId, Module1.DataStructure data, string j, double w, double d, string jn, string jf)
        {
            var ctx = ParseQPTRafterContext(j); EraseAndMarkStale(timberId);
            Module1.Span = ctx.Span; Module1.EaveHt = ctx.EaveHt; Module1.Pitch = ctx.Pitch;
            Module1.Beta = ctx.Beta; Module1.Make3D = ctx.Make3D;
            var s = new QPTRafterLeft { Width=w, Depth=d,
                StartPoint=new Point3d(ctx.StartX, ctx.StartY, ctx.StartZ),
                Type=data.Type, BentNumber=data.BentNumber, Designation=data.Designation };
            s.Draw(); ApplyJointTypes(s.TimberId, jn, jf, farTenonId: s.TenonId);
        }

        private static void RegenerateQPTRafterRight(ObjectId timberId, Module1.DataStructure data, string j, double w, double d, string jn, string jf)
        {
            var ctx = ParseQPTRafterContext(j); EraseAndMarkStale(timberId);
            Module1.Span = ctx.Span; Module1.EaveHt = ctx.EaveHt; Module1.Pitch = ctx.Pitch;
            Module1.Beta = ctx.Beta; Module1.Make3D = ctx.Make3D;
            var s = new QPTRafterRight { Width=w, Depth=d, RafterDepth=ctx.RafterDepth,
                StartPoint=new Point3d(ctx.StartX, ctx.StartY, ctx.StartZ),
                Type=data.Type, BentNumber=data.BentNumber, Designation=data.Designation };
            s.Draw(); ApplyJointTypes(s.TimberId, jn, jf, s.TenonDownId, s.TenonUpId);
        }

        private static void RegenerateQPTStrutLeft(ObjectId timberId, Module1.DataStructure data, string j, double w, double d, string jn, string jf)
        {
            var ctx = ParseQPTStrutContext(j); EraseAndMarkStale(timberId);
            Module1.Span = ctx.Span; Module1.EaveHt = ctx.EaveHt; Module1.Pitch = ctx.Pitch;
            Module1.Beta = ctx.Beta; Module1.Make3D = ctx.Make3D; Module1.OffsetType = ctx.OffsetType;
            var s = new QPTStrutLeft { Width=w, Depth=d, RafterWidth=ctx.RafterWidth, RafterDepth=ctx.RafterDepth,
                StartPoint=new Point3d(ctx.StartX, ctx.StartY, 0),
                Type=data.Type, BentNumber=data.BentNumber, Designation=data.Designation };
            s.Draw(); ApplyJointTypes(s.TimberId, jn, jf, s.TenonDownId, s.TenonUpId);
        }

        private static void RegenerateQPTStrutRight(ObjectId timberId, Module1.DataStructure data, string j, double w, double d, string jn, string jf)
        {
            var ctx = ParseQPTStrutContext(j); EraseAndMarkStale(timberId);
            Module1.Span = ctx.Span; Module1.EaveHt = ctx.EaveHt; Module1.Pitch = ctx.Pitch;
            Module1.Beta = ctx.Beta; Module1.Make3D = ctx.Make3D; Module1.OffsetType = ctx.OffsetType;
            var s = new QPTStrutRight { Width=w, Depth=d, RafterWidth=ctx.RafterWidth, RafterDepth=ctx.RafterDepth,
                StartPoint=new Point3d(ctx.StartX, ctx.StartY, 0),
                Type=data.Type, BentNumber=data.BentNumber, Designation=data.Designation };
            s.Draw(); ApplyJointTypes(s.TimberId, jn, jf, s.TenonDownId, s.TenonUpId);
        }

        private static void RegenerateQPTUpperGirt(ObjectId timberId, Module1.DataStructure data, string j, double w, double d, string jn, string jf)
        {
            var ctx = ParseQPTUpperGirtContext(j); EraseAndMarkStale(timberId);
            Module1.Span = ctx.Span; Module1.Make3D = ctx.Make3D; Module1.OffsetType = ctx.OffsetType;
            var s = new QPTUpperGirt { Width=w, Depth=d, QpostDepth=ctx.QpostDepth,
                StartPoint=new Point3d(ctx.StartX, ctx.StartY, ctx.StartZ),
                Type=data.Type, BentNumber=data.BentNumber, Designation=data.Designation };
            s.Draw(); ApplyJointTypes(s.TimberId, jn, jf, s.TenonLeftId, s.TenonRightId);
        }

        // -----------------------------------------------------------------------
        // TrussGirt and Ridge
        // -----------------------------------------------------------------------
        private static void RegenerateTrussGirt(ObjectId timberId, Module1.DataStructure data, string j, double w, double d, string jn, string jf)
        {
            var ctx = ParseTrussGirtContext(j); EraseAndMarkStale(timberId);
            Module1.Span = ctx.Span; Module1.EaveHt = ctx.EaveHt; Module1.Beta = ctx.Beta;
            var s = new TrussGirt { Width=w, Depth=d, RafterDepth=ctx.RafterDepth,
                Type=data.Type, BentNumber=data.BentNumber, Designation=data.Designation };
            s.Draw(); ApplyJointTypes(s.Timber, jn, jf);
        }

        private static void RegenerateRidge(ObjectId timberId, Module1.DataStructure data, string j, double w, double d, string jn, string jf)
        {
            var ctx = ParseRidgeContext(j); EraseAndMarkStale(timberId);
            Module1.Span = ctx.Span; Module1.EaveHt = ctx.EaveHt; Module1.Pitch = ctx.Pitch;
            var s = new Ridge { Width=w, Depth=d, postWidth=ctx.PostWidth, Length=ctx.Length, Make3d=ctx.Make3d };
            s.Draw(); ApplyJointTypes(s.TimberId, jn, jf);
        }

        // -----------------------------------------------------------------------
        // Qpost contexts
        // -----------------------------------------------------------------------
        private struct QPPostContext { public double StartX,StartY,StartZ,GirtWidth,PostDepth,Span,TOG,TOH,Pitch,Beta; public bool Make3D; public int OffsetType; }
        private static QPPostContext ParseQPPostContext(string json) {
            AssertHasContext(json,"QPPost"); using var doc=JsonDocument.Parse(json); var r=doc.RootElement;
            return new QPPostContext { StartX=D(r,"startX"),StartY=D(r,"startY"),StartZ=D(r,"startZ"), GirtWidth=D(r,"girtWidth"),PostDepth=D(r,"postDepth"), Span=D(r,"span"),TOG=D(r,"tog"),TOH=D(r,"toh"),Pitch=D(r,"pitch"),Beta=D(r,"beta"), Make3D=B(r,"make3D"),OffsetType=(int)D(r,"offsetType") }; }

        private struct QPRafterContext { public double StartX,StartY,StartZ,PostDepth,Span,TOH,Pitch,Beta,EaveHt,SitDepth; public bool Make3D; }
        private static QPRafterContext ParseQPRafterContext(string json) {
            AssertHasContext(json,"QPRafter"); using var doc=JsonDocument.Parse(json); var r=doc.RootElement;
            return new QPRafterContext { StartX=D(r,"startX"),StartY=D(r,"startY"),StartZ=D(r,"startZ"), PostDepth=D(r,"postDepth"),Span=D(r,"span"),TOH=D(r,"toh"),Pitch=D(r,"pitch"),Beta=D(r,"beta"),EaveHt=D(r,"eaveHt"), SitDepth=D(r,"sitDepth",3.0), Make3D=B(r,"make3D") }; }

        private struct QPStrutContext { public double StartX,StartY,QPRafterWidth,PostDepth,Span,TOH,Pitch,Beta,B; public bool Make3D; public int OffsetType; }
        private static QPStrutContext ParseQPStrutContext(string json) {
            AssertHasContext(json,"QPStrut"); using var doc=JsonDocument.Parse(json); var r=doc.RootElement;
            // QPStrutRight BuildContextJson bug: old drawings stored span as boolean ("true"/"false")
            // and hid the actual span under "postDepth".  Read "span" only when it is a Number;
            // otherwise fall back to "postDepth" which holds the real span on old entities.
            double span = r.TryGetProperty("span", out var spanEl) && spanEl.ValueKind == JsonValueKind.Number
                ? spanEl.GetDouble() : D(r,"postDepth");
            return new QPStrutContext { StartX=D(r,"startX"),StartY=D(r,"startY"), QPRafterWidth=D(r,"qpRafterWidth"),PostDepth=D(r,"postDepth"), Span=span,TOH=D(r,"toh"),Pitch=D(r,"pitch"),Beta=D(r,"beta"),B=D(r,"b"), Make3D=B(r,"make3D"),OffsetType=(int)D(r,"offsetType") }; }

        private struct QPUpperGirtContext { public double StartX,StartY,QPRafterWidth,QPQpostDepth,PostDepth,Span,TOH,Pitch; public bool Make3D; public int OffsetType; }
        private static QPUpperGirtContext ParseQPUpperGirtContext(string json) {
            AssertHasContext(json,"QPUpperGirt"); using var doc=JsonDocument.Parse(json); var r=doc.RootElement;
            return new QPUpperGirtContext { StartX=D(r,"startX"),StartY=D(r,"startY"), QPRafterWidth=D(r,"qpRafterWidth"),QPQpostDepth=D(r,"qpQpostDepth"),PostDepth=D(r,"postDepth"), Span=D(r,"span"),TOH=D(r,"toh"),Pitch=D(r,"pitch"),Make3D=B(r,"make3D"),OffsetType=(int)D(r,"offsetType") }; }

        // -----------------------------------------------------------------------
        // Hbeam contexts
        // -----------------------------------------------------------------------
        private struct HBeamContext { public double StartX,StartY,StartZ,HbLength,PostWidth,KpostDepth; public int HBDivisor; public bool Make3D; }
        private static HBeamContext ParseHBeamContext(string json) {
            AssertHasContext(json,"HBeam"); using var doc=JsonDocument.Parse(json); var r=doc.RootElement;
            return new HBeamContext { StartX=D(r,"startX"),StartY=D(r,"startY"),StartZ=D(r,"startZ"), HbLength=D(r,"hbLength"),PostWidth=D(r,"postWidth"),KpostDepth=D(r,"kpostDepth"), HBDivisor=(int)D(r,"hbDivisor"),Make3D=B(r,"make3D") }; }

        private struct HBGirtContext { public double StartX,StartY,StartZ,PostDepth,PostWidth,KpostDepth,Span,Pitch; public int HBDivisor; public bool Make3D; }
        private static HBGirtContext ParseHBGirtContext(string json) {
            AssertHasContext(json,"HBGirt"); using var doc=JsonDocument.Parse(json); var r=doc.RootElement;
            return new HBGirtContext { StartX=D(r,"startX"),StartY=D(r,"startY"),StartZ=D(r,"startZ"), PostDepth=D(r,"postDepth"),PostWidth=D(r,"postWidth"),KpostDepth=D(r,"kpostDepth"), Span=D(r,"span"),Pitch=D(r,"pitch"),HBDivisor=(int)D(r,"hbDivisor"),Make3D=B(r,"make3D") }; }

        private struct HBKpostContext { public double StartX,StartY,StartZ,PostDepth,KpostRafterSeatDepth,Span,EaveHt,Pitch,Beta,TOH,TOG; public int HBDivisor; public bool Make3D; }
        private static HBKpostContext ParseHBKpostContext(string json) {
            AssertHasContext(json,"HBKpost"); using var doc=JsonDocument.Parse(json); var r=doc.RootElement;
            return new HBKpostContext { StartX=D(r,"startX"),StartY=D(r,"startY"),StartZ=D(r,"startZ"), PostDepth=D(r,"postDepth"),KpostRafterSeatDepth=D(r,"kpostRafterSeatDepth"), Span=D(r,"span"),EaveHt=D(r,"eaveHt"),Pitch=D(r,"pitch"),Beta=D(r,"beta"),TOH=D(r,"toh"),TOG=D(r,"tog"), HBDivisor=(int)D(r,"hbDivisor"),Make3D=B(r,"make3D") }; }

        private struct HPostContext { public double StartX,StartY,StartZ,PostDepth,PostWidth,KpostDepth,RafterWidth,Span,Pitch,Beta; public int HBDivisor; public bool Make3D; }
        private static HPostContext ParseHPostContext(string json) {
            AssertHasContext(json,"HPost"); using var doc=JsonDocument.Parse(json); var r=doc.RootElement;
            return new HPostContext { StartX=D(r,"startX"),StartY=D(r,"startY"),StartZ=D(r,"startZ"), PostDepth=D(r,"postDepth"),PostWidth=D(r,"postWidth"),KpostDepth=D(r,"kpostDepth"),RafterWidth=D(r,"rafterWidth"), Span=D(r,"span"),Pitch=D(r,"pitch"),Beta=D(r,"beta"),HBDivisor=(int)D(r,"hbDivisor"),Make3D=B(r,"make3D") }; }

        private struct HBBayGirtContext { public int Side; public double StartX,StartY,Baywidth,HPostWidth,HPostDepth; public bool Make3D; }
        private static HBBayGirtContext ParseHBBayGirtContext(string json) {
            AssertHasContext(json,"HBBayGirt"); using var doc=JsonDocument.Parse(json); var r=doc.RootElement;
            return new HBBayGirtContext { Side=(int)D(r,"side"),StartX=D(r,"startX"),StartY=D(r,"startY"), Baywidth=D(r,"baywidth"),HPostWidth=D(r,"hPostWidth"),HPostDepth=D(r,"hPostDepth"),Make3D=B(r,"make3D") }; }

        // -----------------------------------------------------------------------
        // KpostTruss contexts
        // -----------------------------------------------------------------------
        private struct KPTPostContext { public double StartX,StartY,StartZ,KPostWidth,KPostDepth,KPRafterDepth,PostWidth,Span,EaveHt,Pitch,Beta; public bool Make3D; }
        private static KPTPostContext ParseKPTPostContext(string json) {
            AssertHasContext(json,"KPTPost"); using var doc=JsonDocument.Parse(json); var r=doc.RootElement;
            return new KPTPostContext { StartX=D(r,"startX"),StartY=D(r,"startY"),StartZ=D(r,"startZ"), KPostWidth=D(r,"kPostWidth"),KPostDepth=D(r,"kPostDepth"),KPRafterDepth=D(r,"kpRafterDepth"),PostWidth=D(r,"postWidth"), Span=D(r,"span"),EaveHt=D(r,"eaveHt"),Pitch=D(r,"pitch"),Beta=D(r,"beta"),Make3D=B(r,"make3D") }; }

        private struct KPTRafterContext { public double StartX,StartY,StartZ,KPostDepth,PostDepth,KpostRafterSitDepth,Span,EaveHt,Pitch,Beta; public bool Make3D; }
        private static KPTRafterContext ParseKPTRafterContext(string json) {
            AssertHasContext(json,"KPTRafter"); using var doc=JsonDocument.Parse(json); var r=doc.RootElement;
            return new KPTRafterContext { StartX=D(r,"startX"),StartY=D(r,"startY"),StartZ=D(r,"startZ"), KPostDepth=D(r,"kPostDepth"),PostDepth=D(r,"postDepth"),KpostRafterSitDepth=D(r,"kpostRafterSitDepth"), Span=D(r,"span"),EaveHt=D(r,"eaveHt"),Pitch=D(r,"pitch"),Beta=D(r,"beta"),Make3D=B(r,"make3D") }; }

        private struct KPTStrutContext { public double StartX,StartY,KPostWidth,KPostDepth,KPRafterDepth,PostWidth,Span,EaveHt,Pitch,Beta; public bool Make3D; public int OffsetType; }
        private static KPTStrutContext ParseKPTStrutContext(string json) {
            AssertHasContext(json,"KPTStrut"); using var doc=JsonDocument.Parse(json); var r=doc.RootElement;
            return new KPTStrutContext { StartX=D(r,"startX"),StartY=D(r,"startY"), KPostWidth=D(r,"kPostWidth"),KPostDepth=D(r,"kPostDepth"),KPRafterDepth=D(r,"kpRafterDepth"),PostWidth=D(r,"postWidth"), Span=D(r,"span"),EaveHt=D(r,"eaveHt"),Pitch=D(r,"pitch"),Beta=D(r,"beta"),Make3D=B(r,"make3D"),OffsetType=(int)D(r,"offsetType") }; }

        // -----------------------------------------------------------------------
        // QpostTruss contexts
        // -----------------------------------------------------------------------
        private struct QPTPostContext { public double StartX,StartY,StartZ,RafterDepth,RafterWidth,QpostDepth,Span,EaveHt,Pitch,Beta; public bool Make3D; public int OffsetType; }
        private static QPTPostContext ParseQPTPostContext(string json) {
            AssertHasContext(json,"QPTPost"); using var doc=JsonDocument.Parse(json); var r=doc.RootElement;
            return new QPTPostContext { StartX=D(r,"startX"),StartY=D(r,"startY"),StartZ=D(r,"startZ"), RafterDepth=D(r,"rafterDepth"),RafterWidth=D(r,"rafterWidth"),QpostDepth=D(r,"qpostDepth"), Span=D(r,"span"),EaveHt=D(r,"eaveHt"),Pitch=D(r,"pitch"),Beta=D(r,"beta"),Make3D=B(r,"make3D"),OffsetType=(int)D(r,"offsetType") }; }

        private struct QPTRafterContext { public double StartX,StartY,StartZ,RafterDepth,Span,EaveHt,Pitch,Beta; public bool Make3D; }
        private static QPTRafterContext ParseQPTRafterContext(string json) {
            AssertHasContext(json,"QPTRafter"); using var doc=JsonDocument.Parse(json); var r=doc.RootElement;
            return new QPTRafterContext { StartX=D(r,"startX"),StartY=D(r,"startY"),StartZ=D(r,"startZ"), RafterDepth=D(r,"rafterDepth"),Span=D(r,"span"),EaveHt=D(r,"eaveHt"),Pitch=D(r,"pitch"),Beta=D(r,"beta"),Make3D=B(r,"make3D") }; }

        private struct QPTStrutContext { public double StartX,StartY,RafterWidth,RafterDepth,Span,EaveHt,Pitch,Beta; public bool Make3D; public int OffsetType; }
        private static QPTStrutContext ParseQPTStrutContext(string json) {
            AssertHasContext(json,"QPTStrut"); using var doc=JsonDocument.Parse(json); var r=doc.RootElement;
            return new QPTStrutContext { StartX=D(r,"startX"),StartY=D(r,"startY"), RafterWidth=D(r,"rafterWidth"),RafterDepth=D(r,"rafterDepth"), Span=D(r,"span"),EaveHt=D(r,"eaveHt"),Pitch=D(r,"pitch"),Beta=D(r,"beta"),Make3D=B(r,"make3D"),OffsetType=(int)D(r,"offsetType") }; }

        private struct QPTUpperGirtContext { public double StartX,StartY,StartZ,QpostDepth,Span; public bool Make3D; public int OffsetType; }
        private static QPTUpperGirtContext ParseQPTUpperGirtContext(string json) {
            AssertHasContext(json,"QPTUpperGirt"); using var doc=JsonDocument.Parse(json); var r=doc.RootElement;
            return new QPTUpperGirtContext { StartX=D(r,"startX"),StartY=D(r,"startY"),StartZ=D(r,"startZ"), QpostDepth=D(r,"qpostDepth"),Span=D(r,"span"),Make3D=B(r,"make3D"),OffsetType=(int)D(r,"offsetType") }; }

        // -----------------------------------------------------------------------
        // TrussGirt and Ridge contexts
        // -----------------------------------------------------------------------
        private struct TrussGirtContext { public double Depth,Width,RafterDepth,Span,EaveHt,Beta; }
        private static TrussGirtContext ParseTrussGirtContext(string json) {
            AssertHasContext(json,"TrussGirt"); using var doc=JsonDocument.Parse(json); var r=doc.RootElement;
            return new TrussGirtContext { Depth=D(r,"depth"),Width=D(r,"width"),RafterDepth=D(r,"rafterDepth"),Span=D(r,"span"),EaveHt=D(r,"eaveHt"),Beta=D(r,"beta") }; }

        private struct RidgeContext { public double PostWidth,Length,Span,EaveHt,Pitch; public bool Make3d; }
        private static RidgeContext ParseRidgeContext(string json) {
            AssertHasContext(json,"Ridge"); using var doc=JsonDocument.Parse(json); var r=doc.RootElement;
            return new RidgeContext { PostWidth=D(r,"postWidth"),Length=D(r,"length"),Span=D(r,"span"),EaveHt=D(r,"eaveHt"),Pitch=D(r,"pitch"),Make3d=B(r,"make3d") }; }

        // -----------------------------------------------------------------------
        // -----------------------------------------------------------------------
        // No-change detection helper
        // -----------------------------------------------------------------------

        // Returns true when 'current' and 'drawn' represent the same joint params.
        // Treats null drawn snapshot as "unknown" -> always returns false (allow regen).
        // Normalises empty/"{}"/null values so Butt-type members (no params) compare equal.
        private static bool ParamsUnchanged(string current, string drawn)
        {
            if (drawn == null) return false;                 // null = old drawing, no baseline
            string c = string.IsNullOrEmpty(current) ? "{}" : current.Trim();
            string d = string.IsNullOrEmpty(drawn)   ? "{}" : drawn.Trim();
            return string.Equals(c, d, StringComparison.Ordinal);
        }

        // -----------------------------------------------------------------------
        // Parser helpers
        // -----------------------------------------------------------------------
        private static string GetMemberClass(string json)
        {
            if (string.IsNullOrEmpty(json) || json == "{}") return "";
            try {
                using JsonDocument doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("class", out JsonElement cls))
                    return cls.GetString() ?? "";
            } catch { }
            return "";
        }

        private static void AssertHasContext(string json, string label)
        {
            if (string.IsNullOrWhiteSpace(json) || json == "{}")
                throw new InvalidOperationException(
                    $"DrawContext is empty for {label}. The timber must have been drawn " +
                    "after the Phase 2 update for regeneration to work.");
        }

        private static double D(JsonElement el, string key)
            => el.TryGetProperty(key, out JsonElement v) ? v.GetDouble() : 0.0;
        private static double D(JsonElement el, string key, double def)
            => el.TryGetProperty(key, out JsonElement v) ? v.GetDouble() : def;
        private static bool B(JsonElement el, string key)
            => el.TryGetProperty(key, out JsonElement v) && v.GetBoolean();
    }
}
