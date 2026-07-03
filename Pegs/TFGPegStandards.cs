using System;
using System.Collections.Generic;

namespace TimberFrameSuite.Standards
{
    /// <summary>
    /// Timber Framers Guild peg sizing and placement standards reference.
    /// Based on TFEC publications and guild practices.
    /// </summary>
    public static class TFGPegStandards
    {
        /// <summary>
        /// Standard peg sizing rule: peg diameter = 1/2 x tenon thickness
        /// </summary>
        public const double PegDiameterTenonRatio = 0.5;

        /// <summary>
        /// Maximum standard peg diameter used in typical TFG practice
        /// </summary>
        public const double MaximumPegDiameterInches = 1.25;

        /// <summary>
        /// Distance from bearing nose (shoulder) to first peg
        /// Equals thickness of standard framing square body
        /// </summary>
        public const double FirstPegSetbackInches = 2.0;

        /// <summary>
        /// Standard spacing between multiple pegs in peg diameter multiples
        /// For pegs along beam: 3.5 diameters
        /// </summary>
        public const double AlongBeamSpacingMultiplier = 3.5;

        /// <summary>
        /// Minimum edge spacing perpendicular to tenon axis in peg diameters
        /// </summary>
        public const double MinimumEdgeSpacingMultiplier = 3.0;

        /// <summary>
        /// Standard offset from tenon shoulder on beam face
        /// </summary>
        public const double StandardShoulderOffsetInches = 2.0;

        /// <summary>
        /// Offset adjustment for 0.5" housing depth
        /// </summary>
        public const double ShoulderOffsetHalfInch = 2.5;

        /// <summary>
        /// Offset adjustment for 0.75" housing depth
        /// </summary>
        public const double ShoulderOffsetThreeQuarterInch = 2.75;

        /// <summary>
        /// Minimum number of pegs per standard joint
        /// </summary>
        public const int MinimumPegsPerJoint = 2;

        /// <summary>
        /// Common peg diameters used in practice (inches)
        /// </summary>
        public static readonly double[] CommonPegDiameters = { 0.5, 0.625, 0.75, 0.875, 1.0, 1.125, 1.25 };

        /// <summary>
        /// Get preset peg specification for common tenon thicknesses.
        /// </summary>
        public static PegSpecification GetPresetForTenonThickness(double tenonThicknessInches)
        {
            double pegDiameter = tenonThicknessInches * PegDiameterTenonRatio;

            return new PegSpecification
            {
                DiameterInches = RoundToCommonSize(pegDiameter),
                TenonThicknessInches = tenonThicknessInches,
                FirstPegSetbackInches = FirstPegSetbackInches,
                PegSpacingMultiplier = AlongBeamSpacingMultiplier,
                MinimumEdgeSpacingMultiplier = MinimumEdgeSpacingMultiplier,
                OffsetFromShouderInches = StandardShoulderOffsetInches,
                Justification = PegJustification.DownwardGravity,
                PegCount = MinimumPegsPerJoint
            };
        }

        /// <summary>
        /// Preset for a 2" tenon (typical post-to-tie-beam): 1" pegs
        /// </summary>
        public static PegSpecification TieBeam2InchTenon => new()
        {
            DiameterInches = 1.0,
            TenonThicknessInches = 2.0,
            FirstPegSetbackInches = 2.0,
            PegSpacingMultiplier = 3.5,
            MinimumEdgeSpacingMultiplier = 3.0,
            OffsetFromShouderInches = 2.0,
            PegCount = 2
        };

        /// <summary>
        /// Preset for a 1.5" tenon (typical brace): 3/4" pegs
        /// </summary>
        public static PegSpecification Brace15InchTenon => new()
        {
            DiameterInches = 0.75,
            TenonThicknessInches = 1.5,
            FirstPegSetbackInches = 2.0,
            PegSpacingMultiplier = 3.5,
            MinimumEdgeSpacingMultiplier = 3.0,
            OffsetFromShouderInches = 2.0,
            PegCount = 2
        };

