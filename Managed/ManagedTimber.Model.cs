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
    // ManagedTimber part: the MODEL types -- TFrame/TFace and the per-joint spec structs.
    // (Split from the original single file; members are verbatim moves. See CLAUDE.md.)
    public static partial class ManagedTimber
    {
        // A managed timber's placement frame. Convention: the W x D cross-section lies in the local XY
        // plane (WIDTH along X, DEPTH along Y) and the timber is extruded along Z = LENGTH. Reference
        // line = O -> O + Z*L; O is the NEAR end-face centre, O+Z*L the FAR end-face centre. Local
        // extents: X in [-W/2,+W/2], Y in [-D/2,+D/2], Z in [0,L]. NearN/FarN are the OUTWARD normals of
        // the two Z-ends: -Z / +Z for a square-ended box, tilted for a mitered brace (each end lies in
        // its mate's face plane, pointing back toward the mate). The four side faces stay axis-planar.
        public struct TFrame
        {
            public Point3d O; public Vector3d X, Y, Z; public double L, D, W;
            public Vector3d NearN, FarN;
            // Extra CONVEX slice planes (WCS) applied on top of the two end cuts -- the king post
            // gable's second plane, ridge/eave-girt chamfers, etc. Null/empty for a plain box. The
            // analytic Faces() ignore these (they're detail on the nominal box); BuildFramedSolid
            // slices by them so the solid carries the exact convex shape.
            public List<(Point3d P, Vector3d N)> Cuts;
            // CONCAVE/notch features subtracted from the stock (the common-rafter birdsmouth, later
            // the brace arch). Each is a polygon in the timber's LOCAL elevation plane -- a Point3d
            // per vertex with X = length coord (along Z), Y = depth coord (along Y) -- extruded across
            // the full width (X) and boolean-subtracted. LOCAL => transform-invariant. Faces() ignore
            // these too (detail on the nominal box).
            public List<Point3d[]> Subtracts;
            // JOINERY features: LOCALIZED axis-aligned boxes in LOCAL coords (Min/Max over X=width,
            // Y=depth, Z=length). Subtract=true carves a pocket (a mortise); Subtract=false unions a
            // stub (a tenon projecting past the end). LOCAL => transform-invariant; Faces() ignore them
            // (detail on the nominal box, so coincidence/TScan still see the clean bearing faces).
            public List<(Point3d Min, Point3d Max, bool Subtract, int Joint)> Features;
            // PEG bores: subtract-only CYLINDERS (center C, unit Axis, radius R, half-length Half) in LOCAL
            // coords (transform-invariant, like Features). `Joint` = the owning joint id. Faces() ignore
            // them too. Full and Blind bores are both just cylinder segments -- only the endpoints differ.
            public List<(Point3d C, Vector3d Axis, double R, double Half, int Joint)> Pegs;
            // JOINT polygons: id-carrying LOCAL elevation polygons that UNION or SUBTRACT (same shape +
            // extrude as Subtracts, but tagged + signed). A SLOPED joint shoulder -- the rafter-foot housing
            // (horizontal bottom shelf, pitched top) -- can't be an axis-aligned Feature box; the post gets
            // the wedge as a SUBTRACT (pocket) and the rafter gets the SAME wedge as a UNION (the housed stub
            // extending the foot in). The id keeps them distinct from shape Subtracts (e.g. a birdsmouth) for
            // clean re-cut / delete. Each poly: X = length coord (along Z), Y = depth coord (along Y),
            // extruded across the width band [Xlo, Xhi] (timber-local X) -- full width for the housing, the
            // tongue width for a tenon. Faces() ignore them too.
            public List<(Point3d[] Poly, int Joint, bool Subtract, double Xlo, double Xhi)> JointPolys;
            // Same as JointPolys but the polygon lives in the (X, Y) CROSS-SECTION plane and is extruded
            // ALONG the length (across f.Z) over [Xlo, Xhi] -- for a section-shaped feature that runs down the
            // member (e.g. the ridge's chamfered TONGUE bedding into the king post). First step toward general
            // extrusion-axis polygons; the X-extruded JointPolys above are unchanged.
            public List<(Point3d[] Poly, int Joint, bool Subtract, double Xlo, double Xhi)> JointPolysZ;
            // GENERAL oriented-prism cut: a PLANAR polygon stored as 3D LOCAL points (worldPt = O + X*p.X
            // + Y*p.Y + Z*p.Z), extruded PERPENDICULAR to its own plane by the local Extrude vector
            // (direction + length). Unlike JointPolys / JointPolysZ (a cross-section in a frame-axis plane,
            // extruded along a frame axis), the polygon may lie at ANY orientation -- so a cut that is
            // OBLIQUE in this timber's local frame (the purlin dovetail housing in a SLOPED rafter) is still
            // exact. UNION (false) / SUBTRACT (true), id-carrying. Faces() ignore them too.
            public List<(Point3d[] Poly, Vector3d Extrude, int Joint, bool Subtract)> JointPrisms;
        }

        // One rectangular face: center C, outward normal N, two in-plane axes U/V with half-extents.
        public struct TFace
        {
            public Point3d C; public Vector3d N; public Vector3d U; public double UHalf; public Vector3d V; public double VHalf;
        }

        // Editable sizing for the girt -> post tenon (inches, girt-LOCAL). Thickness / Length = the stub's
        // width (X) and projection (Z). ShoulderTop / ShoulderBottom pull the tenon in from the girt's top (+Y) /
        // bottom (-Y), leaving bearing shoulders above/below the post mortise. Offset shifts
        // the tenon sideways in the width (X): 0 = centered; pushed to the face = a barefaced tenon.
        public struct TenonSpec
        {
            public bool On;
            public double Thickness, Length, ShoulderTop, ShoulderBottom, Offset;
            public static TenonSpec Default => new TenonSpec
            { On = true, Thickness = 2.0, Length = 4.0, ShoulderTop = 1.0, ShoulderBottom = 1.0, Offset = 0.0 };
        }

        // How far a peg bore runs through the mortise host (the post). Full = all the way through; Blind =
        // in one face and stopping BlindDepth past the tenon's far broad face (a peg that doesn't show out
        // the back). The shop pre-bores only the mortise, never the tenon (the tenon is bored in the field).
        public enum PegBore { Full, Blind }

        // Editable peg layout for a girt -> post joint (inches). Count pegs are STACKED across the girt
        // depth (a vertical column on the tenon) at one station Setback into the post from the bearing
        // face; Spacing = center-to-center between stacked pegs; Diameter = bore size; BlindDepth applies
        // only to a Blind bore. BlindFlip swaps which girt.X face a Blind bore enters from (Full ignores it).
        // Count 0 = no pegs.
        public struct PegSpec
        {
            public int Count;
            public double Diameter, Setback, Spacing, BlindDepth;
            public PegBore Bore;
            public bool BlindFlip;
            public static PegSpec Default => new PegSpec
            { Count = 2, Diameter = 1.0, Setback = 2.0, Spacing = 4.0, BlindDepth = 2.0, Bore = PegBore.Full, BlindFlip = false };
        }

        // Optional HOUSING -- a shallow recess in the post that the girt's end seats into. Its footprint is
        // specified like the TENON: Thickness (width X; 0 = full), Offset (lateral X), ShoulderTop/ShoulderBottom
        // (depth Y insets from the girt's top/bottom, world-up oriented). Seat = recess depth into the post
        // (the housing's projection). When On, the tenon shoulder shifts to the housing BACK. Housings do
        // NOT receive pegs.
        // The footprint is a per-face SHOULDER set: ShoulderTop / ShoulderBottom inset from the section's top /
        // bottom (depth axis, world-up oriented), ShoulderSide1 / ShoulderSide2 inset from its two side faces
        // (width axis, member-local -X / +X). Each is measured FROM that face; 0 = flush = full to that face. (A
        // tenon keeps absolute Thickness + Offset; a housing reads as four bearing shoulders.)
        public struct HousingSpec
        {
            public bool On;
            public double Seat, ShoulderTop, ShoulderBottom, ShoulderSide1, ShoulderSide2;
            public static HousingSpec Default => new HousingSpec
            { On = false, Seat = 1.0, ShoulderTop = 0.0, ShoulderBottom = 0.0, ShoulderSide1 = 0.0, ShoulderSide2 = 0.0 };
        }

        // Optional SHOULDER -- the established 3-pt triangle bearing notch (face-bot, face-top, seat-bot): a
        // HOUSING with the back-top corner dropped, so the top face becomes a diagonal (FIVE-SIDED). The girt
        // gets a triangular tongue, the post a matching triangular notch. `Seat` = the let-in depth into the
        // post: like the housing's Seat, it ADVANCES the shared seat, so the tenon seats that much deeper
        // (total penetration = Seat + tenon Length) and the pegs shift in with it. The face edge spans the
        // section (ShoulderTop/ShoulderBottom insets), Thickness = width (0 = full), Offset = lateral. JointPolys.
        public struct ShoulderSpec
        {
            public bool On;
            public double Seat, Thickness, ShoulderTop, ShoulderBottom, Offset;   // Seat = let-in depth into the post
            public static ShoulderSpec Default => new ShoulderSpec
            { On = false, Seat = 1.5, Thickness = 0.0, ShoulderTop = 0.0, ShoulderBottom = 0.0, Offset = 0.0 };
        }

        // The full recipe for a girt -> post joint: the tenon/mortise + the peg layout + an optional
        // housing / shoulder. The TJoint sticky state is one of these; bundling them is the seed of a future
        // persisted-per-joint / catalog spec.
        public struct JointSpec
        {
            public TenonSpec Tenon;
            public PegSpec Peg;
            public HousingSpec Housing;
            public ShoulderSpec Shoulder;
            public static JointSpec Default => new JointSpec
            { Tenon = TenonSpec.Default, Peg = PegSpec.Default, Housing = HousingSpec.Default, Shoulder = ShoulderSpec.Default };

            // The TUSK TENON factory (floor systems phase 4) -- the classic summer -> girt joint as a
            // combination of the SAME kit: a SOFFIT BEARING housing (bottom band only -- its top
            // shoulder insets everything above the bearing) + a deep tenon riding just above it + one
            // peg. Proportions seed a 10" summer; every value is pane-editable like any box tenon.
            public static JointSpec TuskDefault => new JointSpec
            {
                Tenon = new TenonSpec { On = true, Thickness = 2.0, Length = 4.0, ShoulderTop = 4.0, ShoulderBottom = 3.0, Offset = 0.0 },
                Peg = new PegSpec { Count = 1, Diameter = 1.0, Setback = 2.0, Spacing = 4.0, BlindDepth = 2.0, Bore = PegBore.Full, BlindFlip = false },
                Housing = new HousingSpec { On = true, Seat = 1.0, ShoulderTop = 7.0, ShoulderBottom = 0.0, ShoulderSide1 = 0.0, ShoulderSide2 = 0.0 },
                Shoulder = ShoulderSpec.Default
            };
        }

        // The recipe for a principal-rafter FOOT housed into a post SIDE -- a kit like the girt joint, but
        // the cuts are sloped WEDGES (level seat + pitched top). HOUSING = a full-section seat recessed
        // `Seat` into the post. TENON = a reduced tongue (Thickness wide, centered at Offset, ShoulderTop /
        // ShoulderBottom inset from the rafter top / seat) projecting `Length` PAST the housing into a matching
        // mortise. The seat height + pitched top are DERIVED from the rafter/post geometry. (Pegs later.)
        public struct RafterFootSpec
        {
            public bool On;            // housing (the full-section seat) on
            public double Seat;        // housing recess (let-in) into the post side
            public bool Tenon;         // add the reduced tongue + mortise past the housing
            public double Thickness, Length, ShoulderTop, ShoulderBottom, Offset;
            public PegSpec Peg;        // peg layout (Count 0 = none); pins the tongue, bores the POST cheeks only
            public static RafterFootSpec Default => new RafterFootSpec
            { On = true, Seat = 1.0, Tenon = true, Thickness = 2.0, Length = 4.0, ShoulderTop = 1.0, ShoulderBottom = 1.0, Offset = 0.0,
              Peg = new PegSpec { Count = 0, Diameter = 1.0, Setback = 1.5, Spacing = 2.0, BlindDepth = 2.0, Bore = PegBore.Full, BlindFlip = false } };
        }

        // The recipe for a principal-rafter HEAD bearing on a KING POST side -- the legacy "shoulder" only
        // (no tenon): a right-triangle bearing notch at the rafter underside (the ShoulderGenerator seat).
        // Seat = the seat depth along the rafter underside, into the king post.
        public struct RafterHeadSpec
        {
            public bool On;
            public double Seat;    // bearing-seat depth along the rafter underside (the legacy "sitdepth")
            public static RafterHeadSpec Default => new RafterHeadSpec { On = true, Seat = 3.0 };
        }

        // The recipe for a RIDGE -> KING POST drop-in housing -- the king post top is cut to the ridge's
        // cross-section (incl. its chamfered top) so the ridge lowers straight in; only the king post is cut.
        // Seat = the nominal bearing depth (the housing seat is ~1" deep).
        public struct RidgeHousingSpec
        {
            public bool On;
            public double Seat;            // bearing-seat depth (nominal; the cut beds the ridge's full overlap)
            public double ShoulderBottom;  // raise the housing bottom: the ridge's lower N inches stay full as a
                                           // bearing shoulder against the host face (0 = full-depth drop-in)
            public static RidgeHousingSpec Default => new RidgeHousingSpec { On = true, Seat = 1.0, ShoulderBottom = 0.0 };
        }

        // The recipe for a PURLIN housed into a RAFTER -- a let-in DOVETAIL, matched to the reference solid.
        // The rafter (HOST) gets a full-section HOUSING `Seat` deep + a dovetail POCKET; the purlin's end fills
        // the housing and grows the matching dovetail TONGUE. The tongue is CENTERED in width (X), projects
        // `Length` past the housing, flares from `Width` at the base to `Width + 2*Length*tan(Angle)` at the
        // tip (the dovetail lock -- the purlin can't pull out along its length), and is a `Depth` band flush
        // with the purlin's TOP face (so it drops in from the top). All cut as JointPrisms.
        public struct PurlinRafterSpec
        {
            public bool On;
            public double Seat, Length, Width, Depth, Angle;   // housing depth; tongue length; base width; band depth; taper half-angle (deg)
            public static PurlinRafterSpec Default => new PurlinRafterSpec
            { On = true, Seat = 0.75, Length = 1.75, Width = 1.5, Depth = 2.0, Angle = 15.0 };
        }

        // The recipe for a COMMON RAFTER -> RIDGE let-in HOUSING (a gain). The ridge (HOST) gets a full-section
        // pocket `Seat` deep on the side face the common's head dies into; the common's head fills it. The
        // footprint is the common's SECTION SILHOUETTE on the ridge face (so it shears with the roof pitch),
        // and the pocket floor is a plane parallel to the face `Seat` in -- matched to the reference solid.
        // Seat is the only knob; the width/height come from the common's section + its pitch. Cut as JointPrisms.
        public struct CommonRidgeSpec
        {
            public bool On;
            public double Seat;        // let-in housing depth, measured perpendicular into the ridge face
            public static CommonRidgeSpec Default => new CommonRidgeSpec { On = true, Seat = 0.75 };
        }

        // The recipe for a HOUSED COMMON RAFTER -> EAVE GIRT birdsmouth (both timbers cut). The rafter beds
        // `Seat` below the girt top and `Heel` inside the heel face; the girt gets the matching pocket. The
        // heel side, seat run, taper and pitch are all geometric (from the two picked timbers). Seat/Heel are
        // the canonical glossary names (the let-in depths of the birdsmouth's seat + heel cuts).
        public struct CommonEaveSpec
        {
            public bool On;
            public double Seat;    // seat let-in: how far the seat beds BELOW the girt top (vertical)
            public double Heel;    // heel let-in: how far the heel beds INSIDE the heel face (horizontal)
            public static CommonEaveSpec Default => new CommonEaveSpec { On = true, Seat = 1.0, Heel = 0.75 };
        }

        // The recipe for a STRUT tenon onto a HOST FACE (both timbers cut). A strut end bears flush on a host
        // face and a central tongue enters a matching mortise. HOST-NEUTRAL: the host face is any flat bearing
        // face -- a rafter UNDERSIDE (sloped), a king-post / post SIDE (vertical), etc.; the geometry is derived
        // from the bearing pair, so one joint covers them all. Handles ANY strut angle (the tongue walls adapt
        // to the lean). SPECIFIED LIKE THE STANDARD TENON (TenonSpec / RafterFootSpec): Thickness/Length = the
        // tongue's width + projection; Offset = lateral in the WIDTH (0 = centered, clamped inside the stock);
        // ShoulderTop/ShoulderBottom = DEPTH insets pulling the tongue in from the two depth edges, oriented by
        // WORLD UP so ShoulderTop is the HIGHER edge and ShoulderBottom the LOWER -- the reference flips with the
        // world, not the strut's local axes (mirrors SectionBox's world-up rule). Defaults reproduce
        // strut_to_rafter / vstrut_to_rafter / strut_to_kpost .stl (barefaced in depth -> shoulders 0).
        public struct StrutTenonSpec
        {
            public bool On;          // master gate (the joint is active)
            public double Thickness, Length, ShoulderTop, ShoulderBottom, Offset;
            public bool Tenon;       // cut the tongue (INDEPENDENT of the housing -- either / both / neither)
            public HousingSpec Hsg;  // the housing, a per-face SHOULDER footprint like the box tenon (On + Seat +
                                     // ShoulderTop/ShoulderBottom + ShoulderSide1/ShoulderSide2; every shoulder 0 = full section).
            public PegSpec Peg;      // peg layout (the SAME struct the box tenon uses); Peg.Count 0 = no pegs.
                                     // Pegs pin the tongue and bore the HOST cheeks only (the tongue is field-bored).
            public static StrutTenonSpec Default => new StrutTenonSpec
            { On = true, Thickness = 2.0, Length = 4.0, ShoulderTop = 0.0, ShoulderBottom = 0.0, Offset = 0.0, Tenon = true,
              Hsg = new HousingSpec { On = false, Seat = 1.0, ShoulderTop = 0.0, ShoulderBottom = 0.0, ShoulderSide1 = 0.0, ShoulderSide2 = 0.0 },
              Peg = new PegSpec { Count = 0, Diameter = 1.0, Setback = 1.5, Spacing = 2.0, BlindDepth = 2.0, Bore = PegBore.Full, BlindFlip = false } };

            // The BRACE variant's factory seed -- same end->side tenon engine, just thinner (1.5") and
            // conventionally barefaced (the Offset is computed from the brace width at CUT time, so it is
            // not part of the seed). Was the TBrace sticky's hand literal.
            public static StrutTenonSpec BraceDefault => new StrutTenonSpec
            { On = true, Thickness = 1.5, Length = 4.0, ShoulderTop = 0.0, ShoulderBottom = 0.0, Offset = 0.0, Tenon = true,
              Hsg = new HousingSpec { On = false, Seat = 1.0, ShoulderTop = 0.0, ShoulderBottom = 0.0, ShoulderSide1 = 0.0, ShoulderSide2 = 0.0 },
              Peg = new PegSpec { Count = 0, Diameter = 1.0, Setback = 1.5, Spacing = 2.0, BlindDepth = 2.0, Bore = PegBore.Full, BlindFlip = false } };

            // The QP rafter APEX factory seed -- a short housed tongue at the peak bearing: housing ON,
            // Length 2.0, 1.0 shoulders, pegs set back 1.0. Was the TQPRafter sticky's hand literal (and
            // now also seeds the pane's "QP rafter apex" preset, unifying the two).
            public static StrutTenonSpec QPRafterDefault => new StrutTenonSpec
            { On = true, Thickness = 2.0, Length = 2.0, ShoulderTop = 1.0, ShoulderBottom = 1.0, Offset = 0.0, Tenon = true,
              Hsg = new HousingSpec { On = true, Seat = 1.0, ShoulderTop = 0.0, ShoulderBottom = 0.0, ShoulderSide1 = 0.0, ShoulderSide2 = 0.0 },
              Peg = new PegSpec { Count = 0, Diameter = 1.0, Setback = 1.0, Spacing = 2.0, BlindDepth = 2.0, Bore = PegBore.Full, BlindFlip = false } };
        }
    }
}
