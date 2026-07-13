using System;
using Xunit;

namespace TimberDraw.DomainTests
{
    // Characterization: loading OLD saved data. These pin the migration behavior the plugin
    // ships today; a failure means saved drawings would load differently -- treat as an
    // on-disk-format incident, not a test to update casually.
    public class FrameSpecMigrationTests
    {
        // Pre-specVersion-2 saves stored Separation as "gap to the NEXT bent" (last unused).
        // The loader converts to "gap FROM the previous bent" (bent 1 = 0) preserving positions:
        // [96, 120, 0] -> [0, 96, 120].
        [Fact]
        public void SpecVersion1_Separations_ShiftToFromPrevious()
        {
            FrameSpec s = FrameSpecStore.FromJson(TestSupport.ReadFixture("legacy-specversion1.framespec"));
            Assert.Equal(3, s.Bents.Count);
            Assert.Equal(0.0, s.Bents[0].Separation);
            Assert.Equal(96.0, s.Bents[1].Separation);
            Assert.Equal(120.0, s.Bents[2].Separation);
        }

        [Fact]
        public void SpecVersion2_Separations_LoadUnchanged()
        {
            string json = FrameSpecStore.ToJson(TestSupport.CanonicalKingPost());
            FrameSpec s = FrameSpecStore.FromJson(json);
            Assert.Equal(0.0, s.Bents[0].Separation);
            Assert.Equal(120.0, s.Bents[1].Separation);
        }

        // The oldest format: one ordered "elements" list alternating bent/bay. Bents become the
        // Bents axis (separation taken from the FOLLOWING bay's spacing, then shifted by the
        // specVersion migration), each old bay splits into Wall A / Wall B cells.
        [Fact]
        public void LegacyElements_MigrateToTwoAxes()
        {
            FrameSpec s = FrameSpecStore.FromJson(TestSupport.ReadFixture("legacy-elements.framespec"));
            Assert.Equal(2, s.Bents.Count);
            Assert.Equal(0.0, s.Bents[0].Separation);
            Assert.Equal(96.0, s.Bents[1].Separation);
            Assert.NotNull(s.WallLeft());
            Assert.NotNull(s.WallRight());
            Assert.Equal("A", s.WallLeft().Letter);
        }

        // queenFraction dimensioned the queen's OUTER face from the left edge; the loader
        // converts to center-to-inner QueenOffset = span*(0.5 - fraction) - queenD. The fixture
        // carries a timbers array WITHOUT a Queen:B leaf, so queenD falls back to the literal 6.0:
        // 288*(0.5-0.25) - 6 = 66.
        [Fact]
        public void QueenFraction_MigratesToQueenOffset()
        {
            FrameSpec s = FrameSpecStore.FromJson(TestSupport.ReadFixture("legacy-queenfraction.framespec"));
            Assert.Equal(66.0, s.Bents[0].QueenOffset, 10);
        }

        // Pre-Foot/Head saves carried a single brace run (Length) + Angle; the loader derives the
        // triangle: Head = Length, Foot = Length * tan(Angle).
        [Fact]
        public void PreFootHeadBrace_DerivesLegsFromRunAndAngle()
        {
            FrameSpec s = FrameSpecStore.FromJson(TestSupport.ReadFixture("legacy-queenfraction.framespec"));
            Timber brace = s.Bents[0].Find("Brace:A");
            Assert.NotNull(brace);
            Assert.Equal(36.0, brace.Size.Head, 10);
            Assert.Equal(36.0 * Math.Tan(30.0 * Math.PI / 180.0), brace.Size.Foot, 10);
        }
    }
}
