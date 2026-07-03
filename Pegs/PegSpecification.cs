using System;
using System.Collections.Generic;

namespace TimberFrameSuite.Standards
{
    /// <summary>
    /// Peg specification following Timber Framers Guild standards.
    /// Reference: Edge Spacing of Pegs in Mortise and Tenon Joints (TFEC)
    /// </summary>
    public class PegSpecification
    {
        /// <summary>
        /// Peg diameter in inches.
        /// Standard: peg diameter = 1/2 x tenon thickness
        /// Common sizes: 0.5", 0.75", 1.0", 1.25"
        /// Maximum: 1.25" (TFG standard practice)
        /// </summary>
        public double DiameterInches { get; set; }

        /// <summary>
        /// Tenon thickness in inches (for reference/calculation).
        /// Used to determine peg sizing ratio.
        /// </summary>
        public double TenonThicknessInches { get; set; }

        /// <summary>
        /// Distance from bearing nose (shoulder) to first peg in inches.
        /// Standard: 2" (thickness of framing square body)
        /// </summary>
        public double FirstPegSetbackInches { get; set; } = 2.0;

        /// <summary>
        /// Spacing between multiple pegs along beam in peg diameters.
        /// Standard: 3.5 diameters minimum
        /// Example: 1" peg = 3.5" spacing
        /// </summary>
        public double PegSpacingMultiplier { get; set; } = 3.5;

        /// <summary>
        /// Spacing along face of beam in peg diameters.
        /// Standard: 3 diameters minimum (perpendicular to tenon axis)
        /// Example: 1" peg = 3" minimum spacing
        /// </summary>
        public double MinimumEdgeSpacingMultiplier { get; set; } = 3.0;

        /// <summary>
        /// Distance off tenon shoulder on beam face in inches.
        /// Standard: 2" off shoulder (unless brace on opposite side)
        /// Adjust for housing depth: 2.5" (0.5" housing), 2.75" (0.75" housing)
        /// </summary>
        public double OffsetFromShouderInches { get; set; } = 2.0;

        /// <summary>
        /// Orientation preference. Pegs should be justified toward gravity
        /// so they remain true as beam dries.
        /// </summary>
        public PegJustification Justification { get; set; } = PegJustification.DownwardGravity;

        /// <summary>
        /// Number of pegs in this joint.
        /// Minimum: 2 pegs (standard practice)
        /// </summary>
        public int PegCount { get; set; } = 2;

        /// <summary>
        /// Calculated spacing value in inches between consecutive pegs.
        /// = DiameterInches x PegSpacingMultiplier
        /// </summary>
        public double CalculatedSpacingInches
        {
            get => DiameterInches * PegSpacingMultiplier;
        }

        /// <summary>
        /// Calculated minimum edge spacing in inches.
        /// = DiameterInches x MinimumEdgeSpacingMultiplier
        /// </summary>
        public double CalculatedMinimumEdgeSpacingInches
        {
            get => DiameterInches * MinimumEdgeSpacingMultiplier;
        }

        /// <summary>
        /// Validate peg sizing per TFG standards.
        /// </summary>
        public PegValidationResult Validate()
        {
            var result = new PegValidationResult { IsValid = true };

            // Check diameter vs tenon thickness ratio
            if (Math.Abs(DiameterInches - (TenonThicknessInches * 0.5)) > 0.125)
            {
                result.Warnings.Add(
                    $"Peg diameter ({DiameterInches}\") deviates from standard 1/2 tenon thickness rule ({TenonThicknessInches * 0.5}\")");
            }

            // Check maximum peg size
            if (DiameterInches > 1.25)
            {
                result.Warnings.Add($"Peg diameter ({DiameterInches}\") exceeds TFG standard maximum (1.25\")");
            }

            // Check minimum peg count
            if (PegCount < 2)
            {
                result.IsValid = false;
                result.Errors.Add("Minimum 2 pegs required per TFG standards");
            }

            // Check spacing values
            if (CalculatedSpacingInches < DiameterInches * 3.0)
            {
                result.Warnings.Add(
                    $"Spacing ({CalculatedSpacingInches}\") may be too tight; minimum recommended is {DiameterInches * 3.0}\"");
            }

            return result;
        }

        /// <summary>
        /// Generate a summary string for logging/display.
        /// </summary>
        public override string ToString()
        {
            return $"{PegCount}x dia.{DiameterInches}\" pegs @ {CalculatedSpacingInches:F2}\" spacing " +
                   $"(tenon: {TenonThicknessInches}\", ratio: {DiameterInches / TenonThicknessInches:F2}x)";
        }
    }

    /// <summary>
    /// Peg justification for gravity and shrinkage management.
    /// </summary>
    public enum PegJustification
    {
        /// <summary>Pegs aligned toward gravity (standard practice)</summary>
        DownwardGravity,
        /// <summary>Pegs aligned perpendicular to grain for specialized cases</summary>
        CrossGrain,
        /// <summary>Pegs offset for drawboring technique</summary>
        DrawboredOffset
    }

    /// <summary>
    /// Result of peg specification validation.
    /// </summary>
    public class PegValidationResult
    {
        public bool IsValid { get; set; }
        public List<string> Errors { get; set; } = new();
        public List<string> Warnings { get; set; } = new();
    }

    /// <summary>
    /// Joint information including peg specifications.
    /// </summary>
    public class JointSpecification
    {
        /// <summary>Type of joint (e.g., mortise-tenon, housing, etc.)</summary>
        public string JointType { get; set; } = "";

        /// <summary>Description of joint for reference.</summary>
        public string Description { get; set; } = "";

        /// <summary>Peg specification for this joint.</summary>
        public PegSpecification Pegs { get; set; } = new();

        /// <summary>Reference to Timber Framers Guild publication or TFEC standard.</summary>
        public string GuildReference { get; set; } = "TFEC: Edge Spacing of Pegs in Mortise and Tenon Joints";

        /// <summary>Validation result cached from last validation.</summary>
        public PegValidationResult LastValidation { get; set; }

        /// <summary>Validate this joint specification.</summary>
        public PegValidationResult Validate()
        {
            LastValidation = Pegs.Validate();
            return LastValidation;
        }
    }
}
