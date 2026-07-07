using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;

namespace TimberDraw
{
    // TimberScribe export: writes .tsj laser jobs straight from the managed model (no SOLPROF, no
    // TimberTag round-trip). One file per side face (RS1-RS4) that carries scribe marks; the datum
    // rule and burn set live in ScribeFaces, the schema in ScribeTsj.
    public partial class ManagedCommands
    {
        [CommandMethod("TScribe")]
        public static void ScribeExport()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;

            var pso = new PromptSelectionOptions
            {
                MessageForAdding = "\nSelect managed timbers to export for scribing"
            };
            var filter = new SelectionFilter(new[]
            {
                new TypedValue((int)DxfCode.Start, "3DSOLID")
            });
            PromptSelectionResult sel = ed.GetSelection(pso, filter);
            if (sel.Status != PromptStatus.OK) return;

            var picked = new HashSet<ObjectId>(sel.Value.GetObjectIds());
            RunExport(doc, t => picked.Contains(t.Id), clearFolder: false);
        }

        // Diagnostic: pick one managed timber -> print every Brep face with its per-RS label
        // verdict (front/back/buried/pocket, bevel/depth, thin/full-length gates). For chasing
        // missing or extra scribe labels with facts instead of guesses.
        [CommandMethod("TScribeProbe")]
        public static void ScribeProbe()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            PromptEntityOptions peo = new PromptEntityOptions("\nPick a managed timber to probe");
            peo.SetRejectMessage("\nMust be a 3D solid.");
            peo.AddAllowedClass(typeof(Solid3d), true);
            PromptEntityResult per = ed.GetEntity(peo);
            if (per.Status != PromptStatus.OK) return;

            List<ManagedTimber.ShopInfo> all = ManagedTimber.EnumerateForShop(db);
            ManagedTimber.ShopInfo t = all.FirstOrDefault(s => s.Id == per.ObjectId);
            if (t == null) { ed.WriteMessage("\nNot a managed timber."); return; }

