using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;

namespace TimberDraw
{
    // The kind of a single composable joint element. A joint at one contact is a STACK of these.
    public enum ElementKind { Tenon, Housing, Shoulder, Dovetail, Pegs }

    // How a param renders / validates in the UI. Choice = a fixed set of labels (Value = the 0-based index).
    public enum ParamKind { Length, Count, Angle, Toggle, Choice }

    // One editable parameter on an element: a descriptor (Name / Default / Min / Max / Kind [/ Choices]) + its
    // live Value. For ParamKind.Choice, Choices holds the option labels and Value is the selected index.
    public class JointParam
    {
        public readonly string Name;
        public double Value;
        public readonly double Default, Min, Max;
        public readonly ParamKind Kind;
        public readonly string[] Choices;   // ParamKind.Choice only (Value = index); null otherwise

        public JointParam(string name, double dflt, double min, double max, ParamKind kind, string[] choices = null)
        { Name = name; Default = dflt; Value = dflt; Min = min; Max = max; Kind = kind; Choices = choices; }

        public JointParam Clone() => new JointParam(Name, Default, Min, Max, Kind, Choices) { Value = Value };
    }

    // One composable element of a joint (a tenon, a housing, ...). Toggleable, carrying its own params.
    public class JointElement
    {
        public readonly ElementKind Kind;
        public bool Enabled;
        public readonly List<JointParam> Params;

        public JointElement(ElementKind kind, bool enabled, params JointParam[] ps)
        { Kind = kind; Enabled = enabled; Params = new List<JointParam>(ps); }

        public JointParam P(string name) => Params.Find(p => p.Name == name);

        public JointElement Clone()
        {
            var e = new JointElement(Kind, Enabled);
            foreach (JointParam p in Params) e.Params.Add(p.Clone());
            return e;
        }
    }

    // The outcome of applying a connection type to a timber pair. AId/BId are the REBUILT solids (RebuildFromFrame
    // replaces the originals), so the caller can refresh its held ids.
    public struct ApplyResult { public bool Ok; public string Diag; public ObjectId AId; public ObjectId BId; public int Jid; }

    // The cut a preset performs on a picked timber pair (a = the first/male timber, b = the host). It maps the
    // element stack back to the existing sticky spec and calls the existing cutter UNCHANGED.
    public delegate ApplyResult JointApply(Database db, ObjectId aId, ManagedTimber.TFrame a,
        ObjectId bId, ManagedTimber.TFrame b, ConnectionType ct);

    // Single-source ELEMENT factories: each joint element is DEFINED ONCE here, so the same kind renders
    // identically across every preset (no drift). Presets compose these. Where an engine genuinely supports more
    // (the box-tenon partial housing), that's an EXPLICIT factory variant, not accidental difference. Canonical
    // param names / ranges / ParamKind live here, matching GLOSSARY.md.
    internal static class ElementKit
    {
        // The standard 5-field tenon tongue (Thickness / Length / ShoulderTop / ShoulderBottom / Offset).
        public static JointElement Tenon(bool enabled, double thickness, double length, double shTop, double shBot, double offset)
            => new JointElement(ElementKind.Tenon, enabled,
                new JointParam("Thickness", thickness, 0.0, 1000.0, ParamKind.Length),
                new JointParam("Length", length, 0.0, 1000.0, ParamKind.Length),
                new JointParam("ShoulderTop", shTop, 0.0, 1000.0, ParamKind.Length),
                new JointParam("ShoulderBottom", shBot, 0.0, 1000.0, ParamKind.Length),
                new JointParam("Offset", offset, -1000.0, 1000.0, ParamKind.Length));

        // The canonical FULL peg layout -- the SAME set on every tenon (Count / Diameter / Setback / Spacing /
        // Bore[Full|Blind] / BlindDepth / BlindFlip), seeded from a PegSpec.
        public static JointElement Pegs(bool enabled, ManagedTimber.PegSpec d)
            => new JointElement(ElementKind.Pegs, enabled,
                new JointParam("Count", d.Count, 0.0, 12.0, ParamKind.Count),
                new JointParam("Diameter", d.Diameter, 0.0, 100.0, ParamKind.Length),
                new JointParam("Setback", d.Setback, 0.0, 1000.0, ParamKind.Length),
                new JointParam("Spacing", d.Spacing, 0.0, 1000.0, ParamKind.Length),
                new JointParam("Bore", (int)d.Bore, 0.0, 1.0, ParamKind.Choice, new[] { "Full", "Blind" }),
                new JointParam("BlindDepth", d.BlindDepth, 0.0, 1000.0, ParamKind.Length),
                new JointParam("BlindFlip", d.BlindFlip ? 1.0 : 0.0, 0.0, 1.0, ParamKind.Toggle));

