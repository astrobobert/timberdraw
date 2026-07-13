using System;
using System.IO;

namespace TimberDraw.DomainTests
{
    // Shared helpers: the canonical in-code spec (deterministic via the Commands stub) and the
    // path to the SOURCE fixtures folder (tests read committed fixtures; the golden test can
    // (re)establish its fixture there on first run).
    internal static class TestSupport
    {
        // Walk up from the test output dir to the project dir (the one holding this csproj).
        internal static string ProjectDir()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "TimberDraw.Domain.Tests.csproj")))
                dir = dir.Parent;
            if (dir == null) throw new InvalidOperationException("Could not locate TimberDraw.Domain.Tests.csproj above " + AppContext.BaseDirectory);
            return dir.FullName;
        }

        internal static string FixturePath(string name) => Path.Combine(ProjectDir(), "fixtures", name);

        internal static string ReadFixture(string name) => File.ReadAllText(FixturePath(name));

        // The canonical current-format spec: two KingPost bents 120" apart, wall lines synced.
        // Everything it seeds comes from the test stub's pinned KPBentParams, so the serialized
        // form is fully deterministic.
        internal static FrameSpec CanonicalKingPost()
        {
            var s = FrameSpec.NewFromSettings();
            s.FrameTag = "A";
            BentSpec b1 = s.AddBent();
            b1.BentType = "KingPost";
            b1.RebuildTimbers();
            BentSpec b2 = s.AddBent();
            b2.BentType = "KingPost";
            b2.RebuildTimbers();
            b2.Separation = 120.0;   // gap FROM bent 1 (bent 1 = 0, the datum)
            s.SyncWallRoles();
            s.SyncBays();
            return s;
        }
    }
}
