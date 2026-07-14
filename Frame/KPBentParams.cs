using System;
using System.Collections.Generic;

namespace TimberDraw
{
    // Parameter bundle for a King Post bent. Derived structural levels are computed
    // properties so the graph builder stays consistent.
    // Mirrors the derivations in KPBent.Draw() (TOH/TOG/BOG, Beta, PlumbLength).
    public class KPBentParams
    {
        public double Span;
        public double EaveHt;
        public double Pitch;
        public double PostW, PostD;
        public double GirtW, GirtD;
        public double RafterW, RafterD;
        public double KpostW, KpostD;
        public double RidgeW, RidgeD;   // ridge section (own size, not the king post)
        public double BraceW, BraceD, BraceLength;
        public double BraceAngle;   // degrees from horizontal; default 45
        public double BraceFoot, BraceHead;   // brace legs: Foot = down the post (vertical),
                                              // Head = along the girt (horizontal); tan(Angle) = Foot/Head
        public bool   HasBrace;
        public double StrutW, StrutD;
        public bool   HasStrut;
        public double VStrutW, VStrutD;
        public bool   HasVStrut;
        public double StrutAngle;   // degrees from horizontal; default 45
        // Per-side strut overrides (Robert's bug, batch-2 #12: the L and R struts were hard-linked --
        // both sides built from the Strut:A leaf and either checkbox enabled both, like the floor
        // braces once were). Zero W/D/Angle means "same as the left"; the *On flags default true so
        // older callers stay symmetric.
        public bool   StrutLOn = true, StrutROn = true;
        public bool   VStrutLOn = true, VStrutROn = true;
        public double StrutWR, StrutDR, StrutAngleR;   // right strut; 0 = mirror the left
        public double VStrutWR, VStrutDR;              // right vertical strut; 0 = mirror the left
        public int    OffsetType;   // 0 Back, 1 Centered, 2 Front (BraceStrutPlacement)
        public double[] BaySpacings; // center-to-center bent spacing per bay (face-to-same-face);
                                     // bent count = BaySpacings.Length + 1
        public bool   UseCommons;       // roof mode: true = common rafters; false = purlins
        public int    CommonMode;       // commons spacing: 0 = by count, 1 = by center-to-center spacing
        public int    CommonCount;      // mode 0: number of commons per bay
        public double CommonSpacing;    // mode 1: requested on-center spacing
        public double CommonW, CommonD; // common rafter section (own size, not the principal rafter)
        public int    PurlinMode;       // purlins spacing: 0 = by count, 1 = by on-center spacing (along slope)
        public int    PurlinCount;      // mode 0: number of purlins per slope per bay
        public double PurlinSpacing;    // mode 1: requested on-center spacing (along the slope)
        public double PurlinW, PurlinD; // purlin section: W along the slope, D perpendicular (down from roof)
        public double QueenW, QueenD;       // Queen Post bent: queen post section
        public double UpperGirtW, UpperGirtD;// Queen Post bent: straining beam section
        public double QueenOffset;          // Queen Post bent: queen post INNER face at Span/2 - QueenOffset (from center)
        public bool   HasQueen, HasUpperGirt;// Queen Post bent: member gates (struts reuse HasStrut/StrutAngle)
        public double HBeamW, HBeamD;       // Hammer Beam bent: hammer beam section
        public double HPostW, HPostD;       // Hammer Beam bent: hammer post section
        public double CollarW, CollarD;     // Hammer Beam bent: collar (HBGirt) section
        public int    HBDivisor;            // Hammer Beam bent: 4 = single tier (6 = stacked, later)
        public bool   HasHBeam, HasHPost, HasCollar; // Hammer Beam bent: member gates
        public double FloorGirtW, FloorGirtD, FloorGirtHt; // shared floor girt: section + TOP height
        public bool   HasFloorGirt, HasFloorBrace;  // shared floor girt gates
        // Floor girt braces carry their OWN size/legs (from the FloorBrace:A leaf); zero falls back
        // to the bent girt brace values in the builder -- they used to be hard-linked (Robert's bug).
        public double FloorBraceW, FloorBraceD, FloorBraceFoot, FloorBraceHead;
        // Sill BELOW grade (floor systems phase 3, Robert's convention 2026-07-07): the post feet
        // stay the frame datum at Y=0 (full-length posts, grid unchanged); the sill TOP sits at
        // SillHt (default 0 = under the post feet; negative offsets it deeper), body SillHt-D..SillHt.
        // W = thickness along the building (matches the posts), D = vertical depth.
        public double SillW, SillD, SillHt;
        public bool   HasSill;
        public double GirtDrop;             // tie elevation: tie TOP sits GirtDrop below the rafter-foot line (TOH), min 6"
        public bool   Make3D;

        // Per-member centering by "Role:Designation" key: 0 Back / 1 Center / 2 Front, already resolved
        // (a member's Justify.Default -> OffsetType). PlaceOf falls back to OffsetType for missing keys.
        public Dictionary<string, int> Place = new Dictionary<string, int>();
        public int PlaceOf(string key) => Place.TryGetValue(key, out int v) ? v : OffsetType;

        public double Beta        => Math.Atan(Pitch);
        public double PlumbLength => RafterD / Math.Cos(Beta);
        public double TOH         => (EaveHt + PostD * Pitch) - PlumbLength;
        // Tie top sits GirtDrop below the rafter-foot line; 6" is the MINIMUM (the heel/birdsmouth +
        // king-post bearing need that clearance). Max(6, ..) also makes an unset GirtDrop (0) safe.
        public double TOG         => TOH - Math.Max(6.0, GirtDrop);
        public double BOG         => TOG - GirtD;
    }
}
