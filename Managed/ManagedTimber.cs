using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.GraphicsInterface;
using Autodesk.AutoCAD.Runtime;

[assembly: CommandClass(typeof(TimberDraw.ManagedCommands))]
namespace TimberDraw
{
    // Managed-timber assembly model (NEW direction, additive -- does not touch the TDraw/TFrame
    // pipeline). A timber is a managed solid: its SHAPE comes from attributes (W x D x L, type,
    // per-end cut), drawn as a box placed freely in WCS. Each managed timber also stores its
    // placement FRAME (origin + axes + L/D/W) in an xrecord, so its 6 faces can be computed
    // analytically -- no BRep. Nodes are NOT stored: they are derived from face coincidence by a
    // rescan (TScan). WCS is the source of truth; UCS is only an input convenience.
    public static partial class ManagedTimber
    {
        public const string FrameKey = "TMFrame";   // xrecord on the solid's extension dictionary
        public const string NodeLayer = "TM_NODES"; // transient coincidence markers
        public const string ScarfKey = "TMScarf";   // xrecord: a scarf splice point this timber carries
        public const string SeatKey  = "TMSeat";    // xrecord: explicit seat nodes a timber carries (brace seats -- oblique member cuts aren't analytic faces)
        public const string GridKey  = "TMGrid";    // xrecord on a structural-grid entity: its FrameTag
        public const string JointSpecsKey = "TMJointSpecs"; // xrecord: per joint id, the editor's OPAQUE spec state string (jid:Real, state:Text)*
        public const string GridLayer = "TM_GRID";  // layer for the drawn structural grid (lines + bubbles)
        public const string GridTempLayer = "TM_GRID_TEMP";  // provisional grid lines for Free Assembly elements
        public const string ShopKey     = "TMShop";      // xrecord on a shop-drawing entity (outline / label / context)
        public const string ShopLayer   = "TM_SHOP";     // layer for the drawn 2D assembly maps (primary outlines + labels)
        public const string ShopCtxLayer = "TM_SHOP_CTX"; // faded context (connected neighbors, arris sections, direction marks)
        public const string GroupPrefix = "TM_";     // per-group timber layers: TM_<frame>_Bent<n> / TM_<frame>_Wall<letter>

        // The per-group layer a timber belongs to -- a bent member -> its bent's layer, a longitudinal /
        // wall member -> its wall's layer. Used for tree-checkbox isolation (toggle the layer on/off).
        public static string GroupLayer(string frameTag, string bentTag, string wallTag, string floorTag = "")
        {
            string f = string.IsNullOrWhiteSpace(frameTag) ? "A" : frameTag.Trim();
            if (!string.IsNullOrEmpty(bentTag)) return GroupPrefix + f + "_Bent" + bentTag;
            if (!string.IsNullOrEmpty(wallTag)) return GroupPrefix + f + "_Wall" + wallTag;
            if (!string.IsNullOrEmpty(floorTag)) return GroupPrefix + f + "_Floor" + floorTag;
            return GroupPrefix + f;
        }

        // Ensure a layer exists (neutral color ACI 7; never green -- the user is red/green colorblind).
        // Returns its id. Call inside an open transaction.
        public static ObjectId EnsureFrameLayer(Transaction tr, Database db, string name)
        {
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (lt.Has(name)) return lt[name];
            lt.UpgradeOpen();
            var ltr = new LayerTableRecord
            {
                Name = name,
                Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByAci, 7)
            };
            ObjectId id = lt.Add(ltr);
            tr.AddNewlyCreatedDBObject(ltr, true);
            return id;
        }

