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
    // ManagedCommands part: the strut-tenon engine family -- TStrut, TBrace, TQPRafter
    // (+ Apply/Compute halves, deletes, review loops).
    public partial class ManagedCommands
    {
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
    }
}