        /// <summary>
        /// Preset for a 1" tenon (smaller joinery): 1/2" pegs
        /// </summary>
        public static PegSpecification SmallJoint1InchTenon => new()
        {
            DiameterInches = 0.5,
            TenonThicknessInches = 1.0,
            FirstPegSetbackInches = 2.0,
            PegSpacingMultiplier = 3.5,
            MinimumEdgeSpacingMultiplier = 3.0,
            OffsetFromShouderInches = 2.0,
            PegCount = 1
        };

        /// <summary>
        /// Preset for heavy duty joint (3" tenon): 1.25" pegs (max)
        /// </summary>
        public static PegSpecification HeavyDuty3InchTenon => new()
        {
            DiameterInches = 1.25,  // At maximum standard size
            TenonThicknessInches = 3.0,
            FirstPegSetbackInches = 2.0,
            PegSpacingMultiplier = 3.5,
            MinimumEdgeSpacingMultiplier = 3.0,
            OffsetFromShouderInches = 2.0,
            PegCount = 3
        };

        /// <summary>
        /// Preset for drawbored joint (offset holes for tighter fit)
        /// </summary>
        public static PegSpecification DrawboredJoint1InchTenon => new()
        {
            DiameterInches = 1.0,
            TenonThicknessInches = 2.0,
            FirstPegSetbackInches = 2.0,
            PegSpacingMultiplier = 3.5,
            MinimumEdgeSpacingMultiplier = 3.0,
            OffsetFromShouderInches = 2.0,
            Justification = PegJustification.DrawboredOffset,
            PegCount = 1
        };

        /// <summary>
        /// Round calculated peg diameter to nearest common size.
        /// </summary>
        private static double RoundToCommonSize(double diameter)
        {
            double nearest = CommonPegDiameters[0];
            double minDiff = Math.Abs(diameter - nearest);

            foreach (var size in CommonPegDiameters)
            {
                double diff = Math.Abs(diameter - size);
                if (diff < minDiff)
                {
                    minDiff = diff;
                    nearest = size;
                }
            }

            return nearest;
        }

        /// <summary>
        /// Get all available preset specifications.
        /// </summary>
        public static Dictionary<string, PegSpecification> GetAllPresets()
        {
            return new Dictionary<string, PegSpecification>
            {
                { "Tie Beam (2\" tenon)", TieBeam2InchTenon },
                { "Brace (1.5\" tenon)", Brace15InchTenon },
                { "Small Joint (1\" tenon)", SmallJoint1InchTenon },
                { "Heavy Duty (3\" tenon)", HeavyDuty3InchTenon },
                { "Drawbored (1\" tenon)", DrawboredJoint1InchTenon }
            };
        }

        /// <summary>
        /// Generate TFG guidelines summary for documentation.
        /// </summary>
        public static string GetGuidelinesSummary()
        {
            return @"
===========================================================================
   Timber Framers Guild Peg Sizing & Placement Standards
   (TFEC Research Reports & Standards)
===========================================================================

PEG SIZING:
----------
- Peg diameter = 1/2 x tenon thickness
  Example: 2"" tenon -> 1"" diameter peg

- Maximum standard peg diameter: 1.25""
- Common sizes: 0.5"", 0.625"", 0.75"", 0.875"", 1.0"", 1.125"", 1.25""

PEG PLACEMENT - ALONG BEAM:
---------------------------
- First peg distance from bearing nose: 2"" (framing square thickness)
- Spacing between multiple pegs: 3.5 x peg diameter
  Example: 1"" pegs spaced 3.5"" apart

PEG PLACEMENT - ACROSS FACE:
-----------------------------
- Distance off tenon shoulder: 2"" (standard)
  - With 0.5"" housing: 2.5""
  - With 0.75"" housing: 2.75""

- Minimum edge spacing: 3 x peg diameter
  Example: 1"" pegs minimum 3"" apart perpendicular to joint

JOINT GUIDELINES:
-----------------
- Minimum 2 pegs per standard joint
- Pegs should be justified toward gravity (maintain alignment during drying)
- Drawbored pegs use slight hole offset for tighter fit

REFERENCES:
-----------
- Timber Framers Guild (TFG) Timber Frame Engineering Council (TFEC)
- Publication: ""Edge Spacing of Pegs in Mortise and Tenon Joints""
- Publication: ""Structural Properties of Pegged Timber Connections
  as Affected by End Distance""
";
        }
    }
}
