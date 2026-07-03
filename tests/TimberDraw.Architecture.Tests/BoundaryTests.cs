using System.Text.RegularExpressions;
using Xunit;

namespace TimberDraw.Architecture.Tests;

// Guards the generator <-> managed-editor boundary that lives in the FOLDER layout, not in
// namespaces (everything in TimberDraw is flat `namespace TimberDraw`). A reflection test keys
// on namespace/assembly and so is blind to it; instead we scan the .cs files of each core's
// folders as TEXT and fail if one core names the other's types. Crude (a stray comment can
// trip it, the token lists are hand-maintained) but it goes red the moment a generator file
// imports an editor type, or the editor learns the recipe exists.
public class BoundaryTests
{
    // ---- The hand-maintained lists. THIS is the part you edit as the cores grow. ----
    //
    // These lists ARE the coverage: when you add a generator folder or a recipe/editor type,
    // add it here, or the guard silently stops covering it. Token entries are LITERAL
    // identifiers -- they are whole-word matched and Regex.Escaped (see Scan), so an entry with
    // a '.' or '(' stays a harmless literal and never over-matches or throws.

    // Generator (parametric / FrameSpec recipe + bent geometry).
    private static readonly string[] GeneratorFolders =
        { "Frame", "Bent", "Bay", "Kpost", "Qpost", "Hbeam", "KpostTruss", "QpostTruss", "Network" };

    // Managed-timber editor.
    private const string EditorFolder = "Managed";

    // The ONE sanctioned seam: the generator emits managed timbers at the freeze. These
    // generator-folder files are allowed to name editor types; everything else is one-directional.
    private static readonly HashSet<string> BridgeExemptions =
        new(StringComparer.OrdinalIgnoreCase) { "ManagedFrameEmitter.cs", "FrameGrid.cs" };

    // Editor types a generator file must not name (outside the bridge).
    private static readonly string[] EditorTokens =
    {
        "ManagedTimber", "ManagedBrace", "ManagedSection",
        "PlaceJig", "SpanJig", "ScarfJig", "JigGeometry", "ManagedTransformOverrule",
    };

    // Recipe / generator-only types the editor must not name.
    //
    // RULE: forbid only the RECIPE. Do NOT add shared infrastructure the editor legitimately
    // uses -- Module1, JointFactory, TimberFactory, the Joints/ generators, or FrameGrid (the
    // address layer). The boundary is "the editor must not know the recipe exists," not "the
    // editor must not share geometry primitives."
    private static readonly string[] GeneratorTokens =
    {
        "FrameSpec", "FrameGraph", "FrameEdge", "FrameNode",
        "KingPostBentGraph", "QueenPostBentGraph", "HammerBeamBentGraph", "TrussBentGraph",
        "KPBent", "QPBent", "HBBent", "KPTruss", "QPTruss",
        "BentNetwork", "NetworkManager",
        "FrameRegistry", "FrameRecord", "KPBentParams",
        // the cross-bent pending-mortise queues (generation-time scaffolding)
        "PendingLeftPostMortises", "PendingRightPostMortises",
        "PendingLeftRafterMortises", "PendingRightRafterMortises", "PendingKPostMortises",
    };

    [Fact]
    public void EditorDoesNotReferenceGenerator()
    {
        var violations = Scan(new[] { EditorFolder }, GeneratorTokens, exemptFiles: null);
        Assert.True(violations.Count == 0,
            "Editor code names generator/recipe types (the editor must not know the recipe exists):\n  "
            + string.Join("\n  ", violations));
    }

    [Fact]
    public void GeneratorDoesNotReferenceEditor()
    {
        var violations = Scan(GeneratorFolders, EditorTokens, exemptFiles: BridgeExemptions);
        Assert.True(violations.Count == 0,
            "Generator code names editor types outside the sanctioned freeze bridge:\n  "
            + string.Join("\n  ", violations));
    }

    // For every .cs under each folder (minus exempt filenames), report "<file>: <matched text>"
    // for any forbidden token found. Tokens are Regex.Escaped and whole-word matched, so each is
    // a literal that can't over-match or throw. Fails loud if any folder yields zero .cs files
    // (empty/renamed/emptied folder) -- a guard that silently scans nothing is worse than none.
    private static List<string> Scan(string[] folders, string[] tokens, HashSet<string>? exemptFiles)
    {
        string root = SourceRoot();
        var patterns = tokens
            .Select(t => new Regex($@"\b{Regex.Escape(t)}\b", RegexOptions.Compiled))
            .ToArray();
        var violations = new List<string>();

        foreach (string folder in folders)
        {
            string dir = Path.Combine(root, folder);
            int scanned = 0;
            foreach (string file in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
            {
                scanned++;
                if (exemptFiles != null && exemptFiles.Contains(Path.GetFileName(file))) continue;
                string text = File.ReadAllText(file);
                string rel = Path.GetRelativePath(root, file);
                foreach (Regex rx in patterns)
                {
                    Match m = rx.Match(text);
                    if (m.Success) violations.Add($"{rel}: {m.Value}");
                }
            }
            Assert.True(scanned > 0,
                $"Boundary scan read 0 .cs files under '{folder}' -- it is empty, was renamed, or "
                + "code moved out of it. Update the folder/token lists (a guard that silently scans "
                + "nothing is worse than none).");
        }
        return violations;
    }

    // Walk up from the test binary until we find the TimberDraw source dir (the one holding
    // Frame/FrameSpec.cs). Checks each ancestor itself and a TimberDraw child, so it works from
    // any clone folder name (repo root IS the source dir) and from a parent holding a TimberDraw
    // folder. Robust to wherever bin/<cfg>/<tfm> lands.
    private static string SourceRoot()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
        {
            if (File.Exists(Path.Combine(d.FullName, "Frame", "FrameSpec.cs")))
                return d.FullName;
            string candidate = Path.Combine(d.FullName, "TimberDraw");
            if (File.Exists(Path.Combine(candidate, "Frame", "FrameSpec.cs")))
                return candidate;
        }
        throw new DirectoryNotFoundException(
            "Could not locate the TimberDraw source dir (Frame/FrameSpec.cs) above "
            + AppContext.BaseDirectory);
    }
}
