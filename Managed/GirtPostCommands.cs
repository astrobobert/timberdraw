using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.GraphicsInterface;
using Autodesk.AutoCAD.Runtime;

namespace TimberDraw
{
    // ManagedCommands part: the girt-post box-tenon family -- TJoint, TJointAll (the
    // deliberate batch), TJointSync (re-cut/re-attach), TJointDel.
    public partial class ManagedCommands
    {
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

        // Batch-cut the frame's end->side joinery, DELIBERATELY: the batch is SELECTION-scoped
        // (Robert's call, batch-2 #8 -- the old All keyword is gone). The selected timbers are the
        // ones that GET joints (the male side: girt ends, post feet, summer ends, joist ends);
        // hosts are always found drawing-wide. A pickfirst set is honored -- select the timbers,
        // then run the command; otherwise it asks (AutoCAD's Previous option re-uses the last set).
        // Passes, each with its own sticky recipe, reviewed only when its role is in scope:
        //   girt-family end -> post side  (mortise & tenon + pegs; _joint)
        //   post foot -> sill             (short unpegged stub; _sillJoint)
        //   summer end -> girt/sill       (tusk tenon; _summerJoint)
        //   joist end -> carrier          (housed dovetail; _joistDove -- the deliberate half of TJoist)
        //   common -> ridge + eave girt   (let-in housing at the head, housed birdsmouth at the
        //                                  eave; _comridge / _comeave -- batch-3 #4)
        // Contacts that already carry a joint are SKIPPED (idempotent -- safe to re-run after manual
        // tweaks); a host fed by several members rebuilds once.
        [CommandMethod("TJointAll", CommandFlags.Modal | CommandFlags.UsePickSet)]
        public static void JointAll()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            // DELIBERATE scope (Robert's rule: joinery is applied to selected timbers or groups).
            PromptSelectionResult sel = ed.SelectImplied();
            if (sel.Status != PromptStatus.OK || sel.Value == null || sel.Value.Count == 0)
            {
                var filter = new SelectionFilter(new[] { new TypedValue((int)DxfCode.Start, "3DSOLID") });
                var pso = new PromptSelectionOptions { MessageForAdding = "\nSelect the timbers to joint: " };
                sel = ed.GetSelection(pso, filter);
                if (sel.Status != PromptStatus.OK) return;
            }
            var scope = new HashSet<ObjectId>(sel.Value.GetObjectIds());

            var all = ManagedTimber.EnumerateWithRole(db);
            // Working frames carry the accumulating Features/Pegs; geometry (O/axes/L/D/W) never changes and
            // Faces() ignore features, so mates can be found against the originals throughout the pass.
            var work = new Dictionary<ObjectId, ManagedTimber.TFrame>();
            foreach ((ObjectId Id, ManagedTimber.TFrame F, string Role) t in all) work[t.Id] = t.F;
            var dirty = new HashSet<ObjectId>();
            var cuts = new List<(ObjectId girt, ObjectId post, int jid, ConnectionType ct)>();   // for the joint-type stamp
            int nextId = NextJointId(db);
            int cut = 0, skipped = 0, failed = 0;

            bool InScope(ObjectId id) => scope.Contains(id);
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
                bool doveWasOn = _joistDove.On;   // the pass forces On for its own run; a later
                _joistDove.On = true;             // TJoist must NOT inherit it (deliberate joinery)
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
                            // TOUCHING contact required -- the direction-only test cut PHANTOM
                            // dovetails into every parallel girt (Robert's CSV catch).
                            if (!FindTouchingFootContact(joist, host, out ManagedTimber.TFace hFace)) continue;
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
                _joistDove.On = doveWasOn;   // the sticky leak made a later TJoist auto-cut at place time
            }
            int joistCuts = cut - girtCuts - sillCuts - summerCuts;

