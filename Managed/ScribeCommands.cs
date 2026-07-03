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

            // Braces come in identical groups (one cut mark, many instances). Export ONE set of
            // drawings per UNIQUE brace (role + section + length + group symbol), not one per stick.
            int braceDup = 0;
            var braceSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var dedup = new List<ManagedTimber.ShopInfo>();
            foreach (var t in chosen)
            {
                if (IsBrace(t.Role) && !braceSeen.Add(BraceSig(t))) { braceDup++; continue; }
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
                    // depth/angle labels; visibility is judged per RS face, so pass the 4 RS normals.
                    var solidFaces = ScribeAnnotate.BuildSolidFaces(
                        db, t.Id, ffs.Select(fr => fr.N).ToArray());

                    foreach (ScribeFaces.FaceFrame ff in ffs)
                    {
                        var face = new ScribeFaces.Face
                        {
                            Number = ff.Number, LengthIn = ff.Overall,
                            WidthIn = ff.FaceW, ThickIn = 2.0 * ff.HalfN
                        };
                        face.Marks = ScribeSolprof.ProfileFace(doc, t.Id, ff, st);
                        face.VisibleCount = face.Marks.Count(m => m.Visible);
                        face.DimCount = ScribeAnnotate.Emit(ff, face.Marks, solidFaces);
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

                    // Braces get a readable shared stem (their group symbol sanitizes to nothing);
                    // other timbers key off their grid label. Collisions get a distinguishing suffix.
                    string stem = IsBrace(t.Role)
                        ? ScribeTsj.Sanitise($"Brace_{Math.Round(t.F.W)}x{Math.Round(t.F.D)}")
                        : ScribeTsj.Sanitise(sheet.Id);
                    if (usedStems.TryGetValue(stem, out int n)) { usedStems[stem] = n + 1; stem += "_" + (n + 1); }
                    else usedStems[stem] = 1;

                    foreach (var fc in withMarks)
                    {
                        ScribeTsj.Write(sheet, fc, folder, stem);
                        files++;
                    }
                    int dims = withMarks.Sum(fc => fc.DimCount);
                    ed.WriteMessage($"\n  {Label(t)}: faces {string.Join(",", withMarks.Select(fc => fc.Number))}" +
                                    $" ({withMarks.Sum(fc => fc.VisibleCount) - dims} marks + {dims} dims) -> {stem}_face*.tsj");
                }
            }
            finally
            {
                ScribeSolprof.Cleanup(doc, st);
            }

            ed.WriteMessage($"\nScribe export: {files} .tsj file(s) from {chosen.Count - skipped} timber(s)" +
                            (skipped > 0 ? $" ({skipped} skipped)" : "") +
                            (braceDup > 0 ? $" ({braceDup} duplicate brace instance(s) collapsed)" : "") +
                            $"\n  -> {folder}");
        }

        private static bool IsBrace(string role) =>
            !string.IsNullOrEmpty(role) && role.IndexOf("brace", StringComparison.OrdinalIgnoreCase) >= 0;

        // Identity of a UNIQUE brace: its GEOMETRY -- section + length (to the nearest 1/2"). Role and
        // the group symbol are deliberately ignored: a bent brace and a bay brace of the same size are
        // the same cut, and the symbol is sometimes blank, so geometry is the reliable key. Identical
        // braces collapse to one set of drawings.
        private static string BraceSig(ManagedTimber.ShopInfo t) =>
            $"{Math.Round(t.F.W)}x{Math.Round(t.F.D)}x{Math.Round(t.F.L * 2.0) / 2.0}";

        // Output folder, defaulting next to the drawing (or Documents for an unsaved one).
        private static string PromptFolder(Editor ed, Database db)
        {
            string baseDir;
            try { baseDir = Path.GetDirectoryName(db.Filename); } catch { baseDir = null; }
            if (string.IsNullOrEmpty(baseDir))
                baseDir = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            string def = Path.Combine(baseDir, "Scribe");

            var opts = new PromptStringOptions("\nOutput folder")
            {
                AllowSpaces = true,
                DefaultValue = def,
                UseDefaultValue = true
            };
            PromptResult r = ed.GetString(opts);
            if (r.Status != PromptStatus.OK) return null;
            return string.IsNullOrWhiteSpace(r.StringResult) ? def : r.StringResult.Trim();
        }

        private static string Label(ManagedTimber.ShopInfo t)
        {
            if (!string.IsNullOrWhiteSpace(t.GridLabel)) return t.GridLabel;
            if (!string.IsNullOrWhiteSpace(t.Designation)) return t.Designation;
            return string.IsNullOrWhiteSpace(t.Role) ? t.Id.Handle.ToString() : t.Role;
        }
    }
}