        // Read/Write a group layer's visibility (On/Off) for tree-checkbox isolation. Visible = layer On.
        public static bool IsGroupVisible(Database db, string layerName)
        {
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                bool vis = !lt.Has(layerName) ||
                           !((LayerTableRecord)tr.GetObject(lt[layerName], OpenMode.ForRead)).IsOff;
                tr.Commit();
                return vis;
            }
        }

        public static void SetGroupVisible(Database db, string layerName, bool visible)
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            using (doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                ObjectId lid = EnsureFrameLayer(tr, db, layerName);
                var ltr = (LayerTableRecord)tr.GetObject(lid, OpenMode.ForWrite);
                ltr.IsOff = !visible;
                tr.Commit();
            }
        }

        // Erase every MANAGED timber in the current space (those carrying a TMFrame xrecord) -- the
        // tree editor's draw/redraw clears the prior frame before re-emitting, leaving non-managed
        // solids untouched. Returns the count erased. Reversible via AutoCAD UNDO. NOTE: today this
        // clears ALL managed timbers (one generated frame at a time); per-frame clearing (multiple
        // managed frames in one drawing) needs a frame tag on each timber -- see the grouping layer.
        public static int EraseAllManaged(Database db) => EraseFrame(db, null);

        // Erase managed timbers (those carrying a TMFrame xrecord). When `frameTag` is null, clears
        // EVERY managed timber (legacy whole-frame redraw). When `frameTag` is given, clears only the
        // GENERATOR'S OWN timbers carrying that FrameTag -- a regenerate never touches hand-placed
        // (free-assembly) work, assigned or not: a timber survives when it carries the Free marker
        // (stamped at editor creation) OR a FloorTag (floor-owned members are free by construction --
        // the generator never writes FloorTag, so this also protects joists/summers placed before the
        // marker existed). Non-managed solids untouched. Reversible via AutoCAD UNDO. Returns the count.
        public static int EraseFrame(Database db, string frameTag)
        {
            int n = 0;
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return 0;
            using (doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                var btr = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead);
                foreach (ObjectId id in btr)
                {
                    if (!(tr.GetObject(id, OpenMode.ForRead) is Entity ent)) continue;
                    if (!TryReadFrame(tr, ent, out _)) continue;
                    if (frameTag != null)
                    {
                        if (ReadXTextField(tr, ent, "FrameTag") != frameTag) continue;
                        if (ReadXTextField(tr, ent, "Free") == "1") continue;        // hand-placed: keep
                        if (ReadXTextField(tr, ent, "FloorTag") != "") continue;     // floor-owned: keep
                    }
                    ent.UpgradeOpen(); ent.Erase(); n++;
                }
                tr.Commit();
            }
            return n;
        }

        // Erase the drawn structural-grid entities (lines + bubbles) carrying a TMGrid xrecord whose
        // value matches `frameTag` -- the per-frame grid redraw (mirrors EraseFrame). Returns the count.
        public static int EraseGrid(Database db, string frameTag)
        {
            int n = 0;
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return 0;
            using (doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                var btr = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead);
                foreach (ObjectId id in btr)
                {
                    if (!(tr.GetObject(id, OpenMode.ForRead) is Entity ent)) continue;
                    if (ReadGridTag(tr, ent) != frameTag) continue;
                    ent.UpgradeOpen(); ent.Erase(); n++;
                }
                tr.Commit();
            }
            return n;
        }

        // The FrameTag stored in an entity's TMGrid xrecord, or null if it carries none (not a grid entity).
        private static string ReadGridTag(Transaction tr, Entity ent)
        {
            if (ent.ExtensionDictionary.IsNull) return null;
            if (!(tr.GetObject(ent.ExtensionDictionary, OpenMode.ForRead) is DBDictionary dict)) return null;
            if (!dict.Contains(GridKey)) return null;
            if (!(tr.GetObject(dict.GetAt(GridKey), OpenMode.ForRead) is Xrecord xr)) return null;
            TypedValue[] v = xr.Data?.AsArray();
            return (v != null && v.Length > 0) ? v[0].Value.ToString() : "";
        }

        // Tag an entity as part of frame `frameTag`'s structural grid (a TMGrid xrecord on its extension
        // dictionary) so EraseGrid can clear it on redraw. Call inside an open transaction with the
        // entity open for write and newly appended.
        public static void WriteGridTag(Transaction tr, Entity ent, string frameTag)
        {
            ent.CreateExtensionDictionary();
            var dict = (DBDictionary)tr.GetObject(ent.ExtensionDictionary, OpenMode.ForWrite);
            var xr = new Xrecord { Data = new ResultBuffer(new TypedValue((int)DxfCode.Text, frameTag ?? "")) };
            dict.SetAt(GridKey, xr);
            tr.AddNewlyCreatedDBObject(xr, true);
        }

        // Tag a shop-drawing entity (outline / label) with a TMShop xrecord so EraseShop can clear the 2D
        // assembly maps on redraw -- the shop counterpart of WriteGridTag, keyed separately (ShopKey) so
        // regenerating the maps never touches the structural grid. Call inside an open transaction with
        // the entity open for write and newly appended.
        public static void WriteShopTag(Transaction tr, Entity ent)
        {
            ent.CreateExtensionDictionary();
            var dict = (DBDictionary)tr.GetObject(ent.ExtensionDictionary, OpenMode.ForWrite);
            var xr = new Xrecord { Data = new ResultBuffer(new TypedValue((int)DxfCode.Text, ShopLayer)) };
            dict.SetAt(ShopKey, xr);
            tr.AddNewlyCreatedDBObject(xr, true);
        }

        private static bool HasShopTag(Transaction tr, Entity ent)
        {
            if (ent.ExtensionDictionary.IsNull) return false;
            if (!(tr.GetObject(ent.ExtensionDictionary, OpenMode.ForRead) is DBDictionary dict)) return false;
            return dict.Contains(ShopKey);
        }

        // Erase every drawn shop-map entity (those carrying a TMShop xrecord) in the current space --
        // the 2D-map redraw primitive (mirrors EraseGrid). Returns the count erased. Reversible via UNDO.
        public static int EraseShop(Database db)
        {
            int n = 0;
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return 0;
            using (doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                var btr = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead);
                foreach (ObjectId id in btr)
                {
                    if (!(tr.GetObject(id, OpenMode.ForRead) is Entity ent)) continue;
                    if (!HasShopTag(tr, ent)) continue;
                    ent.UpgradeOpen(); ent.Erase(); n++;
                }
                tr.Commit();
            }
            return n;
        }

        // Read one text-valued XData field from an entity's extension dictionary inside an existing
        // transaction (Module1.GetXdata opens its own transaction, which we cannot nest in the erase
        // loop). Returns "" when the dictionary or key is absent.
        private static string ReadXTextField(Transaction tr, Entity ent, string key)
        {
            if (ent.ExtensionDictionary.IsNull) return "";
            if (!(tr.GetObject(ent.ExtensionDictionary, OpenMode.ForRead) is DBDictionary dict)) return "";
            if (!dict.Contains(key)) return "";
            if (!(tr.GetObject(dict.GetAt(key), OpenMode.ForRead) is Xrecord xr)) return "";
            TypedValue[] v = xr.Data?.AsArray();
            return (v != null && v.Length > 0) ? v[0].Value.ToString() : "";
        }
    }


    public partial class ManagedCommands
    {
        // Session-sticky joint recipes -- remembered across cuts, seeded from the USER's saved defaults
        // (JointDefaults; factory *Spec.Default when none saved). Console review loops mutate these in
        // place per session; "Set as default" in the Joints pane persists + re-seeds them (ReseedJointSticky).
        private static ManagedTimber.JointSpec _joint = JointDefaults.Joint;
        // Post foot -> sill stub tenon (floor systems phase 3): traditionally a SHORT UNPEGGED stub
        // (gravity does the work), so its sticky seeds tenon-only, 2" long, no pegs. Reviewed by
        // TJointAll's sill pass; session-sticky like _joint.
        private static ManagedTimber.JointSpec _sillJoint = SillJointSeed();
        private static ManagedTimber.JointSpec SillJointSeed()
        {
            var s = ManagedTimber.JointSpec.Default;
            s.Tenon.Length = 2.0;
            s.Peg.Count = 0;
            return s;
        }
        // Summer end -> girt side (floor systems phase 4): the classic TUSK TENON (soffit-bearing
        // housing + deep tenon). Reviewed by TJointAll's summer pass; session-sticky like _joint.
        private static ManagedTimber.JointSpec _summerJoint = JointDefaults.Tusk;
        private static ManagedTimber.RafterFootSpec _rfoot = JointDefaults.RafterFoot;
        private static ManagedTimber.RafterHeadSpec _rhead = JointDefaults.RafterHead;
        private static ManagedTimber.RidgeHousingSpec _ridge = JointDefaults.Ridge;
        private static ManagedTimber.RidgeHousingSpec _ridgeRafter = JointDefaults.Ridge;   // ridge -> principal-rafter head (king-post-less bents; shares the one "Ridge housing" default)
        private static ManagedTimber.PurlinRafterSpec _purlin = JointDefaults.Purlin;
        private static ManagedTimber.CommonRidgeSpec _comridge = JointDefaults.CommonRidge;
        private static ManagedTimber.CommonEaveSpec _comeave = JointDefaults.CommonEave;
        private static ManagedTimber.StrutTenonSpec _strut = JointDefaults.Strut;
        // Braces are the SAME end->side tenon (StrutTenonJoint), just thinner (1.5") and conventionally
        // BAREFACED -- the tongue flush to one cheek (a clean soffit). Barefaced is computed from the brace
        // width at cut time (= (W - Thickness)/2), so it tracks any stock; Flip picks which cheek.
        private static ManagedTimber.StrutTenonSpec _brace = JointDefaults.Brace;
        private static bool _braceBarefaced = true;
        private static bool _braceFlip = false;

        // Re-seed the session sticky for one saved/reset joint default so the console T* commands pick it
        // up immediately (no restart). Called by JointDefaults.Save/Reset. Only the MATCHING key re-seeds
        // -- never the rest, so unrelated in-session console tweaks survive. Structs: assignment = full copy.
        internal static void ReseedJointSticky(string key)
        {
            switch (key)
            {
                case JointDefaults.KeyBox:         _joint = JointDefaults.Joint; break;
                case JointDefaults.KeyStrut:       _strut = JointDefaults.Strut; break;
                case JointDefaults.KeyBrace:       _brace = JointDefaults.Brace; break;
                case JointDefaults.KeyRafterFoot:  _rfoot = JointDefaults.RafterFoot; break;
                case JointDefaults.KeyRafterHead:  _rhead = JointDefaults.RafterHead; break;
                case JointDefaults.KeyRidge:       _ridge = JointDefaults.Ridge; _ridgeRafter = JointDefaults.Ridge; break;
                case JointDefaults.KeyCommonRidge: _comridge = JointDefaults.CommonRidge; break;
                case JointDefaults.KeyBirdsmouth:  _comeave = JointDefaults.CommonEave; break;
                case JointDefaults.KeyPurlin:      _purlin = JointDefaults.Purlin; _joistDove = JoistDoveSeed(); break;
                case JointDefaults.KeyQPRafter:    _qprafter = JointDefaults.QPRafter; break;
                case JointDefaults.KeyTusk:        _summerJoint = JointDefaults.Tusk; break;
            }
        }

        // Place one managed timber. Pick the EXTRUSION DIRECTION with the cursor: a start point (the near
        // end-face centre) and a direction point. The timber extrudes the given LENGTH along that
        // direction, so you DON'T have to re-roll the UCS Z per member. The W x D section's roll comes
        // from the UCS: DEPTH follows UCS Y (vertical in the rotate-90-about-X bent UCS), projected
        // perpendicular to the length; WIDTH completes a right-handed frame. Stored in WCS.
        [CommandMethod("TPlace")]
        public static void PlaceTimber()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;

            if (!GetSection(ed, out double w, out double d, out string type)) return;

            PromptPointResult p1 = ed.GetPoint("\nStart point (near end centre): ");
            if (p1.Status != PromptStatus.OK) return;

            // Section in UCS XY (width=X, depth=Y), length extruded along UCS Z. Axes resolved to WCS.
            Matrix3d ucs = ed.CurrentUserCoordinateSystem;
            Point3d a = p1.Value.TransformBy(ucs);
            CoordinateSystem3d cs = ucs.CoordinateSystem3d;
            Vector3d ux = cs.Xaxis.GetNormal(), uy = cs.Yaxis.GetNormal(), uz = cs.Zaxis.GetNormal();

            // Two-phase ghost jig: phase 1 rolls the W x D section about the base point, phase 2
            // drags the length out along UCS Z. Both phases preview the timber live.
            var jig = new PlaceJig(a, ux, uy, uz, w, d, 96.0);
            jig.Phase = PlaceJig.JigPhase.Roll;
            if (ed.Drag(jig).Status != PromptStatus.OK) return;
            jig.Phase = PlaceJig.JigPhase.Length;
            if (ed.Drag(jig).Status != PromptStatus.OK) return;

            double angle = jig.Angle, len = jig.Length;
            // Roll the section axes about UCS Z (right-handed: widthAxis x depthAxis = lengthAxis).
            Vector3d widthAxis  = ux * Math.Cos(angle) + uy * Math.Sin(angle);
            Vector3d depthAxis  = ux * (-Math.Sin(angle)) + uy * Math.Cos(angle);
            Vector3d lengthAxis = uz;

            ObjectId id = ManagedTimber.DrawBox(a, lengthAxis, depthAxis, widthAxis, len, d, w, type, "", "butt", "butt");
            ed.WriteMessage("\nTPlace: " + type + " " + (int)w + "x" + (int)d + "x" + len.ToString("0.#") +
                            " roll " + (angle * 180.0 / Math.PI).ToString("0.#") + " deg (" + id.Handle + ").");
        }

        // Assign freely-placed timbers to the frame's organization HIERARCHY:
        //   Frame -> Bent N -> members | Wall X -> Bay -> members | Floor N -> members
        // Pick one or more managed timbers, then say which frame + owner -- or set the target on the
        // TPanel Assembly group first and the command runs promptless (ManagedAssembly). Floor is the
        // owner of floor-system members (joists, summers), numbered bottom-up (1 = first). Writes the grouping
        // tags as an IN-PLACE XData patch -- the solid is NOT rebuilt (no erase/redraw, same handle)
        // -- so the Browser regroups them on Refresh. A wall/floor exists the moment a timber claims
        // it (implicit organizational labels). Repetitive free families (Joist, Summer) also get their
        // owner-addressed GridLabel minted here (J-<floor>-1..n in run order, continuing after the
        // group's existing numbering) -- the label conventions' free-timber scheme.
        [CommandMethod("TAssign")]
        public static void AssignToFrame()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            // Targets: a pane handoff (the Frame Browser's selected rows, consumed ONCE) or an
            // interactive selection (honors a pickfirst set). Either way only MANAGED timbers
            // (frame xrecord) survive; stale browser rows (erased handles) are skipped.
            List<ObjectId> picked = ManagedAssembly.TakeIds();
            if (picked == null)
            {
                var filter = new SelectionFilter(new[] { new TypedValue((int)DxfCode.Start, "3DSOLID") });
                var pso = new PromptSelectionOptions { MessageForAdding = "\nSelect timbers to assign: " };
                PromptSelectionResult sel = ed.GetSelection(pso, filter);
                if (sel.Status != PromptStatus.OK) return;
                picked = new List<ObjectId>(sel.Value.GetObjectIds());
            }

            var ids = new List<ObjectId>();
            var frames = new Dictionary<ObjectId, ManagedTimber.TFrame>();
            int skipped = 0;
            foreach (ObjectId id in picked)
            {
                if (!id.IsNull && !id.IsErased
                    && ManagedTimber.TryReadFrame(db, id, out ManagedTimber.TFrame f)) { ids.Add(id); frames[id] = f; }
                else skipped++;
            }
            if (ids.Count == 0) { ed.WriteMessage("\nNo managed timbers in the selection."); return; }

            // The palette's Assembly pane supplies the target SILENTLY when it is set (the pane is
            // the command's visibility -- the ManagedSection pattern); the command-line prompts
            // below remain for a console-driven assign or an unparseable pane value.
            string frame = null;
            string bentTag = "", wallTag = "", bayTag = "", floorTag = "", colTag = "";
            if (ManagedAssembly.HasCurrent)
            {
                string owner = ManagedAssembly.Owner;
                switch (ManagedAssembly.Kind)
                {
                    case "Bent":
                        // Owner box = the bent number ("2", or typed intersection "2C"); the pane's
                        // second coordinate box supplies the column letter -- together the grid
                        // intersection a free post stands on.
                        SplitIntersection(owner, out string pb, out string pc);
                        if (pb.Length > 0)
                        {
                            bentTag = pb;
                            colTag = pc.Length > 0 ? pc : LettersOnly(ManagedAssembly.Bay);
                        }
                        break;
                    case "Wall":
                        if (owner.Length > 0 && char.IsLetter(owner[0]))
                        { wallTag = owner.ToUpperInvariant(); bayTag = ManagedAssembly.Bay.ToUpperInvariant(); }
                        break;
                    case "Floor":
                        if (int.TryParse(owner, out int fn) && fn > 0) floorTag = fn.ToString();
                        break;
                }
                if (bentTag.Length + wallTag.Length + floorTag.Length > 0) frame = ManagedAssembly.FrameTag;
            }

            if (frame == null)
            {
                // Frame tag: default to the first selected timber's existing frame, else "A".
                string defFrame = "A";
                Module1.DataStructure first = Module1.GetXdata(ids[0]);
                if (first != null && !string.IsNullOrEmpty(first.FrameTag)) defFrame = first.FrameTag;
                PromptResult fr = ed.GetString(
                    new PromptStringOptions("\nFrame tag: ") { DefaultValue = defFrame, AllowSpaces = false });
                if (fr.Status != PromptStatus.OK) return;
                frame = string.IsNullOrWhiteSpace(fr.StringResult) ? defFrame : fr.StringResult.Trim();

                // Owner kind: a numbered bent, a lettered wall (-> optional bay), or a numbered floor.
                var pko = new PromptKeywordOptions("\nAssign as");   // API appends "[Bent/Wall/Floor] <Bent>:"
                pko.Keywords.Add("Bent");
                pko.Keywords.Add("Wall");
                pko.Keywords.Add("Floor");
                pko.Keywords.Default = "Bent";
                PromptResult kr = ed.GetKeywords(pko);
                if (kr.Status != PromptStatus.OK) return;

                if (kr.StringResult == "Bent")
                {
                    // A bare number owns the timber ("2"); a grid INTERSECTION ("2C") also stamps the
                    // post-style GridLabel -- how a free post joins the frame at bent 2 x wall C.
                    PromptResult br = ed.GetString(new PromptStringOptions(
                        "\nBent number (or intersection, e.g. 2C): ") { DefaultValue = "1", AllowSpaces = false });
                    if (br.Status != PromptStatus.OK) return;
                    SplitIntersection(br.StringResult, out bentTag, out colTag);
                    if (bentTag.Length == 0) { ed.WriteMessage("\nTAssign: a bent must lead with its number."); return; }
                }
                else if (kr.StringResult == "Wall")
                {
                    PromptResult wr = ed.GetString(
                        new PromptStringOptions("\nWall letter: ") { DefaultValue = "A", AllowSpaces = false });
                    if (wr.Status != PromptStatus.OK) return;
                    wallTag = (string.IsNullOrWhiteSpace(wr.StringResult) ? "A" : wr.StringResult.Trim()).ToUpperInvariant();

                    PromptResult yr = ed.GetString(
                        new PromptStringOptions("\nBay (Roman numeral, blank for none): ") { AllowSpaces = false });
                    if (yr.Status != PromptStatus.OK) return;
                    bayTag = (yr.StringResult ?? "").Trim().ToUpperInvariant();
                }
                else   // Floor: floor-system members (joists, summers), level numbered bottom-up
                {
                    PromptIntegerResult lr = ed.GetInteger(new PromptIntegerOptions("\nFloor number (1 = first): ")
                    { DefaultValue = 1, LowerLimit = 1, AllowNegative = false, AllowZero = false });
                    if (lr.Status != PromptStatus.OK) return;
                    floorTag = lr.Value.ToString();
                }
            }

            // Owner-addressed labels (FAM-owner-seq, e.g. J-1-1 / P-A-1): minted in run order before
            // the patch so the loop below writes tags + label in one SetXdata. A grid INTERSECTION
            // assign skips the mint -- the intersection itself is the address (P-2C below).
            Dictionary<ObjectId, string> mint = colTag.Length > 0
                ? new Dictionary<ObjectId, string>()
                : MintOwnerLabels(db, ids, frames, bentTag, wallTag, bayTag, floorTag);

            // In-place XData patch (no rebuild): set the grouping tags on each timber + move it to the
            // group layer so it isolates with its bent/wall/floor.
            string groupLayer = ManagedTimber.GroupLayer(frame, bentTag, wallTag, floorTag);
            int n = 0;
            using (doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                ObjectId lid = ManagedTimber.EnsureFrameLayer(tr, db, groupLayer);
                foreach (ObjectId id in ids)
                    if (tr.GetObject(id, OpenMode.ForWrite) is Entity ent) ent.LayerId = lid;
                tr.Commit();
            }
            foreach (ObjectId id in ids)
            {
                Module1.DataStructure xd = Module1.GetXdata(id);
                if (xd == null) continue;
                // A FULLY unassigned timber cannot be a skeleton member (the emitter stamps every
                // emit), so its first assignment also marks it FREE -- hand-placed timbers from
                // before the marker existed gain regenerate protection the moment they're assigned.
                // An already-assigned timber is left as-is (it could be a skeleton member re-owned).
                if (string.IsNullOrEmpty(xd.FrameTag) && string.IsNullOrEmpty(xd.BentNumber)
                    && string.IsNullOrEmpty(xd.WallTag) && string.IsNullOrEmpty(xd.BayTag)
                    && string.IsNullOrEmpty(xd.FloorTag) && string.IsNullOrEmpty(xd.GridLabel))
                    xd.Free = "1";
                xd.FrameTag = frame;
                xd.BentNumber = bentTag;
                xd.WallTag = wallTag;
                xd.BayTag = bayTag;
                xd.FloorTag = floorTag;
                if (mint.TryGetValue(id, out string label)) xd.GridLabel = label;
                else if (colTag.Length > 0)
                {
                    // Grid intersection, TYPE-FIRST like the emitter (P-2C beside the skeleton's
                    // KP-2C -- distinct labels, so the shop-map dedup labels both). FamilyFor
                    // guarantees the prefix (unknown types use their initial).
                    xd.GridLabel = FamilyFor(xd.Type) + "-" + bentTag + colTag;
                }
                Module1.SetXdata(id, xd);
                n++;
            }

            string target = bentTag.Length > 0
                ? "Bent " + bentTag + (colTag.Length > 0 ? " (grid " + bentTag + colTag + ")" : "")
                : wallTag.Length > 0 ? "Wall " + wallTag + (bayTag.Length > 0 ? " / Bay " + bayTag : "")
                : "Floor " + floorTag;
            // Braces are label-by-symbol, skipped by the owner-seq mint above -- re-derive their
            // *, ** grouping from the whole model so a just-assigned brace shows its symbol, not a blank.
            RelabelBraces(db);

            ed.WriteMessage("\nTAssign: " + n + " timber(s) -> Frame " + frame + " / " + target
                + (mint.Count > 0 ? " (" + mint.Count + " label(s) minted)" : "")
                + (skipped > 0 ? " (skipped " + skipped + " non-managed)" : "") + ".");
            ManagedAssembly.RaiseApplied();   // an open Frame Browser refreshes its rows
        }

        // The free families TAssign mints owner-addressed labels for (label grammar FAM-OWNER-SEQ;
        // hierarchy: floor-system members belong to their FLOOR -> J-1-1..n). Editor-local map -- the
        // generator's FamilyCode table stays on its side of the boundary.
        private static readonly Dictionary<string, string> OwnerLabelFamilies =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            { { "Joist", "J" }, { "Summer", "SB" } };

        // Bent family codes for the grid-intersection mint (P-2C, KP-2C) -- the editor-local mirror of
        // the generator's BentFamilyCode (the boundary keeps the tables separate on purpose). Unknown
        // roles mint the bare anchor.
        private static readonly Dictionary<string, string> BentLabelFamilies =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            { { "Post", "P" }, { "KingPost", "KP" }, { "QueenPost", "QP" }, { "Rafter", "RF" },
              { "Strut", "ST" }, { "VStrut", "VS" }, { "HBeam", "HB" }, { "HPost", "HP" },
              { "Girt", "G" }, { "Brace", "B" }, { "Ridge", "RG" }, { "Common", "C" },
              { "Purlin", "PU" }, { "EaveGirt", "EG" }, { "FloorGirt", "FG" } };

        // The label family for ANY type: the owner table, the bent table, else the type's initial
        // letter uppercased -- every TAssign label carries a family prefix (Robert's call; the bare
        // anchor read as unlabeled).
        private static string FamilyFor(string type)
        {
            if (string.IsNullOrWhiteSpace(type)) return "T";
            if (OwnerLabelFamilies.TryGetValue(type, out string f)) return f;
            if (BentLabelFamilies.TryGetValue(type, out f)) return f;
            return char.ToUpperInvariant(type.Trim()[0]).ToString();
        }

        // Mint FAM-<owner>-<seq> GridLabels for the selected family members. Owner preference: floor,
        // else bay, else wall, else bent. Sequence runs ALONG THE ROW (midpoints sorted on the
        // horizontal direction across the members, oriented +X/+Y so numbering is reproducible) and
        // continues after the highest existing sequence in the same group among UNSELECTED timbers --
        // so adding a second field to a floor extends the count, while re-assigning the whole field
        // renumbers it.
        private static Dictionary<ObjectId, string> MintOwnerLabels(Database db, List<ObjectId> ids,
            Dictionary<ObjectId, ManagedTimber.TFrame> frames, string bentTag, string wallTag, string bayTag,
            string floorTag)
        {
            var mint = new Dictionary<ObjectId, string>();
            string owner = floorTag.Length > 0 ? floorTag
                : bayTag.Length > 0 ? bayTag : wallTag.Length > 0 ? wallTag : bentTag;
            if (owner.Length == 0) return mint;

            var byFam = new Dictionary<string, List<ObjectId>>(StringComparer.Ordinal);
            foreach (ObjectId id in ids)
            {
                Module1.DataStructure xd = Module1.GetXdata(id);
                if (xd == null || string.IsNullOrEmpty(xd.Type)) continue;
                // Braces are the group-symbol family (*, **, ...): they are NEVER owner-seq numbered.
                // RelabelBraces (called after the assign) re-derives their symbol by size+shape; minting
                // B-owner-seq here would clobber it (B-3-1 instead of *).
                if (string.Equals(xd.Type, "Brace", StringComparison.OrdinalIgnoreCase)) continue;
                string fam = FamilyFor(xd.Type);   // EVERY other type mints FAM-owner-seq
                if (!byFam.TryGetValue(fam, out var list)) byFam[fam] = list = new List<ObjectId>();
                list.Add(id);
            }
            if (byFam.Count == 0) return mint;

            List<ManagedTimber.ShopInfo> all = ManagedTimber.EnumerateForShop(db);
            var selected = new HashSet<ObjectId>(ids);
            foreach (var kv in byFam)
            {
                string prefix = kv.Key + "-" + owner + "-";
                int next = 1;
                foreach (var t in all)
                {
                    if (selected.Contains(t.Id)) continue;
                    string gl = t.GridLabel ?? "";
                    if (!gl.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                    if (int.TryParse(gl.Substring(prefix.Length), out int seq) && seq >= next) next = seq + 1;
                }

                Vector3d run = Vector3d.ZAxis.CrossProduct(frames[kv.Value[0]].Z);
                if (run.Length < 1e-6) run = Vector3d.XAxis;         // vertical member: any stable order
                run = run.GetNormal();
                if (run.X < -1e-9 || (Math.Abs(run.X) < 1e-9 && run.Y < 0)) run = -run;
                kv.Value.Sort((a, b) => MidAlong(frames[a], run).CompareTo(MidAlong(frames[b], run)));
                foreach (ObjectId id in kv.Value) mint[id] = prefix + (next++);
            }
            return mint;
        }

        private static double MidAlong(ManagedTimber.TFrame f, Vector3d dir)
            => (f.O + f.Z * (f.L / 2.0)).GetAsVector().DotProduct(dir);

        // A 1-2 letter column string, uppercased; anything else -> "".
        private static string LettersOnly(string s)
        {
            s = (s ?? "").Trim().ToUpperInvariant();
            if (s.Length == 0 || s.Length > 2) return "";
            foreach (char c in s) if (!char.IsLetter(c)) return "";
            return s;
        }

        // "2C" -> bent "2" + column "C"; "2" -> bent "2", no column; "1.1B" -> "1.1" + "B" (sub-bent
        // lines carry dots). Digits lead, a 1-2 letter column may trail; anything else parses empty.
        private static void SplitIntersection(string s, out string bent, out string col)
        {
            s = (s ?? "").Trim().ToUpperInvariant();
            int i = 0;
            while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '.')) i++;
            bent = s.Substring(0, i);
            col = s.Substring(i);
            foreach (char c in col)
                if (!char.IsLetter(c)) { col = ""; break; }
            if (col.Length > 2 || bent.Length == 0) col = "";
        }

        // Span two timbers: pick two managed timbers; the facing faces are found from their stored
        // frames and a timber of the current W/D is dropped flush in the gap, perpendicular to both
        // faces (so its ends bear flush -> TScan finds the nodes). Declines if no facing overlap.
        // (Milestone 1: the girt centers on the face overlap; choosing which faces / the height, and
        // the angled match-face miter, come with subentity face-picking later.)
        [CommandMethod("TSpan")]
        public static void SpanFaces()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            if (!GetSection(ed, out double w, out double d, out string type)) return;

            if (!PickTimber(ed, db, "\nPick first timber: ", out _, out ManagedTimber.TFrame A)) return;
            if (!PickTimber(ed, db, "\nPick second timber: ", out _, out ManagedTimber.TFrame B)) return;

            if (!ManagedTimber.FindFacingPair(A, B, out ManagedTimber.TFace fa, out ManagedTimber.TFace fb, out double gap))
            { ed.WriteMessage("\nThose timbers have no facing, overlapping faces to span."); return; }

            // Lateral (fa.V) + gap (fa.N) placement: centre on the overlap. The position ALONG the
            // timbers (fa.U) is set by the jig -- the UCS Y=0 datum initially, then dragged.
            Vector3d off = (fb.C - fa.C) - (fb.C - fa.C).DotProduct(fa.N) * fa.N;
            Point3d origin = fa.C + off * 0.5;

            // DEPTH rides the more-vertical in-plane axis. On post sides the rail (fa.U = the host's
            // length axis) is vertical and depth lies on it (a girt between posts); on girt sides the
            // rail is horizontal and depth lies on fa.V (a summer between bent girts) -- the fixed
            // fa.U assignment was rolling that section 90 degrees.
            bool railVertical = Math.Abs(fa.U.GetNormal().DotProduct(Vector3d.ZAxis))
                             >= Math.Abs(fa.V.GetNormal().DotProduct(Vector3d.ZAxis));
            Vector3d dAxis = railVertical ? fa.U : fa.V;
            Vector3d wAxis = railVertical ? fa.V : fa.U;

            // Ghost the span and let the user set its height along the post rail, measured from the UCS
            // origin (datum s=0 = base). The girt's Center/Bottom/Top face lands on that line. Height is
            // a rail distance: TYPE the number (exact, any UCS) or drag/snap up the rail; the drag loop
            // handles the Center/Bottom/Top keywords. A free cursor reads height only in an elevation UCS.
            CoordinateSystem3d ucs = ed.CurrentUserCoordinateSystem.CoordinateSystem3d;
            var jig = new SpanJig(origin, fa, gap, railVertical ? d : w, railVertical ? w : d, ucs.Origin);
            bool canPickHeight = Math.Abs(fa.U.GetNormal().DotProduct(ucs.Zaxis.GetNormal())) < 0.5;
            if (!canPickHeight)
                ed.WriteMessage("\nTip: a cursor can't read the height in this UCS (post is end-on) -- " +
                                "type the height, or switch to an elevation UCS (Bent/Wall).");

            while (true)
            {
                PromptResult pr = ed.Drag(jig);
                if (pr.Status == PromptStatus.Keyword) { jig.SetJustify(pr.StringResult); continue; }
                if (pr.Status != PromptStatus.OK) return;
                break;
            }

            ObjectId id = ManagedTimber.DrawBox(jig.Origin, fa.N, dAxis, wAxis, gap, d, w, type, "", "matchface", "matchface");
            ed.WriteMessage("\nTSpan: " + type + " " + (int)w + "x" + (int)d + "x" + gap.ToString("0.#") +
                            " " + jig.Mode + " @ height " + jig.LineY.ToString("0.#") + " (" + id.Handle + ").");
        }

        // Connect two EXPLICITLY-picked faces with a member. You pick the exact face on each timber
        // (native subentity highlight), so multi-candidate ambiguity (brace above/below a girt) is your
        // choice, not a guess. Two cases, chosen automatically from the picked faces:
        //   FACING (opposing-parallel) faces -- a girt between two posts: a member fills the
        //     perpendicular gap with SQUARE ends (flush).
        //   ANGLED faces -- a brace from a post inner face to a girt underside: the member runs
        //     diagonally and each end is MITERED flush to its face plane, so TScan finds the nodes.
        [CommandMethod("TJoin")]
        public static void Join()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            if (!GetSection(ed, out double w, out double d, out string type)) return;

            if (!ManagedTimber.PickFace(ed, db, "\nPick the FIRST face: ", out ObjectId idA, out ManagedTimber.TFace fa)) return;
            if (!ManagedTimber.PickFace(ed, db, "\nPick the SECOND face: ", out ObjectId idB, out ManagedTimber.TFace fb)) return;

            double facing = fa.N.DotProduct(fb.N);
            if (facing > 0.99)
            { ed.WriteMessage("\nThose two faces point the SAME way -- pick faces that look toward each other."); return; }

            ObjectId id;
            if (facing < -0.99)
            {
                // Opposing-parallel: square-ended span filling the perpendicular gap, centred on the
                // overlap. Depth rides the more-vertical in-plane axis (same rule as TSpan -- a fixed
                // fa.U assignment rolled the section 90 degrees on girt-side spans).
                double gap = (fb.C - fa.C).DotProduct(fa.N);
                if (gap <= 1e-6) { ed.WriteMessage("\nNo positive gap between the faces."); return; }
                Vector3d off = (fb.C - fa.C) - gap * fa.N;
                Point3d origin = fa.C + off * 0.5;
                bool railVertical = Math.Abs(fa.U.GetNormal().DotProduct(Vector3d.ZAxis))
                                 >= Math.Abs(fa.V.GetNormal().DotProduct(Vector3d.ZAxis));
                id = ManagedTimber.DrawBox(origin, fa.N, railVertical ? fa.U : fa.V,
                                           railVertical ? fa.V : fa.U, gap, d, w, type, "", "butt", "butt");
                ed.WriteMessage("\nTJoin (square): " + type + " " + (int)w + "x" + (int)d + "x" + gap.ToString("0.#") +
                                " (" + id.Handle + ").");
            }
            else
            {
                // Angled: knee brace seated in the corner, each end mitered flush to its picked face. The
                // runs are how far the foot/head sit back from the corner along each picked face. Body
                // centres let the geometry place the foot/head on the OPEN side of each face (away from
                // the other timber's bulk) regardless of how the stored face normals are signed.
                if (!ManagedTimber.TryReadFrame(db, idA, out ManagedTimber.TFrame frA) ||
                    !ManagedTimber.TryReadFrame(db, idB, out ManagedTimber.TFrame frB))
                { ed.WriteMessage("\nCouldn't read the timber frames."); return; }
                Point3d bodyA = frA.O + frA.Z * (frA.L / 2.0);
                Point3d bodyB = frB.O + frB.Z * (frB.L / 2.0);

                // The palette (ManagedBrace) initializes the foot/head runs ONCE; the ghost previews the
                // brace and the user approves with Enter or cancels with Esc. To resize, set the palette
                // first and re-run (the size is read once at command start, not tracked live).
                double footRun = ManagedBrace.HasCurrent ? ManagedBrace.FootRun : 18.0;
                double headRun = ManagedBrace.HasCurrent ? ManagedBrace.HeadRun : 18.0;
                if (!ManagedTimber.TryBraceFrame(fa, fb, d, w, footRun, headRun, bodyA, bodyB,
                                                 out ManagedTimber.TFrame bframe))
                { ed.WriteMessage("\nThose faces don't form a brace corner."); return; }

                bool place;
                using (Solid3d ghost = ManagedTimber.BuildFramedSolid(bframe))
                {
                    ghost.ColorIndex = 5;   // blue, colour-blind safe
                    TransientManager tm = TransientManager.CurrentTransientManager;
                    var ints = new IntegerCollection();
                    tm.AddTransient(ghost, TransientDrawingMode.DirectShortTerm, 128, ints);
                    try
                    {
                        // No "[Yes/No] <Yes>" in the message -- PromptKeywordOptions appends that itself
                        // (putting it in the text too made the prompt render doubled).
                        var pko = new PromptKeywordOptions(
                            "\nPlace the brace (foot " + footRun.ToString("0.#") + ", head " +
                            headRun.ToString("0.#") + " -- set size on the palette)? ");
                        pko.Keywords.Add("Yes");
                        pko.Keywords.Add("No");
                        pko.Keywords.Default = "Yes";
                        pko.AllowNone = true;   // Enter = Yes
                        PromptResult kr = ed.GetKeywords(pko);
                        place = kr.Status == PromptStatus.None ||
                                (kr.Status == PromptStatus.OK && kr.StringResult == "Yes");
                    }
                    finally { tm.EraseTransient(ghost, ints); }
                }
                if (!place) { ed.WriteMessage("\nBrace cancelled."); return; }

                id = ManagedTimber.DrawMiteredBrace(fa, fb, d, w, footRun, headRun, type, "", bodyA, bodyB);
                if (id.IsNull) { ed.WriteMessage("\nCouldn't build a brace between those faces."); return; }
                ed.WriteMessage("\nTJoin (knee brace): " + type + " " + (int)w + "x" + (int)d +
                                " foot " + footRun.ToString("0.#") + " head " + headRun.ToString("0.#") +
                                " (" + id.Handle + ").");
            }
        }

        // Fit the END of an existing timber onto a target face: pick the timber's end face (the one to
        // move), then the face it should land on. The picked end is trimmed OR extended along the
        // timber's own axis until it lands flush on the target plane -- square if the target is square
        // to the axis, mitered if angled (a rafter foot on a plate, a strut into a brace). The other end
        // stays put. The solid is rebuilt; type/designation carry over. TScan then finds the new node.
        [CommandMethod("TFit")]
        public static void Fit()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            if (!ManagedTimber.PickFace(ed, db, "\nPick the END to move (the timber's end face): ",
                out ObjectId mid, out ManagedTimber.TFace endFace)) return;
            if (!ManagedTimber.TryReadFrame(db, mid, out ManagedTimber.TFrame f))
            { ed.WriteMessage("\nNot a managed timber."); return; }

            // Must be an END face (normal roughly along the LENGTH axis Z), not a side face.
            Vector3d axis = f.Z.GetNormal();
            if (Math.Abs(endFace.N.DotProduct(axis)) < 0.5)
            { ed.WriteMessage("\nThat's a SIDE face -- pick the timber's END face (the end you want to land)."); return; }
            bool isNear = endFace.C.DistanceTo(f.O) < endFace.C.DistanceTo(f.O + f.Z * f.L);

            if (!ManagedTimber.PickFace(ed, db, "\nPick the TARGET face to land on: ",
                out ObjectId tid, out ManagedTimber.TFace target)) return;
            if (tid == mid) { ed.WriteMessage("\nPick a target on a DIFFERENT timber."); return; }

            // Crossing of the timber centreline (O + t*Z) with the target plane.
            double denom = f.Z.DotProduct(target.N);
            if (Math.Abs(denom) < 1e-6)
            { ed.WriteMessage("\nThe timber runs parallel to that face -- it can't land there."); return; }
            double t = (target.C - f.O).DotProduct(target.N) / denom;
            Point3d crossing = f.O + f.Z * t;

            ManagedTimber.TFrame nf = f;
            if (isNear)
            {
                Point3d farC = f.O + f.Z * f.L;
                double newL = (farC - crossing).DotProduct(f.Z);
                if (newL <= 1e-3) { ed.WriteMessage("\nThat would collapse the timber past its far end."); return; }
                nf.O = crossing; nf.L = newL; nf.NearN = target.N.Negate();   // far end unchanged
            }
            else
            {
                double newL = (crossing - f.O).DotProduct(f.Z);
                if (newL <= 1e-3) { ed.WriteMessage("\nThat would collapse the timber past its near end."); return; }
                nf.L = newL; nf.FarN = target.N.Negate();                     // near end unchanged
            }

            // Tag the fitted end "matchface" on the STORED xdata, then rebuild through RebuildFromFrame
            // -- which preserves the timber's whole identity (frame/owner tags, grid label, group layer,
            // production number, floor + free markers, persisted joint specs). The old bare
            // DrawFramedSolid redraw silently stripped all of it on every TFit.
            Module1.DataStructure old = Module1.GetXdata(mid);
            string type = string.IsNullOrEmpty(old?.Type) ? "Timber" : old.Type;
            if (old != null)
            {
                if (isNear) old.JointNear = "matchface"; else old.JointFar = "matchface";
                Module1.SetXdata(mid, old);
            }

            ObjectId nid = ManagedTimber.RebuildFromFrame(mid, nf);
            ed.WriteMessage("\nTFit: " + type + " " + (isNear ? "near" : "far") +
                            " end fitted; new length " + nf.L.ToString("0.#") + " (" + nid.Handle + ").");
        }

        // Re-section ONE managed timber in place: pick any managed timber (skeleton OR free -- the editor
        // is one surface over the managed set), then change its W x D. Placement (O/axes/L), end cuts,
        // feature cuts, and the grouping tags (frame/bent/bay/wall/grid label) all carry over via
        // RegenerateSection. The prompts default to the timber's CURRENT section, so a quick change is one
        // or two keystrokes; Enter on both leaves it unchanged. Type is kept (re-section is a section edit,
        // not a retype). The solid is rebuilt (erase + redraw -> a new handle); re-run TScan afterward as
        // the side faces may have moved.
        [CommandMethod("TSection")]
        public static void Section()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            if (!PickTimber(ed, db, "\nPick the timber to re-section: ", out ObjectId id, out ManagedTimber.TFrame f)) return;

            if (!GetPositive(ed, "Width", f.W, out double newW)) return;
            if (!GetPositive(ed, "Depth", f.D, out double newD)) return;

            if (Math.Abs(newW - f.W) < 1e-6 && Math.Abs(newD - f.D) < 1e-6)
            { ed.WriteMessage("\nTSection: section unchanged (" + (int)f.W + "x" + (int)f.D + ")."); return; }

            ObjectId nid = ManagedTimber.RegenerateSection(id, newW, newD, "");   // "" = keep existing type
            if (nid.IsNull) { ed.WriteMessage("\nTSection: could not re-section that timber."); return; }

            Module1.DataStructure xd = Module1.GetXdata(nid);
            string type = string.IsNullOrEmpty(xd?.Type) ? "Timber" : xd.Type;
            ed.WriteMessage("\nTSection: " + type + " -> " + (int)newW + "x" + (int)newD +
                            " (" + nid.Handle + "). Re-run TScan to refresh nodes.");
        }

        // Apply a keyed hook scarf to ONE full-length beam at a picked point, splitting it into two
        // interlocking managed timbers (the shop joint from drawscarf.lsp). Stage A: the splayed/squinted
        // BLADE only -- tables + key come in Stages B/C. Each piece overlaps the other by the scarf
        // length, so the assembled run stays the beam's drawn length while each piece's stored Size is its
        // FULL physical length including the scarf tongue (printed for manufacturing). TScan nodes at the
        // scarf centre.
        [CommandMethod("TScarf")]
        public static void ScarfSplice()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            if (!PickTimber(ed, db, "\nPick the beam to scarf: ", out ObjectId beamId, out ManagedTimber.TFrame f)) return;

            double l = 3.0 * f.D;
            if (l + 1.0 > f.L)
            { ed.WriteMessage("\nThe beam is too short for a " + l.ToString("0.#") + "\" scarf."); return; }

            // Ghost the scarf footprint sliding along the beam; the cursor projects onto the beam axis
            // (clamped to fit), and the Length keyword resizes the region live. A pick commits.
            var jig = new ScarfJig(f, l);
            while (true)
            {
                PromptResult dr = ed.Drag(jig);
                if (dr.Status == PromptStatus.Keyword)
                {
                    if (dr.StringResult == "Length")
                    {
                        var lopts = new PromptDistanceOptions("\nScarf length: ")
                        { DefaultValue = jig.Len, UseDefaultValue = true, AllowNegative = false, AllowZero = false };
                        PromptDoubleResult lr = ed.GetDistance(lopts);
                        if (lr.Status == PromptStatus.OK)
                        {
                            if (lr.Value + 1.0 > f.L)
                                ed.WriteMessage("\nThe beam is too short for a " + lr.Value.ToString("0.#") + "\" scarf.");
                            else jig.SetLength(lr.Value);
                        }
                    }
                    continue;
                }
                if (dr.Status != PromptStatus.OK) return;
                break;
            }
            double xc = jig.Xc;
            l = jig.Len;
            double h = l / 2.0;
            if (xc - h < 0.5 || xc + h > f.L - 0.5)
            { ed.WriteMessage("\nThe scarf (" + l.ToString("0.#") + "\") doesn't fit within the beam at that point."); return; }

            Module1.DataStructure od = Module1.GetXdata(beamId);
            string type = string.IsNullOrEmpty(od?.Type) ? "Timber" : od.Type;
            string desg = od?.Designation ?? "";
            Point3d cs = f.O + f.Z * xc;

            // Piece frames: piece1 = [0, xc+h], piece2 = [xc-h, L] along Z -- they overlap over the scarf.
            ManagedTimber.TFrame f1 = f; f1.L = xc + h; f1.NearN = f.Z.Negate(); f1.FarN = f.Z;
            ManagedTimber.TFrame f2 = f; f2.O = f.O + f.Z * (xc - h); f2.L = f.L - (xc - h); f2.NearN = f.Z.Negate(); f2.FarN = f.Z;

            ObjectId nP1, nP2;
            using (doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                var btr = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);

                // Tongue1 (left piece) + tongue2 = 180-degree point reflection about the scarf centre.
                ObjectId t1id = ManagedTimber.BuildScarfTongue(tr, btr, f, xc, l);
                var tongue1 = (Solid3d)tr.GetObject(t1id, OpenMode.ForWrite);
                var tongue2 = (Solid3d)tongue1.Clone();
                btr.AppendEntity(tongue2); tr.AddNewlyCreatedDBObject(tongue2, true);
                tongue2.TransformBy(Matrix3d.Rotation(Math.PI, f.X, cs));   // reflect about the WIDTH axis

                // Un-scarfed stubs, then union each piece's tongue onto its stub.
                Solid3d leftStub = ManagedTimber.MakeBoxSolid(f, 0, xc - h);
                Solid3d rightStub = ManagedTimber.MakeBoxSolid(f, xc + h, f.L);
                nP1 = btr.AppendEntity(leftStub); tr.AddNewlyCreatedDBObject(leftStub, true);
                nP2 = btr.AppendEntity(rightStub); tr.AddNewlyCreatedDBObject(rightStub, true);
                leftStub.BooleanOperation(BooleanOperationType.BoolUnite, tongue1);
                rightStub.BooleanOperation(BooleanOperationType.BoolUnite, tongue2);

                // Stage B: bearing tables (locks). Each 1.5x2 abutment notch is filled across the OUTER
                // width bands [+1,+W/2] and [-W/2,-1] ((W-2)/2 each); the middle 2" band [-1,+1] stays
                // open as the key slot (Stage C). Bottom-right abutment -> piece2; the 180-rotated
                // top-left abutment -> piece1. Locks overrun Mx into their stub for a clean union.
                double D = f.D, W = f.W, Mx = 0.25;
                // bottom-right (piece2 / rightStub)
                UnionBox(tr, btr, rightStub, f, xc + h - 1.5, xc + h + Mx, -D / 2.0, -D / 2.0 + 2.0, 1.0, W / 2.0);
                UnionBox(tr, btr, rightStub, f, xc + h - 1.5, xc + h + Mx, -D / 2.0, -D / 2.0 + 2.0, -W / 2.0, -1.0);
                // top-left (piece1 / leftStub)
                UnionBox(tr, btr, leftStub, f, xc - h - Mx, xc - h + 1.5, D / 2.0 - 2.0, D / 2.0, 1.0, W / 2.0);
                UnionBox(tr, btr, leftStub, f, xc - h - Mx, xc - h + 1.5, D / 2.0 - 2.0, D / 2.0, -W / 2.0, -1.0);

                // Stage C: the key band -- the middle 2" width band [-1,+1] of each abutment, drawn as the
                // ALTERNATING piece's material (per drawscarf.lsp): bottom-right -> piece1, top-left ->
                // piece2. It interlocks through the other piece's tables (integral, not a loose wedge).
                // It runs flush to the abutment face (no overrun into the mating piece) and Mx into its
                // own tongue for a clean union.
                UnionBox(tr, btr, leftStub, f, xc + h - 1.5 - Mx, xc + h, -D / 2.0, -D / 2.0 + 2.0, -1.0, 1.0);
                UnionBox(tr, btr, rightStub, f, xc - h, xc - h + 1.5 + Mx, D / 2.0 - 2.0, D / 2.0, -1.0, 1.0);

                // Drop boolean OPERATION HISTORY so the scarf pieces save cleanly (see BuildFramedSolid).
                try { leftStub.RecordHistory = false; leftStub.ShowHistory = false;
                      rightStub.RecordHistory = false; rightStub.ShowHistory = false; } catch { }

                ManagedTimber.WriteFrameXrecord(tr, leftStub, f1);
                ManagedTimber.WriteFrameXrecord(tr, rightStub, f2);

                // The key is INTEGRAL: Stage C above unions the middle width band as the alternating
                // piece's material (per drwscarf.lsp's scarfkey1/scarfkey2), so the two halves interlock
                // through each other's tables. There is no loose key wedge -- and the keyway HOOK + VOID
                // are formed by the tongue profile (BuildScarfTongue) + its 180 rotation, not by a cut.

                ((Entity)tr.GetObject(beamId, OpenMode.ForWrite)).Erase();
                tr.Commit();
            }

            // XData (outside the txn, like the other commands) + the explicit splice node.
            string sz1 = (int)Math.Round(f.W) + "x" + (int)Math.Round(f.D) + "x" + Module1.BuyLongFeet(f1.L);
            string sz2 = (int)Math.Round(f.W) + "x" + (int)Math.Round(f.D) + "x" + Module1.BuyLongFeet(f2.L);
            // Pieces inherit the parent's free-assembly origin: scarfed FREE timbers stay regen-proof;
            // scarfed SKELETON halves stay skeleton (a regenerate replaces the unsplit member).
            var xd1 = new Module1.DataStructure(type, "", desg, sz1, "0", 0, 0, 0, f.W, f.D, f1.L, "scarf", "scarf", false);
            var xd2 = new Module1.DataStructure(type, "", desg, sz2, "0", 0, 0, 0, f.W, f.D, f2.L, "scarf", "scarf", false);
            xd1.Free = od?.Free ?? "";
            xd2.Free = od?.Free ?? "";
            Module1.SetXdata(nP1, xd1);
            Module1.SetXdata(nP2, xd2);
            ManagedTimber.WriteScarfNode(db, nP1, cs);
            ManagedTimber.WriteScarfNode(db, nP2, cs);

            ed.WriteMessage("\nTScarf: l=" + l.ToString("0.#") +
                            "  piece1 overall " + f1.L.ToString("0.#") + "\" (" + nP1.Handle + ")" +
                            ",  piece2 overall " + f2.L.ToString("0.#") + "\" (" + nP2.Handle + ").");
        }

        // Stamp the joint TYPE onto both rebuilt timbers (the same TMJointSpecs the Join pane writes), so the
        // BOM and a pane re-pick can recover the joint's elements. Serializes the preset built from the live
        // command spec. No-op for an unkeyed cut (jid 0).
        private static void StampJoint(ObjectId a, ObjectId b, int jid, ConnectionType ct)
        {
            if (jid == 0 || ct == null) return;
            string st = ct.SerializeState();
            ManagedTimber.WriteJointSpec(a, jid, st);
            ManagedTimber.WriteJointSpec(b, jid, st);
        }

        // Cut a girt -> post MORTISE & TENON (+ peg bores). Pick the TENONED timber (a girt) then the
        // MORTISED timber (a post): the girt end-cap that bears on a post SIDE face is found, the joint
        // recipe is reviewed (sticky tenon thickness / length / top + bottom shoulders / width offset, and a
        // Pegs sub-menu), the girt gets a shouldered/offset tenon, the post a matching mortise + the peg
        // bores (the shop bores only the mortise), and both solids rebuild in place. v1 handles the
        // end-into-face case (a horizontal girt into a vertical post); the cuts survive MOVE / TFit (LOCAL,
        // serialized Features/Pegs) and TScan still reports the bearing node (nominal faces unchanged).
        // Re-cutting the same girt+post REPLACES the joint (by its id); re-run per end for a both-ends girt.
        [CommandMethod("TJoint")]
        public static void JointMortiseTenon()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            if (!PickTimber(ed, db, "\nPick the TENONED timber (girt / summer): ", out ObjectId girtId, out ManagedTimber.TFrame girt)) return;
            if (!PickTimber(ed, db, "\nPick the MORTISED timber (post / carrier): ", out ObjectId postId, out ManagedTimber.TFrame post)) return;
            if (girtId == postId) { ed.WriteMessage("\nPick two different timbers."); return; }

            // The girt END-cap (Faces 0/1) that mates a post SIDE face (coplanar, opposing, overlapping).
            ManagedTimber.TFace[] gf = ManagedTimber.Faces(girt);
            ManagedTimber.TFace[] pf = ManagedTimber.Faces(post);
            bool found = false;
            ManagedTimber.TFace gEnd = default;
            for (int gi = 0; gi <= 1 && !found; gi++)
                foreach (ManagedTimber.TFace ps in pf)
                {
                    if (Math.Abs(ps.N.DotProduct(post.Z)) >= 0.5) continue;       // the post face must be a SIDE
                    if (ManagedTimber.FacesMate(gf[gi], ps, 0.25, out _)) { gEnd = gf[gi]; found = true; break; }
                }
            if (!found)
            { ed.WriteMessage("\nNo end-into-face contact -- the tenoned end must bear on a side face of the host."); return; }

            if (!ReviewJoint(ed)) return;   // review / adjust the sticky joint recipe (Enter / Cut proceeds)

            if (!ManagedTimber.GirtPostJoint(girt, post, gEnd, _joint,
                    out List<(Point3d Min, Point3d Max, bool Subtract)> features,
                    out List<(Point3d C, Vector3d Axis, double R, double Half)> pegs,
                    out List<(Point3d[] Poly, bool OnPost, double Xlo, double Xhi)> polys))
            {
                ed.WriteMessage("\nNothing to cut -- enable a tenon, housing or shoulder (or the tenon collapsed).");
                return;
            }

            if (girt.Features == null) girt.Features = new List<(Point3d, Point3d, bool, int)>();
            if (post.Features == null) post.Features = new List<(Point3d, Point3d, bool, int)>();

            // De-dup: if this contact already carries a joint, replace it (re-cut = edit) instead of
            // stacking. The girt tenon at this end identifies the joint; reuse its id (group-remove all its
            // features from both timbers), or -- for a shoulder-only joint (no tenon box) -- the shared
            // JointPolys id, or for a legacy/unkeyed (id 0) cut sweep by geometry.
            bool farEnd = gEnd.N.DotProduct(girt.Z) > 0.0;
            int ti = girt.Features.FindIndex(f => !f.Subtract &&
                         (((f.Min.Z + f.Max.Z) / 2.0 > girt.L / 2.0) == farEnd));
            int reuseId = ti >= 0 ? girt.Features[ti].Joint : 0;
            if (reuseId == 0) reuseId = ExistingRafterFootId(girt, post);   // shoulder-only (poly) joint at this pair
            int jid;
            if (reuseId != 0)
            {
                girt.Features.RemoveAll(f => f.Joint == reuseId);
                post.Features.RemoveAll(f => f.Joint == reuseId);
                post.Pegs?.RemoveAll(p => p.Joint == reuseId);            // old pegs go with the old joint
                girt.JointPolys?.RemoveAll(j => j.Joint == reuseId);      // old shoulder polys too
                post.JointPolys?.RemoveAll(j => j.Joint == reuseId);
                jid = reuseId;
            }
            else if (ti >= 0)
            {
                girt.Features.RemoveAt(ti);
                // legacy id-0 joint predates ids: drop old post subtracts overlapping any new pocket.
                post.Features.RemoveAll(f => f.Subtract &&
                    features.Exists(nf => nf.Subtract && BoxesOverlap(f.Min, f.Max, nf.Min, nf.Max)));
                jid = NextJointId(db);
            }
            else jid = NextJointId(db);

            ApplyJoint(ref girt, ref post, jid, features, pegs);
            ApplyRafterFoot(ref girt, ref post, jid, polys);   // shoulder triangle polys (shared poly applier)

            ObjectId ngirt = ManagedTimber.RebuildFromFrame(girtId, girt);
            ObjectId npost = ManagedTimber.RebuildFromFrame(postId, post);
            StampJoint(ngirt, npost, jid, ConnectionType.BoxTenon(_joint));
            ed.WriteMessage("\nTJoint: joint #" + jid + " cut -- " + JointSummary(_joint) +
                            " (girt " + ngirt.Handle + ", post " + npost.Handle + ").");
        }

        // Route a cutter result onto the two frames, stamped with the joint id: each feature box to the
        // girt (UNION) or post (SUBTRACT) by its Subtract flag; each peg cylinder to the post. Shared by
        // TJoint + TJointAll (TFrame is a struct -> ref).
        private static void ApplyJoint(ref ManagedTimber.TFrame girt, ref ManagedTimber.TFrame post, int jid,
            List<(Point3d Min, Point3d Max, bool Subtract)> features,
            List<(Point3d C, Vector3d Axis, double R, double Half)> pegs)
        {
            if (girt.Features == null) girt.Features = new List<(Point3d, Point3d, bool, int)>();
            if (post.Features == null) post.Features = new List<(Point3d, Point3d, bool, int)>();
            foreach ((Point3d Min, Point3d Max, bool Subtract) f in features)
                (f.Subtract ? post.Features : girt.Features).Add((f.Min, f.Max, f.Subtract, jid));
            if (pegs.Count > 0)
            {
                if (post.Pegs == null) post.Pegs = new List<(Point3d, Vector3d, double, double, int)>();
                foreach ((Point3d C, Vector3d Axis, double R, double Half) p in pegs)
                    post.Pegs.Add((p.C, p.Axis, p.R, p.Half, jid));
            }
        }

        // Human-readable list of the elements a JointSpec cuts (for command messages).
        private static string JointSummary(ManagedTimber.JointSpec s)
        {
            string parts = "";
            if (s.Tenon.On) parts = "tenon";
            if (s.Housing.On && s.Housing.Seat > 1e-6) parts += (parts.Length > 0 ? " + " : "") + "housing";
            if (s.Shoulder.On && s.Shoulder.Seat > 1e-6) parts += (parts.Length > 0 ? " + " : "") + "shoulder";
            if (s.Peg.Count > 0) parts += (parts.Length > 0 ? " + " : "") + s.Peg.Count + " peg(s)";
            return parts.Length > 0 ? parts : "(none)";
        }

        // Role sets for the batch auto-cut: a girt-family END that bears on a Post SIDE face gets a M&T;
        // a Post FOOT that bears on a Sill side (its top) gets the stub tenon (floor systems phase 3).
        private static readonly HashSet<string> GirtRoles = new HashSet<string> { "Girt", "EaveGirt", "FloorGirt" };
        private static readonly HashSet<string> PostRoles = new HashSet<string> { "Post" };
        private static readonly HashSet<string> SillRoles = new HashSet<string> { "Sill" };
        private static readonly HashSet<string> SummerRoles = new HashSet<string> { "Summer" };
        // A summer's end can die into a bent floor girt, the tie, or (first floor) a sill.
        private static readonly HashSet<string> SummerHostRoles = new HashSet<string> { "Girt", "FloorGirt", "Sill" };
        private static readonly HashSet<string> JoistRoles = new HashSet<string> { "Joist" };
        // A joist's end dies into any floor carrier.
        private static readonly HashSet<string> JoistHostRoles = new HashSet<string> { "Girt", "FloorGirt", "EaveGirt", "Summer", "Sill" };

        // Batch-cut the frame's end->side joinery, DELIBERATELY: the first prompt scopes the batch to
        // All timbers or a SELECTION -- the selected timbers are the ones that GET joints (the male
        // side: girt ends, post feet, summer ends, joist ends); hosts are always found drawing-wide.
        // Passes, each with its own sticky recipe, reviewed only when its role is in scope:
        //   girt-family end -> post side  (mortise & tenon + pegs; _joint)
        //   post foot -> sill             (short unpegged stub; _sillJoint)
        //   summer end -> girt/sill       (tusk tenon; _summerJoint)
        //   joist end -> carrier          (housed dovetail; _joistDove -- the deliberate half of TJoist)
        // Contacts that already carry a joint are SKIPPED (idempotent -- safe to re-run after manual
        // tweaks); a host fed by several members rebuilds once.
        [CommandMethod("TJointAll")]
        public static void JointAll()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            // DELIBERATE scope (Robert's rule: joinery is applied to selected timbers or groups).
            HashSet<ObjectId> scope = null;
            var sko = new PromptKeywordOptions("\nCut joinery for") { AllowNone = true };
            sko.Keywords.Add("All");
            sko.Keywords.Add("Select");
            sko.Keywords.Default = "All";
            PromptResult skr = ed.GetKeywords(sko);
            if (skr.Status != PromptStatus.OK && skr.Status != PromptStatus.None) return;
            if (skr.Status == PromptStatus.OK && skr.StringResult == "Select")
            {
                var filter = new SelectionFilter(new[] { new TypedValue((int)DxfCode.Start, "3DSOLID") });
                var pso = new PromptSelectionOptions { MessageForAdding = "\nSelect the timbers to joint: " };
                PromptSelectionResult sel = ed.GetSelection(pso, filter);
                if (sel.Status != PromptStatus.OK) return;
                scope = new HashSet<ObjectId>(sel.Value.GetObjectIds());
            }

            var all = ManagedTimber.EnumerateWithRole(db);
            // Working frames carry the accumulating Features/Pegs; geometry (O/axes/L/D/W) never changes and
            // Faces() ignore features, so mates can be found against the originals throughout the pass.
            var work = new Dictionary<ObjectId, ManagedTimber.TFrame>();
            foreach ((ObjectId Id, ManagedTimber.TFrame F, string Role) t in all) work[t.Id] = t.F;
            var dirty = new HashSet<ObjectId>();
            var cuts = new List<(ObjectId girt, ObjectId post, int jid, ConnectionType ct)>();   // for the joint-type stamp
            int nextId = NextJointId(db);
            int cut = 0, skipped = 0, failed = 0;

            bool InScope(ObjectId id) => scope == null || scope.Contains(id);
            bool RolePresent(HashSet<string> roles)
            {
                foreach ((ObjectId Id, ManagedTimber.TFrame F, string Role) t in all)
                    if (roles.Contains(t.Role) && InScope(t.Id)) return true;
                return false;
            }

            // One end->side pass: every in-scope `maleRoles` END that bears on a `hostRoles` SIDE face
            // gets the M&T from `spec` (already-jointed contacts skip). Girt / sill / summer passes.
            void Pass(HashSet<string> maleRoles, HashSet<string> hostRoles,
                ManagedTimber.JointSpec spec, ConnectionType ct)
            {
                foreach ((ObjectId Id, ManagedTimber.TFrame F, string Role) g in all)
                {
                    if (!maleRoles.Contains(g.Role) || !InScope(g.Id)) continue;
                    ManagedTimber.TFrame girt = work[g.Id];
                    ManagedTimber.TFace[] gf = ManagedTimber.Faces(girt);
                    for (int gi = 0; gi <= 1; gi++)
                    {
                        ManagedTimber.TFace gEnd = gf[gi];

                        // The host whose SIDE face this end bears on (same test as TJoint).
                        ObjectId postId = ObjectId.Null;
                        foreach ((ObjectId Id, ManagedTimber.TFrame F, string Role) pc in all)
                        {
                            if (pc.Id == g.Id || !hostRoles.Contains(pc.Role)) continue;
                            bool mate = false;
                            foreach (ManagedTimber.TFace ps in ManagedTimber.Faces(pc.F))
                            {
                                if (Math.Abs(ps.N.DotProduct(pc.F.Z)) >= 0.5) continue;   // host face must be a SIDE
                                if (ManagedTimber.FacesMate(gEnd, ps, 0.25, out _)) { mate = true; break; }
                            }
                            if (mate) { postId = pc.Id; break; }
                        }
                        if (postId.IsNull) continue;

                        // Skip a contact that already carries a tenon (a union box) OR a shoulder (shared polys)
                        // at this end (idempotent -- safe to re-run).
                        bool farEnd = gEnd.N.DotProduct(girt.Z) > 0.0;
                        ManagedTimber.TFrame post = work[postId];
                        if ((girt.Features != null && girt.Features.Exists(f => !f.Subtract &&
                                (((f.Min.Z + f.Max.Z) / 2.0 > girt.L / 2.0) == farEnd)))
                            || ExistingRafterFootId(girt, post) != 0)
                        { skipped++; continue; }

                        if (!ManagedTimber.GirtPostJoint(girt, post, gEnd, spec,
                                out List<(Point3d Min, Point3d Max, bool Subtract)> features,
                                out List<(Point3d C, Vector3d Axis, double R, double Half)> pegs,
                                out List<(Point3d[] Poly, bool OnPost, double Xlo, double Xhi)> polys))
                        { failed++; continue; }

                        int jid = nextId++;
                        ApplyJoint(ref girt, ref post, jid, features, pegs);
                        ApplyRafterFoot(ref girt, ref post, jid, polys);   // shoulder triangle polys
                        work[g.Id] = girt; work[postId] = post;   // persist the (struct) frames' new lists
                        dirty.Add(g.Id); dirty.Add(postId);
                        cuts.Add((g.Id, postId, jid, ct));
                        cut++;
                    }
                }
            }

            // The girt -> post pass: reviewed first (Escape here aborts the whole batch, the
            // long-standing contract); runs only when a girt-family male is in scope.
            if (RolePresent(GirtRoles))
            {
                if (!ReviewJoint(ed)) return;
                Pass(GirtRoles, PostRoles, _joint, ConnectionType.BoxTenon(_joint));
            }
            int girtCuts = cut;

            // Extra passes only when the frame carries the roles in scope. Each has its own sticky
            // recipe, reviewed through the same editor by temporarily swapping the _joint sticky;
            // Escape skips just that pass.
            bool ReviewSwapped(ref ManagedTimber.JointSpec sticky)
            {
                ManagedTimber.JointSpec saveJoint = _joint;
                _joint = sticky;
                bool go = ReviewJoint(ed);
                sticky = _joint;
                _joint = saveJoint;
                return go;
            }

            // SILL pass: post foot -> sill, the short unpegged stub.
            if (RolePresent(SillRoles))
            {
                ed.WriteMessage("\nSill pass -- post foot -> sill stub tenon:");
                if (ReviewSwapped(ref _sillJoint))
                    Pass(PostRoles, SillRoles, _sillJoint, ConnectionType.BoxTenon(_sillJoint));
                else ed.WriteMessage("\nSill stub tenons skipped.");
            }
            int sillCuts = cut - girtCuts;

            // SUMMER pass: summer end -> girt/sill side, the tusk tenon (soffit bearing + deep tenon).
            if (RolePresent(SummerRoles))
            {
                ed.WriteMessage("\nSummer pass -- summer end -> girt tusk tenon:");
                if (ReviewSwapped(ref _summerJoint))
                    Pass(SummerRoles, SummerHostRoles, _summerJoint, ConnectionType.TuskTenon(_summerJoint));
                else ed.WriteMessage("\nSummer tusk tenons skipped.");
            }
            int summerCuts = cut - girtCuts - sillCuts;

            // JOIST pass: joist end -> carrier, the housed dovetail -- the deliberate half of TJoist's
            // Joint option (place plain, adjust, select, cut). The pass turns the sticky ON (running it
            // IS the deliberate act); Off + Done in the review still vetoes. Already-dovetailed pairs
            // skip by their shared joint id. Cuts JointPrisms via PurlinRafterJoint, not GirtPostJoint.
            if (RolePresent(JoistRoles))
            {
                ed.WriteMessage("\nJoist pass -- joist end -> carrier housed dovetail:");
                _joistDove.On = true;
                if (!ReviewJoistDove(ed) || !_joistDove.On)
                    ed.WriteMessage("\nJoist dovetails skipped.");
                else
                {
                    ConnectionType dove = ConnectionType.HousedDovetail(_joistDove);
                    foreach ((ObjectId Id, ManagedTimber.TFrame F, string Role) j in all)
                    {
                        if (!JoistRoles.Contains(j.Role) || !InScope(j.Id)) continue;
                        foreach ((ObjectId Id, ManagedTimber.TFrame F, string Role) h in all)
                        {
                            if (h.Id == j.Id || !JoistHostRoles.Contains(h.Role)) continue;
                            ManagedTimber.TFrame joist = work[j.Id];
                            ManagedTimber.TFrame host = work[h.Id];
                            if (ExistingRafterFootId(joist, host) != 0) { skipped++; continue; }
                            if (!FindFootContact(joist, host, out ManagedTimber.TFace hFace)) continue;
                            if (!ManagedTimber.PurlinRafterJoint(joist, host, hFace, _joistDove,
                                    out List<(Point3d[] Poly, Vector3d Extrude, bool OnRafter)> prisms, out _))
                            { failed++; continue; }
                            int jid = nextId++;
                            if (joist.JointPrisms == null) joist.JointPrisms = new List<(Point3d[], Vector3d, int, bool)>();
                            if (host.JointPrisms == null) host.JointPrisms = new List<(Point3d[], Vector3d, int, bool)>();
                            foreach ((Point3d[] Poly, Vector3d Extrude, bool OnRafter) p in prisms)
                                (p.OnRafter ? host.JointPrisms : joist.JointPrisms).Add((p.Poly, p.Extrude, jid, p.OnRafter));
                            work[j.Id] = joist; work[h.Id] = host;
                            dirty.Add(j.Id); dirty.Add(h.Id);
                            cuts.Add((j.Id, h.Id, jid, dove));
                            cut++;
                        }
                    }
                }
            }
            int joistCuts = cut - girtCuts - sillCuts - summerCuts;

            var remap = new Dictionary<ObjectId, ObjectId>();
            foreach (ObjectId id in dirty) remap[id] = ManagedTimber.RebuildFromFrame(id, work[id]);
            foreach ((ObjectId girt, ObjectId post, int jid, ConnectionType ct) c in cuts)
                StampJoint(remap[c.girt], remap[c.post], c.jid, c.ct);
            ed.WriteMessage("\nTJointAll: cut " + cut + " joint(s)" +
                            (sillCuts > 0 ? " (" + sillCuts + " post-foot -> sill)" : "") +
                            (summerCuts > 0 ? " (" + summerCuts + " summer -> girt)" : "") +
                            (joistCuts > 0 ? " (" + joistCuts + " joist end(s))" : "") +
                            ", skipped " + skipped + " already-jointed" +
                            (failed > 0 ? ", " + failed + " collapsed" : "") + ".");
        }

        // TJointSync -- DELIBERATE joint maintenance (the other half of "joinery travels with the
        // timber"): after MOVING jointed timbers, or after a re-Generate replaced the skeleton around
        // surviving free timbers, select them and re-cut their joints in place. Per selected timber,
        // every joint id it carries is re-applied from its STORED recipe (the per-joint stamp every
        // cutter writes):
        //   - partner found by the shared id -> RE-CUT at the current contact, same id (both pick
        //     orders are tried -- the Apply helpers find the end-into-side contact themselves and
        //     fail WITHOUT mutating when the order or contact is wrong);
        //   - no partner carries the id (the regen case) -> GEOMETRIC RE-ATTACH: the timber it now
        //     touches is found first, the orphaned features are stripped, and the recipe is applied
        //     against the new mate under a fresh id + stamp;
        //   - partner exists but no contact any more (moved apart) -> left untouched, reported
        //     (delete deliberately via the joint's ...Del or the pane).
        // Joints with no stored recipe (pre-stamp legacy cuts) are reported and skipped. Crossing
        // joints (birdsmouth) can re-cut by id but not re-attach (no end-into-side contact to find).
        [CommandMethod("TJointSync")]
        public static void JointSync()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            var filter = new SelectionFilter(new[] { new TypedValue((int)DxfCode.Start, "3DSOLID") });
            var pso = new PromptSelectionOptions { MessageForAdding = "\nSelect timbers whose joints to re-sync: " };
            PromptSelectionResult sel = ed.GetSelection(pso, filter);
            if (sel.Status != PromptStatus.OK) return;

            List<ConnectionType> presets = ConnectionType.BuiltIns();
            var done = new HashSet<int>();                       // each joint syncs once, even if both partners are selected
            var remap = new Dictionary<ObjectId, ObjectId>();    // every re-cut rebuilds both sides -> fresh ObjectIds
            ObjectId Cur(ObjectId id) { while (!id.IsNull && remap.TryGetValue(id, out ObjectId nx)) id = nx; return id; }
            void Map(ObjectId from, ObjectId to) { if (!from.IsNull && !to.IsNull && from != to) remap[from] = to; }

            int recut = 0, reattached = 0, apart = 0, unknown = 0;

            foreach (ObjectId rawId in sel.Value.GetObjectIds())
            {
                ObjectId selId = Cur(rawId);
                if (selId.IsNull || selId.IsErased
                    || !ManagedTimber.TryReadFrame(db, selId, out ManagedTimber.TFrame sf)) continue;

                // Every joint id this timber carries: all five feature primitives + the stored recipes.
                var jids = new List<int>(AllJointIds(sf));
                foreach (int k in ManagedTimber.ReadJointSpecs(selId).Keys)
                    if (k != 0 && !jids.Contains(k)) jids.Add(k);

                foreach (int jid in jids)
                {
                    if (jid == 0 || !done.Add(jid)) continue;
                    selId = Cur(selId);
                    if (selId.IsNull || !ManagedTimber.TryReadFrame(db, selId, out sf)) break;

                    // The stored recipe: this side's stamp first, the partner's as fallback.
                    ManagedTimber.ReadJointSpecs(selId).TryGetValue(jid, out string state);

                    // The partner = the other timber carrying this joint id (fresh enumeration --
                    // earlier re-cuts changed ids).
                    ObjectId partnerId = ObjectId.Null; ManagedTimber.TFrame pfr = default;
                    foreach ((ObjectId Id, ManagedTimber.TFrame F, string Role) t in ManagedTimber.EnumerateWithRole(db))
                    {
                        if (t.Id == selId || !AllJointIds(t.F).Contains(jid)) continue;
                        partnerId = t.Id; pfr = t.F; break;
                    }
                    if (state == null && !partnerId.IsNull)
                        ManagedTimber.ReadJointSpecs(partnerId).TryGetValue(jid, out state);
                    ConnectionType ct = state != null ? ConnectionType.FromState(presets, state) : null;
                    if (ct == null)
                    { unknown++; ed.WriteMessage("\n  joint #" + jid + ": no stored recipe -- skipped."); continue; }

                    if (!partnerId.IsNull)
                    {
                        // RE-CUT in place (idempotent replace by the shared id).
                        ApplyResult r = ct.Apply(db, selId, sf, partnerId, pfr);
                        if (r.Ok) { Map(selId, r.AId); Map(partnerId, r.BId); }
                        else
                        {
                            r = ct.Apply(db, partnerId, pfr, selId, sf);
                            if (r.Ok) { Map(partnerId, r.AId); Map(selId, r.BId); }
                        }
                        if (r.Ok) { StampJoint(r.AId, r.BId, r.Jid, ct); recut++; }
                        else
                        {
                            apart++;
                            ed.WriteMessage("\n  joint #" + jid + " (" + ct.Name + "): no contact with its partner -- left as-is.");
                        }
                        continue;
                    }

                    // RE-ATTACH: the partner died (a regenerate replaced it). Find the timber this one
                    // now touches end-into-side, in either direction, skipping mates it is already
                    // jointed to -- only then strip the orphaned features and cut the recipe fresh.
                    ObjectId hostId = ObjectId.Null; ManagedTimber.TFrame hfr = default; bool selIsMale = true;
                    foreach ((ObjectId Id, ManagedTimber.TFrame F, string Role) t in ManagedTimber.EnumerateWithRole(db))
                    {
                        if (t.Id == selId || SharedJointId(sf, t.F) != 0) continue;
                        if (FindFootContact(sf, t.F, out _)) { hostId = t.Id; hfr = t.F; selIsMale = true; break; }
                        if (FindFootContact(t.F, sf, out _)) { hostId = t.Id; hfr = t.F; selIsMale = false; break; }
                    }
                    if (hostId.IsNull)
                    {
                        apart++;
                        ed.WriteMessage("\n  joint #" + jid + " (" + ct.Name + "): partner gone and nothing to re-attach to -- left as-is.");
                        continue;
                    }

                    StripJoint(ref sf, jid);
                    ObjectId nid = ManagedTimber.RebuildFromFrame(selId, sf);
                    Map(selId, nid); selId = nid;
                    if (!ManagedTimber.TryReadFrame(db, selId, out sf)) break;

                    ApplyResult ra = selIsMale ? ct.Apply(db, selId, sf, hostId, hfr)
                                               : ct.Apply(db, hostId, hfr, selId, sf);
                    if (ra.Ok)
                    {
                        if (selIsMale) { Map(selId, ra.AId); Map(hostId, ra.BId); }
                        else { Map(hostId, ra.AId); Map(selId, ra.BId); }
                        StampJoint(ra.AId, ra.BId, ra.Jid, ct);
                        reattached++;
                    }
                    else
                    {
                        apart++;
                        ed.WriteMessage("\n  joint #" + jid + " (" + ct.Name + "): re-attach failed -- " + ra.Diag +
                                        " (orphaned features stripped; UNDO restores).");
                    }
                }
            }

            ed.WriteMessage("\nTJointSync: " + recut + " re-cut, " + reattached + " re-attached"
                            + (apart > 0 ? ", " + apart + " without contact (left as-is)" : "")
                            + (unknown > 0 ? ", " + unknown + " with no stored recipe (skipped)" : "") + ".");
        }

        // Review / adjust the sticky joint recipe (_joint) as a KIT OF PARTS: the elements (Tenon, Housing,
        // Pegs) are peers, each a toggleable sub-menu. Enter / "Cut" proceeds (returns true); Escape cancels
        // (false). Shared by TJoint (per cut) and TJointAll (once). A new element type adds a keyword + a
        // Review<Kind> sub-menu here.
        private static bool ReviewJoint(Editor ed)
        {
            while (true)
            {
                string tn = _joint.Tenon.On ? "On (T" + _joint.Tenon.Thickness + " L" + _joint.Tenon.Length + ")" : "Off";
                string hs = _joint.Housing.On ? "On (" + _joint.Housing.Seat + ")" : "Off";
                string sh = _joint.Shoulder.On ? "On (" + _joint.Shoulder.Seat + ")" : "Off";
                var pko = new PromptKeywordOptions(
                    "\nJoint -- Tenon: " + tn + " | Housing: " + hs + " | Shoulder: " + sh + " | Pegs: " + _joint.Peg.Count + ". ")
                { AllowNone = true };
                pko.Keywords.Add("Cut");
                pko.Keywords.Add("Tenon");
                pko.Keywords.Add("Housing");
                pko.Keywords.Add("Shoulder");
                pko.Keywords.Add("Pegs");
                pko.Keywords.Default = "Cut";
                PromptResult kr = ed.GetKeywords(pko);
                if (kr.Status != PromptStatus.OK && kr.Status != PromptStatus.None) return false;   // cancelled
                string kw = kr.Status == PromptStatus.None ? "Cut" : kr.StringResult;
                if (kw == "Cut") return true;
                switch (kw)
                {
                    case "Tenon":    ReviewTenon(ed); break;
                    case "Housing":  ReviewHousing(ed, ref _joint.Housing); break;
                    case "Shoulder": ReviewShoulder(ed); break;
                    case "Pegs":     ReviewPegs(ed, ref _joint.Peg); break;
                }
            }
        }

        // The tenon sub-menu: edit the sticky _joint.Tenon. Enter / "Done" returns.
        private static void ReviewTenon(Editor ed)
        {
            while (true)
            {
                var pko = new PromptKeywordOptions(
                    "\nTenon On=" + (_joint.Tenon.On ? "Yes" : "No") + " Thickness=" + _joint.Tenon.Thickness +
                    " Length=" + _joint.Tenon.Length + " TopShoulder=" + _joint.Tenon.ShoulderTop +
                    " BotShoulder=" + _joint.Tenon.ShoulderBottom + " Offset=" + _joint.Tenon.Offset + ". ")
                { AllowNone = true };
                pko.Keywords.Add("Done");
                pko.Keywords.Add("On");
                pko.Keywords.Add("Thickness");
                pko.Keywords.Add("Length");
                pko.Keywords.Add("TopShoulder");
                pko.Keywords.Add("BotShoulder");
                pko.Keywords.Add("Offset");
                pko.Keywords.Default = "Done";
                PromptResult kr = ed.GetKeywords(pko);
                if (kr.Status != PromptStatus.OK && kr.Status != PromptStatus.None) return;
                string kw = kr.Status == PromptStatus.None ? "Done" : kr.StringResult;
                if (kw == "Done") return;
                switch (kw)
                {
                    case "On":        _joint.Tenon.On = !_joint.Tenon.On; break;
                    case "Thickness": if (GetPositive(ed, "Tenon thickness", _joint.Tenon.Thickness, out double tv)) _joint.Tenon.Thickness = tv; break;
                    case "Length":    if (GetPositive(ed, "Tenon length",    _joint.Tenon.Length,    out double lv)) _joint.Tenon.Length    = lv; break;
                    case "TopShoulder": if (GetDouble  (ed, "Top shoulder",      _joint.Tenon.ShoulderTop, false, out double rt)) _joint.Tenon.ShoulderTop = rt; break;
                    case "BotShoulder": if (GetDouble  (ed, "Bottom shoulder",   _joint.Tenon.ShoulderBottom, false, out double rb)) _joint.Tenon.ShoulderBottom = rb; break;
                    case "Offset":    if (GetDouble  (ed, "Width offset",    _joint.Tenon.Offset,    true,  out double ov)) _joint.Tenon.Offset = ov; break;
                }
            }
        }

        // The shared housing sub-menu: edit a HousingSpec footprint (On + Seat + the four per-face shoulders --
        // Top / Bottom + Side1 / Side2; every shoulder 0 = full section). Enter / "Done" returns. Used by TJoint
        // (box) AND the polygon tenons (strut / brace / QP apex) -- one housing UI everywhere.
        private static void ReviewHousing(Editor ed, ref ManagedTimber.HousingSpec hsg)
        {
            while (true)
            {
                var pko = new PromptKeywordOptions(
                    "\nHousing On=" + (hsg.On ? "Yes" : "No") + " Seat=" + hsg.Seat +
                    " TopShoulder=" + hsg.ShoulderTop + " BotShoulder=" + hsg.ShoulderBottom +
                    " Side1=" + hsg.ShoulderSide1 + " Side2=" + hsg.ShoulderSide2 + ". ")
                { AllowNone = true };
                pko.Keywords.Add("Done");
                pko.Keywords.Add("On");
                pko.Keywords.Add("Seat");
                pko.Keywords.Add("TopShoulder");
                pko.Keywords.Add("BotShoulder");
                pko.Keywords.Add("Side1");
                pko.Keywords.Add("Side2");
                pko.Keywords.Default = "Done";
                PromptResult kr = ed.GetKeywords(pko);
                if (kr.Status != PromptStatus.OK && kr.Status != PromptStatus.None) return;
                string kw = kr.Status == PromptStatus.None ? "Done" : kr.StringResult;
                if (kw == "Done") return;
                switch (kw)
                {
                    case "On":        hsg.On = !hsg.On; break;
                    case "Seat":      if (GetPositive(ed, "Housing seat depth (let-in)", hsg.Seat, out double cv)) { hsg.Seat = cv; hsg.On = true; } break;
                    case "TopShoulder": if (GetDouble(ed, "Top shoulder (flush band at the top)",    hsg.ShoulderTop, false, out double rt)) hsg.ShoulderTop = rt; break;
                    case "BotShoulder": if (GetDouble(ed, "Bottom shoulder (flush band at the bottom)", hsg.ShoulderBottom, false, out double rb)) hsg.ShoulderBottom = rb; break;
                    case "Side1":     if (GetDouble(ed, "Side-1 shoulder (inset from one side)",       hsg.ShoulderSide1, false, out double s1)) hsg.ShoulderSide1 = s1; break;
                    case "Side2":     if (GetDouble(ed, "Side-2 shoulder (inset from the other side)", hsg.ShoulderSide2, false, out double s2)) hsg.ShoulderSide2 = s2; break;
                }
            }
        }

        // The shoulder sub-menu of TJoint: edit the sticky _joint.Shoulder -- the 3-pt triangle notch
        // (face-bot, face-top, seat-bot). Seat = the bearing-seat depth into the post; like the housing it
        // advances the seat, so it extends the tenon and shifts the pegs. The face edge spans the section
        // (top/bottom shoulders); the top is a diagonal. Enter / "Done" returns.
        private static void ReviewShoulder(Editor ed)
        {
            while (true)
            {
                var pko = new PromptKeywordOptions(
                    "\nShoulder (3-pt triangle) On=" + (_joint.Shoulder.On ? "Yes" : "No") + " Seat=" + _joint.Shoulder.Seat +
                    " Thickness=" + (_joint.Shoulder.Thickness > 0.0 ? _joint.Shoulder.Thickness.ToString() : "full") +
                    " TopShoulder=" + _joint.Shoulder.ShoulderTop + " BotShoulder=" + _joint.Shoulder.ShoulderBottom +
                    " Offset=" + _joint.Shoulder.Offset + ". ")
                { AllowNone = true };
                pko.Keywords.Add("Done");
                pko.Keywords.Add("On");
                pko.Keywords.Add("Seat");
                pko.Keywords.Add("Thickness");
                pko.Keywords.Add("TopShoulder");
                pko.Keywords.Add("BotShoulder");
                pko.Keywords.Add("Offset");
                pko.Keywords.Default = "Done";
                PromptResult kr = ed.GetKeywords(pko);
                if (kr.Status != PromptStatus.OK && kr.Status != PromptStatus.None) return;
                string kw = kr.Status == PromptStatus.None ? "Done" : kr.StringResult;
                if (kw == "Done") return;
                switch (kw)
                {
                    case "On":        _joint.Shoulder.On = !_joint.Shoulder.On; break;
                    case "Seat":      if (GetPositive(ed, "Seat depth into the post", _joint.Shoulder.Seat, out double cv)) _joint.Shoulder.Seat = cv; break;
                    case "Thickness": if (GetDouble(ed, "Shoulder width (0 = full)", _joint.Shoulder.Thickness, false, out double hw)) _joint.Shoulder.Thickness = hw; break;
                    case "TopShoulder": if (GetDouble(ed, "Top shoulder (inset the face top)", _joint.Shoulder.ShoulderTop, false, out double rt)) _joint.Shoulder.ShoulderTop = rt; break;
                    case "BotShoulder": if (GetDouble(ed, "Bottom shoulder (raise the seat)", _joint.Shoulder.ShoulderBottom, false, out double rb)) _joint.Shoulder.ShoulderBottom = rb; break;
                    case "Offset":    if (GetDouble(ed, "Width offset",  _joint.Shoulder.Offset,    true,  out double ov)) _joint.Shoulder.Offset = ov; break;
                }
            }
        }

        // The shared peg sub-menu: edit a PegSpec layout (Count 0 = no pegs). Enter / "Done" returns. Used by
        // TJoint (box) AND the polygon tenons (strut / brace / QP apex / rafter foot) -- one peg UI everywhere.
        private static void ReviewPegs(Editor ed, ref ManagedTimber.PegSpec peg)
        {
            while (true)
            {
                var pko = new PromptKeywordOptions(
                    "\nPegs Count=" + peg.Count + " Diameter=" + peg.Diameter +
                    " Setback=" + peg.Setback + " Spacing=" + peg.Spacing +
                    " Bore=" + peg.Bore + " BlindDepth=" + peg.BlindDepth +
                    " Flip=" + (peg.BlindFlip ? "On" : "Off") + ". ")
                { AllowNone = true };
                pko.Keywords.Add("Done");
                pko.Keywords.Add("Count");
                pko.Keywords.Add("Diameter");
                pko.Keywords.Add("Setback");
                pko.Keywords.Add("Spacing");
                pko.Keywords.Add("Bore");
                pko.Keywords.Add("BlindDepth");
                pko.Keywords.Add("Flip");
                pko.Keywords.Default = "Done";
                PromptResult kr = ed.GetKeywords(pko);
                if (kr.Status != PromptStatus.OK && kr.Status != PromptStatus.None) return;
                string kw = kr.Status == PromptStatus.None ? "Done" : kr.StringResult;
                if (kw == "Done") return;
                switch (kw)
                {
                    case "Count":
                        var io = new PromptIntegerOptions("\nPeg count <" + peg.Count + ">: ")
                        { AllowNegative = false, DefaultValue = peg.Count, UseDefaultValue = true };
                        PromptIntegerResult ir = ed.GetInteger(io);
                        if (ir.Status == PromptStatus.OK) peg.Count = ir.Value;
                        break;
                    case "Diameter":  if (GetPositive(ed, "Peg diameter",  peg.Diameter,   out double dv)) peg.Diameter = dv; break;
                    case "Setback":   if (GetDouble  (ed, "Setback into the tongue", peg.Setback, false, out double sb)) peg.Setback = sb; break;
                    case "Spacing":   if (GetDouble  (ed, "Stacked spacing", peg.Spacing,  false, out double sp)) peg.Spacing = sp; break;
                    case "BlindDepth":if (GetDouble  (ed, "Blind depth past tenon", peg.BlindDepth, false, out double bd)) peg.BlindDepth = bd; break;
                    case "Flip":      peg.BlindFlip = !peg.BlindFlip; break;
                    case "Bore":
                        var bo = new PromptKeywordOptions("\nBore type. ") { AllowNone = true };
                        bo.Keywords.Add("Full");
                        bo.Keywords.Add("Blind");
                        bo.Keywords.Default = peg.Bore == ManagedTimber.PegBore.Blind ? "Blind" : "Full";
                        PromptResult br = ed.GetKeywords(bo);
                        if (br.Status == PromptStatus.OK)
                            peg.Bore = br.StringResult == "Blind" ? ManagedTimber.PegBore.Blind : ManagedTimber.PegBore.Full;
                        break;
                }
            }
        }

        // Doc-wide next joint id = 1 + the max joint id stamped on any managed timber's features (0 is
        // reserved for legacy / unkeyed). Girt + post are read fresh before any write, so this is unique.
        private static int NextJointId(Database db)
        {
            int max = 0;
            foreach (ManagedTimber.TFrame f in ManagedTimber.EnumerateFrameFrames(db, null))
            {
                if (f.Features != null)
                    foreach ((Point3d Min, Point3d Max, bool Subtract, int Joint) ft in f.Features)
                        if (ft.Joint > max) max = ft.Joint;
                if (f.JointPolys != null)   // rafter-foot (and future polygon) joints share the id space
                    foreach ((Point3d[] Poly, int Joint, bool Subtract, double Xlo, double Xhi) jp in f.JointPolys)
                        if (jp.Joint > max) max = jp.Joint;
                if (f.JointPolysZ != null)   // Z-extruded polys (ridge tongue) share it too
                    foreach ((Point3d[] Poly, int Joint, bool Subtract, double Xlo, double Xhi) jp in f.JointPolysZ)
                        if (jp.Joint > max) max = jp.Joint;
                if (f.JointPrisms != null)   // oriented-prism joints (common->ridge, purlin->rafter) share it too --
                    foreach ((Point3d[] Poly, Vector3d Extrude, int Joint, bool Subtract) jp in f.JointPrisms)
                        if (jp.Joint > max) max = jp.Joint;   // missing this collided every prism joint onto one id
                if (f.Pegs != null)          // pegs ride a joint id -- count them so a fresh id never lands on one
                    foreach ((Point3d C, Vector3d Axis, double R, double Half, int Joint) pg in f.Pegs)
                        if (pg.Joint > max) max = pg.Joint;
            }
            return max + 1;
        }

        // True when two axis-aligned boxes (same local frame) overlap on all three axes.
        private static bool BoxesOverlap(Point3d aMin, Point3d aMax, Point3d bMin, Point3d bMax) =>
            aMin.X < bMax.X && aMax.X > bMin.X &&
            aMin.Y < bMax.Y && aMax.Y > bMin.Y &&
            aMin.Z < bMax.Z && aMax.Z > bMin.Z;

        // Remove a girt -> post joint: the tenon + mortise (and anything else sharing its id, e.g. future
        // pegs). Pick the girt then the post, like TJoint; the bearing contact is re-found and the joint at
        // that end is deleted from BOTH timbers, which then rebuild whole. Run once per end for a girt that
        // tenons into the same post at both ends (the matched end is deleted; the other is left intact).
        [CommandMethod("TJointDel")]
        public static void JointDelete()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            if (!PickTimber(ed, db, "\nPick the TENONED timber (girt / summer): ", out ObjectId girtId, out ManagedTimber.TFrame girt)) return;
            if (!PickTimber(ed, db, "\nPick the MORTISED timber (post / carrier): ", out ObjectId postId, out ManagedTimber.TFrame post)) return;
            if (girtId == postId) { ed.WriteMessage("\nPick two different timbers."); return; }

            // The girt END-cap that bears on a post SIDE face (same find as TJoint).
            ManagedTimber.TFace[] gf = ManagedTimber.Faces(girt);
            ManagedTimber.TFace[] pf = ManagedTimber.Faces(post);
            bool found = false;
            ManagedTimber.TFace gEnd = default;
            for (int gi = 0; gi <= 1 && !found; gi++)
                foreach (ManagedTimber.TFace ps in pf)
                {
                    if (Math.Abs(ps.N.DotProduct(post.Z)) >= 0.5) continue;       // the post face must be a SIDE
                    if (ManagedTimber.FacesMate(gf[gi], ps, 0.25, out _)) { gEnd = gf[gi]; found = true; break; }
                }
            if (!found)
            { ed.WriteMessage("\nNo end-into-face contact -- pick the girt + post that share a joint."); return; }

            bool farEnd = gEnd.N.DotProduct(girt.Z) > 0.0;
            int ti = girt.Features == null ? -1 : girt.Features.FindIndex(f => !f.Subtract &&
                         (((f.Min.Z + f.Max.Z) / 2.0 > girt.L / 2.0) == farEnd));
            // No tenon box at this end? It may still be a shoulder-only (poly) joint -- delete by its id.
            if (ti < 0)
            {
                int sid = ExistingRafterFootId(girt, post);
                if (sid == 0) { ed.WriteMessage("\nNo joint at that contact -- nothing to delete."); return; }
                girt.JointPolys?.RemoveAll(j => j.Joint == sid);
                post.JointPolys?.RemoveAll(j => j.Joint == sid);
                ObjectId ng = ManagedTimber.RebuildFromFrame(girtId, girt);
                ObjectId np = ManagedTimber.RebuildFromFrame(postId, post);
                ed.WriteMessage("\nTJointDel: joint #" + sid + " removed (girt " + ng.Handle + ", post " + np.Handle + ").");
                return;
            }

            int id = girt.Features[ti].Joint;
            if (id != 0)
            {
                girt.Features.RemoveAll(f => f.Joint == id);
                post.Features.RemoveAll(f => f.Joint == id);
                post.Pegs?.RemoveAll(p => p.Joint == id);   // pegs go with the joint
                girt.JointPolys?.RemoveAll(j => j.Joint == id);   // shoulder polys ride the same id
                post.JointPolys?.RemoveAll(j => j.Joint == id);
            }
            else
            {
                // Legacy / unkeyed: map the tenon corners into post-local for its footprint, drop the
                // overlapping post mortise(s).
                var t = girt.Features[ti];
                double mnX = double.MaxValue, mnY = double.MaxValue, mnZ = double.MaxValue;
                double mxX = double.MinValue, mxY = double.MinValue, mxZ = double.MinValue;
                foreach (double lx in new[] { t.Min.X, t.Max.X })
                    foreach (double ly in new[] { t.Min.Y, t.Max.Y })
                        foreach (double lz in new[] { t.Min.Z, t.Max.Z })
                        {
                            Point3d w = girt.O + girt.X * lx + girt.Y * ly + girt.Z * lz;
                            Vector3d r = w - post.O;
                            double px = r.DotProduct(post.X), py = r.DotProduct(post.Y), pz = r.DotProduct(post.Z);
                            if (px < mnX) mnX = px; if (px > mxX) mxX = px;
                            if (py < mnY) mnY = py; if (py > mxY) mxY = py;
                            if (pz < mnZ) mnZ = pz; if (pz > mxZ) mxZ = pz;
                        }
                Point3d fpMin = new Point3d(mnX, mnY, mnZ), fpMax = new Point3d(mxX, mxY, mxZ);
                girt.Features.RemoveAt(ti);
                if (post.Features != null)
                    post.Features.RemoveAll(f => f.Subtract && BoxesOverlap(f.Min, f.Max, fpMin, fpMax));
            }

            ObjectId ngirt = ManagedTimber.RebuildFromFrame(girtId, girt);
            ObjectId npost = ManagedTimber.RebuildFromFrame(postId, post);
            ed.WriteMessage("\nTJointDel: joint " + (id != 0 ? "#" + id : "(legacy)") +
                            " removed (girt " + ngirt.Handle + ", post " + npost.Handle + ").");
        }

        // Cut a principal-rafter FOOT housed into a post SIDE -- a girt-at-a-pitch HOUSING (+ optional TENON).
        // Pick the RAFTER then the POST: the post gets the wedge pocket/mortise and the rafter foot is grown
        // into it (housed stub + tenon tongue) -- a level seat with a pitch-matched top. The recipe (housing
        // depth + tenon thickness/length/shoulders/offset) is reviewed; the cuts are id-keyed polygons (re-cut
        // REPLACES; TRafterFootDel removes) and ride MOVE / TFit + SAVE. Needs a post DEPTH (+/-Y) face.
        [CommandMethod("TRafterFoot")]
        public static void RafterFoot()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            if (!PickTimber(ed, db, "\nPick the RAFTER: ", out ObjectId rId, out ManagedTimber.TFrame rafter)) return;
            if (!PickTimber(ed, db, "\nPick the POST: ", out ObjectId pId, out ManagedTimber.TFrame post)) return;
            if (rId == pId) { ed.WriteMessage("\nPick two different timbers."); return; }

            if (!ReviewRafterFoot(ed)) return;

            // Cut via the factored apply-half (the same path the ConnectionType facade drives; it finds the contact).
            if (!ApplyRafterFootJoint(db, rId, ref rafter, pId, ref post, _rfoot,
                    out ObjectId nr, out ObjectId np, out int jid, out string diag))
            { ed.WriteMessage("\nNothing to cut -- " + diag + "."); return; }
            StampJoint(nr, np, jid, ConnectionType.RafterFoot(_rfoot));
            ed.WriteMessage("\n[diag] " + diag);
            ed.WriteMessage("\nTRafterFoot: joint #" + jid + " cut -- " + diag +
                            " (rafter " + nr.Handle + ", post " + np.Handle + ").");
        }

        // The apply-half of TRafterFoot, factored for the ConnectionType facade: FINDS the post-side contact
        // itself, cuts the sloped housing (+ optional tenon), shares/mints the joint id (dropping overlapping
        // polys on a re-cut), rebuilds both. False + diag = no contact or nothing enabled.
        public static bool ApplyRafterFootJoint(Database db, ObjectId rId, ref ManagedTimber.TFrame rafter,
            ObjectId pId, ref ManagedTimber.TFrame post, ManagedTimber.RafterFootSpec spec,
            out ObjectId newRafterId, out ObjectId newPostId, out int jid, out string diag)
        {
            newRafterId = ObjectId.Null; newPostId = ObjectId.Null;
            if (!ComputeRafterFootJoint(db, ref rafter, ref post, spec, out jid, out diag)) return false;
            newRafterId = ManagedTimber.RebuildFromFrame(rId, rafter);
            newPostId = ManagedTimber.RebuildFromFrame(pId, post);
            return true;
        }

        // Compute-only core of the rafter-foot joint (housing + optional tenon): find the post-side contact, cut
        // the wedge, share/mint the id and route the polys onto the two frames WITHOUT the DB rebuild. Driven by
        // ApplyRafterFootJoint (commit) and the joint PREVIEW (on cloned frames). False + diag = nothing cut.
        public static bool ComputeRafterFootJoint(Database db, ref ManagedTimber.TFrame rafter,
            ref ManagedTimber.TFrame post, ManagedTimber.RafterFootSpec spec, out int jid, out string diag)
        {
            jid = 0;
            if (!FindFootContact(rafter, post, out ManagedTimber.TFace pFace))
            { diag = "no foot-into-side contact -- the rafter's foot must die into a post side"; return false; }
            if (!ManagedTimber.RafterFootJoint(rafter, post, pFace, spec,
                    out List<(Point3d[] Poly, bool OnPost, double Xlo, double Xhi)> polys,
                    out List<(Point3d C, Vector3d Axis, double R, double Half)> postPegs, out diag))
                return false;
            int reuse = ExistingRafterFootId(rafter, post);
            jid = reuse != 0 ? reuse : NextJointId(db);
            if (reuse != 0) post.Pegs?.RemoveAll(p => p.Joint == reuse);   // old peg bores go with the re-cut joint
            // Replace THIS joint by its id; the geometry-overlap purge applies ONLY to legacy id-0 features --
            // a DIFFERENT identified joint sharing the host must never be swept up by the overlap net.
            foreach ((Point3d[] Poly, bool OnPost, double Xlo, double Xhi) p in polys)
            {
                if (p.OnPost) post.JointPolys?.RemoveAll(j => (reuse != 0 && j.Joint == reuse) || (j.Joint == 0 && PolysOverlap(j.Poly, p.Poly)));
                else          rafter.JointPolys?.RemoveAll(j => (reuse != 0 && j.Joint == reuse) || (j.Joint == 0 && PolysOverlap(j.Poly, p.Poly)));
            }
            ApplyRafterFoot(ref rafter, ref post, jid, polys);
            if (postPegs.Count > 0)
            {
                if (post.Pegs == null) post.Pegs = new List<(Point3d, Vector3d, double, double, int)>();
                foreach ((Point3d C, Vector3d Axis, double R, double Half) pg in postPegs)
                    post.Pegs.Add((pg.C, pg.Axis, pg.R, pg.Half, jid));     // BORE the post cheeks
            }
            return true;
        }

        // Remove a rafter-foot joint: pick the rafter + post, drop the polygons sharing their joint id from
        // both, rebuild. (Parallels TJointDel.)
        [CommandMethod("TRafterFootDel")]
        public static void RafterFootDelete()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            if (!PickTimber(ed, db, "\nPick the RAFTER: ", out ObjectId rId, out ManagedTimber.TFrame rafter)) return;
            if (!PickTimber(ed, db, "\nPick the POST: ", out ObjectId pId, out ManagedTimber.TFrame post)) return;
            if (rId == pId) { ed.WriteMessage("\nPick two different timbers."); return; }

            int id = ExistingRafterFootId(rafter, post);
            if (id == 0) { ed.WriteMessage("\nNo rafter-foot joint between those two timbers."); return; }
            rafter.JointPolys?.RemoveAll(j => j.Joint == id);
            post.JointPolys?.RemoveAll(j => j.Joint == id);
            post.Pegs?.RemoveAll(p => p.Joint == id);

            ObjectId nr = ManagedTimber.RebuildFromFrame(rId, rafter);
            ObjectId np = ManagedTimber.RebuildFromFrame(pId, post);
            ed.WriteMessage("\nTRafterFootDel: joint #" + id + " removed (rafter " + nr.Handle + ", post " + np.Handle + ").");
        }

        // Cut a principal-rafter HEAD bearing on a KING POST side -- the legacy "shoulder" notch only. Pick
        // the RAFTER then the KING POST: the king post gets the bearing notch and the rafter head grows the
        // matching tongue. Seat is reviewed; the cut is an id-keyed polygon (re-cut REPLACES;
        // TRafterHeadDel removes) and rides MOVE / TFit + SAVE. Needs a king-post DEPTH (+/-Y) face.
        [CommandMethod("TRafterHead")]
        public static void RafterHead()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            if (!PickTimber(ed, db, "\nPick the RAFTER: ", out ObjectId rId, out ManagedTimber.TFrame rafter)) return;
            if (!PickTimber(ed, db, "\nPick the KING POST: ", out ObjectId pId, out ManagedTimber.TFrame kingpost)) return;
            if (rId == pId) { ed.WriteMessage("\nPick two different timbers."); return; }

            if (!ReviewRafterHead(ed)) return;

            // Cut via the factored apply-half (the same path the ConnectionType facade drives; it finds the contact).
            if (!ApplyRafterHeadJoint(db, rId, ref rafter, pId, ref kingpost, _rhead,
                    out ObjectId nr, out ObjectId np, out int jid, out string diag))
            { ed.WriteMessage("\nNothing to cut -- " + diag + "."); return; }
            StampJoint(nr, np, jid, ConnectionType.RafterHead(_rhead));
            ed.WriteMessage("\n[diag] " + diag);
            ed.WriteMessage("\nTRafterHead: joint #" + jid + " cut -- " + diag +
                            " (rafter " + nr.Handle + ", king post " + np.Handle + ").");
        }

        // Remove a rafter-head joint: pick the rafter + king post, drop the polygons sharing their joint id
        // from both, rebuild. (Parallels TRafterFootDel.)
        [CommandMethod("TRafterHeadDel")]
        public static void RafterHeadDelete()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            if (!PickTimber(ed, db, "\nPick the RAFTER: ", out ObjectId rId, out ManagedTimber.TFrame rafter)) return;
            if (!PickTimber(ed, db, "\nPick the KING POST: ", out ObjectId pId, out ManagedTimber.TFrame kingpost)) return;
            if (rId == pId) { ed.WriteMessage("\nPick two different timbers."); return; }

            int id = ExistingRafterFootId(rafter, kingpost);
            if (id == 0) { ed.WriteMessage("\nNo rafter-head joint between those two timbers."); return; }
            rafter.JointPolys?.RemoveAll(j => j.Joint == id);
            kingpost.JointPolys?.RemoveAll(j => j.Joint == id);

            ObjectId nr = ManagedTimber.RebuildFromFrame(rId, rafter);
            ObjectId np = ManagedTimber.RebuildFromFrame(pId, kingpost);
            ed.WriteMessage("\nTRafterHeadDel: joint #" + id + " removed (rafter " + nr.Handle + ", king post " + np.Handle + ").");
        }

        // The rafter-head shoulder sub-menu: edit the sticky _rhead (just the bearing-seat depth). Enter /
        // "Cut" proceeds.
        private static bool ReviewRafterHead(Editor ed)
        {
            while (true)
            {
                var pko = new PromptKeywordOptions(
                    "\nRafter head shoulder -- Seat=" + _rhead.Seat + " (On=" + (_rhead.On ? "Yes" : "No") + "). ")
                { AllowNone = true };
                pko.Keywords.Add("Cut");
                pko.Keywords.Add("Seat");
                pko.Keywords.Add("On");
                pko.Keywords.Default = "Cut";
                PromptResult kr = ed.GetKeywords(pko);
                if (kr.Status != PromptStatus.OK && kr.Status != PromptStatus.None) return false;
                string kw = kr.Status == PromptStatus.None ? "Cut" : kr.StringResult;
                if (kw == "Cut") return true;
                switch (kw)
                {
                    case "Seat": if (GetPositive(ed, "Shoulder seat depth into the king post", _rhead.Seat, out double sv)) _rhead.Seat = sv; break;
                    case "On":       _rhead.On = !_rhead.On; break;
                }
            }
        }

        // Cut a RIDGE -> KING POST drop-in housing -- the king post top is cut to the ridge's cross-section
        // (incl. its chamfered top) so the ridge lowers straight in. Pick the RIDGE then the KING POST: only
        // the king post is cut (an id-keyed polygon; re-cut REPLACES, TRidgeDel removes) and it rides MOVE /
        // TFit + SAVE. The ridge must run along the king-post width.
        [CommandMethod("TRidge")]
        public static void Ridge()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            if (!PickTimber(ed, db, "\nPick the RIDGE: ", out ObjectId rId, out ManagedTimber.TFrame ridge)) return;
            if (!PickTimber(ed, db, "\nPick the KING POST: ", out ObjectId pId, out ManagedTimber.TFrame kingpost)) return;
            if (rId == pId) { ed.WriteMessage("\nPick two different timbers."); return; }

            if (!ReviewRidge(ed)) return;

            if (!ManagedTimber.RidgeKpostJoint(ridge, kingpost, _ridge,
                    out List<(Point3d[] Poly, bool OnPost, double Xlo, double Xhi)> polys,
                    out List<(Point3d[] Poly, bool Subtract, double Xlo, double Xhi)> ridgeZPolys, out string diag))
            { ed.WriteMessage("\nNothing to cut -- " + diag + "."); return; }
            ed.WriteMessage("\n[diag] " + diag);

            int reuse = ExistingRafterFootId(ridge, kingpost);
            int jid = reuse != 0 ? reuse : NextJointId(db);
            // Replace THIS joint by its id; the overlap purge applies ONLY to legacy id-0 features -- the
            // rafter-HEAD notches share the king-post apex and must never be swept up by the overlap net.
            foreach ((Point3d[] Poly, bool OnPost, double Xlo, double Xhi) p in polys)   // king post pocket
                if (p.OnPost) kingpost.JointPolys?.RemoveAll(j => (reuse != 0 && j.Joint == reuse) || (j.Joint == 0 && PolysOverlap(j.Poly, p.Poly)));
            if (reuse != 0) ridge.JointPolysZ?.RemoveAll(j => j.Joint == reuse);   // old tongue (re-cut)

            ApplyRafterFoot(ref ridge, ref kingpost, jid, polys);   // king post pocket subtract (shared id)
            if (ridgeZPolys.Count > 0)   // ridge TONGUE (chamfered, Z-extruded into the pocket)
            {
                if (ridge.JointPolysZ == null) ridge.JointPolysZ = new List<(Point3d[], int, bool, double, double)>();
                foreach ((Point3d[] Poly, bool Subtract, double Xlo, double Xhi) z in ridgeZPolys)
                    ridge.JointPolysZ.Add((z.Poly, jid, z.Subtract, z.Xlo, z.Xhi));
            }

            ObjectId nr = ManagedTimber.RebuildFromFrame(rId, ridge);
            ObjectId np = ManagedTimber.RebuildFromFrame(pId, kingpost);
            StampJoint(nr, np, jid, ConnectionType.RidgeHousing(_ridge));
            ed.WriteMessage("\nTRidge: joint #" + jid + " cut -- " + diag +
                            " (ridge " + nr.Handle + ", king post " + np.Handle + ").");
        }

        // Remove a ridge -> king post joint: pick the ridge + king post, drop the polygons sharing their joint
        // id from both, rebuild. (Parallels TRafterHeadDel.)
        [CommandMethod("TRidgeDel")]
        public static void RidgeDelete()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            if (!PickTimber(ed, db, "\nPick the RIDGE: ", out ObjectId rId, out ManagedTimber.TFrame ridge)) return;
            if (!PickTimber(ed, db, "\nPick the KING POST: ", out ObjectId pId, out ManagedTimber.TFrame kingpost)) return;
            if (rId == pId) { ed.WriteMessage("\nPick two different timbers."); return; }

            int id = ExistingRafterFootId(ridge, kingpost);
            if (id == 0) { ed.WriteMessage("\nNo ridge -> king post joint between those two timbers."); return; }
            ridge.JointPolys?.RemoveAll(j => j.Joint == id);
            kingpost.JointPolys?.RemoveAll(j => j.Joint == id);
            ridge.JointPolysZ?.RemoveAll(j => j.Joint == id);   // the chamfered tongue

            ObjectId nr = ManagedTimber.RebuildFromFrame(rId, ridge);
            ObjectId np = ManagedTimber.RebuildFromFrame(pId, kingpost);
            ed.WriteMessage("\nTRidgeDel: joint #" + id + " removed (ridge " + nr.Handle + ", king post " + np.Handle + ").");
        }

        // The ridge-housing sub-menu: edit the sticky _ridge (the bearing-seat depth). Enter / "Cut" proceeds.
        private static bool ReviewRidge(Editor ed)
        {
            while (true)
            {
                var pko = new PromptKeywordOptions(
                    "\nRidge housing -- Seat=" + _ridge.Seat + " ShoulderBottom=" + _ridge.ShoulderBottom +
                    " (On=" + (_ridge.On ? "Yes" : "No") + "). ")
                { AllowNone = true };
                pko.Keywords.Add("Cut");
                pko.Keywords.Add("Seat");
                pko.Keywords.Add("ShoulderBottom");
                pko.Keywords.Add("On");
                pko.Keywords.Default = "Cut";
                PromptResult kr = ed.GetKeywords(pko);
                if (kr.Status != PromptStatus.OK && kr.Status != PromptStatus.None) return false;
                string kw = kr.Status == PromptStatus.None ? "Cut" : kr.StringResult;
                if (kw == "Cut") return true;
                switch (kw)
                {
                    case "Seat": if (GetPositive(ed, "Housing seat depth", _ridge.Seat, out double sv)) _ridge.Seat = sv; break;
                    case "ShoulderBottom": if (GetDouble(ed, "Bottom shoulder -- the ridge's lower N inches stay full and bear (0 = full drop-in)", _ridge.ShoulderBottom, false, out double bv)) _ridge.ShoulderBottom = bv; break;
                    case "On":   _ridge.On = !_ridge.On; break;
                }
            }
        }

        // Cut a RIDGE -> PRINCIPAL RAFTER drop-in housing -- the SAME geometry as TRidge (the rafter head is
        // cut to the ridge's chamfered cross-section risen to the apex, so the ridge lowers straight in), but
        // the host is a sloped principal rafter instead of a king post. This is the king-post-LESS bent: the
        // two rafters carry the ridge themselves, so the ridge is housed into BOTH rafter heads (run this once
        // per rafter). Reuses RidgeKpostJoint verbatim -- the pocket maps to the rafter's local Z x Y and
        // extrudes across the rafter WIDTH (= the ridge axis), independent of the rafter pitch. Pick the RIDGE
        // then the RAFTER. Only the rafter is cut (an id-keyed pocket) + a chamfered tongue on the ridge; re-cut
        // REPLACES, TRidgeRafterDel removes, and it rides MOVE / TFit + SAVE.
        [CommandMethod("TRidgeRafter")]
        public static void RidgeRafter()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            if (!PickTimber(ed, db, "\nPick the RIDGE: ", out ObjectId rId, out ManagedTimber.TFrame ridge)) return;
            if (!PickTimber(ed, db, "\nPick the RAFTER: ", out ObjectId fId, out ManagedTimber.TFrame rafter)) return;
            if (rId == fId) { ed.WriteMessage("\nPick two different timbers."); return; }

            if (!ReviewRidgeRafter(ed)) return;

            // Cut via the factored apply-half (host-neutral; the same path the ConnectionType facade drives).
            if (!ApplyRidgeHousingJoint(db, rId, ref ridge, fId, ref rafter, _ridgeRafter,
                    out ObjectId nr, out ObjectId nf, out int jid, out string diag))
            { ed.WriteMessage("\nNothing to cut -- " + diag + "."); return; }
            StampJoint(nr, nf, jid, ConnectionType.RidgeHousing(_ridgeRafter));
            ed.WriteMessage("\n[diag] " + diag);
            ed.WriteMessage("\nTRidgeRafter: joint #" + jid + " cut -- " + diag +
                            " (ridge " + nr.Handle + ", rafter " + nf.Handle + ").");
        }

        // The apply-half of TRidgeRafter, factored for the ConnectionType facade. HOST-NEUTRAL: the drop-in
        // pocket + chamfered tongue cut identically into a king post OR a principal rafter (RidgeKpostJoint maps
        // to the host's local frame). Id-only removal so a two-bay host keeps the other bay's pocket. False +
        // diag = nothing cut (e.g. the ridge does not run along the host width).
        public static bool ApplyRidgeHousingJoint(Database db, ObjectId rId, ref ManagedTimber.TFrame ridge,
            ObjectId hId, ref ManagedTimber.TFrame host, ManagedTimber.RidgeHousingSpec spec,
            out ObjectId newRidgeId, out ObjectId newHostId, out int jid, out string diag)
        {
            newRidgeId = ObjectId.Null; newHostId = ObjectId.Null;
            if (!ComputeRidgeHousingJoint(db, ref ridge, ref host, spec, out jid, out diag)) return false;
            newRidgeId = ManagedTimber.RebuildFromFrame(rId, ridge);
            newHostId = ManagedTimber.RebuildFromFrame(hId, host);
            return true;
        }

        // Compute-only core of the ridge housing (host pocket SUBTRACT + ridge TONGUE union, host-neutral king-post
        // OR rafter) WITHOUT the DB rebuild. Driven by ApplyRidgeHousingJoint (commit) and the joint PREVIEW (on
        // cloned frames). False + diag = nothing cut.
        public static bool ComputeRidgeHousingJoint(Database db, ref ManagedTimber.TFrame ridge,
            ref ManagedTimber.TFrame host, ManagedTimber.RidgeHousingSpec spec, out int jid, out string diag)
        {
            jid = 0;
            if (!ManagedTimber.RidgeKpostJoint(ridge, host, spec,
                    out List<(Point3d[] Poly, bool OnPost, double Xlo, double Xhi)> polys,
                    out List<(Point3d[] Poly, bool Subtract, double Xlo, double Xhi)> ridgeZPolys, out diag))
                return false;
            int reuse = ExistingRafterFootId(ridge, host);
            jid = reuse != 0 ? reuse : NextJointId(db);
            if (reuse != 0)
            {
                host.JointPolys?.RemoveAll(j => j.Joint == reuse);
                ridge.JointPolysZ?.RemoveAll(j => j.Joint == reuse);
            }
            ApplyRafterFoot(ref ridge, ref host, jid, polys);   // host pocket subtract (OnPost), shared id
            if (ridgeZPolys.Count > 0)   // ridge TONGUE (chamfered, Z-extruded into the pocket)
            {
                if (ridge.JointPolysZ == null) ridge.JointPolysZ = new List<(Point3d[], int, bool, double, double)>();
                foreach ((Point3d[] Poly, bool Subtract, double Xlo, double Xhi) z in ridgeZPolys)
                    ridge.JointPolysZ.Add((z.Poly, jid, z.Subtract, z.Xlo, z.Xhi));
            }
            return true;
        }

        // Remove a ridge -> principal-rafter housing: pick the ridge + rafter, drop the pocket + tongue sharing
        // their joint id from both, rebuild. (Parallels TRidgeDel.)
        [CommandMethod("TRidgeRafterDel")]
        public static void RidgeRafterDelete()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            if (!PickTimber(ed, db, "\nPick the RIDGE: ", out ObjectId rId, out ManagedTimber.TFrame ridge)) return;
            if (!PickTimber(ed, db, "\nPick the RAFTER: ", out ObjectId fId, out ManagedTimber.TFrame rafter)) return;
            if (rId == fId) { ed.WriteMessage("\nPick two different timbers."); return; }

            int id = ExistingRafterFootId(ridge, rafter);
            if (id == 0) { ed.WriteMessage("\nNo ridge -> rafter joint between those two timbers."); return; }
            ridge.JointPolys?.RemoveAll(j => j.Joint == id);
            rafter.JointPolys?.RemoveAll(j => j.Joint == id);
            ridge.JointPolysZ?.RemoveAll(j => j.Joint == id);   // the chamfered tongue

            ObjectId nr = ManagedTimber.RebuildFromFrame(rId, ridge);
            ObjectId nf = ManagedTimber.RebuildFromFrame(fId, rafter);
            ed.WriteMessage("\nTRidgeRafterDel: joint #" + id + " removed (ridge " + nr.Handle + ", rafter " + nf.Handle + ").");
        }

        // The ridge -> rafter housing sub-menu: edit the sticky _ridgeRafter (the bearing-seat depth). Enter / "Cut" proceeds.
        private static bool ReviewRidgeRafter(Editor ed)
        {
            while (true)
            {
                var pko = new PromptKeywordOptions(
                    "\nRidge->rafter housing -- Seat=" + _ridgeRafter.Seat + " ShoulderBottom=" + _ridgeRafter.ShoulderBottom +
                    " (On=" + (_ridgeRafter.On ? "Yes" : "No") + "). ")
                { AllowNone = true };
                pko.Keywords.Add("Cut");
                pko.Keywords.Add("Seat");
                pko.Keywords.Add("ShoulderBottom");
                pko.Keywords.Add("On");
                pko.Keywords.Default = "Cut";
                PromptResult kr = ed.GetKeywords(pko);
                if (kr.Status != PromptStatus.OK && kr.Status != PromptStatus.None) return false;
                string kw = kr.Status == PromptStatus.None ? "Cut" : kr.StringResult;
                if (kw == "Cut") return true;
                switch (kw)
                {
                    case "Seat": if (GetPositive(ed, "Housing seat depth", _ridgeRafter.Seat, out double sv)) _ridgeRafter.Seat = sv; break;
                    case "ShoulderBottom": if (GetDouble(ed, "Bottom shoulder -- the ridge's lower N inches stay full and bear (0 = full drop-in)", _ridgeRafter.ShoulderBottom, false, out double bv)) _ridgeRafter.ShoulderBottom = bv; break;
                    case "On":   _ridgeRafter.On = !_ridgeRafter.On; break;
                }
            }
        }

        // The post SIDE face a rafter foot dies into. The rafter foot is a PLUMB end (the bent graph clips it
        // with a vertical plane), so it butts the post side like a girt. Pick the rafter END nearest the post
        // (the foot) and the post side face whose outward normal most OPPOSES that foot's outward normal (the
        // face the foot butts). The cutter checks it's a depth face. False if no side face opposes.
        private static bool FindFootContact(ManagedTimber.TFrame rafter, ManagedTimber.TFrame post, out ManagedTimber.TFace pFace)
        {
            pFace = default;
            Point3d postC = post.O + post.Z * (post.L / 2.0);
            Point3d c0 = rafter.O, c1 = rafter.O + rafter.Z * rafter.L;
            Vector3d footN = (c0 - postC).Length <= (c1 - postC).Length ? rafter.NearN : rafter.FarN;

            double best = 0.0; bool found = false;
            foreach (ManagedTimber.TFace ps in ManagedTimber.Faces(post))
            {
                if (Math.Abs(ps.N.DotProduct(post.Z)) >= 0.5) continue;   // SIDE faces only
                double opp = footN.DotProduct(ps.N);                       // most negative = most opposing
                if (!found || opp < best) { best = opp; pFace = ps; found = true; }
            }
            return found && best < -0.1;   // the foot must actually point into a side face
        }

        // The joint id shared by both frames' polygon lists (JointPolys + JointPolysZ) -- a polygon joint
        // (rafter foot/head, ridge) already cut between them, or 0 if none. v1: at most one per pair.
        private static int ExistingRafterFootId(ManagedTimber.TFrame a, ManagedTimber.TFrame b)
        {
            System.Collections.Generic.HashSet<int> bIds = PolyJointIds(b);
            foreach (int id in PolyJointIds(a))
                if (id != 0 && bIds.Contains(id)) return id;
            return 0;
        }

        // All joint ids carried by a frame's polygon features (both extrusion orientations).
        private static System.Collections.Generic.HashSet<int> PolyJointIds(ManagedTimber.TFrame f)
        {
            var s = new System.Collections.Generic.HashSet<int>();
            if (f.JointPolys != null)
                foreach ((Point3d[] Poly, int Joint, bool Subtract, double Xlo, double Xhi) jp in f.JointPolys) s.Add(jp.Joint);
            if (f.JointPolysZ != null)
                foreach ((Point3d[] Poly, int Joint, bool Subtract, double Xlo, double Xhi) jp in f.JointPolysZ) s.Add(jp.Joint);
            if (f.JointPrisms != null)
                foreach ((Point3d[] Poly, Vector3d Extrude, int Joint, bool Subtract) jp in f.JointPrisms) s.Add(jp.Joint);
            return s;
        }

        // Two LOCAL elevation polygons (same frame: X = length, Y = depth) overlap if their length x depth
        // bounding boxes overlap. Used to de-dup a re-cut / orphan rafter-foot pocket by geometry.
        private static bool PolysOverlap(Point3d[] a, Point3d[] b)
        {
            if (a == null || b == null || a.Length == 0 || b.Length == 0) return false;
            double aXmin = double.MaxValue, aXmax = double.MinValue, aYmin = double.MaxValue, aYmax = double.MinValue;
            foreach (Point3d p in a) { if (p.X < aXmin) aXmin = p.X; if (p.X > aXmax) aXmax = p.X; if (p.Y < aYmin) aYmin = p.Y; if (p.Y > aYmax) aYmax = p.Y; }
            double bXmin = double.MaxValue, bXmax = double.MinValue, bYmin = double.MaxValue, bYmax = double.MinValue;
            foreach (Point3d p in b) { if (p.X < bXmin) bXmin = p.X; if (p.X > bXmax) bXmax = p.X; if (p.Y < bYmin) bYmin = p.Y; if (p.Y > bYmax) bYmax = p.Y; }
            return aXmin < bXmax && aXmax > bXmin && aYmin < bYmax && aYmax > bYmin;
        }

        // Two PRISM polygons (3D LOCAL points on the SAME timber) overlap if their axis-aligned bounding boxes do
        // -- de-dup a re-cut / ORPHAN prism joint (common->ridge, purlin->rafter) by geometry, not just id, so a
        // stale tongue/pocket whose id desynced is cleared. Sibling joints sit at different stations, so their
        // boxes don't overlap and they survive.
        private static bool PrismPolysOverlap(Point3d[] a, Point3d[] b)
        {
            if (a == null || b == null || a.Length == 0 || b.Length == 0) return false;
            double aXm = double.MaxValue, aXM = double.MinValue, aYm = double.MaxValue, aYM = double.MinValue, aZm = double.MaxValue, aZM = double.MinValue;
            foreach (Point3d p in a) { if (p.X < aXm) aXm = p.X; if (p.X > aXM) aXM = p.X; if (p.Y < aYm) aYm = p.Y; if (p.Y > aYM) aYM = p.Y; if (p.Z < aZm) aZm = p.Z; if (p.Z > aZM) aZM = p.Z; }
            double bXm = double.MaxValue, bXM = double.MinValue, bYm = double.MaxValue, bYM = double.MinValue, bZm = double.MaxValue, bZM = double.MinValue;
            foreach (Point3d p in b) { if (p.X < bXm) bXm = p.X; if (p.X > bXM) bXM = p.X; if (p.Y < bYm) bYm = p.Y; if (p.Y > bYM) bYM = p.Y; if (p.Z < bZm) bZm = p.Z; if (p.Z > bZM) bZM = p.Z; }
            return aXm < bXM && aXM > bXm && aYm < bYM && aYM > bYm && aZm < bZM && aZM > bZm;
        }

        // Route the cutter's polygons onto the two frames, stamped with the joint id: the post pocket is a
        // SUBTRACT (OnPost), the rafter stub a UNION. TFrame is a struct -> ref.
        private static void ApplyRafterFoot(ref ManagedTimber.TFrame rafter, ref ManagedTimber.TFrame post, int jid,
            List<(Point3d[] Poly, bool OnPost, double Xlo, double Xhi)> polys)
        {
            if (rafter.JointPolys == null) rafter.JointPolys = new List<(Point3d[], int, bool, double, double)>();
            if (post.JointPolys == null) post.JointPolys = new List<(Point3d[], int, bool, double, double)>();
            foreach ((Point3d[] Poly, bool OnPost, double Xlo, double Xhi) p in polys)
                (p.OnPost ? post.JointPolys : rafter.JointPolys).Add((p.Poly, jid, p.OnPost, p.Xlo, p.Xhi));   // post subtract, rafter union
        }

        // The rafter-foot recipe sub-menu: edit the sticky _rfoot (housing depth + the tenon tongue, like the
        // girt tenon -- thickness / length / top + bottom shoulders / width offset). Enter / "Cut" proceeds.
        private static bool ReviewRafterFoot(Editor ed)
        {
            while (true)
            {
                string tn = _rfoot.Tenon ? "On (T" + _rfoot.Thickness + " L" + _rfoot.Length + ")" : "Off";
                string pg = _rfoot.Peg.Count > 0 ? ("On x" + _rfoot.Peg.Count) : "Off";
                var pko = new PromptKeywordOptions(
                    "\nRafter foot -- Seat=" + _rfoot.Seat + " | Tenon: " + tn +
                    " (TopShoulder=" + _rfoot.ShoulderTop + " BotShoulder=" + _rfoot.ShoulderBottom + " Offset=" + _rfoot.Offset + ") | Pegs: " + pg + ". ")
                { AllowNone = true };
                pko.Keywords.Add("Cut");
                pko.Keywords.Add("Seat");
                pko.Keywords.Add("Tenon");
                pko.Keywords.Add("Thickness");
                pko.Keywords.Add("Length");
                pko.Keywords.Add("TopShoulder");
                pko.Keywords.Add("BotShoulder");
                pko.Keywords.Add("Offset");
                pko.Keywords.Add("Pegs");
                pko.Keywords.Default = "Cut";
                PromptResult kr = ed.GetKeywords(pko);
                if (kr.Status != PromptStatus.OK && kr.Status != PromptStatus.None) return false;
                string kw = kr.Status == PromptStatus.None ? "Cut" : kr.StringResult;
                if (kw == "Cut") return true;
                switch (kw)
                {
                    case "Seat":      if (GetDouble  (ed, "Housing seat depth into the post (0 = none)", _rfoot.Seat, false, out double dv)) _rfoot.Seat = dv; break;
                    case "Tenon":     _rfoot.Tenon = !_rfoot.Tenon; break;
                    case "Thickness": if (GetPositive(ed, "Tenon thickness", _rfoot.Thickness, out double tv)) _rfoot.Thickness = tv; break;
                    case "Length":    if (GetPositive(ed, "Tenon length past the housing", _rfoot.Length, out double lv)) _rfoot.Length = lv; break;
                    case "TopShoulder": if (GetDouble  (ed, "Top shoulder (down from the rafter top)", _rfoot.ShoulderTop, false, out double rt)) _rfoot.ShoulderTop = rt; break;
                    case "BotShoulder": if (GetDouble  (ed, "Bottom shoulder (up from the seat)", _rfoot.ShoulderBottom, false, out double rb)) _rfoot.ShoulderBottom = rb; break;
                    case "Offset":    if (GetDouble  (ed, "Width offset", _rfoot.Offset, true, out double ov)) _rfoot.Offset = ov; break;
                    case "Pegs":      ReviewPegs(ed, ref _rfoot.Peg); break;
                }
            }
        }

        // Cut a PURLIN housed into a RAFTER as a let-in DOVETAIL. Pick the PURLIN then the RAFTER: the rafter
        // gets the dovetail pocket (subtract) and the purlin's end grows the matching tongue (union). The recipe
        // (seat / height / width / flare) is reviewed; the cut is an id-keyed JointPrism on each timber (re-cut
        // REPLACES; TPurlinDel removes) and rides MOVE / SAVE. Needs a rafter SIDE face the purlin dies into.
        [CommandMethod("TPurlin")]
        public static void Purlin()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            if (!PickTimber(ed, db, "\nPick the PURLIN: ", out ObjectId puId, out ManagedTimber.TFrame purlin)) return;
            if (!PickTimber(ed, db, "\nPick the RAFTER: ", out ObjectId rId, out ManagedTimber.TFrame rafter)) return;
            if (puId == rId) { ed.WriteMessage("\nPick two different timbers."); return; }

            if (!ReviewPurlin(ed)) return;

            // Cut via the factored apply-half (the same path the ConnectionType facade drives; it finds the contact).
            if (!ApplyPurlinJoint(db, puId, ref purlin, rId, ref rafter, _purlin,
                    out ObjectId npu, out ObjectId nr, out int jid, out string diag))
            { ed.WriteMessage("\nNothing to cut -- " + diag + "."); return; }
            StampJoint(npu, nr, jid, ConnectionType.HousedDovetail(_purlin));
            ed.WriteMessage("\n[diag] " + diag);
            ed.WriteMessage("\nTPurlin: joint #" + jid + " cut -- " + diag +
                            " (purlin " + npu.Handle + ", rafter " + nr.Handle + ").");
        }

        // Remove a purlin dovetail: pick the purlin + rafter, drop the prisms sharing their joint id, rebuild.
        [CommandMethod("TPurlinDel")]
        public static void PurlinDelete()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            if (!PickTimber(ed, db, "\nPick the PURLIN: ", out ObjectId puId, out ManagedTimber.TFrame purlin)) return;
            if (!PickTimber(ed, db, "\nPick the RAFTER: ", out ObjectId rId, out ManagedTimber.TFrame rafter)) return;
            if (puId == rId) { ed.WriteMessage("\nPick two different timbers."); return; }

            int id = ExistingRafterFootId(purlin, rafter);
            if (id == 0) { ed.WriteMessage("\nNo purlin joint between those two timbers."); return; }
            purlin.JointPrisms?.RemoveAll(j => j.Joint == id);
            rafter.JointPrisms?.RemoveAll(j => j.Joint == id);

            ObjectId npu = ManagedTimber.RebuildFromFrame(puId, purlin);
            ObjectId nr  = ManagedTimber.RebuildFromFrame(rId, rafter);
            ed.WriteMessage("\nTPurlinDel: joint #" + id + " removed (purlin " + npu.Handle + ", rafter " + nr.Handle + ").");
        }

        // The purlin-dovetail recipe sub-menu: edit the sticky _purlin (housing seat / tongue length / base
        // width / band depth / taper angle). Enter / "Cut" proceeds.
        private static bool ReviewPurlin(Editor ed)
        {
            while (true)
            {
                var pko = new PromptKeywordOptions(
                    "\nHoused dovetail -- Seat=" + _purlin.Seat + " Length=" + _purlin.Length +
                    " Width=" + _purlin.Width + " Depth=" + _purlin.Depth + " Angle=" + _purlin.Angle + ". ")
                { AllowNone = true };
                pko.Keywords.Add("Cut");
                pko.Keywords.Add("Seat");
                pko.Keywords.Add("Length");
                pko.Keywords.Add("Width");
                pko.Keywords.Add("Depth");
                pko.Keywords.Add("Angle");
                pko.Keywords.Default = "Cut";
                PromptResult kr = ed.GetKeywords(pko);
                if (kr.Status != PromptStatus.OK && kr.Status != PromptStatus.None) return false;
                string kw = kr.Status == PromptStatus.None ? "Cut" : kr.StringResult;
                if (kw == "Cut") return true;
                switch (kw)
                {
                    case "Seat":   if (GetPositive(ed, "Full-section housing depth into the rafter", _purlin.Seat, out double sv)) _purlin.Seat = sv; break;
                    case "Length": if (GetPositive(ed, "Dovetail tongue length past the housing", _purlin.Length, out double lv)) _purlin.Length = lv; break;
                    case "Width":  if (GetPositive(ed, "Dovetail base width", _purlin.Width, out double wv)) _purlin.Width = wv; break;
                    case "Depth":  if (GetPositive(ed, "Dovetail band depth (flush with the top face)", _purlin.Depth, out double dv)) _purlin.Depth = dv; break;
                    case "Angle":  if (GetDouble  (ed, "Dovetail taper half-angle (degrees)", _purlin.Angle, false, out double av)) _purlin.Angle = av; break;
                }
            }
        }

        // Cut a COMMON RAFTER's head into a RIDGE as a let-in HOUSING. Pick the COMMON then the RIDGE: the ridge
        // gets the full-section gain (subtract) and the common's head fills it (union, same shape). The seat is
        // reviewed; the cut is an id-keyed JointPrism on each timber (re-cut REPLACES; TCommonRidgeDel removes)
        // and rides MOVE / SAVE. Needs a ridge SIDE face the common's head dies into.
        [CommandMethod("TCommonRidge")]
        public static void CommonRidge()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            if (!PickTimber(ed, db, "\nPick the COMMON rafter: ", out ObjectId cId, out ManagedTimber.TFrame common)) return;
            if (!PickTimber(ed, db, "\nPick the RIDGE: ", out ObjectId rId, out ManagedTimber.TFrame ridge)) return;
            if (cId == rId) { ed.WriteMessage("\nPick two different timbers."); return; }

            if (!ReviewCommonRidge(ed)) return;

            // Cut via the factored apply-half (the same path the ConnectionType facade drives; it finds the contact).
            if (!ApplyCommonRidgeJoint(db, cId, ref common, rId, ref ridge, _comridge,
                    out ObjectId nc, out ObjectId nr, out int jid, out string diag))
            { ed.WriteMessage("\nNothing to cut -- " + diag + "."); return; }
            StampJoint(nc, nr, jid, ConnectionType.CommonRidge(_comridge));
            ed.WriteMessage("\n[diag] " + diag);
            ed.WriteMessage("\nTCommonRidge: joint #" + jid + " cut -- " + diag +
                            " (common " + nc.Handle + ", ridge " + nr.Handle + ").");
        }

        // Remove a common -> ridge housing: pick the common + ridge, drop the prisms sharing their joint id, rebuild.
        [CommandMethod("TCommonRidgeDel")]
        public static void CommonRidgeDelete()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            if (!PickTimber(ed, db, "\nPick the COMMON rafter: ", out ObjectId cId, out ManagedTimber.TFrame common)) return;
            if (!PickTimber(ed, db, "\nPick the RIDGE: ", out ObjectId rId, out ManagedTimber.TFrame ridge)) return;
            if (cId == rId) { ed.WriteMessage("\nPick two different timbers."); return; }

            int id = ExistingRafterFootId(common, ridge);
            if (id == 0) { ed.WriteMessage("\nNo common -> ridge joint between those two timbers."); return; }
            common.JointPrisms?.RemoveAll(j => j.Joint == id);
            ridge.JointPrisms?.RemoveAll(j => j.Joint == id);

            ObjectId nc = ManagedTimber.RebuildFromFrame(cId, common);
            ObjectId nr = ManagedTimber.RebuildFromFrame(rId, ridge);
            ed.WriteMessage("\nTCommonRidgeDel: joint #" + id + " removed (common " + nc.Handle + ", ridge " + nr.Handle + ").");
        }

        // The common->ridge housing sub-menu: edit the sticky _comridge (the let-in seat). Enter / "Cut" proceeds.
        private static bool ReviewCommonRidge(Editor ed)
        {
            while (true)
            {
                var pko = new PromptKeywordOptions(
                    "\nCommon->ridge housing -- Seat=" + _comridge.Seat + ". ") { AllowNone = true };
                pko.Keywords.Add("Cut");
                pko.Keywords.Add("Seat");
                pko.Keywords.Default = "Cut";
                PromptResult kr = ed.GetKeywords(pko);
                if (kr.Status != PromptStatus.OK && kr.Status != PromptStatus.None) return false;
                string kw = kr.Status == PromptStatus.None ? "Cut" : kr.StringResult;
                if (kw == "Cut") return true;
                if (kw == "Seat" && GetPositive(ed, "Let-in housing depth into the ridge face", _comridge.Seat, out double sv))
                    _comridge.Seat = sv;
            }
        }

        // Cut a HOUSED COMMON RAFTER -> EAVE GIRT birdsmouth. Pick the COMMON then the GIRT: the rafter is
        // notched (seat let-in below the girt top + heel let-in inside the heel face) and the girt gets the
        // matching top pocket -- BOTH are cut, sharing one joint id (re-cut REPLACES; TCommonEaveDel removes).
        // The recipe (the two let-ins) is reviewed; the cuts ride MOVE / SAVE.
        [CommandMethod("TCommonEave")]
        public static void CommonEave()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            if (!PickTimber(ed, db, "\nPick the COMMON rafter: ", out ObjectId cId, out ManagedTimber.TFrame common)) return;
            if (!PickTimber(ed, db, "\nPick the EAVE GIRT: ", out ObjectId gId, out ManagedTimber.TFrame girt)) return;
            if (cId == gId) { ed.WriteMessage("\nPick two different timbers."); return; }

            if (!ReviewCommonEave(ed)) return;

            // Cut via the factored apply-half (the same path the ConnectionType facade drives).
            if (!ApplyCommonEaveJoint(db, cId, ref common, gId, ref girt, _comeave,
                    out ObjectId nc, out ObjectId ng, out int jid, out string diag))
            { ed.WriteMessage("\nNothing to cut -- " + diag + "."); return; }
            StampJoint(nc, ng, jid, ConnectionType.Birdsmouth(_comeave));
            ed.WriteMessage("\n[diag] " + diag);
            ed.WriteMessage("\nTCommonEave: joint #" + jid + " cut -- " + diag +
                            " (common " + nc.Handle + ", girt " + ng.Handle + ").");
        }

        // Remove a housed common -> eave-girt birdsmouth: pick the common + girt, drop the rafter notch + girt
        // pocket sharing their joint id, rebuild both.
        [CommandMethod("TCommonEaveDel")]
        public static void CommonEaveDelete()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            if (!PickTimber(ed, db, "\nPick the COMMON rafter: ", out ObjectId cId, out ManagedTimber.TFrame common)) return;
            if (!PickTimber(ed, db, "\nPick the EAVE GIRT: ", out ObjectId gId, out ManagedTimber.TFrame girt)) return;
            if (cId == gId) { ed.WriteMessage("\nPick two different timbers."); return; }

            int id = ExistingRafterFootId(common, girt);
            if (id == 0) { ed.WriteMessage("\nNo birdsmouth between those two timbers."); return; }
            common.JointPolys?.RemoveAll(j => j.Joint == id);
            girt.JointPolysZ?.RemoveAll(j => j.Joint == id);

            ObjectId nc = ManagedTimber.RebuildFromFrame(cId, common);
            ObjectId ng = ManagedTimber.RebuildFromFrame(gId, girt);
            ed.WriteMessage("\nTCommonEaveDel: birdsmouth #" + id + " removed (common " + nc.Handle + ", girt " + ng.Handle + ").");
        }

        // The housed-birdsmouth sub-menu: edit the sticky _comeave (the seat + heel let-ins). Enter / "Cut" proceeds.
        private static bool ReviewCommonEave(Editor ed)
        {
            while (true)
            {
                var pko = new PromptKeywordOptions(
                    "\nHoused birdsmouth -- Seat=" + _comeave.Seat + " Heel=" + _comeave.Heel + ". ")
                { AllowNone = true };
                pko.Keywords.Add("Cut");
                pko.Keywords.Add("Seat");
                pko.Keywords.Add("Heel");
                pko.Keywords.Default = "Cut";
                PromptResult kr = ed.GetKeywords(pko);
                if (kr.Status != PromptStatus.OK && kr.Status != PromptStatus.None) return false;
                string kw = kr.Status == PromptStatus.None ? "Cut" : kr.StringResult;
                if (kw == "Cut") return true;
                switch (kw)
                {
                    case "Seat": if (GetPositive(ed, "Seat let-in below the girt top", _comeave.Seat, out double sv)) _comeave.Seat = sv; break;
                    case "Heel": if (GetPositive(ed, "Heel let-in inside the heel face", _comeave.Heel, out double hv)) _comeave.Heel = hv; break;
                }
            }
        }

        // Strut tenon onto a host face: pick the strut then the member it beds into (rafter, post, king post),
        // review the tenon, cut. ONE solid is UNIONed to the strut (the tongue) and SUBTRACTed from the host
        // (the matching mortise), sharing one joint id (re-cut REPLACES; TStrutDel removes).
        [CommandMethod("TStrut")]
        public static void Strut()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            if (!PickTimber(ed, db, "\nPick the STRUT: ", out ObjectId sId, out ManagedTimber.TFrame strut)) return;
            if (!PickTimber(ed, db, "\nPick the member it beds into (rafter / post): ", out ObjectId hId, out ManagedTimber.TFrame host)) return;
            if (sId == hId) { ed.WriteMessage("\nPick two different timbers."); return; }

            if (!ReviewStrut(ed)) return;

            // Cut via the factored apply-half (the same path the ConnectionType facade drives).
            if (!ApplyStrutTenonJoint(db, sId, ref strut, hId, ref host, _strut,
                    out ObjectId ns, out ObjectId nh, out int jid, out string diag))
            { ed.WriteMessage("\nNothing to cut -- " + diag + "."); return; }
            StampJoint(ns, nh, jid, ConnectionType.StrutTenon(_strut));
            ed.WriteMessage("\n[diag] " + diag);
            ed.WriteMessage("\nTStrut: joint #" + jid + " cut -- " + diag +
                            " (strut " + ns.Handle + ", host " + nh.Handle + ").");
        }

        // Drop both halves of a strut/brace tenon by joint id: the strut tongue (JointPolys) + the host mortise
        // (JointPrisms now, JointPolys/JointPolysZ for joints cut by an older build) + the host peg bores (Pegs).
        private static void DropStrutJoint(ManagedTimber.TFrame strut, ManagedTimber.TFrame host, int id)
        {
            strut.JointPolys?.RemoveAll(j => j.Joint == id);
            host.JointPrisms?.RemoveAll(j => j.Joint == id);
            host.JointPolys?.RemoveAll(j => j.Joint == id);
            host.JointPolysZ?.RemoveAll(j => j.Joint == id);
            host.Pegs?.RemoveAll(p => p.Joint == id);
        }

        // The apply-half of TStrut / TBrace, factored so the ConnectionType facade can drive the SAME cut from a
        // timber PAIR (no command pick/review). Computes the tenon, shares/mints the joint id, UNIONs the tongue
        // onto the strut + SUBTRACTs the mortise from the host, rebuilds both solids. The frames are updated in
        // place (ref); the rebuilt ObjectIds + joint id come back for the caller's message. False + diag = nothing cut.
        public static bool ApplyStrutTenonJoint(Database db, ObjectId sId, ref ManagedTimber.TFrame strut,
            ObjectId hId, ref ManagedTimber.TFrame host, ManagedTimber.StrutTenonSpec spec,
            out ObjectId newStrutId, out ObjectId newHostId, out int jid, out string diag)
        {
            newStrutId = ObjectId.Null; newHostId = ObjectId.Null;
            if (!ComputeStrutTenonJoint(db, ref strut, ref host, spec, out jid, out diag)) return false;
            newStrutId = ManagedTimber.RebuildFromFrame(sId, strut);
            newHostId = ManagedTimber.RebuildFromFrame(hId, host);
            return true;
        }

        // Compute-only core of the strut/brace tenon: mutate the two frames (UNION the tongue onto the strut,
        // SUBTRACT the same solid from the host, sharing one joint id) WITHOUT touching the DB. ApplyStrutTenonJoint
        // adds the rebuild; the joint PREVIEW runs this on CLONED frames and ghosts the result. False + diag = nothing cut.
        public static bool ComputeStrutTenonJoint(Database db, ref ManagedTimber.TFrame strut,
            ref ManagedTimber.TFrame host, ManagedTimber.StrutTenonSpec spec, out int jid, out string diag)
        {
            jid = 0;
            if (!ManagedTimber.StrutTenonJoint(strut, host, spec,
                    out List<(Point3d[] Poly, double Xlo, double Xhi)> malePolys,
                    out List<(Point3d[] Poly, Vector3d Extrude)> hostPrisms,
                    out List<(Point3d C, Vector3d Axis, double R, double Half)> hostPegs, out diag))
                return false;
            jid = RouteStrutLists(db, ref strut, ref host, malePolys, hostPrisms, hostPegs);
            return true;
        }

        // Stamp the strut-engine output onto the two frames: share/mint the joint id (replacing any prior joint
        // between this pair), UNION every male poly onto the strut (JointPolys), SUBTRACT every host prism from the
        // host (JointPrisms), and bore every host peg into the host (Pegs). Shared by the strut/brace tenon and the
        // QP rafter apex.
        private static int RouteStrutLists(Database db, ref ManagedTimber.TFrame strut, ref ManagedTimber.TFrame host,
            List<(Point3d[] Poly, double Xlo, double Xhi)> malePolys, List<(Point3d[] Poly, Vector3d Extrude)> hostPrisms,
            List<(Point3d C, Vector3d Axis, double R, double Half)> hostPegs)
        {
            int reuse = ExistingRafterFootId(strut, host);
            int jid = reuse != 0 ? reuse : NextJointId(db);
            if (reuse != 0) DropStrutJoint(strut, host, reuse);
            if (strut.JointPolys == null) strut.JointPolys = new List<(Point3d[], int, bool, double, double)>();
            if (host.JointPrisms == null) host.JointPrisms = new List<(Point3d[], Vector3d, int, bool)>();
            foreach ((Point3d[] Poly, double Xlo, double Xhi) p in malePolys)
                strut.JointPolys.Add((p.Poly, jid, false, p.Xlo, p.Xhi));   // UNION onto the strut/male
            foreach ((Point3d[] Poly, Vector3d Extrude) p in hostPrisms)
                host.JointPrisms.Add((p.Poly, p.Extrude, jid, true));       // SUBTRACT from the host
            if (hostPegs != null && hostPegs.Count > 0)
            {
                if (host.Pegs == null) host.Pegs = new List<(Point3d, Vector3d, double, double, int)>();
                foreach ((Point3d C, Vector3d Axis, double R, double Half) pg in hostPegs)
                    host.Pegs.Add((pg.C, pg.Axis, pg.R, pg.Half, jid));     // BORE the host cheeks
            }
            return jid;
        }

        // Remove a strut tenon: pick the strut + host, drop the tongue + mortise sharing their joint id, rebuild both.
        [CommandMethod("TStrutDel")]
        public static void StrutDelete()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            if (!PickTimber(ed, db, "\nPick the STRUT: ", out ObjectId sId, out ManagedTimber.TFrame strut)) return;
            if (!PickTimber(ed, db, "\nPick the member it beds into (rafter / post): ", out ObjectId hId, out ManagedTimber.TFrame host)) return;
            if (sId == hId) { ed.WriteMessage("\nPick two different timbers."); return; }

            int id = ExistingRafterFootId(strut, host);
            if (id == 0) { ed.WriteMessage("\nNo strut tenon between those two timbers."); return; }
            DropStrutJoint(strut, host, id);

            ObjectId ns = ManagedTimber.RebuildFromFrame(sId, strut);
            ObjectId nh = ManagedTimber.RebuildFromFrame(hId, host);
            ed.WriteMessage("\nTStrutDel: tenon #" + id + " removed (strut " + ns.Handle + ", host " + nh.Handle + ").");
        }

        // The strut tenon sub-menu: edit the sticky _strut (thickness / length / shoulders / lateral offset).
        // Enter / "Cut" proceeds. Shoulders are world-up keyed (Top = higher edge, Bottom = lower edge).
        private static bool ReviewStrut(Editor ed)
        {
            while (true)
            {
                string hg = _strut.Hsg.On ? ("On (Seat " + _strut.Hsg.Seat + ")") : "Off";
                string pg = _strut.Peg.Count > 0 ? ("On x" + _strut.Peg.Count) : "Off";
                var pko = new PromptKeywordOptions(
                    "\nStrut tenon -- Thickness=" + _strut.Thickness + " Length=" + _strut.Length +
                    " ShoulderTop=" + _strut.ShoulderTop + " ShoulderBottom=" + _strut.ShoulderBottom +
                    " Offset=" + _strut.Offset + " | Housing: " + hg + " | Pegs: " + pg + ". ")
                { AllowNone = true };
                pko.Keywords.Add("Cut");
                pko.Keywords.Add("Thickness");
                pko.Keywords.Add("Length");
                pko.Keywords.Add("ShoulderTop");
                pko.Keywords.Add("ShoulderBottom");
                pko.Keywords.Add("Offset");
                pko.Keywords.Add("Housing");
                pko.Keywords.Add("Seat");
                pko.Keywords.Add("Pegs");
                pko.Keywords.Default = "Cut";
                PromptResult kr = ed.GetKeywords(pko);
                if (kr.Status != PromptStatus.OK && kr.Status != PromptStatus.None) return false;
                string kw = kr.Status == PromptStatus.None ? "Cut" : kr.StringResult;
                if (kw == "Cut") return true;
                switch (kw)
                {
                    case "Thickness":      if (GetPositive(ed, "Tenon thickness (width)", _strut.Thickness, out double tv)) _strut.Thickness = tv; break;
                    case "Length":         if (GetPositive(ed, "Tenon length into the host", _strut.Length, out double lv)) _strut.Length = lv; break;
                    case "ShoulderTop":    if (GetPositive(ed, "Shoulder inset from the higher (world-up) edge", _strut.ShoulderTop, out double stv)) _strut.ShoulderTop = stv; break;
                    case "ShoulderBottom": if (GetPositive(ed, "Shoulder inset from the lower edge", _strut.ShoulderBottom, out double sbv)) _strut.ShoulderBottom = sbv; break;
                    case "Offset":         if (GetDouble(ed, "Lateral offset off the strut center", _strut.Offset, true, out double ov)) _strut.Offset = ov; break;
                    case "Housing":        ReviewHousing(ed, ref _strut.Hsg); break;
                    case "Seat":           if (GetDouble(ed, "Housing seat depth", _strut.Hsg.Seat, false, out double sv)) { _strut.Hsg.Seat = sv; _strut.Hsg.On = sv > 1e-6; } break;
                    case "Pegs":           ReviewPegs(ed, ref _strut.Peg); break;
                }
            }
        }

        // Brace tenon: the SAME end->side tenon as TStrut (StrutTenonJoint) -- no new engine -- just the brace
        // defaults (1.5" thick) and a BAREFACED option (tongue flush to one cheek). Pick the brace then the
        // member it beds into (girt / post). ONE solid UNIONs the strut tongue, SUBTRACTs the host mortise,
        // sharing one joint id (re-cut REPLACES; TBraceDel removes).
        [CommandMethod("TBrace")]
        public static void Brace()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            if (!PickTimber(ed, db, "\nPick the BRACE: ", out ObjectId sId, out ManagedTimber.TFrame brace)) return;
            if (!PickTimber(ed, db, "\nPick the member it beds into (girt / post): ", out ObjectId hId, out ManagedTimber.TFrame host)) return;
            if (sId == hId) { ed.WriteMessage("\nPick two different timbers."); return; }

            if (!ReviewBrace(ed)) return;

            // Barefaced overrides Offset: push the tongue flush to a cheek (= (W - Thickness)/2), Flip picks side.
            ManagedTimber.StrutTenonSpec spec = _brace;
            if (_braceBarefaced)
            {
                double t = System.Math.Min(System.Math.Max(_brace.Thickness, 0.0), brace.W);
                spec.Offset = (_braceFlip ? -1.0 : 1.0) * (brace.W - t) / 2.0;
            }

            if (!ApplyStrutTenonJoint(db, sId, ref brace, hId, ref host, spec,
                    out ObjectId ns, out ObjectId nh, out int jid, out string diag))
            { ed.WriteMessage("\nNothing to cut -- " + diag + "."); return; }
            // Stamp as the BRACE-named type so re-picking the pair loads the Brace preset in the pane
            // (older brace stamps say "Strut tenon" -- FromState resolves those by name unchanged).
            StampJoint(ns, nh, jid, ConnectionType.BraceTenon(spec));
            ed.WriteMessage("\n[diag] " + diag + (_braceBarefaced ? " (barefaced offset " + spec.Offset.ToString("0.00") + ")" : ""));
            ed.WriteMessage("\nTBrace: joint #" + jid + " cut -- " + diag +
                            " (brace " + ns.Handle + ", host " + nh.Handle + ").");
        }

        // Remove a brace tenon: pick the brace + host, drop the tongue + mortise sharing their joint id, rebuild.
        [CommandMethod("TBraceDel")]
        public static void BraceDelete()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            if (!PickTimber(ed, db, "\nPick the BRACE: ", out ObjectId sId, out ManagedTimber.TFrame brace)) return;
            if (!PickTimber(ed, db, "\nPick the member it beds into (girt / post): ", out ObjectId hId, out ManagedTimber.TFrame host)) return;
            if (sId == hId) { ed.WriteMessage("\nPick two different timbers."); return; }

            int id = ExistingRafterFootId(brace, host);
            if (id == 0) { ed.WriteMessage("\nNo brace tenon between those two timbers."); return; }
            DropStrutJoint(brace, host, id);

            ObjectId ns = ManagedTimber.RebuildFromFrame(sId, brace);
            ObjectId nh = ManagedTimber.RebuildFromFrame(hId, host);
            ed.WriteMessage("\nTBraceDel: tenon #" + id + " removed (brace " + ns.Handle + ", host " + nh.Handle + ").");
        }

        // The brace tenon sub-menu: the strut fields + Barefaced (flush to a cheek) and Flip (which cheek).
        // When Barefaced is On, Offset is computed from the brace width and the manual Offset is ignored.
        private static bool ReviewBrace(Editor ed)
        {
            while (true)
            {
                string face = _braceBarefaced ? ("On (flush " + (_braceFlip ? "-" : "+") + " cheek)") : "Off";
                string hg = _brace.Hsg.On ? ("On (Seat " + _brace.Hsg.Seat + ")") : "Off";
                string pg = _brace.Peg.Count > 0 ? ("On x" + _brace.Peg.Count) : "Off";
                var pko = new PromptKeywordOptions(
                    "\nBrace tenon -- Thickness=" + _brace.Thickness + " Length=" + _brace.Length +
                    " ShoulderTop=" + _brace.ShoulderTop + " ShoulderBottom=" + _brace.ShoulderBottom +
                    " Barefaced=" + face + (_braceBarefaced ? "" : " Offset=" + _brace.Offset) + " | Housing: " + hg + " | Pegs: " + pg + ". ")
                { AllowNone = true };
                pko.Keywords.Add("Cut");
                pko.Keywords.Add("Thickness");
                pko.Keywords.Add("Length");
                pko.Keywords.Add("ShoulderTop");
                pko.Keywords.Add("ShoulderBottom");
                pko.Keywords.Add("Barefaced");
                pko.Keywords.Add("Flip");
                pko.Keywords.Add("Offset");
                pko.Keywords.Add("Housing");
                pko.Keywords.Add("Seat");
                pko.Keywords.Add("Pegs");
                pko.Keywords.Default = "Cut";
                PromptResult kr = ed.GetKeywords(pko);
                if (kr.Status != PromptStatus.OK && kr.Status != PromptStatus.None) return false;
                string kw = kr.Status == PromptStatus.None ? "Cut" : kr.StringResult;
                if (kw == "Cut") return true;
                switch (kw)
                {
                    case "Thickness":      if (GetPositive(ed, "Tenon thickness (width)", _brace.Thickness, out double tv)) _brace.Thickness = tv; break;
                    case "Length":         if (GetPositive(ed, "Tenon length into the host", _brace.Length, out double lv)) _brace.Length = lv; break;
                    case "ShoulderTop":    if (GetPositive(ed, "Shoulder inset from the higher (world-up) edge", _brace.ShoulderTop, out double stv)) _brace.ShoulderTop = stv; break;
                    case "ShoulderBottom": if (GetPositive(ed, "Shoulder inset from the lower edge", _brace.ShoulderBottom, out double sbv)) _brace.ShoulderBottom = sbv; break;
                    case "Barefaced":      _braceBarefaced = !_braceBarefaced; break;
                    case "Flip":           _braceFlip = !_braceFlip; break;
                    case "Offset":         if (GetDouble(ed, "Lateral offset off the brace center (when not barefaced)", _brace.Offset, true, out double ov)) _brace.Offset = ov; break;
                    case "Housing":        ReviewHousing(ed, ref _brace.Hsg); break;
                    case "Seat":           if (GetDouble(ed, "Housing seat depth", _brace.Hsg.Seat, false, out double sv)) { _brace.Hsg.Seat = sv; _brace.Hsg.On = sv > 1e-6; } break;
                    case "Pegs":           ReviewPegs(ed, ref _brace.Peg); break;
                }
            }
        }

        // ---- QP rafter APEX box tenon (two principal rafters meeting at the peak, no king post) -------------

        // The QP apex is a STRUT TENON + HOUSING (housing on by default) cut at the apex bearing -- same engine,
        // same spec as TStrut, just fed the male rafter's beveled peak end-cap as the bearing. Seeded from
        // the user's saved default (factory = StrutTenonSpec.QPRafterDefault, the old hand literal).
        private static ManagedTimber.StrutTenonSpec _qprafter = JointDefaults.QPRafter;

        // The MALE rafter's beveled PEAK end-cap that meets the HOST at the apex = the male end-cap whose outward
        // normal points most toward the host body. (The two heads don't cleanly coplanar-mate -- one houses INTO
        // the other -- so this picks by direction, not FacesMate.)
        private static bool FindApexSeat(ManagedTimber.TFrame male, ManagedTimber.TFrame host, out ManagedTimber.TFace seatFace)
        {
            seatFace = default;
            ManagedTimber.TFace[] mf = ManagedTimber.Faces(male);
            Point3d hc = host.O + host.Z * (host.L / 2.0);
            double best = 0.0; bool found = false;
            for (int i = 0; i <= 1; i++)   // the two END caps (0 = near, 1 = far)
            {
                Vector3d toHost = hc - mf[i].C;
                if (toHost.Length < 1e-9) continue;
                double d = mf[i].N.DotProduct(toHost.GetNormal());
                if (d > best) { best = d; seatFace = mf[i]; found = true; }
            }
            return found;
        }

        // Compute-only core: find the apex seat (the male rafter's beveled peak end-cap) and drive the STRUT engine
        // with that explicit bearing + the housing -- the QP apex IS a strut tenon + housing. No DB rebuild (Apply
        // adds it). `bearingFaceN = -seatFace.N` so the into-host direction (bn) = the male end-cap's outward normal.
        public static bool ComputeQPRafterJoint(Database db, ref ManagedTimber.TFrame male, ref ManagedTimber.TFrame host,
            ManagedTimber.StrutTenonSpec spec, out int jid, out string diag)
        {
            jid = 0;
            if (!FindApexSeat(male, host, out ManagedTimber.TFace seatFace))
            { diag = "no apex contact -- the two rafter heads must meet at the peak"; return false; }
            if (!ManagedTimber.StrutTenonJoint(male, host, spec,
                    out List<(Point3d[] Poly, double Xlo, double Xhi)> malePolys,
                    out List<(Point3d[] Poly, Vector3d Extrude)> hostPrisms,
                    out List<(Point3d C, Vector3d Axis, double R, double Half)> hostPegs, out diag,
                    hasBearing: true, bearingCtr: seatFace.C, bearingFaceN: -seatFace.N))
                return false;
            jid = RouteStrutLists(db, ref male, ref host, malePolys, hostPrisms, hostPegs);
            return true;
        }

        // Apply-half: Compute then rebuild both solids. Shared by TQPRafter (command) and the ConnectionType facade.
        public static bool ApplyQPRafterJoint(Database db, ObjectId mId, ref ManagedTimber.TFrame male,
            ObjectId hId, ref ManagedTimber.TFrame host, ManagedTimber.StrutTenonSpec spec,
            out ObjectId newMaleId, out ObjectId newHostId, out int jid, out string diag)
        {
            newMaleId = ObjectId.Null; newHostId = ObjectId.Null;
            if (!ComputeQPRafterJoint(db, ref male, ref host, spec, out jid, out diag)) return false;
            newMaleId = ManagedTimber.RebuildFromFrame(mId, male);
            newHostId = ManagedTimber.RebuildFromFrame(hId, host);
            return true;
        }

        // QP rafter APEX box tenon: pick the MALE rafter (its peak end seats + tenons into the host) then the HOST
        // rafter. Cuts the housing + tenon (+ optional pegs); re-cut REPLACES by joint id (TQPRafterDel removes).
        [CommandMethod("TQPRafter")]
        public static void QPRafter()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            if (!PickTimber(ed, db, "\nPick the MALE rafter (its peak end seats into the other): ", out ObjectId mId, out ManagedTimber.TFrame male)) return;
            if (!PickTimber(ed, db, "\nPick the HOST rafter it beds into: ", out ObjectId hId, out ManagedTimber.TFrame host)) return;
            if (mId == hId) { ed.WriteMessage("\nPick two different timbers."); return; }

            if (!ReviewQPRafter(ed)) return;

            if (!ApplyQPRafterJoint(db, mId, ref male, hId, ref host, _qprafter,
                    out ObjectId nm, out ObjectId nh, out int jid, out string diag))
            { ed.WriteMessage("\nNothing to cut -- " + diag + "."); return; }
            StampJoint(nm, nh, jid, ConnectionType.QPRafterApex(_qprafter));
            ed.WriteMessage("\nTQPRafter: joint #" + jid + " cut -- " + diag +
                            " (male " + nm.Handle + ", host " + nh.Handle + ").");
        }

        // Remove a QP rafter apex joint: pick the two rafters, drop the housing/tenon/pegs sharing their id, rebuild.
        [CommandMethod("TQPRafterDel")]
        public static void QPRafterDelete()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            if (!PickTimber(ed, db, "\nPick the MALE rafter: ", out ObjectId mId, out ManagedTimber.TFrame male)) return;
            if (!PickTimber(ed, db, "\nPick the HOST rafter: ", out ObjectId hId, out ManagedTimber.TFrame host)) return;
            if (mId == hId) { ed.WriteMessage("\nPick two different timbers."); return; }

            int id = ExistingRafterFootId(male, host);
            if (id == 0) { ed.WriteMessage("\nNo apex joint between those two rafters."); return; }
            DropStrutJoint(male, host, id);   // male tongue + host mortise (JointPrisms) + host peg bores

            ObjectId nm = ManagedTimber.RebuildFromFrame(mId, male);
            ObjectId nh = ManagedTimber.RebuildFromFrame(hId, host);
            ed.WriteMessage("\nTQPRafterDel: joint #" + id + " removed (male " + nm.Handle + ", host " + nh.Handle + ").");
        }

        // QP rafter apex sub-menu (a strut tenon + housing): the tongue (thickness / length / shoulders / offset)
        // + the housing (toggle + Seat). Enter / "Cut" proceeds.
        private static bool ReviewQPRafter(Editor ed)
        {
            while (true)
            {
                string hg = _qprafter.Hsg.On ? ("On (Seat " + _qprafter.Hsg.Seat + ")") : "Off";
                string pg = _qprafter.Peg.Count > 0 ? ("On x" + _qprafter.Peg.Count) : "Off";
                var pko = new PromptKeywordOptions(
                    "\nQP rafter apex -- Thickness=" + _qprafter.Thickness + " Length=" + _qprafter.Length +
                    " ShoulderTop=" + _qprafter.ShoulderTop + " ShoulderBottom=" + _qprafter.ShoulderBottom +
                    " Offset=" + _qprafter.Offset + " | Housing: " + hg + " | Pegs: " + pg + ". ")
                { AllowNone = true };
                pko.Keywords.Add("Cut");
                pko.Keywords.Add("Thickness");
                pko.Keywords.Add("Length");
                pko.Keywords.Add("ShoulderTop");
                pko.Keywords.Add("ShoulderBottom");
                pko.Keywords.Add("Offset");
                pko.Keywords.Add("Housing");
                pko.Keywords.Add("Seat");
                pko.Keywords.Add("Pegs");
                pko.Keywords.Default = "Cut";
                PromptResult kr = ed.GetKeywords(pko);
                if (kr.Status != PromptStatus.OK && kr.Status != PromptStatus.None) return false;
                string kw = kr.Status == PromptStatus.None ? "Cut" : kr.StringResult;
                if (kw == "Cut") return true;
                switch (kw)
                {
                    case "Thickness":      if (GetPositive(ed, "Tenon thickness (tongue width)", _qprafter.Thickness, out double tv)) _qprafter.Thickness = tv; break;
                    case "Length":         if (GetPositive(ed, "Tenon length past the housing", _qprafter.Length, out double lv)) _qprafter.Length = lv; break;
                    case "ShoulderTop":    if (GetDouble(ed, "Shoulder inset from the higher (world-up) edge", _qprafter.ShoulderTop, false, out double stv)) _qprafter.ShoulderTop = stv; break;
                    case "ShoulderBottom": if (GetDouble(ed, "Shoulder inset from the lower edge", _qprafter.ShoulderBottom, false, out double sbv)) _qprafter.ShoulderBottom = sbv; break;
                    case "Offset":         if (GetDouble(ed, "Lateral offset off the tongue center", _qprafter.Offset, true, out double ofv)) _qprafter.Offset = ofv; break;
                    case "Housing":        ReviewHousing(ed, ref _qprafter.Hsg); break;
                    case "Seat":           if (GetDouble(ed, "Housing seat depth", _qprafter.Hsg.Seat, false, out double sv)) { _qprafter.Hsg.Seat = sv; _qprafter.Hsg.On = sv > 1e-6; } break;
                    case "Pegs":           ReviewPegs(ed, ref _qprafter.Peg); break;
                }
            }
        }


        // Rescan managed timbers for coincident (mating) faces and mark the derived nodes. Nodes are
        // NOT stored -- each scan recomputes them; separate the timbers and rescan -> the node is gone.
        [CommandMethod("TScan")]
        public static void Scan()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            int count = ManagedTimber.Enumerate(db).Count;
            var nodes = ManagedTimber.ComputeNodes(db);

            DrawNodeMarkers(doc, db, nodes);
            ed.WriteMessage("\nTScan: " + count + " managed timbers, " + nodes.Count + " node(s).");
            foreach (Point3d n in nodes)
                ed.WriteMessage("\n  node @ " + n.X.ToString("0.#") + "," + n.Y.ToString("0.#") + "," + n.Z.ToString("0.#"));
        }

        // SPIKE (A0): confirm interactive face picking + the BRep read. Run TPickFace, pick a face on a
        // managed timber -> it highlights natively; prints the matched analytic face's centre + normal.
        // (Throwaway; no geometry created.)
        [CommandMethod("TPickFace")]
        public static void PickFaceSpike()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            if (!ManagedTimber.PickFace(ed, db, "\nPick a timber FACE: ", out ObjectId id, out ManagedTimber.TFace f))
            { ed.WriteMessage("\nTPickFace: no face picked."); return; }

            ed.WriteMessage("\nTPickFace: timber " + id.Handle +
                "\n  centre " + f.C.X.ToString("0.#") + "," + f.C.Y.ToString("0.#") + "," + f.C.Z.ToString("0.#") +
                "\n  normal " + f.N.X.ToString("0.##") + "," + f.N.Y.ToString("0.##") + "," + f.N.Z.ToString("0.##") +
                "\n  extents " + f.UHalf.ToString("0.#") + " x " + f.VHalf.ToString("0.#"));
        }

        // (TMove / TRotate removed: a ManagedTransformOverrule on Solid3d now keeps each timber's stored
        // frame + scarf node in lockstep through NATIVE MOVE / ROTATE / MIRROR / ALIGN, so no special
        // commands are needed. See Managed/ManagedTransformOverrule.cs and ApplyManagedTransform above.)

        // ---- orientation presets (palette) ------------------------------------------------
        // The managed verbs read the section roll -- and, for TPlace, the extrusion direction --
        // from the current UCS. These three presets set the standard orthographic UCSs so the
        // user doesn't hand-roll the UCS per member. In the model basis the BENTS lie in the
        // world YZ plane and the WALLS in the world XZ plane, so (Robert's mapping, 2026-07-07 --
        // the two were reversed before):
        //   Plan (Top)  -> X = world +X, Y = world +Y : Z = world +Z (posts / verticals)
        //   Bent        -> X = world +Y, Y = world +Z : Z = world +X (the bent cross-plane;
        //                  TPlace extrudes along the building)
        //   Wall        -> X = world +X, Y = world +Z : Z = world -Y (the wall elevation;
        //                  TPlace extrudes across the span, into the screen)
        // TSpan / TJoin take their direction from the picked faces, so for them the UCS only sets
        // the section roll.

        [CommandMethod("TUcsPlan")]
        public static void UcsPlan()
            => SetUcs(Vector3d.XAxis, Vector3d.YAxis, Vector3d.ZAxis, "Plan (Top)");

        [CommandMethod("TUcsBent")]
        public static void UcsBent()
            => SetUcs(Vector3d.YAxis, Vector3d.ZAxis, Vector3d.XAxis, "Bent elevation");

        [CommandMethod("TUcsWall")]
        public static void UcsWall()
            => SetUcs(Vector3d.XAxis, Vector3d.ZAxis, Vector3d.YAxis.Negate(), "Wall elevation");

        private static void SetUcs(Vector3d x, Vector3d y, Vector3d z, string label)
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            ed.CurrentUserCoordinateSystem = Matrix3d.AlignCoordinateSystem(
                Point3d.Origin, Vector3d.XAxis, Vector3d.YAxis, Vector3d.ZAxis,
                Point3d.Origin, x, y, z);
            ed.WriteMessage("\nUCS: " + label + ".");
        }

        // ---- helpers ----------------------------------------------------------------------

        // Pick a managed timber (whole entity); returns its id + stored placement frame.
        private static bool PickTimber(Editor ed, Database db, string msg,
            out ObjectId id, out ManagedTimber.TFrame frame)
        {
            id = ObjectId.Null; frame = default;
            var peo = new PromptEntityOptions(msg);
            peo.SetRejectMessage("\nPick a managed timber (any managed member -- placed or generated).");
            peo.AddAllowedClass(typeof(Solid3d), exactMatch: false);
            PromptEntityResult per = ed.GetEntity(peo);
            if (per.Status != PromptStatus.OK) return false;
            id = per.ObjectId;
            if (!ManagedTimber.TryReadFrame(db, id, out frame))
            { ed.WriteMessage("\nNot a managed timber."); return false; }
            return true;
        }

        private static void DrawNodeMarkers(Document doc, Database db, List<Point3d> nodes)
        {
            using (doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                // Ensure the node layer (blue -- ACI 5, safe for red/green colour-blindness).
                var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                if (!lt.Has(ManagedTimber.NodeLayer))
                {
                    lt.UpgradeOpen();
                    var ltr = new LayerTableRecord { Name = ManagedTimber.NodeLayer, Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByAci, 5) };
                    lt.Add(ltr); tr.AddNewlyCreatedDBObject(ltr, true);
                }

                var btr = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
                // Clear previous markers (nodes are transient).
                foreach (ObjectId id in btr)
                    if (tr.GetObject(id, OpenMode.ForRead) is Entity e && e.Layer == ManagedTimber.NodeLayer)
                    { e.UpgradeOpen(); e.Erase(); }

                // Markers are spheres sized to poke OUT of the timbers, so they read from any view
                // (a flat circle goes edge-on and vanishes) and aren't buried inside the solids.
                foreach (Point3d n in nodes)
                {
                    var sph = new Solid3d { Layer = ManagedTimber.NodeLayer };
                    sph.CreateSphere(1.0);   // 2" dia marker
                    sph.TransformBy(Matrix3d.Displacement(n - Point3d.Origin));
                    btr.AppendEntity(sph); tr.AddNewlyCreatedDBObject(sph, true);
                }
                tr.Commit();
            }
        }

        // Build a frame-local box and BoolUnite it into target (in the current transaction).
        private static void UnionBox(Transaction tr, BlockTableRecord btr, Solid3d target, ManagedTimber.TFrame f,
            double x0, double x1, double y0, double y1, double z0, double z1)
        {
            Solid3d box = ManagedTimber.MakeBoxSolidLocal(f, x0, x1, y0, y1, z0, z1);
            btr.AppendEntity(box); tr.AddNewlyCreatedDBObject(box, true);
            target.BooleanOperation(BooleanOperationType.BoolUnite, box);
        }

        // Fill a rectangle with flat floor JOISTS by FOUR face picks: the two facing SPAN faces (the
        // carriers -- girt / summer / sill sides -- the joists run flush between) + the two
        // DISTRIBUTION faces (the run bounds), then Count or center Spacing. Each joist is a flat box
        // hung between the span faces (square ends -> TScan finds the seat nodes), arrayed along the
        // run, with its TOP FLUSH with the carrier tops (the dropped-in dovetail arrangement) minus
        // the sticky Drop (recessed-deck practice; 0 = flush). Role is always "Joist" (the verb IS the
        // family -- the palette section supplies W/D only) so role-keyed systems see the field. Joists
        // are left untagged -- TAssign the field to its FLOOR afterward (which also mints the
        // J-<floor>-n labels). (Joist = the pitch-0 case of a future generalized purlin/common/joist
        // array.)
        //
        // END JOINERY (floor systems phase 2): with the sticky Joint spec ON (the default), every joist
        // lands DOVETAILED -- both ends cut into the two span carriers as dropped-in housed dovetails
        // via the host-neutral PurlinRafterJoint engine (housing + flared top-band tongue, JointPrisms).
        // Each end gets its own joint id + a "Housed dovetail" pane stamp, so the Joints pane can
        // re-cut or delete any single end; the carriers rebuild ONCE for the whole row. The Joint
        // keyword reviews the sticky spec (On/Off + the five dovetail knobs).
        private static double _joistDrop = 0.0;   // session-sticky Drop below the carrier tops
        // Session-sticky joist-end dovetail recipe -- the pane's saved "Housed dovetail" values (the
        // same cut, host-neutral) but seeded OFF: joinery is DELAYED AND DELIBERATE (Robert's rule) --
        // joists land plain, get moved/adjusted freely, then a selection + TJointAll cuts the
        // dovetails. The Joint keyword still opts in at place time.
        private static ManagedTimber.PurlinRafterSpec _joistDove = JoistDoveSeed();
        private static ManagedTimber.PurlinRafterSpec JoistDoveSeed()
        {
            var s = JointDefaults.Purlin;
            s.On = false;
            return s;
        }

        // A managed box's extent along a world direction: its center projected onto dir, plus/minus the
        // box half-dimensions projected the same way. Used to find where two carriers overlap along the
        // joist run without any face picking.
        private static void ExtentAlong(ManagedTimber.TFrame f, Vector3d dir, out double lo, out double hi)
        {
            double c = (f.O + f.Z * (f.L / 2.0)).GetAsVector().DotProduct(dir);
            double half = System.Math.Abs(f.X.DotProduct(dir)) * (f.W / 2.0)
                        + System.Math.Abs(f.Y.DotProduct(dir)) * (f.D / 2.0)
                        + System.Math.Abs(f.Z.DotProduct(dir)) * (f.L / 2.0);
            lo = c - half; hi = c + half;
        }

        [CommandMethod("TJoist")]
        public static void Joists()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            // W/D from the palette section (or prompts); the role is fixed -- no type prompt.
            const string type = "Joist";
            double w, d;
            if (ManagedSection.HasCurrent)
            {
                w = ManagedSection.Width; d = ManagedSection.Depth;
                ed.WriteMessage("\nSection: " + (int)w + "x" + (int)d + " (palette).");
            }
            else
            {
                if (!GetPositive(ed, "Width", 8.0, out w)) return;
                if (!GetPositive(ed, "Depth", 10.0, out d)) return;
            }

            // Carrier pair: pick the two CARRIER TIMBERS (like TSpan) and let FindFacingPair locate the
            // facing bearing faces the joists run between -- no face picking. The ids are kept for the
            // end-dovetail cut.
            if (!PickTimber(ed, db, "\nPick the FIRST carrier timber: ", out ObjectId caId, out ManagedTimber.TFrame carAfr)) return;
            if (!PickTimber(ed, db, "\nPick the SECOND carrier timber: ", out ObjectId cbId, out ManagedTimber.TFrame carBfr)) return;
            if (!ManagedTimber.FindFacingPair(carAfr, carBfr, out ManagedTimber.TFace fa, out ManagedTimber.TFace fb, out double gap))
            { ed.WriteMessage("\nThose timbers have no facing, overlapping faces to bear joists between."); return; }
            if (gap <= 1e-6) { ed.WriteMessage("\nNo positive gap between the carriers."); return; }
            Vector3d off = (fb.C - fa.C) - gap * fa.N;
            Point3d cL = fa.C + off * 0.5;                       // lateral center between the faces

            // depth = the vertical in-plane axis of the bearing face; width/run = the horizontal one
            // (joists repeat along it). Floor is WCS XY, so "vertical" = WCS Z.
            Vector3d up = Vector3d.ZAxis;
            Vector3d depthAxis, runDir;
            if (System.Math.Abs(fa.U.GetNormal().DotProduct(up)) >= System.Math.Abs(fa.V.GetNormal().DotProduct(up)))
            { depthAxis = fa.U.GetNormal(); runDir = fa.V.GetNormal(); }
            else
            { depthAxis = fa.V.GetNormal(); runDir = fa.U.GetNormal(); }

            // FLUSH TOPS: joist top = carrier top (a full side face's upper edge IS the carrier top),
            // minus the sticky Drop. Unequal carriers take the lower top so no joist stands proud.
            Vector3d vUp = depthAxis.DotProduct(up) >= 0 ? depthAxis : -depthAxis;
            double topA = FaceTop(fa, vUp), topB = FaceTop(fb, vUp);
            double top = System.Math.Min(topA, topB);
            if (System.Math.Abs(topA - topB) > 0.01)
                ed.WriteMessage("\nCarrier tops differ by " + System.Math.Abs(topA - topB).ToString("0.###")
                                + "\" -- using the lower.");

            // Run bounds: default to where the two carriers OVERLAP along the run direction -- no picks
            // (the joists fill the shared bearing length). The Bounds keyword below overrides this with
            // two explicit run-bound faces for a partial fill.
            ExtentAlong(carAfr, runDir, out double loA, out double hiA);
            ExtentAlong(carBfr, runDir, out double loB, out double hiB);
            double r0 = System.Math.Max(loA, loB), r1 = System.Math.Min(hiA, hiB);
            if (r1 - r0 <= 1e-6) { ed.WriteMessage("\nThe carriers don't overlap along their length -- nothing to fill."); return; }
            double L = r1 - r0;

            // Count (N even, end-inset) or on-center Spacing (default 36", centered in the run). Bounds
            // re-picks the run extent (two faces) for a partial fill; Drop edits the sticky top recess
            // (0 = flush tops); Joint reviews the sticky end-dovetail recipe (On/Off + knobs). All re-ask.
            string mode;
            for (; ; )
            {
                var pko = new PromptKeywordOptions("\nDistribute by");
                pko.Keywords.Add("Count");
                pko.Keywords.Add("Spacing");
                pko.Keywords.Add("Bounds");
                pko.Keywords.Add("Drop");
                pko.Keywords.Add("Joint");
                pko.Keywords.Default = "Spacing";
                PromptResult kr = ed.GetKeywords(pko);
                if (kr.Status != PromptStatus.OK) return;
                if (kr.StringResult == "Joint") { if (!ReviewJoistDove(ed)) return; continue; }
                if (kr.StringResult == "Bounds")
                {
                    if (!ManagedTimber.PickFace(ed, db, "\nPick the FIRST run-bound face: ", out _, out ManagedTimber.TFace fc)) continue;
                    if (!ManagedTimber.PickFace(ed, db, "\nPick the SECOND run-bound face: ", out _, out ManagedTimber.TFace fd)) continue;
                    double a = fc.C.GetAsVector().DotProduct(runDir), b = fd.C.GetAsVector().DotProduct(runDir);
                    r0 = System.Math.Min(a, b); r1 = System.Math.Max(a, b);
                    L = r1 - r0;
                    if (L <= 1e-6) { ed.WriteMessage("\nThose faces don't bound a run along the carriers."); r0 = System.Math.Max(loA, loB); r1 = System.Math.Min(hiA, hiB); L = r1 - r0; }
                    else ed.WriteMessage("\nRun bounds set: " + L.ToString("0.#") + "\" along the carriers.");
                    continue;
                }
                if (kr.StringResult != "Drop") { mode = kr.StringResult; break; }
                if (!GetDouble(ed, "Drop below carrier tops", _joistDrop, false, out double dr)) return;
                _joistDrop = System.Math.Max(0.0, dr);
            }

            var stations = new List<double>();
            if (mode == "Count")
            {
                var io = new PromptIntegerOptions("\nNumber of joists: ")
                { DefaultValue = 5, LowerLimit = 1, AllowNegative = false, AllowZero = false };
                PromptIntegerResult ir = ed.GetInteger(io);
                if (ir.Status != PromptStatus.OK) return;
                int N = ir.Value;
                double step = L / (N + 1);
                for (int k = 1; k <= N; k++) stations.Add(r0 + k * step);
            }
            else
            {
                var so = new PromptDistanceOptions("\nOn-center spacing <36>: ")
                { DefaultValue = 36.0, UseDefaultValue = true, AllowNegative = false, AllowZero = false };
                PromptDoubleResult sr = ed.GetDistance(so);
                if (sr.Status != PromptStatus.OK) return;
                double S = sr.Value > 0 ? sr.Value : 36.0;
                // Symmetric field at exactly S on-center, but never an end margin below a half-space:
                // N = floor(L/S) makes the end margin (L-(N-1)S)/2 >= S/2 in every case. Round() would
                // cram in an extra joist when L sits past the half-step and push the outer joists to
                // within S/4 of the ends (the "too close to the ends" bug). Epsilon so an exact multiple
                // keeps the higher count -- a clean half-space at each end.
                int N = System.Math.Max(1, (int)System.Math.Floor(L / S + 1e-6));
                double first = r0 + (L - (N - 1) * S) / 2.0;   // center the field in the run
                for (int k = 0; k < N; k++) stations.Add(first + k * S);
            }

            // Place each joist: shift the lateral center to the run station and lift the section so
            // its top sits at (carrier top - Drop); box runs `gap` along fa.N, d deep, w wide.
            cL += vUp * ((top - _joistDrop - d / 2.0) - cL.GetAsVector().DotProduct(vUp));
            double baseRun = cL.GetAsVector().DotProduct(runDir);
            int drawn = 0;
            var joists = new List<(ObjectId Id, ManagedTimber.TFrame F)>();
            foreach (double s in stations)
            {
                Point3d origin = cL + runDir * (s - baseRun);
                ObjectId jId = ManagedTimber.DrawBox(origin, fa.N, depthAxis, runDir, gap, d, w, type, "", "butt", "butt");
                drawn++;
                if (_joistDove.On && !jId.IsNull && ManagedTimber.TryReadFrame(db, jId, out ManagedTimber.TFrame jfr))
                    joists.Add((jId, jfr));
            }

            // END DOVETAILS: cut both ends of every joist into its carrier with the sticky spec. All
            // prisms accumulate on working frames -- each joist rebuilds once, each CARRIER once for
            // the whole row (not per joist). Ids are minted from one batch base (NextJointId scans the
            // DB, which doesn't see the in-memory prisms). Fresh joists carry no prior joints, so no
            // reuse/overlap purge is needed. Each end is stamped "Housed dovetail" for the Joints pane.
            int cutEnds = 0, missedEnds = 0;
            if (joists.Count > 0
                && ManagedTimber.TryReadFrame(db, caId, out ManagedTimber.TFrame carA)
                && ManagedTimber.TryReadFrame(db, cbId, out ManagedTimber.TFrame carB))
            {
                int nextId = NextJointId(db);
                var stamps = new List<(ObjectId J, bool OnA, int Jid)>();
                for (int ji = 0; ji < joists.Count; ji++)
                {
                    ManagedTimber.TFrame jf = joists[ji].F;
                    for (int side = 0; side <= 1; side++)
                    {
                        ManagedTimber.TFrame host = side == 0 ? carA : carB;
                        if (!FindFootContact(jf, host, out ManagedTimber.TFace hFace)
                            || !ManagedTimber.PurlinRafterJoint(jf, host, hFace, _joistDove,
                                   out List<(Point3d[] Poly, Vector3d Extrude, bool OnRafter)> prisms, out _))
                        { missedEnds++; continue; }
                        int jid = nextId++;
                        if (jf.JointPrisms == null) jf.JointPrisms = new List<(Point3d[], Vector3d, int, bool)>();
                        if (host.JointPrisms == null) host.JointPrisms = new List<(Point3d[], Vector3d, int, bool)>();
                        foreach ((Point3d[] Poly, Vector3d Extrude, bool OnRafter) p in prisms)
                            (p.OnRafter ? host.JointPrisms : jf.JointPrisms).Add((p.Poly, p.Extrude, jid, p.OnRafter));
                        if (side == 0) carA = host; else carB = host;
                        stamps.Add((joists[ji].Id, side == 0, jid));
                        cutEnds++;
                    }
                    joists[ji] = (joists[ji].Id, jf);
                }
                if (cutEnds > 0)
                {
                    var remap = new Dictionary<ObjectId, ObjectId>();
                    foreach ((ObjectId Id, ManagedTimber.TFrame F) j in joists)
                        remap[j.Id] = ManagedTimber.RebuildFromFrame(j.Id, j.F);
                    ObjectId nA = ManagedTimber.RebuildFromFrame(caId, carA);
                    ObjectId nB = ManagedTimber.RebuildFromFrame(cbId, carB);
                    ConnectionType dove = ConnectionType.HousedDovetail(_joistDove);   // one recipe for the row
                    foreach ((ObjectId J, bool OnA, int Jid) st in stamps)
                        StampJoint(remap[st.J], st.OnA ? nA : nB, st.Jid, dove);
                }
            }

            ed.WriteMessage("\nTJoist: " + drawn + " " + type + " " + (int)w + "x" + (int)d + "x" +
                            gap.ToString("0.#") + (_joistDrop > 0 ? " dropped " + _joistDrop.ToString("0.###") + "\"" : ", tops flush")
                            + (_joistDove.On
                               ? ", " + cutEnds + " end dovetail(s)" + (missedEnds > 0 ? " (" + missedEnds + " end(s) missed the carrier)" : "")
                               : ", ends butt (Joint off)")
                            + " -- TAssign the field to its floor for J-labels.");
        }

        // The joist end-dovetail sub-menu: toggle + edit the sticky _joistDove (the same five knobs as
        // the purlin housed dovetail -- one engine, one vocabulary). Enter / "Done" returns to TJoist.
        private static bool ReviewJoistDove(Editor ed)
        {
            while (true)
            {
                var pko = new PromptKeywordOptions(
                    "\nJoist end dovetail [" + (_joistDove.On ? "ON" : "OFF") + "] -- Seat=" + _joistDove.Seat +
                    " Length=" + _joistDove.Length + " Width=" + _joistDove.Width +
                    " Depth=" + _joistDove.Depth + " Angle=" + _joistDove.Angle + ". ") { AllowNone = true };
                pko.Keywords.Add("Done");
                pko.Keywords.Add(_joistDove.On ? "Off" : "On");   // the prompt rebuilds each pass
                pko.Keywords.Add("Seat");
                pko.Keywords.Add("Length");
                pko.Keywords.Add("Width");
                pko.Keywords.Add("Depth");
                pko.Keywords.Add("Angle");
                pko.Keywords.Default = "Done";
                PromptResult kr = ed.GetKeywords(pko);
                if (kr.Status != PromptStatus.OK && kr.Status != PromptStatus.None) return false;
                string kw = kr.Status == PromptStatus.None ? "Done" : kr.StringResult;
                if (kw == "Done") return true;
                switch (kw)
                {
                    case "On":     _joistDove.On = true; break;
                    case "Off":    _joistDove.On = false; break;
                    case "Seat":   if (GetPositive(ed, "Full-section housing depth into the carrier", _joistDove.Seat, out double sv)) _joistDove.Seat = sv; break;
                    case "Length": if (GetPositive(ed, "Dovetail tongue length past the housing", _joistDove.Length, out double lv)) _joistDove.Length = lv; break;
                    case "Width":  if (GetPositive(ed, "Dovetail base width", _joistDove.Width, out double wv)) _joistDove.Width = wv; break;
                    case "Depth":  if (GetPositive(ed, "Dovetail band depth (flush with the top face)", _joistDove.Depth, out double dv)) _joistDove.Depth = dv; break;
                    case "Angle":  if (GetDouble  (ed, "Dovetail taper half-angle (degrees)", _joistDove.Angle, false, out double av)) _joistDove.Angle = av; break;
                }
            }
        }

        // Highest reach of a rectangular face along an up axis: center + both projected half-extents.
        private static double FaceTop(ManagedTimber.TFace f, Vector3d vUp)
            => f.C.GetAsVector().DotProduct(vUp)
               + System.Math.Abs(f.U.GetNormal().DotProduct(vUp)) * f.UHalf
               + System.Math.Abs(f.V.GetNormal().DotProduct(vUp)) * f.VHalf;

        // Width / Depth / type for a new member. When the palette has a current section active
        // (ManagedSection), use it with no prompts; otherwise prompt at the command line as before.
        private static bool GetSection(Editor ed, out double w, out double d, out string type)
        {
            if (ManagedSection.HasCurrent)
            {
                w = ManagedSection.Width; d = ManagedSection.Depth; type = ManagedSection.Type;
                ed.WriteMessage("\nSection: " + type + " " + (int)w + "x" + (int)d + " (palette).");
                return true;
            }
            w = 0; d = 0; type = null;
            if (!GetPositive(ed, "Width", 8.0, out w)) return false;
            if (!GetPositive(ed, "Depth", 10.0, out d)) return false;
            type = GetType(ed);
            return type != null;
        }

        private static bool GetPositive(Editor ed, string label, double dflt, out double value)
        {
            var o = new PromptDoubleOptions("\n" + label + " <" + dflt + ">: ")
            { AllowNegative = false, AllowZero = false, DefaultValue = dflt, UseDefaultValue = true };
            PromptDoubleResult r = ed.GetDouble(o);
            value = r.Value;
            return r.Status == PromptStatus.OK;
        }

        // Like GetPositive but allows zero (a shoulder of 0) and, when allowNeg, a negative value (a width
        // offset to either side). Returns false (leaving the sticky default) if the user cancels.
        private static bool GetDouble(Editor ed, string label, double dflt, bool allowNeg, out double value)
        {
            var o = new PromptDoubleOptions("\n" + label + " <" + dflt + ">: ")
            { AllowNegative = allowNeg, AllowZero = true, DefaultValue = dflt, UseDefaultValue = true };
            PromptDoubleResult r = ed.GetDouble(o);
            value = r.Value;
            return r.Status == PromptStatus.OK;
        }

        private static string GetType(Editor ed)
        {
            var so = new PromptStringOptions("\nTimber type <Timber>: ") { AllowSpaces = false };
            PromptResult r = ed.GetString(so);
            if (r.Status != PromptStatus.OK) return null;
            return string.IsNullOrWhiteSpace(r.StringResult) ? "Timber" : r.StringResult;
        }
    }
}