        // Full-section housing -- just the let-in Seat depth (strut / rafter foot / common ridge).
        public static JointElement HousingSeat(bool enabled, double seat)
            => new JointElement(ElementKind.Housing, enabled,
                new JointParam("Seat", seat, 0.0, 1000.0, ParamKind.Length));

        // Box-tenon partial-footprint housing -- Seat + the tenon-like footprint (Thickness / Offset / shoulders).
        public static JointElement HousingFull(bool enabled, ManagedTimber.HousingSpec d)
            => new JointElement(ElementKind.Housing, enabled,
                new JointParam("Seat", d.Seat, 0.0, 1000.0, ParamKind.Length),
                new JointParam("ShoulderTop", d.ShoulderTop, 0.0, 1000.0, ParamKind.Length),
                new JointParam("ShoulderBottom", d.ShoulderBottom, 0.0, 1000.0, ParamKind.Length),
                new JointParam("ShoulderSide1", d.ShoulderSide1, 0.0, 1000.0, ParamKind.Length),
                new JointParam("ShoulderSide2", d.ShoulderSide2, 0.0, 1000.0, ParamKind.Length));

        // Ridge drop-in housing -- Seat + a bottom bearing shoulder.
        public static JointElement HousingRidge(bool enabled, double seat, double shoulderBottom)
            => new JointElement(ElementKind.Housing, enabled,
                new JointParam("Seat", seat, 0.0, 1000.0, ParamKind.Length),
                new JointParam("ShoulderBottom", shoulderBottom, 0.0, 1000.0, ParamKind.Length));

        // Shoulder bearing notch -- just Seat (rafter head).
        public static JointElement Shoulder(bool enabled, double seat)
            => new JointElement(ElementKind.Shoulder, enabled,
                new JointParam("Seat", seat, 0.0, 1000.0, ParamKind.Length));

        // Birdsmouth shoulder -- Seat (vertical let-in below the girt top) + Heel (horizontal let-in).
        public static JointElement Birdsmouth(bool enabled, double seat, double heel)
            => new JointElement(ElementKind.Shoulder, enabled,
                new JointParam("Seat", seat, 0.0, 1000.0, ParamKind.Length),
                new JointParam("Heel", heel, 0.0, 1000.0, ParamKind.Length));

        // Dovetail housing -- Seat / Length / Width / Depth / Angle.
        public static JointElement Dovetail(bool enabled, ManagedTimber.PurlinRafterSpec d)
            => new JointElement(ElementKind.Dovetail, enabled,
                new JointParam("Seat", d.Seat, 0.0, 1000.0, ParamKind.Length),
                new JointParam("Length", d.Length, 0.0, 1000.0, ParamKind.Length),
                new JointParam("Width", d.Width, 0.0, 1000.0, ParamKind.Length),
                new JointParam("Depth", d.Depth, 0.0, 1000.0, ParamKind.Length),
                new JointParam("Angle", d.Angle, 0.0, 89.0, ParamKind.Angle));
    }

    // A FACADE over one existing cutter: a NAMED stack of composable elements + a timber-pair Apply. The UI renders
    // the stack (checkbox per element + param fields) and re-cuts the real joint through Apply as the user edits
    // (the joint id makes each re-cut REPLACE in place). NO geometry lives here -- it maps the element stack to the
    // existing sticky spec (one Spec* helper per preset) and dispatches to the ManagedCommands.Apply* helpers, which
    // reuse the verified ManagedTimber cutters verbatim. Built-in presets come from the static factories.
    public class ConnectionType
    {
        public readonly string Name;
        public readonly List<JointElement> Elements;
        private readonly JointApply _apply;

        public ConnectionType(string name, List<JointElement> elements, JointApply apply)
        { Name = name; Elements = elements; _apply = apply; }

        public JointElement E(ElementKind k) => Elements.Find(e => e.Kind == k);

        public ApplyResult Apply(Database db, ObjectId aId, ManagedTimber.TFrame a, ObjectId bId, ManagedTimber.TFrame b)
            => _apply(db, aId, a, bId, b, this);

        public ConnectionType Clone()
        {
            var es = new List<JointElement>();
            foreach (JointElement e in Elements) es.Add(e.Clone());
            return new ConnectionType(Name, es, _apply);
        }

        // ---- persisted state: an OPAQUE string for the timber's JointSpecs xrecord, so the pane can repopulate a
        //      joint's settings on re-pick. Format "<Name>|<e0>,<v00>,<v01>,...;<e1>,..." (per element: Enabled 0/1
        //      then its param Values in order). The Name re-establishes the structure; the Spec* mappers do the rest.