            ScribeFaces.Sheet sheet = ScribeFaces.Frames(db, t, ScribeFaces.FrameCenter(all),
                                                         out ScribeFaces.FaceFrame[] ffs);
            if (sheet == null) { ed.WriteMessage("\nSolid geometry unavailable."); return; }
            ed.WriteMessage($"\nProbe {Label(t)} -- L={ffs[0].Overall:0.0}, faces RS1..RS4:");
            ScribeAnnotate.DebugProbe(ed, db, t.Id, ffs);
        }

        [CommandMethod("TScribeAll")]
        public static void ScribeExportAll()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            RunExport(doc, t => true, clearFolder: true);
        }

        private static void RunExport(Document doc, Func<ManagedTimber.ShopInfo, bool> take, bool clearFolder)
        {
            Editor ed = doc.Editor;
            Database db = doc.Database;

            List<ManagedTimber.ShopInfo> all = ManagedTimber.EnumerateForShop(db);
            if (all.Count == 0)
            {
                ed.WriteMessage("\nNo managed timbers found -- nothing to export.");
                return;
            }
            List<ManagedTimber.ShopInfo> chosen = all.Where(take).ToList();
            if (chosen.Count == 0)
            {
                ed.WriteMessage("\nNo managed timbers in the selection.");
                return;
            }

            // Repetitive families (braces, joists, commons, purlins) come in identical groups (one cut
            // mark, many instances). Export ONE set of drawings per UNIQUE geometry within a family --
            // the first stick stands for the group and carries its COUNT (in the stem + the echo).
            int repDup = 0;
            var repCount = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);  // sig -> instances
            var repSig = new Dictionary<ObjectId, string>();                               // representative -> sig
            var dedup = new List<ManagedTimber.ShopInfo>();
            foreach (var t in chosen)
            {
                if (RepFamily(t.Role) is string fam)
                {
                    string sig = fam + "|" + GeomSig(t);
                    if (repCount.TryGetValue(sig, out int c)) { repCount[sig] = c + 1; repDup++; continue; }
                    repCount[sig] = 1;
                    repSig[t.Id] = sig;
                }
                dedup.Add(t);
            }
            chosen = dedup;

            // The post outward-face rule judges from the WHOLE frame's plan centroid, selection or not.
            var center = ScribeFaces.FrameCenter(all);

            string folder = PromptFolder(ed, db);
            if (folder == null) return;
            try { Directory.CreateDirectory(folder); }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nCannot create output folder: {ex.Message}");
                return;
            }

            // A full export owns the folder: clear prior .tsj scribe files first so stale sticks
            // (renamed, removed, or deduped-away braces) don't linger. A selection export leaves the
            // rest of the set intact.
            if (clearFolder)
            {
                try
                {
                    foreach (string old in Directory.GetFiles(folder, "*_face?.tsj"))
                        File.Delete(old);
                }
                catch (System.Exception ex)
                {
                    ed.WriteMessage($"\nCould not clear old scribe files: {ex.Message}");
                }
            }

            int files = 0, skipped = 0;
            var usedStems = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            // SOLPROF needs a temp paper-space layout + viewport; everything is torn down in the
            // finally (temp layout, PV/PH layers, UCS, prior layout) even on a mid-run error.
            ScribeSolprof.State st = ScribeSolprof.Prepare(doc);
            try
            {
                foreach (var t in chosen)
                {
                    ScribeFaces.Sheet sheet = ScribeFaces.Frames(db, t, center, out ScribeFaces.FaceFrame[] ffs);
                    if (sheet == null)
                    {
                        ed.WriteMessage($"\n  {Label(t)}: solid geometry unavailable -- skipped.");
                        skipped++;
                        continue;
                    }

                    // The solid's real faces, once per timber (before SOLPROF), drive the surface-model
                    // depth/angle labels; labelability (the cut opens at that face) is judged per RS
                    // face, so pass the 4 FaceFrames (the probe needs each face's plane).
                    var solidFaces = ScribeAnnotate.BuildSolidFaces(db, t.Id, ffs);

                    foreach (ScribeFaces.FaceFrame ff in ffs)
                    {
                        var face = new ScribeFaces.Face
                        {
                            Number = ff.Number, LengthIn = ff.Overall,
                            WidthIn = ff.FaceW, ThickIn = 2.0 * ff.HalfN
                        };
                        face.Marks = ScribeSolprof.ProfileFace(doc, t.Id, ff, st);
                        // cut-to-length: explicit full-width end lines on every face, both ends
                        ScribeFaces.AddEndCutLines(ff, face.Marks);
                        face.VisibleCount = face.Marks.Count(m => m.Visible);
                        face.DimCount = ScribeAnnotate.Emit(ff, face.Marks, solidFaces);
                        face.DimCount += ScribeAnnotate.EmitBlindPegMarks(t.F, ff, face.Marks);
                        face.VisibleCount += face.DimCount;
                        sheet.Faces[ff.Number - 1] = face;
                    }

                    var withMarks = sheet.Faces.Where(fc => fc.VisibleCount > 0).ToList();
                    if (withMarks.Count == 0)
                    {
                        ed.WriteMessage($"\n  {Label(t)}: no scribe marks (plain stick) -- skipped.");
                        skipped++;
                        continue;
                    }

                    // Repetitive families get a readable shared stem carrying the group COUNT (a
                    // brace's group symbol sanitizes to nothing; a deduped joist's J-1-1 would mislead
                    // -- the file stands for the whole group: "cut this many"); other timbers key off
                    // their grid label. Collisions get a suffix.
                    int repN = repSig.TryGetValue(t.Id, out string sig) ? repCount[sig] : 1;
                    string stem = RepFamily(t.Role) is string repFam
                        ? ScribeTsj.Sanitise($"{repFam}_{Math.Round(t.F.W)}x{Math.Round(t.F.D)}"
                                             + (repN > 1 ? $"_x{repN}" : ""))
                        : ScribeTsj.Sanitise(sheet.Id);
                    if (usedStems.TryGetValue(stem, out int n)) { usedStems[stem] = n + 1; stem += "_" + (n + 1); }
                    else usedStems[stem] = 1;

                    foreach (var fc in withMarks)
                    {
                        ScribeTsj.Write(sheet, fc, folder, stem);
                        files++;
                    }
                    int dims = withMarks.Sum(fc => fc.DimCount);
                    ed.WriteMessage($"\n  {Label(t)}{(repN > 1 ? $" (x{repN} identical)" : "")}:" +
                                    $" faces {string.Join(",", withMarks.Select(fc => fc.Number))}" +
                                    $" ({withMarks.Sum(fc => fc.VisibleCount) - dims} marks + {dims} dims) -> {stem}_face*.tsj");
                }
            }
            finally
            {
                ScribeSolprof.Cleanup(doc, st);
            }

            ed.WriteMessage($"\nScribe export: {files} .tsj file(s) from {chosen.Count - skipped} timber(s)" +
                            (skipped > 0 ? $" ({skipped} skipped)" : "") +
                            (repDup > 0 ? $" ({repDup} identical repeated member(s) collapsed)" : "") +
                            $"\n  -> {folder}");
        }

        // The repetitive families: many identical sticks share one cut mark, so the export collapses
        // them. Role VARIANTS inside a family are deliberately merged (a bent brace and a bay brace of
        // the same size are the same cut) -- geometry is the reliable key, the family just keeps, say,
        // a brace from merging with a joist of the same dimensions.
        private static string RepFamily(string role)
        {
            if (string.IsNullOrEmpty(role)) return null;
            if (role.IndexOf("brace", StringComparison.OrdinalIgnoreCase) >= 0) return "Brace";
            if (role.IndexOf("joist", StringComparison.OrdinalIgnoreCase) >= 0) return "Joist";
            if (role.IndexOf("common", StringComparison.OrdinalIgnoreCase) >= 0) return "Common";
            if (role.IndexOf("purlin", StringComparison.OrdinalIgnoreCase) >= 0) return "Purlin";
            return null;
        }

        // Identity of a unique stick within a family: section + length to the nearest 1/2".
        private static string GeomSig(ManagedTimber.ShopInfo t) =>
            $"{Math.Round(t.F.W)}x{Math.Round(t.F.D)}x{Math.Round(t.F.L * 2.0) / 2.0}";

        // Output folder via a REMEMBERED folder browser (Robert's call: no command-line prompt).
        // First run defaults next to the drawing (or Documents for an unsaved one); afterward the
        // dialog opens at the last-used folder, so accepting is one click.
        private static string PromptFolder(Editor ed, Database db)
        {
            string def = Properties.Settings.Default.ScribeFolder;
            if (string.IsNullOrWhiteSpace(def) || !Directory.Exists(def))
            {
                string baseDir;
                try { baseDir = Path.GetDirectoryName(db.Filename); } catch { baseDir = null; }
                if (string.IsNullOrEmpty(baseDir))
                    baseDir = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                def = Path.Combine(baseDir, "Scribe");
            }

            using (var dlg = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Scribe output folder (the .tsj files land here)",
                SelectedPath = def,
                ShowNewFolderButton = true
            })
            {
                if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return null;
                string folder = dlg.SelectedPath;
                Properties.Settings.Default.ScribeFolder = folder;
                Properties.Settings.Default.Save();
                return folder;
            }
        }

        private static string Label(ManagedTimber.ShopInfo t)
        {
            if (!string.IsNullOrWhiteSpace(t.GridLabel)) return t.GridLabel;
            if (!string.IsNullOrWhiteSpace(t.Designation)) return t.Designation;
            return string.IsNullOrWhiteSpace(t.Role) ? t.Id.Handle.ToString() : t.Role;
        }
    }
}