            // COMMON pass (batch-3 #4 -- "why doesn't TJointAll work on Commons"): each in-scope
            // common rafter gets BOTH its cuts -- the head's let-in housing into the ridge and the
            // housed birdsmouth over the eave girt. Two sticky recipes (_comridge / _comeave), each
            // reviewed once; Escape skips just that half. Already-cut pairs skip by shared id.
            if (RolePresent(CommonRoles))
            {
                ed.WriteMessage("\nCommon pass -- head -> ridge housing:");
                if (!ReviewCommonRidge(ed)) ed.WriteMessage("\nCommon -> ridge housings skipped.");
                else
                {
                    ConnectionType cridge = ConnectionType.CommonRidge(_comridge);
                    foreach ((ObjectId Id, ManagedTimber.TFrame F, string Role) c in all)
                    {
                        if (!CommonRoles.Contains(c.Role) || !InScope(c.Id)) continue;
                        foreach ((ObjectId Id, ManagedTimber.TFrame F, string Role) r in all)
                        {
                            if (r.Id == c.Id || !RidgeRoles.Contains(r.Role)) continue;
                            ManagedTimber.TFrame common = work[c.Id];
                            ManagedTimber.TFrame ridge = work[r.Id];
                            if (ExistingRafterFootId(common, ridge) != 0) { skipped++; continue; }
                            // TOUCHING contact, like the joist pass -- direction-only would cut a
                            // phantom housing for every common/ridge pairing in the drawing.
                            if (!FindTouchingFootContact(common, ridge, out ManagedTimber.TFace rFace)) continue;
                            if (!ManagedTimber.CommonRidgeJoint(common, ridge, rFace, _comridge,
                                    out List<(Point3d[] Poly, Vector3d Extrude, bool OnRidge)> prisms, out _))
                            { failed++; continue; }
                            int jid = nextId++;
                            if (common.JointPrisms == null) common.JointPrisms = new List<(Point3d[], Vector3d, int, bool)>();
                            if (ridge.JointPrisms == null) ridge.JointPrisms = new List<(Point3d[], Vector3d, int, bool)>();
                            foreach ((Point3d[] Poly, Vector3d Extrude, bool OnRidge) p in prisms)
                                (p.OnRidge ? ridge.JointPrisms : common.JointPrisms).Add((p.Poly, p.Extrude, jid, p.OnRidge));
                            work[c.Id] = common; work[r.Id] = ridge;
                            dirty.Add(c.Id); dirty.Add(r.Id);
                            cuts.Add((c.Id, r.Id, jid, cridge));
                            cut++;
                        }
                    }
                }

                ed.WriteMessage("\nCommon pass -- eave-girt birdsmouth:");
                if (!ReviewCommonEave(ed)) ed.WriteMessage("\nBirdsmouths skipped.");
                else
                {
                    ConnectionType bmouth = ConnectionType.Birdsmouth(_comeave);
                    foreach ((ObjectId Id, ManagedTimber.TFrame F, string Role) c in all)
                    {
                        if (!CommonRoles.Contains(c.Role) || !InScope(c.Id)) continue;
                        foreach ((ObjectId Id, ManagedTimber.TFrame F, string Role) g in all)
                        {
                            if (g.Id == c.Id || !EaveGirtRoles.Contains(g.Role)) continue;
                            ManagedTimber.TFrame common = work[c.Id];
                            ManagedTimber.TFrame girt = work[g.Id];
                            if (ExistingRafterFootId(common, girt) != 0) { skipped++; continue; }
                            // CommonEaveJoint finds the crossing itself -- a false here just means
                            // the common doesn't ride this girt (not a collapsed cut).
                            if (!ManagedTimber.CommonEaveJoint(common, girt, _comeave,
                                    out Point3d[] rNotch, out double rXlo, out double rXhi,
                                    out Point3d[] gPocket, out double gZlo, out double gZhi, out _))
                                continue;
                            int jid = nextId++;
                            if (common.JointPolys == null) common.JointPolys = new List<(Point3d[], int, bool, double, double)>();
                            if (girt.JointPolysZ == null) girt.JointPolysZ = new List<(Point3d[], int, bool, double, double)>();
                            common.JointPolys.Add((rNotch, jid, false, rXlo, rXhi));
                            girt.JointPolysZ.Add((gPocket, jid, true, gZlo, gZhi));
                            work[c.Id] = common; work[g.Id] = girt;
                            dirty.Add(c.Id); dirty.Add(g.Id);
                            cuts.Add((c.Id, g.Id, jid, bmouth));
                            cut++;
                        }
                    }
                }
            }
            int commonCuts = cut - girtCuts - sillCuts - summerCuts - joistCuts;

            var remap = new Dictionary<ObjectId, ObjectId>();
            foreach (ObjectId id in dirty) remap[id] = ManagedTimber.RebuildFromFrame(id, work[id]);
            foreach ((ObjectId girt, ObjectId post, int jid, ConnectionType ct) c in cuts)
                StampJoint(remap[c.girt], remap[c.post], c.jid, c.ct);
            ed.WriteMessage("\nTJointAll: cut " + cut + " joint(s)" +
                            (sillCuts > 0 ? " (" + sillCuts + " post-foot -> sill)" : "") +
                            (summerCuts > 0 ? " (" + summerCuts + " summer -> girt)" : "") +
                            (joistCuts > 0 ? " (" + joistCuts + " joist end(s))" : "") +
                            (commonCuts > 0 ? " (" + commonCuts + " common cut(s))" : "") +
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
                            ed.WriteMessage("\n  joint #" + jid + " (" + ct.DisplayName + "): no contact with its partner -- left as-is.");
                        }
                        continue;
                    }

                    // RE-ATTACH: the partner died (a regenerate replaced it). Find the timber this one
                    // now TOUCHES end-into-side (FacesMate -- the direction-only test could re-attach
                    // to a non-touching host), in either direction, skipping mates it is already
                    // jointed to -- only then strip the orphaned features and cut the recipe fresh.
                    ObjectId hostId = ObjectId.Null; ManagedTimber.TFrame hfr = default; bool selIsMale = true;
                    foreach ((ObjectId Id, ManagedTimber.TFrame F, string Role) t in ManagedTimber.EnumerateWithRole(db))
                    {
                        if (t.Id == selId || SharedJointId(sf, t.F) != 0) continue;
                        if (FindTouchingFootContact(sf, t.F, out _)) { hostId = t.Id; hfr = t.F; selIsMale = true; break; }
                        if (FindTouchingFootContact(t.F, sf, out _)) { hostId = t.Id; hfr = t.F; selIsMale = false; break; }
                    }
                    if (hostId.IsNull)
                    {
                        apart++;
                        ed.WriteMessage("\n  joint #" + jid + " (" + ct.DisplayName + "): partner gone and nothing to re-attach to -- left as-is.");
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
                        ed.WriteMessage("\n  joint #" + jid + " (" + ct.DisplayName + "): re-attach failed -- " + ra.Diag +
                                        " (orphaned features stripped; UNDO restores).");
                    }
                }
            }

            ed.WriteMessage("\nTJointSync: " + recut + " re-cut, " + reattached + " re-attached"
                            + (apart > 0 ? ", " + apart + " without contact (left as-is)" : "")
                            + (unknown > 0 ? ", " + unknown + " with no stored recipe (skipped)" : "") + ".");
        }

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
    }
}
