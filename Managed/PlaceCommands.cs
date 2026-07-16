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
    // ManagedCommands part: placement verbs -- TPlace, TSpan, TJoin, TFit, TSection.
    public partial class ManagedCommands
    {
        // No-op backstop for SolidGhost's palette nudge: the hidden keyword a palette edit queues
        // into a pending ghost prompt normally lands AS that keyword; if the loop ended a beat
        // earlier (race), the queued word reaches the bare command line instead -- where it runs
        // this and does nothing, rather than erroring "Unknown command". Not a user verb.
        [CommandMethod(SolidGhost.NudgeKeyword, CommandFlags.NoHistory)]
        public static void PaletteNudgeNoop() { }

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

            // Two-phase SOLID preview (Robert's call 2026-07-15: no transient wireframes): phase 1
            // rolls the W x D section about the base point, phase 2 sets the length along UCS Z.
            // Every pick/typed value reconstructs the preview solid in place; Enter accepts the
            // phase. A palette catalog click mid-command re-sections it (the SolidGhost nudge).
            double angle = 0.0, len = 96.0;
            // Roll the section axes about UCS Z (right-handed: widthAxis x depthAxis = lengthAxis).
            Vector3d RollW() => ux * Math.Cos(angle) + uy * Math.Sin(angle);
            Vector3d RollD() => ux * (-Math.Sin(angle)) + uy * Math.Cos(angle);
            void Reread()
            {
                if (!ManagedSection.HasCurrent) return;
                w = ManagedSection.Width; d = ManagedSection.Depth; type = ManagedSection.Type;
            }
            using (var ghost = new SolidGhost())
            {
                void Rebuild() => ghost.Update(SolidGhost.BoxFrame(a, uz, RollD(), RollW(), len, d, w));
                System.Action follow = SolidGhost.Nudge;
                ManagedSection.Changed += follow;
                try
                {
                    Rebuild();
                    while (true)   // phase 1: roll
                    {
                        var ao = new PromptAngleOptions("\nRoll the section -- pick/type an angle, Enter accepts <"
                            + (angle * 180.0 / Math.PI).ToString("0.#") + " deg>: ")
                        { UseBasePoint = true, BasePoint = a, AllowNone = true };
                        SolidGhost.AddNudge(ao.Keywords);
                        PromptDoubleResult r = ed.GetAngle(ao);
                        if (r.Status == PromptStatus.Keyword) { Reread(); Rebuild(); continue; }
                        if (r.Status == PromptStatus.None) break;
                        if (r.Status != PromptStatus.OK) return;
                        angle = r.Value; Rebuild();
                    }
                    while (true)   // phase 2: length
                    {
                        var lo = new PromptDistanceOptions("\nLength -- pick/type a distance, Enter accepts <"
                            + len.ToString("0.#") + ">: ")
                        { UseBasePoint = true, BasePoint = a, AllowNone = true };
                        SolidGhost.AddNudge(lo.Keywords);
                        PromptDoubleResult r = ed.GetDistance(lo);
                        if (r.Status == PromptStatus.Keyword) { Reread(); Rebuild(); continue; }
                        if (r.Status == PromptStatus.None) break;
                        if (r.Status != PromptStatus.OK) return;
                        if (r.Value > 1e-6) len = r.Value;
                        Rebuild();
                    }
                }
                finally { ManagedSection.Changed -= follow; }
            }

            ObjectId id = ManagedTimber.DrawBox(a, uz, RollD(), RollW(), len, d, w, type, "", "butt", "butt");
            ed.WriteMessage("\nTPlace: " + type + " " + (int)w + "x" + (int)d + "x" + len.ToString("0.#") +
                            " roll " + (angle * 180.0 / Math.PI).ToString("0.#") + " deg (" + id.Handle + ").");
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

            // SOLID preview (Robert's call 2026-07-15: no transient wireframes): the span slides on
            // one rail coordinate s along the connected timbers (fa.U oriented up), measured from
            // the UCS-origin datum (s=0 = base). The girt's Center/Bottom/Top face lands on that
            // line. Height comes from PICKED POINTS -- the point's rail component only, so a snap
            // anywhere transfers its HEIGHT, never its diagonal distance; each pick reconstructs
            // the preview in place and Enter applies. Exact keyboard entry stays via the Height
            // keyword; a palette catalog click mid-command re-sections it (the SolidGhost nudge).
            CoordinateSystem3d ucs = ed.CurrentUserCoordinateSystem.CoordinateSystem3d;
            Vector3d rail = fa.U.GetNormal();
            if (rail.DotProduct(Vector3d.ZAxis) < 0.0) rail = rail.Negate();   // +s is upward
            Point3d ucsOrg = ucs.Origin;
            double S(Point3d p) => (p - ucsOrg).DotProduct(rail);
            // The s=0 point on the rail = origin slid down to the UCS-origin datum. Used as the
            // pick base point so a typed "0" (zero from the base) lands on the floor.
            Point3d floorAnchor = origin - rail * S(origin);
            double sTarget = 0.0;
            string just = "Center";
            // Near end-face centre that puts the justified face on the line s == sTarget. Only the
            // rail component of origin moves; fa.V / fa.N stay as set by origin.
            Point3d SpanOrigin()
            {
                double railDim = railVertical ? d : w;
                double centerS = sTarget;
                if (just == "Bottom") centerS += railDim / 2.0;        // lower face on the line
                else if (just == "Top") centerS -= railDim / 2.0;      // upper face on the line
                return origin + rail * (centerS - S(origin));
            }
            bool canPickHeight = Math.Abs(fa.U.GetNormal().DotProduct(ucs.Zaxis.GetNormal())) < 0.5;
            if (!canPickHeight)
                ed.WriteMessage("\nTip: a cursor can't read the height in this UCS (post is end-on) -- " +
                                "snap to a feature at the height you want, or switch to an elevation UCS (Bent/Wall).");

            using (var ghost = new SolidGhost())
            {
                void Rebuild() => ghost.Update(SolidGhost.BoxFrame(SpanOrigin(), fa.N, dAxis, wAxis, gap, d, w));
                System.Action follow = SolidGhost.Nudge;
                ManagedSection.Changed += follow;
                try
                {
                    Rebuild();
                    while (true)
                    {
                        var ppo = new PromptPointOptions(
                            "\nHeight above base -- pick/snap a point at the height, Enter accepts; [Center/Bottom/Top/Height]: ")
                        { UseBasePoint = true, BasePoint = floorAnchor, AllowNone = true };
                        ppo.Keywords.Add("Center");
                        ppo.Keywords.Add("Bottom");
                        ppo.Keywords.Add("Top");
                        ppo.Keywords.Add("Height");
                        SolidGhost.AddNudge(ppo.Keywords);
                        PromptPointResult pr = ed.GetPoint(ppo);
                        if (pr.Status == PromptStatus.Keyword)
                        {
                            if (pr.StringResult == SolidGhost.NudgeKeyword)
                            {
                                if (ManagedSection.HasCurrent)
                                { w = ManagedSection.Width; d = ManagedSection.Depth; type = ManagedSection.Type; }
                            }
                            // Height = exact keyboard entry (a number typed at the point prompt
                            // would be direct-distance along the cursor -- this stays exact in
                            // any UCS).
                            else if (pr.StringResult == "Height")
                            {
                                if (GetDouble(ed, "Height above base", sTarget, true, out double hv))
                                    sTarget = hv;
                            }
                            else just = pr.StringResult;
                            Rebuild();
                            continue;
                        }
                        if (pr.Status == PromptStatus.OK)
                        { sTarget = S(pr.Value); Rebuild(); continue; }   // pick reconstructs; Enter applies
                        if (pr.Status == PromptStatus.None) break;
                        return;
                    }
                }
                finally { ManagedSection.Changed -= follow; }
            }

            ObjectId id = ManagedTimber.DrawBox(SpanOrigin(), fa.N, dAxis, wAxis, gap, d, w, type, "", "matchface", "matchface");
            ed.WriteMessage("\nTSpan: " + type + " " + (int)w + "x" + (int)d + "x" + gap.ToString("0.#") +
                            " " + just + " @ height " + sTarget.ToString("0.#") + " (" + id.Handle + ").");
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

            // The first pick doubles as the MODIFY gate (Robert's call: extend existing verbs, don't
            // mint new ones): type M / Modify to re-seat an existing knee brace's legs in place
            // instead of placing a new member.
            int pick = ManagedTimber.PickFaceKeyword(ed, db, "\nPick the FIRST face or [Modify]: ",
                "Modify", out ObjectId idA, out ManagedTimber.TFace fa);
            if (pick < 0) return;
            if (pick == 0) { ModifyBrace(ed, db); return; }
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

                // LIVE SOLID preview (Robert's call 2026-07-15: no transient wireframes): a real db
                // solid that re-solves the moment the palette's Brace spec OR section changes. The
                // palette handler can't write the db (no document lock), so its Changed handler
                // queues SolidGhost's hidden nudge keyword into this prompt and the loop rebuilds
                // in command context. Click/Enter places, Flip swaps a Back/Front side, Escape
                // cancels.
                double footRun = ManagedBrace.HasCurrent ? ManagedBrace.FootRun : 18.0;
                double headRun = ManagedBrace.HasCurrent ? ManagedBrace.HeadRun : 18.0;
                int bplace = ManagedBrace.HasCurrent ? ManagedBrace.Placement : 1;
                double gFoot, gHead, gD, gW; int gPlace;
                using (var ghost = new BraceGhost(fa, fb, d, w, footRun, headRun, bplace, bodyA, bodyB, true))
                {
                    if (!ghost.Solve()) { ed.WriteMessage("\nThose faces don't form a brace corner."); return; }
                    System.Action follow = SolidGhost.Nudge;
                    ManagedBrace.Changed += follow;
                    ManagedSection.Changed += follow;
                    bool place = false;
                    try
                    {
                        while (true)
                        {
                            var ppo = new PromptPointOptions(
                                "\nPlace the brace -- Enter/click places; the palette's Brace spec moves it live")
                            { AllowNone = true };
                            if (ghost.Placement != 1) ppo.Keywords.Add("Flip");
                            SolidGhost.AddNudge(ppo.Keywords);
                            PromptPointResult pr = ed.GetPoint(ppo);
                            if (pr.Status == PromptStatus.Keyword)
                            {
                                if (pr.StringResult == "Flip") ghost.Flip();
                                else ghost.OnPaletteChanged();
                                continue;
                            }
                            place = pr.Status == PromptStatus.OK || pr.Status == PromptStatus.None;
                            break;
                        }
                    }
                    finally { ManagedBrace.Changed -= follow; ManagedSection.Changed -= follow; }
                    if (!place) { ed.WriteMessage("\nBrace cancelled."); return; }
                    gFoot = ghost.FootRun; gHead = ghost.HeadRun; gPlace = ghost.Placement;
                    gD = ghost.Depth; gW = ghost.Width;
                }

                if (ManagedSection.HasCurrent) type = ManagedSection.Type;   // a mid-loop catalog click retypes too
                id = ManagedTimber.DrawMiteredBrace(fa, fb, gD, gW, gFoot, gHead, type, "",
                                                    bodyA, bodyB, gPlace);
                if (id.IsNull) { ed.WriteMessage("\nCouldn't build a brace between those faces."); return; }
                RelabelBracesIfBrace(db, id);   // a new brace joins (or founds) its size+shape group now
                ed.WriteMessage("\nTJoin (knee brace): " + type + " " + (int)gW + "x" + (int)gD +
                                " foot " + gFoot.ToString("0.#") + " head " + gHead.ToString("0.#") +
                                " " + PlaceName(gPlace) + " (" + id.Handle + ").");
            }
        }

        // TJoin's MODIFY branch: re-seat an existing knee brace's LEGS in place. The TJoin anchors
        // aren't stored on the timber, but the brace's mitered ends still lie ON its two host face
        // planes (that's the TryBraceFrame contract) -- so FacesMate re-finds the hosts, the current
        // legs seed the prompts, TryBraceFrame re-solves from the new runs, and the rebuild keeps
        // identity (production number, Free marker, layer). The re-seat makes any joint features
        // STALE, so each joint the brace carries is stripped from BOTH sides -- the recipes/stamps
        // are kept, TJointSync re-cuts. Section changes ride along too: TSection the brace first,
        // then Modify re-seats the new stock to the corner-to-toe rule (which TSection alone can't).
        private static void ModifyBrace(Editor ed, Database db)
        {
            if (!PickTimber(ed, db, "\nPick the knee BRACE to re-seat: ", out ObjectId braceId, out ManagedTimber.TFrame bf)) return;

            // The hosts whose faces the two miters bear on (near end = foot / face A, far = head / B).
            ManagedTimber.TFace[] bfa = ManagedTimber.Faces(bf);
            if (!FindBraceHost(db, braceId, bfa[0], out ManagedTimber.TFrame frA, out ManagedTimber.TFace fa) ||
                !FindBraceHost(db, braceId, bfa[1], out ManagedTimber.TFrame frB, out ManagedTimber.TFace fb))
            {
                ed.WriteMessage("\nCouldn't find the two host faces this brace dies into -- both miters"
                    + " must bear on their hosts (if it was moved off them, erase + TJoin fresh).");
                return;
            }
            Point3d bodyA = frA.O + frA.Z * (frA.L / 2.0);
            Point3d bodyB = frB.O + frB.Z * (frB.L / 2.0);

            // Current legs (corner -> toe) seed the prompts: the toe tips lie on the host planes, so
            // each run is the toe edge's step from the corner anchor along the measuring direction
            // (the toe is the depth side FARTHER out -- hence the max). The anchor's station along
            // the corner doesn't enter this (dirFoot/dirHead are perpendicular to it), so any
            // placement works for the read-back.
            int bplace = ManagedBrace.HasCurrent ? ManagedBrace.Placement : 1;
            if (!ManagedTimber.TryBraceAnchors(fa, fb, bodyA, bodyB, bf.W, bplace,
                                               out Point3d pa, out Vector3d dirFoot,
                                               out Point3d pb, out Vector3d dirHead))
            { ed.WriteMessage("\nThose host faces don't form a brace corner any more."); return; }
            Point3d farC = bf.O + bf.Z * bf.L;
            double curFoot = Math.Max((bf.O + bf.Y * (bf.D / 2.0) - pa).DotProduct(dirFoot),
                                      (bf.O - bf.Y * (bf.D / 2.0) - pa).DotProduct(dirFoot));
            double curHead = Math.Max((farC + bf.Y * (bf.D / 2.0) - pb).DotProduct(dirHead),
                                      (farC - bf.Y * (bf.D / 2.0) - pb).DotProduct(dirHead));

            if (!GetPositive(ed, "Foot (corner to toe)", Math.Round(curFoot, 1), out double footRun)) return;
            if (!GetPositive(ed, "Head (corner to toe)", Math.Round(curHead, 1), out double headRun)) return;

            // Preview + confirm via the same SOLID ghost as the place path (Flip available for
            // Back/Front); the PROMPTED legs stay authoritative here (no live palette tracking)
            // and nothing mutates on an Escape.
            ManagedTimber.TFrame nf;
            using (var ghost = new BraceGhost(fa, fb, bf.D, bf.W, footRun, headRun, bplace, bodyA, bodyB, false))
            {
                if (!ghost.Solve()) { ed.WriteMessage("\nThose legs don't solve a brace in this corner."); return; }
                bool go = false;
                while (true)
                {
                    var ppo = new PromptPointOptions("\nRe-seat the brace -- Enter/click accepts")
                    { AllowNone = true };
                    if (ghost.Placement != 1) ppo.Keywords.Add("Flip");
                    PromptPointResult pr = ed.GetPoint(ppo);
                    if (pr.Status == PromptStatus.Keyword) { ghost.Flip(); continue; }
                    go = pr.Status == PromptStatus.OK || pr.Status == PromptStatus.None;
                    break;
                }
                if (!go) { ed.WriteMessage("\nRe-seat cancelled."); return; }
                nf = ghost.Frame;
                bplace = ghost.Placement;
            }

            // The re-seat obsoletes the brace's joints GEOMETRICALLY: strip each joint id from the
            // partner too (the mate's pocket at the old seat is stale wood). Recipes/stamps stay on
            // both sides, so TJointSync re-cuts at the new contact.
            int strippedJoints = 0;
            foreach (int jid in ManagedTimber.JointIds(bf))
            {
                if (jid == 0) continue;
                foreach ((ObjectId Id, ManagedTimber.TFrame F, string Role) t in ManagedTimber.EnumerateWithRole(db))
                {
                    if (t.Id == braceId || !ManagedTimber.JointIds(t.F).Contains(jid)) continue;
                    ManagedTimber.TFrame pf = t.F;
                    ManagedTimber.StripJoint(ref pf, jid);
                    ManagedTimber.RebuildFromFrame(t.Id, pf);
                    strippedJoints++;
                    break;
                }
            }

            ObjectId nid = ManagedTimber.RebuildFromFrame(braceId, nf);   // fresh frame = plain brace; identity carried
            RelabelBracesIfBrace(db, nid);   // the re-seat moves it between size+shape groups
            ed.WriteMessage("\nTJoin (modify): brace re-seated -- foot " + footRun.ToString("0.#")
                + " head " + headRun.ToString("0.#") + " " + PlaceName(bplace)
                + ", length " + nf.L.ToString("0.#") + " (" + nid.Handle + ")."
                + (strippedJoints > 0
                    ? " " + strippedJoints + " joint(s) stripped both sides -- select the brace and TJointSync to re-cut."
                    : ""));
        }

        private static string PlaceName(int p) => p == 0 ? "Back" : p == 2 ? "Front" : "Center";

        // The host whose SIDE face a brace END bears on (FacesMate, the cutters' tolerance).
        private static bool FindBraceHost(Database db, ObjectId braceId, ManagedTimber.TFace end,
            out ManagedTimber.TFrame host, out ManagedTimber.TFace face)
        {
            host = default; face = default;
            foreach ((ObjectId Id, ManagedTimber.TFrame F, string Role) t in ManagedTimber.EnumerateWithRole(db))
            {
                if (t.Id == braceId) continue;
                foreach (ManagedTimber.TFace s in ManagedTimber.Faces(t.F))
                {
                    if (Math.Abs(s.N.DotProduct(t.F.Z)) >= 0.5) continue;   // side faces only
                    if (!ManagedTimber.FacesMate(end, s, 0.25, out _)) continue;
                    host = t.F; face = s;
                    return true;
                }
            }
            return false;
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
            PinSkeleton(nid, ed);            // a shape edit pins a skeleton member (survives regen)
            RelabelBracesIfBrace(db, nid);   // a trimmed/extended brace changes Overall -> its group
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
            PinSkeleton(nid, ed);            // a shape edit pins a skeleton member (survives regen)
            RelabelBracesIfBrace(db, nid);   // a re-sectioned brace changes its size+shape group

            Module1.DataStructure xd = Module1.GetXdata(nid);
            string type = string.IsNullOrEmpty(xd?.Type) ? "Timber" : xd.Type;
            ed.WriteMessage("\nTSection: " + type + " -> " + (int)newW + "x" + (int)newD +
                            " (" + nid.Handle + "). Re-run TScan to refresh nodes.");
        }
    }
}
