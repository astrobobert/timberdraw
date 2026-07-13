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
    // ManagedCommands part: the SHARED SPINE -- session-sticky joint specs + reseed, the
    // joint stamp/apply/id helpers every cutter uses, the shared review loops, and the
    // pick/prompt utilities. (Verbatim moves from the original single file; see CLAUDE.md.)
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
