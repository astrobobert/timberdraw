using System.IO;
using Xunit;

namespace TimberDraw.DomainTests
{
    // Characterization: the .framespec on-disk format. ToJson -> FromJson -> ToJson must be
    // IDENTICAL (idempotent) -- any additive format change shows up here first. The golden
    // fixture pins the exact bytes the canonical spec serializes to; regenerate it DELIBERATELY
    // (delete the fixture, run the test once, commit the new file in the same commit as the
    // format change).
    public class FrameSpecStoreRoundTripTests
    {
        [Fact]
        public void RoundTrip_IsIdempotent_ForCanonicalSpec()
        {
            FrameSpec s = TestSupport.CanonicalKingPost();
            string once = FrameSpecStore.ToJson(s);
            string twice = FrameSpecStore.ToJson(FrameSpecStore.FromJson(once));
            Assert.Equal(once, twice);
        }

        [Fact]
        public void RoundTrip_IsIdempotent_WithFreeAssemblyBent()
        {
            FrameSpec s = TestSupport.CanonicalKingPost();
            BentSpec fa = s.InsertBent(s.Bents[1], before: false, sep: 60.0);
            Assert.NotNull(fa);                       // insert appends past the last bent
            Assert.Equal("Free Assembly", fa.BentType);
            string once = FrameSpecStore.ToJson(s);
            string twice = FrameSpecStore.ToJson(FrameSpecStore.FromJson(once));
            Assert.Equal(once, twice);
        }

        [Fact]
        public void CurrentFormat_CarriesSpecVersion2()
        {
            string json = FrameSpecStore.ToJson(TestSupport.CanonicalKingPost());
            Assert.StartsWith("{\"specVersion\":2,", json);
        }

        [Fact]
        public void GoldenFixture_MatchesCanonicalSerialization()
        {
            string path = TestSupport.FixturePath("current-kingpost.framespec");
            string json = FrameSpecStore.ToJson(TestSupport.CanonicalKingPost());
            if (!File.Exists(path))
            {
                File.WriteAllText(path, json);
                Assert.Fail("Golden fixture was missing -- generated at " + path + "; commit it and re-run.");
            }
            Assert.Equal(File.ReadAllText(path), json);
        }

        [Fact]
        public void GoldenFixture_RoundTripsToItself()
        {
            string text = TestSupport.ReadFixture("current-kingpost.framespec");
            Assert.Equal(text, FrameSpecStore.ToJson(FrameSpecStore.FromJson(text)));
        }
    }
}
