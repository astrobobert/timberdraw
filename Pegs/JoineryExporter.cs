using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TimberFrameSuite.Standards;

namespace TimberScribe.Export
{
    /// <summary>
    /// Extension for TimberScribe to include joinery and peg specifications
    /// in the .tsj (TimberScribe JSON) export.
    /// </summary>
    public class JoineryExporter
    {
        /// <summary>
        /// Add joinery metadata to TimberMeta for JSON export.
        /// Extension method would add to existing TimberMeta.
        /// </summary>
        public class JoineryMetadata
        {
            public string TimberID { get; set; } = "";
            public List<JointSpecification> Joints { get; set; } = new();
            public string TFGGuidelinesVersion { get; set; } = "TFEC 2024";
            public List<JoineryValidationError> ValidationErrors { get; set; } = new();

            /// <summary>
            /// Validate all joints in this timber's joinery.
            /// </summary>
            public bool ValidateAll()
            {
                ValidationErrors.Clear();
                bool allValid = true;

                foreach (var joint in Joints)
                {
                    var result = joint.Validate();
                    if (!result.IsValid)
                    {
                        allValid = false;
                        foreach (var error in result.Errors)
                        {
                            ValidationErrors.Add(new JoineryValidationError
                            {
                                JointType = joint.JointType,
                                Severity = JoineryErrorSeverity.Error,
                                Message = error
                            });
                        }
                    }

                    foreach (var warning in result.Warnings)
                    {
                        ValidationErrors.Add(new JoineryValidationError
                        {
                            JointType = joint.JointType,
                            Severity = JoineryErrorSeverity.Warning,
                            Message = warning
                        });
                    }
                }

                return allValid;
            }

            /// <summary>
            /// Export joinery data as JSON-compatible dictionary.
            /// </summary>
            public Dictionary<string, object> ToJsonDictionary()
            {
                var joints = new List<Dictionary<string, object>>();

                foreach (var joint in Joints)
                {
                    joints.Add(new Dictionary<string, object>
                    {
                        { "type", joint.JointType },
                        { "description", joint.Description },
                        { "reference", joint.GuildReference },
                        { "pegs", new Dictionary<string, object>
                        {
                            { "count", joint.Pegs.PegCount },
                            { "diameter_inches", joint.Pegs.DiameterInches },
                            { "tenon_thickness_inches", joint.Pegs.TenonThicknessInches },
                            { "spacing_inches", joint.Pegs.CalculatedSpacingInches },
                            { "first_peg_setback_inches", joint.Pegs.FirstPegSetbackInches },
                            { "edge_spacing_inches", joint.Pegs.CalculatedMinimumEdgeSpacingInches },
                            { "shoulder_offset_inches", joint.Pegs.OffsetFromShouderInches },
                            { "justification", joint.Pegs.Justification.ToString() },
                            { "summary", joint.Pegs.ToString() }
                        }}
                    });
                }

                return new Dictionary<string, object>
                {
                    { "timber_id", TimberID },
                    { "tfg_standards_version", TFGGuidelinesVersion },
                    { "joint_count", Joints.Count },
                    { "joints", joints },
                    { "validation_errors", ValidationErrors.Count > 0 
                        ? ValidationErrors.Select(e => e.ToJsonDictionary()).ToList() 
                        : new List<Dictionary<string, object>>() }
                };
            }
        }

        /// <summary>
        /// Validation error or warning from joinery specification.
        /// </summary>
        public class JoineryValidationError
        {
            public string JointType { get; set; } = "";
            public JoineryErrorSeverity Severity { get; set; }
            public string Message { get; set; } = "";

            public Dictionary<string, object> ToJsonDictionary()
            {
                return new Dictionary<string, object>
                {
                    { "joint_type", JointType },
                    { "severity", Severity.ToString() },
                    { "message", Message }
                };
            }
        }

        public enum JoineryErrorSeverity
        {
            Info,
            Warning,
            Error
        }

