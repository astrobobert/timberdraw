using Xunit;

namespace TimberDraw.DomainTests
{
    // Characterization: the FrameSpec instance model's structural rules -- the bent-separation
    // convention (Separation = gap FROM the previous bent; bent 1 = 0, the datum), insert/remove
    // arithmetic, the Span setter's wall scaling, and the self-heal helpers older saves rely on.
    public class FrameSpecInvariantTests
    {
        private static FrameSpec ThreeBents()
        {
            FrameSpec s = TestSupport.CanonicalKingPost();          // bents at 0 and 120
            BentSpec b3 = s.InsertBent(s.Bents[1], before: false, sep: 96.0);
            Assert.NotNull(b3);                                     // appended: [0, 120, 96]
            return s;
        }

        [Fact]
        public void GapForBentInsert_Interior_IsOwnedByTheDownstreamBent()
        {
            FrameSpec s = ThreeBents();
            double gap = s.GapForBentInsert(s.Bents[1], before: true, out bool interior);
            Assert.True(interior);
            Assert.Equal(120.0, gap);                               // the straddled gap = Bents[1].Separation
        }

        [Fact]
        public void InsertBent_InteriorSplit_PreservesPositions()
        {
            FrameSpec s = ThreeBents();
            BentSpec nb = s.InsertBent(s.Bents[1], before: true, sep: 40.0);
            Assert.NotNull(nb);
            // [0, 120, 96] split at 40 -> [0, 40, 80, 96]; absolute positions 0/40/120/216.
            Assert.Equal(4, s.Bents.Count);
            Assert.Equal(0.0, s.Bents[0].Separation);
            Assert.Equal(40.0, s.Bents[1].Separation);
            Assert.Equal(80.0, s.Bents[2].Separation);
            Assert.Equal(96.0, s.Bents[3].Separation);
        }

        [Fact]
        public void InsertBent_BeforeFirst_NewBentBecomesTheDatum()
        {
            FrameSpec s = ThreeBents();
            BentSpec nb = s.InsertBent(s.Bents[0], before: true, sep: 30.0);
            Assert.NotNull(nb);
            Assert.Same(nb, s.Bents[0]);
            Assert.Equal(0.0, s.Bents[0].Separation);               // new datum
            Assert.Equal(30.0, s.Bents[1].Separation);              // old first now measures from it
        }

        [Fact]
        public void RemoveBent_Datum_PromotesAndZeroesTheNext()
        {
            FrameSpec s = ThreeBents();
            Assert.True(s.RemoveBent(s.Bents[0]));
            Assert.Equal(2, s.Bents.Count);
            Assert.Equal(0.0, s.Bents[0].Separation);
        }

        [Fact]
        public void SpanSetter_ScalesWallSeparations()
        {
            FrameSpec s = TestSupport.CanonicalKingPost();
            double before = s.Span;
            Assert.True(before > 0);
            s.Span = before * 2.0;
            Assert.Equal(before * 2.0, s.Span, 10);                 // wall separations rescaled to the new span
        }

        [Fact]
        public void EnsureSill_RestoresTheMissingLeaf_Disabled()
        {
            FrameSpec s = TestSupport.CanonicalKingPost();
            BentSpec b = s.Bents[0];
            Timber sill = b.Find("Sill:SL");
            Assert.NotNull(sill);                                   // fresh KP catalog carries it
            b.Timbers.Remove(sill);                                 // simulate a pre-floor-systems save
            Assert.Null(b.Find("Sill:SL"));
            b.EnsureSill();
            sill = b.Find("Sill:SL");
            Assert.NotNull(sill);
            Assert.False(sill.Enabled);                             // self-heal seeds OFF
        }

        [Fact]
        public void EnsureSummer_RestoresTheCenterLineLeaf_Disabled()
        {
            FrameSpec s = TestSupport.CanonicalKingPost();
            WallSpec center = s.WallCenter();
            Assert.NotNull(center);                                 // KP wall lines carry a center (ridge) line
            Assert.True(center.Bays.Count > 0);
            BaySpec bay = center.Bays[0];
            Timber summer = bay.Find("Summer:S");
            Assert.NotNull(summer);
            bay.Timbers.Remove(summer);
            bay.EnsureSummer();
            summer = bay.Find("Summer:S");
            Assert.NotNull(summer);
            Assert.False(summer.Enabled);
        }

        [Fact]
        public void NormalizeBraces_SplitsALegacySingleBraceIntoBayEnds()
        {
            FrameSpec s = TestSupport.CanonicalKingPost();
            WallSpec eave = s.WallLeft();
            BaySpec bay = eave.Bays[0];
            // Simulate the legacy single-slot brace: drop the L/R pair, add one ":S".
            bay.Timbers.RemoveAll(t => t.Role == "EaveBrace");
            bay.Timbers.Add(new Timber
            {
                Role = "EaveBrace", Designation = "S", Label = "Eave Brace", Enabled = true,
                Size = new MemberSize { W = 4, D = 6, Foot = 30, Head = 30 }
            });
            bay.NormalizeBraces();
            Assert.Null(bay.Find("EaveBrace:S"));
            Timber left = bay.Find("EaveBrace:L");
            Timber right = bay.Find("EaveBrace:R");
            Assert.NotNull(left);
            Assert.NotNull(right);
            Assert.True(left.Enabled);
            Assert.Equal(30.0, left.Size.Foot);
            Assert.Equal(30.0, right.Size.Head);
        }
    }
}
