using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TimberDraw
{
    // Writes .tsj (TimberScribe Job) v2.0 files from the managed-timber scribe sheets built by
    // ScribeFaces. The schema is kept IDENTICAL to TimberTag\Export\TsjWriter.cs (the original
    // producer) so the Pi's tsj_parser.py consumes both without change: snake_case JSON, inches,
    // one file per side face (1-4), only Visible marks in the burn path, all marks in the SVG
    // preview (visible solid cyan, hidden dashed blue -- no green, colorblind-safe).
    public static class ScribeTsj
    {
        private static readonly JsonSerializerOptions JsonOpts = new JsonSerializerOptions
        {
            WriteIndented          = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNamingPolicy   = JsonNamingPolicy.SnakeCaseLower
        };

        // Write one face job. `fileStem` is the sanitized timber id (collision handling is the
        // caller's job). Returns the full path written.
        public static string Write(ScribeFaces.Sheet sheet, ScribeFaces.Face face,
                                   string folder, string fileStem)
        {
            var job  = BuildJob(sheet, face);
            var json = JsonSerializer.Serialize(job, JsonOpts);
            var path = Path.Combine(folder, fileStem + "_face" + face.Number + ".tsj");
            // No BOM: the Pi parser json.load()s with plain utf-8 and rejects a BOM'd file.
            // (net48 Encoding.UTF8 emits one; TimberTag's TsjWriter has the same latent bug.)
            File.WriteAllText(path, json, new UTF8Encoding(false));
            return path;
        }

        public static string Sanitise(string id)
        {
            char[] bad = Path.GetInvalidFileNameChars();
            string s = string.Concat(id.Select(c => bad.Contains(c) || c == ' ' ? '_' : c));
            return string.IsNullOrWhiteSpace(s.Trim('_')) ? "timber" : s;
        }

        // ---- document ------------------------------------------------------------------------------

        private static TsjDocument BuildJob(ScribeFaces.Sheet s, ScribeFaces.Face face) =>
            new TsjDocument
            {
                TsjVersion = "2.0",
                Generated  = DateTime.UtcNow.ToString("o"),
                Timber     = new TsjTimber
                {
                    Id          = s.Id,
                    Description = string.IsNullOrEmpty(s.Description) ? null : s.Description,
                    LengthIn    = face.LengthIn,
                    WidthIn     = face.WidthIn,
                    HeightIn    = face.ThickIn
                },
                Face = new TsjFace
                {
                    Number   = face.Number,
                    LengthIn = face.LengthIn,
                    WidthIn  = face.WidthIn,
                    Origin   = "datum-edge/anchor-end (upper-left)",
                    XAxis    = "along timber length, anchor end = 0",
                    YAxis    = "toward framer, datum edge = 0"
                },
                Settings   = DefaultSettings(),
                Entities   = face.Marks.Where(m => m.Visible).Select(ToEntity).ToList(),
                PreviewSvg = BuildPreviewSvg(face)
            };

        private static TsjSettings DefaultSettings() => new TsjSettings
        {
            Scribe = new TsjScribeProfile
            {
                FeedInPerMin  = 32,
                LaserPowerPct = 70,
                Passes        = 1,
                Description   = "User-adjustable -- applies to all visible entities"
            },
            Travel = new TsjTravelProfile
            {
                FeedInPerMin  = 118,
                LaserPowerPct = 0,
                Description   = "Laser-off move between scribe segments"
            }
        };

        private static TsjEntity ToEntity(ScribeFaces.Mark m)
        {
            switch (m.Kind)
            {
                case ScribeFaces.MarkKind.Line:
                    return new TsjEntity
                    {
                        Type  = "line",
                        Start = Pt(m.Pts[0]),
                        End   = Pt(m.Pts[m.Pts.Count - 1])
                    };
                case ScribeFaces.MarkKind.Circle:
                    return new TsjEntity
                    {
                        Type         = "circle",
                        Center       = Pt(m.Center),
                        RadiusIn     = m.R,
                        ScribeCenter = true
                    };
                case ScribeFaces.MarkKind.Arc:
                    return new TsjEntity
                    {
                        Type          = "arc",
                        Center        = Pt(m.Center),
                        RadiusIn      = m.R,
                        StartAngleDeg = m.StartDeg,
                        EndAngleDeg   = m.EndDeg
                    };
                default:
                    bool closed = m.Pts.Count > 2 &&
                                  m.Pts[0].GetDistanceTo(m.Pts[m.Pts.Count - 1]) < 1e-6;
                    var pts = closed ? m.Pts.Take(m.Pts.Count - 1) : m.Pts;
                    return new TsjEntity
                    {
                        Type   = "polyline",
                        Closed = closed,
                        Points = pts.Select(Pt).ToList()
                    };
            }
        }

        private static double[] Pt(Autodesk.AutoCAD.Geometry.Point2d p) => new[] { p.X, p.Y };

        // ---- SVG preview (shown in the Pi face-selector UI) ------------------------------------------

        private static string BuildPreviewSvg(ScribeFaces.Face face)
        {
            double L = face.LengthIn, W = face.WidthIn;
            var sb = new StringBuilder();
            sb.Append($"<svg viewBox=\"0 0 {L:F3} {W:F3}\" xmlns=\"http://www.w3.org/2000/svg\" preserveAspectRatio=\"xMidYMid meet\">");
            sb.Append($"<rect width=\"{L:F3}\" height=\"{W:F3}\" fill=\"white\" stroke=\"#ccc\" stroke-width=\"0.05\"/>");

            foreach (var m in face.Marks)
            {
                // CUT lines (burned) -> black + heavier; stock OUTLINE (boundary, not burned) -> gray;
                // any remaining preview-only mark -> light dashed. Cut vs boundary is the distinction
                // the framer needs to read at a glance.
                string col, sw, dash;
                if (m.Visible)       { col = "#000000"; sw = "0.05"; dash = ""; }
                else if (m.Boundary) { col = "#9AA0A6"; sw = "0.03"; dash = ""; }
                else                 { col = "#378ADD"; sw = "0.03"; dash = " stroke-dasharray=\"0.3 0.15\""; }

                switch (m.Kind)
                {
                    case ScribeFaces.MarkKind.Line:
                        var a = m.Pts[0]; var b = m.Pts[m.Pts.Count - 1];
                        sb.Append($"<line x1=\"{a.X:F3}\" y1=\"{a.Y:F3}\" x2=\"{b.X:F3}\" y2=\"{b.Y:F3}\" " +
                                  $"stroke=\"{col}\" stroke-width=\"{sw}\"{dash}/>");
                        break;

                    case ScribeFaces.MarkKind.Circle:
                        sb.Append($"<circle cx=\"{m.Center.X:F3}\" cy=\"{m.Center.Y:F3}\" r=\"{m.R:F3}\" " +
                                  $"fill=\"none\" stroke=\"{col}\" stroke-width=\"{sw}\"{dash}/>");
                        double arm = Math.Min(m.R * 0.4, 0.25);
                        sb.Append($"<line x1=\"{m.Center.X - arm:F3}\" y1=\"{m.Center.Y:F3}\" " +
                                  $"x2=\"{m.Center.X + arm:F3}\" y2=\"{m.Center.Y:F3}\" stroke=\"{col}\" stroke-width=\"{sw}\"/>");
                        sb.Append($"<line x1=\"{m.Center.X:F3}\" y1=\"{m.Center.Y - arm:F3}\" " +
                                  $"x2=\"{m.Center.X:F3}\" y2=\"{m.Center.Y + arm:F3}\" stroke=\"{col}\" stroke-width=\"{sw}\"/>");
                        break;

                    case ScribeFaces.MarkKind.Arc:
                        sb.Append(ArcToSvgPath(m, col, sw, dash));
                        break;

                    default:
                        var pts = string.Join(" ", m.Pts.Select(p => $"{p.X:F3},{p.Y:F3}"));
                        sb.Append($"<polyline points=\"{pts}\" fill=\"none\" stroke=\"{col}\" " +
                                  $"stroke-width=\"{sw}\" stroke-linejoin=\"round\"{dash}/>");
                        break;
                }
            }

            // Anchor-end marker at the origin (upper-left)
            double mk = Math.Min(L, W) * 0.025;
            AppendAnchor(sb, mk);
            sb.Append("</svg>");
            return sb.ToString();
        }

        private static void AppendAnchor(StringBuilder sb, double mk)
        {
            sb.Append($"<circle cx=\"0\" cy=\"0\" r=\"{mk:F3}\" fill=\"none\" stroke=\"#BA7517\" stroke-width=\"0.04\"/>");
            sb.Append($"<line x1=\"{-mk:F3}\" y1=\"0\" x2=\"{mk:F3}\" y2=\"0\" stroke=\"#BA7517\" stroke-width=\"0.03\"/>");
            sb.Append($"<line x1=\"0\" y1=\"{-mk:F3}\" x2=\"0\" y2=\"{mk:F3}\" stroke=\"#BA7517\" stroke-width=\"0.03\"/>");
        }

        // Face coords are y-down (SVG-aligned); marks store CCW angles in that frame, so a
        // theta-increasing sweep renders with SVG sweep-flag 1.
        private static string ArcToSvgPath(ScribeFaces.Mark m, string col, string sw, string dash)
        {
            double sRad = m.StartDeg * Math.PI / 180.0;
            double eRad = m.EndDeg * Math.PI / 180.0;
            double x1 = m.Center.X + m.R * Math.Cos(sRad), y1 = m.Center.Y + m.R * Math.Sin(sRad);
            double x2 = m.Center.X + m.R * Math.Cos(eRad), y2 = m.Center.Y + m.R * Math.Sin(eRad);
            double sweep = m.EndDeg - m.StartDeg;
            if (sweep < 0) sweep += 360.0;
            int largeArc = sweep > 180.0 ? 1 : 0;
            return $"<path d=\"M {x1:F3} {y1:F3} A {m.R:F3} {m.R:F3} 0 {largeArc} 1 {x2:F3} {y2:F3}\" " +
                   $"fill=\"none\" stroke=\"{col}\" stroke-width=\"{sw}\"{dash}/>";
        }
    }

    // ---- JSON schema (byte-compatible with TimberTag's TsjWriter v2.0) -----------------------------

    public class TsjDocument
    {
        public string          TsjVersion { get; set; } = "2.0";
        public string          Generated  { get; set; } = "";
        public TsjTimber       Timber     { get; set; } = new TsjTimber();
        public TsjFace         Face       { get; set; } = new TsjFace();
        public TsjSettings     Settings   { get; set; } = new TsjSettings();
        public List<TsjEntity> Entities   { get; set; } = new List<TsjEntity>();
        public string          PreviewSvg { get; set; } = "";
    }

    public class TsjTimber
    {
        public string Id          { get; set; } = "";
        public string Description { get; set; }
        public double LengthIn    { get; set; }
        public double WidthIn     { get; set; }
        public double? HeightIn   { get; set; }
        public string Species     { get; set; }
    }

    public class TsjFace
    {
        public int    Number   { get; set; }
        public double LengthIn { get; set; }
        public double WidthIn  { get; set; }
        public string Origin   { get; set; } = "";
        public string XAxis    { get; set; } = "";
        public string YAxis    { get; set; } = "";
    }

    public class TsjSettings
    {
        public TsjScribeProfile Scribe { get; set; } = new TsjScribeProfile();
        public TsjTravelProfile Travel { get; set; } = new TsjTravelProfile();
    }

    public class TsjScribeProfile
    {
        public int    FeedInPerMin  { get; set; }
        public int    LaserPowerPct { get; set; }
        public int    Passes        { get; set; } = 1;
        public string Description   { get; set; }
    }

    public class TsjTravelProfile
    {
        public int    FeedInPerMin  { get; set; }
        public int    LaserPowerPct { get; set; }
        public string Description   { get; set; }
    }

    public class TsjEntity
    {
        public string         Type          { get; set; } = "";
        public double[]       Start         { get; set; }
        public double[]       End           { get; set; }
        public bool?          Closed        { get; set; }
        public List<double[]> Points        { get; set; }
        public double[]       Center        { get; set; }
        public double?        RadiusIn      { get; set; }
        public bool?          ScribeCenter  { get; set; }
        public double?        StartAngleDeg { get; set; }
        public double?        EndAngleDeg   { get; set; }
    }
}