        public string SerializeState()
        {
            var sb = new System.Text.StringBuilder(Name);
            sb.Append('|');
            for (int e = 0; e < Elements.Count; e++)
            {
                if (e > 0) sb.Append(';');
                JointElement el = Elements[e];
                sb.Append(el.Enabled ? '1' : '0');
                foreach (JointParam p in el.Params)
                    sb.Append(',').Append(p.Value.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }

        // Apply a serialized state onto THIS preset (set each element's Enabled + param Values). False (untouched)
        // if the name doesn't match or the shape is wrong.
        public bool LoadState(string state)
        {
            if (string.IsNullOrEmpty(state)) return false;
            int bar = state.IndexOf('|');
            if (bar < 0 || state.Substring(0, bar) != Name) return false;
            string[] elems = state.Substring(bar + 1).Split(';');
            if (elems.Length != Elements.Count) return false;
            for (int e = 0; e < Elements.Count; e++)
            {
                string[] f = elems[e].Split(',');
                JointElement el = Elements[e];
                if (f.Length != el.Params.Count + 1) return false;
                el.Enabled = f[0].Trim() == "1";
                for (int i = 0; i < el.Params.Count; i++)
                    if (double.TryParse(f[i + 1], System.Globalization.NumberStyles.Any,
                            System.Globalization.CultureInfo.InvariantCulture, out double v))
                        el.Params[i].Value = v;
            }
            return true;
        }

        // Clone the named preset and load the state into it. Null if no preset matches / the parse fails.
        public static ConnectionType FromState(IEnumerable<ConnectionType> presets, string state)
        {
            if (string.IsNullOrEmpty(state)) return null;
            int bar = state.IndexOf('|');
            string name = bar < 0 ? state : state.Substring(0, bar);
            foreach (ConnectionType ct in presets)
                if (ct.Name == name)
                {
                    ConnectionType clone = ct.Clone();
                    return clone.LoadState(state) ? clone : null;
                }
            return null;
        }

        // Persist this connection type's CURRENT values as the user default for its joint type (the Joints
        // pane's "Set as default"): map the element stack back to the spec via the preset's own Spec* mapper
        // and store it (JointDefaults.Save also re-seeds the matching console sticky). False = unknown name.
        public static bool SaveAsDefault(ConnectionType ct)
        {
            switch (ct?.Name)
            {
                case JointDefaults.KeyBox:         JointDefaults.Save(JointDefaults.KeyBox,         SpecBoxTenon(ct));     return true;
                case JointDefaults.KeyStrut:       JointDefaults.Save(JointDefaults.KeyStrut,       SpecStrut(ct));        return true;
                case JointDefaults.KeyBrace:       JointDefaults.Save(JointDefaults.KeyBrace,       SpecBrace(ct));        return true;
                case JointDefaults.KeyRafterFoot:  JointDefaults.Save(JointDefaults.KeyRafterFoot,  SpecRafterFoot(ct));   return true;
                case JointDefaults.KeyRidge:       JointDefaults.Save(JointDefaults.KeyRidge,       SpecRidgeHousing(ct)); return true;
                case JointDefaults.KeyRafterHead:  JointDefaults.Save(JointDefaults.KeyRafterHead,  SpecRafterHead(ct));   return true;
                case JointDefaults.KeyCommonRidge: JointDefaults.Save(JointDefaults.KeyCommonRidge, SpecCommonRidge(ct));  return true;
                case JointDefaults.KeyBirdsmouth:  JointDefaults.Save(JointDefaults.KeyBirdsmouth,  SpecBirdsmouth(ct));   return true;
                case JointDefaults.KeyPurlin:      JointDefaults.Save(JointDefaults.KeyPurlin,      SpecPurlin(ct));       return true;
                case JointDefaults.KeyQPRafter:    JointDefaults.Save(JointDefaults.KeyQPRafter,    SpecQPRafter(ct));     return true;
                case JointDefaults.KeyTusk:        JointDefaults.Save(JointDefaults.KeyTusk,        SpecTusk(ct));         return true;
                default: return false;
            }
        }

        // The built-in presets, in display order.
        public static List<ConnectionType> BuiltIns() => new List<ConnectionType>
            { BoxTenon(), TuskTenon(), StrutTenon(), BraceTenon(), RafterFoot(), RidgeHousing(), RafterHead(), CommonRidge(), Birdsmouth(), HousedDovetail(), QPRafterApex() };

        // ---- Built-in presets (factory + Spec* mapping + Apply delegate) ----

        // Strut / V-strut / brace END -> any host FACE: a Tenon + an optional Housing + optional Pegs (the pegs
        // bore the host cheeks across the tongue), each independently toggleable. Maps to StrutTenonSpec, cut by the
        // existing StrutTenonJoint. a = the strut (male), b = the host.
        public static ConnectionType StrutTenon() => StrutTenon(JointDefaults.Strut);
        public static ConnectionType StrutTenon(ManagedTimber.StrutTenonSpec d)
            => new ConnectionType("Strut tenon", new List<JointElement>
            {
                ElementKit.Tenon(d.Tenon, d.Thickness, d.Length, d.ShoulderTop, d.ShoulderBottom, d.Offset),
                ElementKit.HousingFull(d.Hsg.On, d.Hsg),
                ElementKit.Pegs(d.Peg.Count > 0, d.Peg)
            }, ApplyStrutTenon);

        private static ManagedTimber.StrutTenonSpec SpecStrut(ConnectionType ct) => SpecStrutLike(ct, JointDefaults.Strut);
        private static ManagedTimber.StrutTenonSpec SpecBrace(ConnectionType ct) => SpecStrutLike(ct, JointDefaults.Brace);

        // The shared StrutTenonSpec mapping (strut / brace / QP apex are the same element stack over the same
        // spec, differing only in which stored default seeds the unsurfaced fields).
        private static ManagedTimber.StrutTenonSpec SpecStrutLike(ConnectionType ct, ManagedTimber.StrutTenonSpec spec)
        {
            JointElement t = ct.E(ElementKind.Tenon);
            spec.On = true;                  // master gate; the tenon/housing/pegs elements toggle independently
            spec.Tenon = t.Enabled;
            spec.Thickness = t.P("Thickness").Value;
            spec.Length = t.P("Length").Value;
            spec.ShoulderTop = t.P("ShoulderTop").Value;
            spec.ShoulderBottom = t.P("ShoulderBottom").Value;
            spec.Offset = t.P("Offset").Value;
            SpecHousing(ct, ref spec.Hsg);
            SpecPegs(ct, ref spec.Peg);
            return spec;
        }

        // Map the canonical Housing element (the box-style partial footprint) onto a HousingSpec. Shared by the box,
        // strut, and QP apex mappers (one housing mapping everywhere). Optional footprint params are read only when
        // present, so a Seat-only Housing element still maps cleanly. Element disabled = housing off.
        private static void SpecHousing(ConnectionType ct, ref ManagedTimber.HousingSpec hsg)
        {
            JointElement h = ct.E(ElementKind.Housing);
            if (h == null) { hsg.On = false; return; }
            hsg.On = h.Enabled;
            hsg.Seat = h.P("Seat").Value;
            JointParam st = h.P("ShoulderTop");     if (st != null) hsg.ShoulderTop = st.Value;
            JointParam sb = h.P("ShoulderBottom");  if (sb != null) hsg.ShoulderBottom = sb.Value;
            JointParam s1 = h.P("ShoulderSide1");   if (s1 != null) hsg.ShoulderSide1 = s1.Value;
            JointParam s2 = h.P("ShoulderSide2");   if (s2 != null) hsg.ShoulderSide2 = s2.Value;
        }

        // Map the canonical Pegs element onto a PegSpec. Shared by the box, strut, QP apex, and rafter-foot mappers
        // (one peg mapping everywhere). Count 0 (or the element disabled) = no pegs.
        private static void SpecPegs(ConnectionType ct, ref ManagedTimber.PegSpec peg)
        {
            JointElement p = ct.E(ElementKind.Pegs);
            if (p == null) { peg.Count = 0; return; }
            peg.Count = p.Enabled ? System.Math.Max(0, (int)System.Math.Round(p.P("Count").Value)) : 0;
            peg.Diameter = p.P("Diameter").Value;
            peg.Setback = p.P("Setback").Value;
            peg.Spacing = p.P("Spacing").Value;
            peg.Bore = p.P("Bore").Value >= 0.5 ? ManagedTimber.PegBore.Blind : ManagedTimber.PegBore.Full;
            peg.BlindDepth = p.P("BlindDepth").Value;
            peg.BlindFlip = p.P("BlindFlip").Value >= 0.5;
        }

        private static ApplyResult ApplyStrutTenon(Database db, ObjectId aId, ManagedTimber.TFrame a,
            ObjectId bId, ManagedTimber.TFrame b, ConnectionType ct)
        {
            ManagedTimber.TFrame strut = a, host = b;
            bool ok = ManagedCommands.ApplyStrutTenonJoint(db, aId, ref strut, bId, ref host, SpecStrut(ct),
                out ObjectId ns, out ObjectId nh, out int jid, out string diag);
            return new ApplyResult { Ok = ok, Diag = diag, AId = ns, BId = nh, Jid = jid };
        }

        // BRACE end -> host face: the SAME end->side tenon engine as the strut, as its own named type so
        // braces carry their own user default (factory: thinner 1.5" stock). NOTE: TBrace's BAREFACED /
        // FLIP conventions are CUT-TIME computations from the actual brace width ((W - Thickness)/2), so
        // they are not part of the stored default; the pane exposes Offset for manual barefacing.
        // a = the brace (male), b = the host.
        public static ConnectionType BraceTenon() => BraceTenon(JointDefaults.Brace);
        public static ConnectionType BraceTenon(ManagedTimber.StrutTenonSpec d)
            => new ConnectionType("Brace tenon", new List<JointElement>
            {
                ElementKit.Tenon(d.Tenon, d.Thickness, d.Length, d.ShoulderTop, d.ShoulderBottom, d.Offset),
                ElementKit.HousingFull(d.Hsg.On, d.Hsg),
                ElementKit.Pegs(d.Peg.Count > 0, d.Peg)
            }, ApplyBraceTenon);

        private static ApplyResult ApplyBraceTenon(Database db, ObjectId aId, ManagedTimber.TFrame a,
            ObjectId bId, ManagedTimber.TFrame b, ConnectionType ct)
        {
            ManagedTimber.TFrame brace = a, host = b;
            bool ok = ManagedCommands.ApplyStrutTenonJoint(db, aId, ref brace, bId, ref host, SpecBrace(ct),
                out ObjectId nb, out ObjectId nh, out int jid, out string diag);
            return new ApplyResult { Ok = ok, Diag = diag, AId = nb, BId = nh, Jid = jid };
        }

        // Rafter FOOT -> post SIDE: a Tenon + a sloped Housing (the let-in seat) + Pegs. Maps to RafterFootSpec,
        // cut by the existing RafterFootJoint (which finds the post-side contact). a = the rafter, b = the post.
        // Uniform Tenon, Housing, Pegs order like the other tenon presets.
        public static ConnectionType RafterFoot() => RafterFoot(JointDefaults.RafterFoot);
        public static ConnectionType RafterFoot(ManagedTimber.RafterFootSpec d)
            => new ConnectionType("Rafter foot", new List<JointElement>
            {
                ElementKit.Tenon(d.Tenon, d.Thickness, d.Length, d.ShoulderTop, d.ShoulderBottom, d.Offset),
                ElementKit.HousingSeat(d.On, d.Seat),
                ElementKit.Pegs(d.Peg.Count > 0, d.Peg)
            }, ApplyRafterFootCut);

        private static ManagedTimber.RafterFootSpec SpecRafterFoot(ConnectionType ct)
        {
            JointElement h = ct.E(ElementKind.Housing);
            JointElement t = ct.E(ElementKind.Tenon);
            ManagedTimber.RafterFootSpec spec = JointDefaults.RafterFoot;   // stored default seeds unsurfaced fields
            spec.On = h.Enabled;
            spec.Seat = h.P("Seat").Value;
            spec.Tenon = t.Enabled;
            spec.Thickness = t.P("Thickness").Value;
            spec.Length = t.P("Length").Value;
            spec.ShoulderTop = t.P("ShoulderTop").Value;
            spec.ShoulderBottom = t.P("ShoulderBottom").Value;
            spec.Offset = t.P("Offset").Value;
            SpecPegs(ct, ref spec.Peg);
            return spec;
        }

        private static ApplyResult ApplyRafterFootCut(Database db, ObjectId aId, ManagedTimber.TFrame a,
            ObjectId bId, ManagedTimber.TFrame b, ConnectionType ct)
        {
            ManagedTimber.TFrame rafter = a, post = b;
            bool ok = ManagedCommands.ApplyRafterFootJoint(db, aId, ref rafter, bId, ref post, SpecRafterFoot(ct),
                out ObjectId nr, out ObjectId np, out int jid, out string diag);
            return new ApplyResult { Ok = ok, Diag = diag, AId = nr, BId = np, Jid = jid };
        }

        // Ridge -> king post / principal rafter: one chamfered drop-in Housing (pocket + tongue). Maps to
        // RidgeHousingSpec, cut by RidgeKpostJoint -- HOST-NEUTRAL, so b can be a king post or a rafter.
        public static ConnectionType RidgeHousing() => RidgeHousing(JointDefaults.Ridge);
        public static ConnectionType RidgeHousing(ManagedTimber.RidgeHousingSpec d)
            => new ConnectionType("Ridge housing",
                new List<JointElement> { ElementKit.HousingRidge(d.On, d.Seat, d.ShoulderBottom) }, ApplyRidgeHousingCut);

        private static ManagedTimber.RidgeHousingSpec SpecRidgeHousing(ConnectionType ct)
        {
            JointElement h = ct.E(ElementKind.Housing);
            ManagedTimber.RidgeHousingSpec spec = JointDefaults.Ridge;   // stored default seeds unsurfaced fields
            spec.On = h.Enabled;
            spec.Seat = h.P("Seat").Value;
            spec.ShoulderBottom = h.P("ShoulderBottom").Value;
            return spec;
        }

        private static ApplyResult ApplyRidgeHousingCut(Database db, ObjectId aId, ManagedTimber.TFrame a,
            ObjectId bId, ManagedTimber.TFrame b, ConnectionType ct)
        {
            ManagedTimber.TFrame ridge = a, host = b;
            bool ok = ManagedCommands.ApplyRidgeHousingJoint(db, aId, ref ridge, bId, ref host, SpecRidgeHousing(ct),
                out ObjectId nr, out ObjectId nh, out int jid, out string diag);
            return new ApplyResult { Ok = ok, Diag = diag, AId = nr, BId = nh, Jid = jid };
        }

        // Rafter HEAD -> king-post side: a Shoulder notch (Seat). a = the rafter, b = the king post.
        public static ConnectionType RafterHead() => RafterHead(JointDefaults.RafterHead);
        public static ConnectionType RafterHead(ManagedTimber.RafterHeadSpec d)
            => new ConnectionType("Rafter head",
                new List<JointElement> { ElementKit.Shoulder(d.On, d.Seat) }, ApplyRafterHeadCut);

        private static ManagedTimber.RafterHeadSpec SpecRafterHead(ConnectionType ct)
        {
            JointElement s = ct.E(ElementKind.Shoulder);
            ManagedTimber.RafterHeadSpec spec = JointDefaults.RafterHead;   // stored default seeds unsurfaced fields
            spec.On = s.Enabled;
            spec.Seat = s.P("Seat").Value;
            return spec;
        }

        private static ApplyResult ApplyRafterHeadCut(Database db, ObjectId aId, ManagedTimber.TFrame a,
            ObjectId bId, ManagedTimber.TFrame b, ConnectionType ct)
        {
            ManagedTimber.TFrame rafter = a, kingpost = b;
            bool ok = ManagedCommands.ApplyRafterHeadJoint(db, aId, ref rafter, bId, ref kingpost, SpecRafterHead(ct),
                out ObjectId nr, out ObjectId np, out int jid, out string diag);
            return new ApplyResult { Ok = ok, Diag = diag, AId = nr, BId = np, Jid = jid };
        }

        // Common-rafter head -> ridge side: a let-in Housing (Seat). a = the common, b = the ridge.
        public static ConnectionType CommonRidge() => CommonRidge(JointDefaults.CommonRidge);
        public static ConnectionType CommonRidge(ManagedTimber.CommonRidgeSpec d)
            => new ConnectionType("Common -> ridge",
                new List<JointElement> { ElementKit.HousingSeat(d.On, d.Seat) }, ApplyCommonRidgeCut);

        private static ManagedTimber.CommonRidgeSpec SpecCommonRidge(ConnectionType ct)
        {
            JointElement h = ct.E(ElementKind.Housing);
            ManagedTimber.CommonRidgeSpec spec = JointDefaults.CommonRidge;   // stored default seeds unsurfaced fields
            spec.On = h.Enabled;
            spec.Seat = h.P("Seat").Value;
            return spec;
        }

        private static ApplyResult ApplyCommonRidgeCut(Database db, ObjectId aId, ManagedTimber.TFrame a,
            ObjectId bId, ManagedTimber.TFrame b, ConnectionType ct)
        {
            ManagedTimber.TFrame common = a, ridge = b;
            bool ok = ManagedCommands.ApplyCommonRidgeJoint(db, aId, ref common, bId, ref ridge, SpecCommonRidge(ct),
                out ObjectId nc, out ObjectId nr, out int jid, out string diag);
            return new ApplyResult { Ok = ok, Diag = diag, AId = nc, BId = nr, Jid = jid };
        }

        // Common-rafter -> eave-girt birdsmouth: a Shoulder (Seat let-in below the girt top + Heel let-in inside
        // the heel face). Both timbers are cut. a = the common, b = the eave girt.
        public static ConnectionType Birdsmouth() => Birdsmouth(JointDefaults.CommonEave);
        public static ConnectionType Birdsmouth(ManagedTimber.CommonEaveSpec d)
            => new ConnectionType("Birdsmouth",
                new List<JointElement> { ElementKit.Birdsmouth(d.On, d.Seat, d.Heel) }, ApplyBirdsmouthCut);

        private static ManagedTimber.CommonEaveSpec SpecBirdsmouth(ConnectionType ct)
        {
            JointElement s = ct.E(ElementKind.Shoulder);
            ManagedTimber.CommonEaveSpec spec = JointDefaults.CommonEave;   // stored default seeds unsurfaced fields
            spec.On = s.Enabled;
            spec.Seat = s.P("Seat").Value;
            spec.Heel = s.P("Heel").Value;
            return spec;
        }

        private static ApplyResult ApplyBirdsmouthCut(Database db, ObjectId aId, ManagedTimber.TFrame a,
            ObjectId bId, ManagedTimber.TFrame b, ConnectionType ct)
        {
            ManagedTimber.TFrame common = a, girt = b;
            bool ok = ManagedCommands.ApplyCommonEaveJoint(db, aId, ref common, bId, ref girt, SpecBirdsmouth(ct),
                out ObjectId nc, out ObjectId ng, out int jid, out string diag);
            return new ApplyResult { Ok = ok, Diag = diag, AId = nc, BId = ng, Jid = jid };
        }

        // Housed dovetail, member end -> carrier side: a Dovetail (housing Seat + tongue Length /
        // Width / band Depth / taper Angle). Host-neutral -- purlin -> rafter and joist -> carrier are
        // the same cut. The cutter does housing + dovetail as one unit (PurlinRafterSpec has a single
        // On). a = the dropped-in member, b = the carrier.
        public static ConnectionType HousedDovetail() => HousedDovetail(JointDefaults.Purlin);
        public static ConnectionType HousedDovetail(ManagedTimber.PurlinRafterSpec d)
            => new ConnectionType("Housed dovetail",
                new List<JointElement> { ElementKit.Dovetail(d.On, d) }, ApplyPurlinCut);

        private static ManagedTimber.PurlinRafterSpec SpecPurlin(ConnectionType ct)
        {
            JointElement e = ct.E(ElementKind.Dovetail);
            ManagedTimber.PurlinRafterSpec spec = JointDefaults.Purlin;   // stored default seeds unsurfaced fields
            spec.On = e.Enabled;
            spec.Seat = e.P("Seat").Value;
            spec.Length = e.P("Length").Value;
            spec.Width = e.P("Width").Value;
            spec.Depth = e.P("Depth").Value;
            spec.Angle = e.P("Angle").Value;
            return spec;
        }

        private static ApplyResult ApplyPurlinCut(Database db, ObjectId aId, ManagedTimber.TFrame a,
            ObjectId bId, ManagedTimber.TFrame b, ConnectionType ct)
        {
            ManagedTimber.TFrame purlin = a, rafter = b;
            bool ok = ManagedCommands.ApplyPurlinJoint(db, aId, ref purlin, bId, ref rafter, SpecPurlin(ct),
                out ObjectId npu, out ObjectId nr, out int jid, out string diag);
            return new ApplyResult { Ok = ok, Diag = diag, AId = npu, BId = nr, Jid = jid };
        }

        // Girt END -> post SIDE: the full kit-of-parts -- Tenon + Housing + Pegs, each independently toggleable.
        // Maps to JointSpec (the Shoulder sub-spec stays at its default-off; not surfaced in v1), cut by the
        // existing GirtPostJoint. a = the girt, b = the post. Pegs carry an int Count, an enum Bore (0 = Full,
        // 1 = Blind) and a bool BlindFlip (0/1) -- mapped in SpecBoxTenon.
        public static ConnectionType BoxTenon() => BoxTenon(JointDefaults.Joint);
        public static ConnectionType BoxTenon(ManagedTimber.JointSpec d) => BoxKit("Box tenon", d);

        // TUSK TENON (floor systems phase 4): the classic summer -> girt joint -- a soffit-bearing
        // HOUSING (bottom band; its top shoulder insets everything above the bearing) + a deep tenon
        // riding above it + a peg. Same element stack, spec and engine as the Box tenon; only the
        // name (its own saved default slot) and the factory proportions differ.
        public static ConnectionType TuskTenon() => TuskTenon(JointDefaults.Tusk);
        public static ConnectionType TuskTenon(ManagedTimber.JointSpec d) => BoxKit(JointDefaults.KeyTusk, d);

        // The shared girt->post kit factory (Box tenon / Tusk tenon: same stack over JointSpec).
        private static ConnectionType BoxKit(string name, ManagedTimber.JointSpec d)
            => new ConnectionType(name, new List<JointElement>
            {
                ElementKit.Tenon(d.Tenon.On, d.Tenon.Thickness, d.Tenon.Length, d.Tenon.ShoulderTop, d.Tenon.ShoulderBottom, d.Tenon.Offset),
                ElementKit.HousingFull(d.Housing.On, d.Housing),
                ElementKit.Pegs(d.Peg.Count > 0, d.Peg)
            }, ApplyBoxTenonCut);

        private static ManagedTimber.JointSpec SpecBoxTenon(ConnectionType ct)
            => SpecBoxLike(ct, JointDefaults.Joint);
        private static ManagedTimber.JointSpec SpecTusk(ConnectionType ct)
            => SpecBoxLike(ct, JointDefaults.Tusk);

        // The shared JointSpec mapping (the stored default for the preset's own slot seeds the
        // unsurfaced fields, e.g. Shoulder).
        private static ManagedTimber.JointSpec SpecBoxLike(ConnectionType ct, ManagedTimber.JointSpec spec)
        {
            JointElement t = ct.E(ElementKind.Tenon);
            spec.Tenon.On = t.Enabled;
            spec.Tenon.Thickness = t.P("Thickness").Value;
            spec.Tenon.Length = t.P("Length").Value;
            spec.Tenon.ShoulderTop = t.P("ShoulderTop").Value;
            spec.Tenon.ShoulderBottom = t.P("ShoulderBottom").Value;
            spec.Tenon.Offset = t.P("Offset").Value;
            SpecHousing(ct, ref spec.Housing);
            SpecPegs(ct, ref spec.Peg);
            return spec;
        }

        private static ApplyResult ApplyBoxTenonCut(Database db, ObjectId aId, ManagedTimber.TFrame a,
            ObjectId bId, ManagedTimber.TFrame b, ConnectionType ct)
        {
            ManagedTimber.TFrame girt = a, post = b;
            ManagedTimber.JointSpec spec = ct.Name == JointDefaults.KeyTusk ? SpecTusk(ct) : SpecBoxTenon(ct);
            bool ok = ManagedCommands.ApplyBoxTenonJoint(db, aId, ref girt, bId, ref post, spec,
                out ObjectId ng, out ObjectId np, out int jid, out string diag);
            return new ApplyResult { Ok = ok, Diag = diag, AId = ng, BId = np, Jid = jid };
        }

        // QP rafter APEX: two principal rafters meeting at the peak (no king post) = a STRUT TENON + HOUSING cut at
        // the apex bearing (the male rafter's beveled peak end-cap). a = the male rafter (its peak seats in),
        // b = the host rafter. Same engine + spec as the strut tenon. The housing-on / short-tongue apex recipe
        // lives in StrutTenonSpec.QPRafterDefault (the factory value behind JointDefaults.QPRafter), so a user
        // default fully replaces it and Reset restores it. Via ApplyQPRafterJoint.
        public static ConnectionType QPRafterApex() => QPRafterApex(JointDefaults.QPRafter);

        // Faithful build from a live spec (for stamping a TQPRafter cut and the parameterless form above).
        public static ConnectionType QPRafterApex(ManagedTimber.StrutTenonSpec d)
            => new ConnectionType("QP rafter apex", new List<JointElement>
            {
                ElementKit.Tenon(d.Tenon, d.Thickness, d.Length, d.ShoulderTop, d.ShoulderBottom, d.Offset),
                ElementKit.HousingFull(d.Hsg.On, d.Hsg),
                ElementKit.Pegs(d.Peg.Count > 0, d.Peg)
            }, ApplyQPRafterApex);

        private static ManagedTimber.StrutTenonSpec SpecQPRafter(ConnectionType ct) => SpecStrutLike(ct, JointDefaults.QPRafter);

        private static ApplyResult ApplyQPRafterApex(Database db, ObjectId aId, ManagedTimber.TFrame a,
            ObjectId bId, ManagedTimber.TFrame b, ConnectionType ct)
        {
            ManagedTimber.TFrame male = a, host = b;
            bool ok = ManagedCommands.ApplyQPRafterJoint(db, aId, ref male, bId, ref host, SpecQPRafter(ct),
                out ObjectId nm, out ObjectId nh, out int jid, out string diag);
            return new ApplyResult { Ok = ok, Diag = diag, AId = nm, BId = nh, Jid = jid };
        }
    }
}
