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
    }
}
