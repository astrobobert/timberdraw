using TimberFrameSuite.Standards;
using Xunit;

namespace TimberDraw.DomainTests
{
    // Characterization: the TFG peg sizing/placement reference data (kept for the future shared
    // library; not yet consumed by the managed joinery engine, which has its own PegSpec).
    public class TFGPegStandardsTests
    {
        [Theory]
        [InlineData(2.0, 1.0)]     // dia = tenon/2
        [InlineData(1.5, 0.75)]
        [InlineData(1.0, 0.5)]
        [InlineData(2.5, 1.25)]
        [InlineData(3.0, 1.25)]    // 1.5 computed -> nearest common size caps at 1.25
        public void PresetForTenon_SizesPegAtHalfThickness_RoundedToCommon(double tenon, double expectedDia)
        {
            PegSpecification spec = TFGPegStandards.GetPresetForTenonThickness(tenon);
            Assert.Equal(expectedDia, spec.DiameterInches, 10);
            Assert.Equal(tenon, spec.TenonThicknessInches);
            Assert.Equal(TFGPegStandards.FirstPegSetbackInches, spec.FirstPegSetbackInches);
            Assert.Equal(TFGPegStandards.AlongBeamSpacingMultiplier, spec.PegSpacingMultiplier);
            Assert.Equal(TFGPegStandards.MinimumPegsPerJoint, spec.PegCount);
        }

        [Fact]
        public void Constants_MatchTFGPractice()
        {
            Assert.Equal(0.5, TFGPegStandards.PegDiameterTenonRatio);
            Assert.Equal(1.25, TFGPegStandards.MaximumPegDiameterInches);
            Assert.Equal(2.0, TFGPegStandards.FirstPegSetbackInches);
            Assert.Equal(3.5, TFGPegStandards.AlongBeamSpacingMultiplier);
            Assert.Equal(3.0, TFGPegStandards.MinimumEdgeSpacingMultiplier);
            Assert.Equal(2, TFGPegStandards.MinimumPegsPerJoint);
        }

        [Fact]
        public void CalculatedSpacing_IsDiameterTimesMultiplier()
        {
            PegSpecification spec = TFGPegStandards.TieBeam2InchTenon;
            Assert.Equal(3.5, spec.CalculatedSpacingInches, 10);            // 1.0 x 3.5
            Assert.Equal(3.0, spec.CalculatedMinimumEdgeSpacingInches, 10); // 1.0 x 3.0
        }

        [Fact]
        public void Validate_FlagsSinglePegAsError()
        {
            PegSpecification spec = TFGPegStandards.SmallJoint1InchTenon;   // PegCount = 1
            PegValidationResult r = spec.Validate();
            Assert.False(r.IsValid);
            Assert.Contains(r.Errors, e => e.Contains("Minimum 2 pegs"));
        }

        [Fact]
        public void Validate_WarnsOnOversizePeg()
        {
            var spec = new PegSpecification { DiameterInches = 1.5, TenonThicknessInches = 3.0, PegCount = 2 };
            PegValidationResult r = spec.Validate();
            Assert.True(r.IsValid);                                         // warning, not error
            Assert.Contains(r.Warnings, w => w.Contains("exceeds TFG standard maximum"));
        }

        [Fact]
        public void AllPresets_Enumerate()
        {
            var presets = TFGPegStandards.GetAllPresets();
            Assert.Equal(5, presets.Count);
        }
    }
}