        /// <summary>
        /// Create a sample timber with common joinery for testing/documentation.
        /// </summary>
        public static JoineryMetadata CreateSampleTieBeamJoinery()
        {
            var metadata = new JoineryMetadata
            {
                TimberID = "POST_B3"
            };

            // Tie beam to post joint
            var tieBeamJoint = new JointSpecification
            {
                JointType = "Mortise & Tenon - Tie Beam",
                Description = "Post to tie beam housing joint with drawbored pegs",
                Pegs = TFGPegStandards.TieBeam2InchTenon,
                GuildReference = "TFEC: Edge Spacing of Pegs in Mortise and Tenon Joints"
            };

            // Brace to post joint
            var braceJoint = new JointSpecification
            {
                JointType = "Mortise & Tenon - Brace",
                Description = "Post to brace pegged joint",
                Pegs = TFGPegStandards.Brace15InchTenon,
                GuildReference = "TFEC: Edge Spacing of Pegs in Mortise and Tenon Joints"
            };

            metadata.Joints.Add(tieBeamJoint);
            metadata.Joints.Add(braceJoint);

            return metadata;
        }

        /// <summary>
        /// Generate a formatted report of joinery specifications.
        /// </summary>
        public static string GenerateJoineryReport(JoineryMetadata metadata)
        {
            var sb = new StringBuilder();
            
            metadata.ValidateAll();

            sb.AppendLine("+================================================================+");
            sb.AppendLine($"| TIMBER JOINERY SPECIFICATION: {metadata.TimberID,-36}|");
            sb.AppendLine($"| TFG Standards Version: {metadata.TFGGuidelinesVersion,-39}|");
            sb.AppendLine("+================================================================+");
            sb.AppendLine();

            if (metadata.Joints.Count == 0)
            {
                sb.AppendLine("  [No joints specified]");
                return sb.ToString();
            }

            for (int i = 0; i < metadata.Joints.Count; i++)
            {
                var joint = metadata.Joints[i];
                sb.AppendLine($"JOINT #{i + 1}: {joint.JointType}");
                sb.AppendLine($"  Description: {joint.Description}");
                sb.AppendLine($"  Reference:   {joint.GuildReference}");
                sb.AppendLine();
                sb.AppendLine($"  Peg Specification:");
                sb.AppendLine($"    - Count:                {joint.Pegs.PegCount} pegs (minimum: 2)");
                sb.AppendLine($"    - Diameter:            {joint.Pegs.DiameterInches}\" (from {joint.Pegs.TenonThicknessInches}\" tenon)");
                sb.AppendLine($"    - Ratio:               1/{1.0 / (joint.Pegs.DiameterInches / joint.Pegs.TenonThicknessInches):F1} (standard: 1/2)");
                sb.AppendLine($"    - Spacing (along beam): {joint.Pegs.CalculatedSpacingInches:F2}\" ({joint.Pegs.PegSpacingMultiplier}x diameter)");
                sb.AppendLine($"    - Edge spacing (min):  {joint.Pegs.CalculatedMinimumEdgeSpacingInches:F2}\" ({joint.Pegs.MinimumEdgeSpacingMultiplier}x diameter)");
                sb.AppendLine($"    - First peg setback:   {joint.Pegs.FirstPegSetbackInches}\" from bearing nose");
                sb.AppendLine($"    - Shoulder offset:     {joint.Pegs.OffsetFromShouderInches}\" from tenon shoulder");
                sb.AppendLine($"    - Justification:       {joint.Pegs.Justification} (gravity alignment)");
                sb.AppendLine();
            }

            if (metadata.ValidationErrors.Count > 0)
            {
                sb.AppendLine("VALIDATION ISSUES:");
                sb.AppendLine();
                foreach (var error in metadata.ValidationErrors)
                {
                    string icon = error.Severity == JoineryErrorSeverity.Error ? "X" : "!";
                    sb.AppendLine($"  {icon} [{error.Severity}] {error.JointType}");
                    sb.AppendLine($"     {error.Message}");
                }
                sb.AppendLine();
            }
            else
            {
                sb.AppendLine("OK - All joints validated successfully against TFG standards");
                sb.AppendLine();
            }

            return sb.ToString();
        }

        // NOTE: InjectJoineryMetadata belongs in TimberScribe (TimberMeta lives there).
        // Placeholder kept as a reminder; implement when TimberScribe project is extracted.
    }
}
