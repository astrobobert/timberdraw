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
    // ManagedCommands part: TJoist -- the floor-joist row placer (plain sticks; joinery is
    // deliberate via TJointAll's joist pass) + its sticky dovetail spec and review loop.
    public partial class ManagedCommands
    {
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
    }
}
