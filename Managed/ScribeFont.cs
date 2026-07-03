using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.Geometry;

namespace TimberDraw
{
    // Single-stroke ("stick") lettering for laser-burned dimension text. The .tsj schema has no text
    // entity, so characters become polyline strokes the head can burn directly.
    //
    // GLYPHS: the public-domain Hershey Simplex Roman vector font ("rowmans" / Occidental Roman
    // Simplex) -- the genuine single-stroke drafting font AutoCAD's romans.shx derives from, the
    // industry standard for engraving/plotter text. Strokes were decoded from the Hershey .jhf data
    // (value = char - 'R'; " R" = pen-up) and normalized ONCE into the em box below. Charset: digits,
    // '-', '/', '.', '"' (inch tick), the degree sign (procedural -- Hershey has no ASCII degree),
    // space, and A-Z (letters carried but unwired -- ready for identity / mate labels).
    //
    // Glyphs live in a 1.0-tall em box, x right, Y DOWN (face coordinates: v grows toward the framer,
    // matching the SVG preview); caps/digits span ~0.05..0.95, baseline near y = 0.95 (a few glyphs
    // -- '/', 'J', 'Q' tails -- run slightly past the box, as in the source font). Each glyph =
    // strokes (flat x,y pairs) + advance width.
    public static class ScribeFont
    {
        // ---- formatting -------------------------------------------------------------------------

        // Trade fractions to the nearest 1/32 (reduced), decimal 2dp fallback for non-clean values.
        public static string FormatInches(double v)
        {
            v = Math.Abs(v);
            long n32 = (long)Math.Round(v * 32.0);
            if (Math.Abs(v - n32 / 32.0) > 1.0 / 64.0)
                return v.ToString("0.00") + "\"";
            if (n32 == 0) return "0\"";
            long whole = n32 / 32, num = n32 % 32;
            if (num == 0) return whole + "\"";
            long g = Gcd(num, 32), den = 32 / g;
            num /= g;
            return (whole > 0 ? whole + "-" : "") + num + "/" + den + "\"";
        }

        public static string FormatDegrees(double deg) => deg.ToString("0.00") + "\u00B0";

        private static long Gcd(long a, long b) { while (b != 0) { long t = a % b; a = b; b = t; } return a; }

        // ---- typesetting ------------------------------------------------------------------------

        // Typeset `text` at `height` (em height, inches), CENTERED on `center` in face coords.
        // Returns one polyline (open unless the glyph stroke closes on itself) per stroke.
        public static List<List<Point2d>> Layout(string text, double height, Point2d center)
        {
            var strokes = new List<List<Point2d>>();
            double width = Width(text, height);
            double penX = center.X - width / 2.0;
            double topY = center.Y - height / 2.0;
            foreach (char c in text)
            {
                Glyph g = Get(c);
                foreach (double[] s in g.Strokes)
                {
                    var pl = new List<Point2d>(s.Length / 2);
                    for (int i = 0; i + 1 < s.Length; i += 2)
                        pl.Add(new Point2d(penX + s[i] * height, topY + s[i + 1] * height));
                    strokes.Add(pl);
                }
                penX += g.Adv * height;
            }
            return strokes;
        }

        public static double Width(string text, double height)
        {
            double w = 0;
            foreach (char c in text) w += Get(c).Adv;
            return w * height;
        }

        // ---- glyphs -------------------------------------------------------------------------------

        private struct Glyph { public double Adv; public double[][] Strokes; }

        private static Glyph Get(char c) =>
            Glyphs.TryGetValue(c, out Glyph g) ? g : Glyphs['-'];   // unknown char reads as a dash

        private static double[] S(params double[] xy) => xy;

        private static readonly Dictionary<char, Glyph> Glyphs = new Dictionary<char, Glyph>
        {
            ['0'] = new Glyph { Adv = 0.857, Strokes = new[] {
                S(0.386,0.05, 0.257,0.093, 0.171,0.221, 0.129,0.436, 0.129,0.564, 0.171,0.779, 0.257,0.907, 0.386,0.95, 0.471,0.95, 0.6,0.907, 0.686,0.779, 0.729,0.564, 0.729,0.436, 0.686,0.221, 0.6,0.093, 0.471,0.05, 0.386,0.05) } },
            ['1'] = new Glyph { Adv = 0.857, Strokes = new[] {
                S(0.257,0.221, 0.343,0.179, 0.471,0.05, 0.471,0.95) } },
            ['2'] = new Glyph { Adv = 0.857, Strokes = new[] {
                S(0.171,0.264, 0.171,0.221, 0.214,0.136, 0.257,0.093, 0.343,0.05, 0.514,0.05, 0.6,0.093, 0.643,0.136, 0.686,0.221, 0.686,0.307, 0.643,0.393, 0.557,0.521, 0.129,0.95, 0.729,0.95) } },
            ['3'] = new Glyph { Adv = 0.857, Strokes = new[] {
                S(0.214,0.05, 0.686,0.05, 0.429,0.393, 0.557,0.393, 0.643,0.436, 0.686,0.479, 0.729,0.607, 0.729,0.693, 0.686,0.821, 0.6,0.907, 0.471,0.95, 0.343,0.95, 0.214,0.907, 0.171,0.864, 0.129,0.779) } },
            ['4'] = new Glyph { Adv = 0.857, Strokes = new[] {
                S(0.557,0.05, 0.129,0.65, 0.771,0.65),
                S(0.557,0.05, 0.557,0.95) } },
            ['5'] = new Glyph { Adv = 0.857, Strokes = new[] {
                S(0.643,0.05, 0.214,0.05, 0.171,0.436, 0.214,0.393, 0.343,0.35, 0.471,0.35, 0.6,0.393, 0.686,0.479, 0.729,0.607, 0.729,0.693, 0.686,0.821, 0.6,0.907, 0.471,0.95, 0.343,0.95, 0.214,0.907, 0.171,0.864, 0.129,0.779) } },
            ['6'] = new Glyph { Adv = 0.857, Strokes = new[] {
                S(0.686,0.179, 0.643,0.093, 0.514,0.05, 0.429,0.05, 0.3,0.093, 0.214,0.221, 0.171,0.436, 0.171,0.65, 0.214,0.821, 0.3,0.907, 0.429,0.95, 0.471,0.95, 0.6,0.907, 0.686,0.821, 0.729,0.693, 0.729,0.65, 0.686,0.521, 0.6,0.436, 0.471,0.393, 0.429,0.393, 0.3,0.436, 0.214,0.521, 0.171,0.65) } },
            ['7'] = new Glyph { Adv = 0.857, Strokes = new[] {
                S(0.729,0.05, 0.3,0.95),
                S(0.129,0.05, 0.729,0.05) } },
            ['8'] = new Glyph { Adv = 0.857, Strokes = new[] {
                S(0.343,0.05, 0.214,0.093, 0.171,0.179, 0.171,0.264, 0.214,0.35, 0.3,0.393, 0.471,0.436, 0.6,0.479, 0.686,0.564, 0.729,0.65, 0.729,0.779, 0.686,0.864, 0.643,0.907, 0.514,0.95, 0.343,0.95, 0.214,0.907, 0.171,0.864, 0.129,0.779, 0.129,0.65, 0.171,0.564, 0.257,0.479, 0.386,0.436, 0.557,0.393, 0.643,0.35, 0.686,0.264, 0.686,0.179, 0.643,0.093, 0.514,0.05, 0.343,0.05) } },
            ['9'] = new Glyph { Adv = 0.857, Strokes = new[] {
                S(0.686,0.35, 0.643,0.479, 0.557,0.564, 0.429,0.607, 0.386,0.607, 0.257,0.564, 0.171,0.479, 0.129,0.35, 0.129,0.307, 0.171,0.179, 0.257,0.093, 0.386,0.05, 0.429,0.05, 0.557,0.093, 0.643,0.179, 0.686,0.35, 0.686,0.564, 0.643,0.779, 0.557,0.907, 0.429,0.95, 0.343,0.95, 0.214,0.907, 0.171,0.821) } },
            ['-'] = new Glyph { Adv = 1.114, Strokes = new[] {
                S(0.171,0.564, 0.943,0.564) } },
            ['/'] = new Glyph { Adv = 0.943, Strokes = new[] {
                S(0.857,-0.121, 0.086,1.25) } },
            ['.'] = new Glyph { Adv = 0.429, Strokes = new[] {
                S(0.214,0.864, 0.171,0.907, 0.214,0.95, 0.257,0.907, 0.214,0.864) } },
            ['\"'] = new Glyph { Adv = 0.686, Strokes = new[] {
                S(0.171,0.05, 0.171,0.35),
                S(0.514,0.05, 0.514,0.35) } },
            ['\u00B0'] = new Glyph { Adv = 0.60, Strokes = new[] {   // degree sign (procedural circle)
                S(0.38,0.16, 0.333,0.273, 0.22,0.32, 0.107,0.273, 0.06,0.16, 0.107,0.047, 0.22,0.00, 0.333,0.047, 0.38,0.16) } },
            ['A'] = new Glyph { Adv = 0.771, Strokes = new[] {
                S(0.386,0.05, 0.043,0.95),
                S(0.386,0.05, 0.729,0.95),
                S(0.171,0.65, 0.6,0.65) } },
            ['B'] = new Glyph { Adv = 0.9, Strokes = new[] {
                S(0.171,0.05, 0.171,0.95),
                S(0.171,0.05, 0.557,0.05, 0.686,0.093, 0.729,0.136, 0.771,0.221, 0.771,0.307, 0.729,0.393, 0.686,0.436, 0.557,0.479),
                S(0.171,0.479, 0.557,0.479, 0.686,0.521, 0.729,0.564, 0.771,0.65, 0.771,0.779, 0.729,0.864, 0.686,0.907, 0.557,0.95, 0.171,0.95) } },
            ['C'] = new Glyph { Adv = 0.9, Strokes = new[] {
                S(0.771,0.264, 0.729,0.179, 0.643,0.093, 0.557,0.05, 0.386,0.05, 0.3,0.093, 0.214,0.179, 0.171,0.264, 0.129,0.393, 0.129,0.607, 0.171,0.736, 0.214,0.821, 0.3,0.907, 0.386,0.95, 0.557,0.95, 0.643,0.907, 0.729,0.821, 0.771,0.736) } },
            ['D'] = new Glyph { Adv = 0.9, Strokes = new[] {
                S(0.171,0.05, 0.171,0.95),
                S(0.171,0.05, 0.471,0.05, 0.6,0.093, 0.686,0.179, 0.729,0.264, 0.771,0.393, 0.771,0.607, 0.729,0.736, 0.686,0.821, 0.6,0.907, 0.471,0.95, 0.171,0.95) } },
            ['E'] = new Glyph { Adv = 0.814, Strokes = new[] {
                S(0.171,0.05, 0.171,0.95),
                S(0.171,0.05, 0.729,0.05),
                S(0.171,0.479, 0.514,0.479),
                S(0.171,0.95, 0.729,0.95) } },
            ['F'] = new Glyph { Adv = 0.771, Strokes = new[] {
                S(0.171,0.05, 0.171,0.95),
                S(0.171,0.05, 0.729,0.05),
                S(0.171,0.479, 0.514,0.479) } },
            ['G'] = new Glyph { Adv = 0.9, Strokes = new[] {
                S(0.771,0.264, 0.729,0.179, 0.643,0.093, 0.557,0.05, 0.386,0.05, 0.3,0.093, 0.214,0.179, 0.171,0.264, 0.129,0.393, 0.129,0.607, 0.171,0.736, 0.214,0.821, 0.3,0.907, 0.386,0.95, 0.557,0.95, 0.643,0.907, 0.729,0.821, 0.771,0.736, 0.771,0.607),
                S(0.557,0.607, 0.771,0.607) } },
            ['H'] = new Glyph { Adv = 0.943, Strokes = new[] {
                S(0.171,0.05, 0.171,0.95),
                S(0.771,0.05, 0.771,0.95),
                S(0.171,0.479, 0.771,0.479) } },
            ['I'] = new Glyph { Adv = 0.343, Strokes = new[] {
                S(0.171,0.05, 0.171,0.95) } },
            ['J'] = new Glyph { Adv = 0.686, Strokes = new[] {
                S(0.514,0.05, 0.514,0.736, 0.471,0.864, 0.429,0.907, 0.343,0.95, 0.257,0.95, 0.171,0.907, 0.129,0.864, 0.086,0.736, 0.086,0.65) } },
            ['K'] = new Glyph { Adv = 0.9, Strokes = new[] {
                S(0.171,0.05, 0.171,0.95),
                S(0.771,0.05, 0.171,0.65),
                S(0.386,0.436, 0.771,0.95) } },
            ['L'] = new Glyph { Adv = 0.729, Strokes = new[] {
                S(0.171,0.05, 0.171,0.95),
                S(0.171,0.95, 0.686,0.95) } },
            ['M'] = new Glyph { Adv = 1.029, Strokes = new[] {
                S(0.171,0.05, 0.171,0.95),
                S(0.171,0.05, 0.514,0.95),
                S(0.857,0.05, 0.514,0.95),
                S(0.857,0.05, 0.857,0.95) } },
            ['N'] = new Glyph { Adv = 0.943, Strokes = new[] {
                S(0.171,0.05, 0.171,0.95),
                S(0.171,0.05, 0.771,0.95),
                S(0.771,0.05, 0.771,0.95) } },
            ['O'] = new Glyph { Adv = 0.943, Strokes = new[] {
                S(0.386,0.05, 0.3,0.093, 0.214,0.179, 0.171,0.264, 0.129,0.393, 0.129,0.607, 0.171,0.736, 0.214,0.821, 0.3,0.907, 0.386,0.95, 0.557,0.95, 0.643,0.907, 0.729,0.821, 0.771,0.736, 0.814,0.607, 0.814,0.393, 0.771,0.264, 0.729,0.179, 0.643,0.093, 0.557,0.05, 0.386,0.05) } },
            ['P'] = new Glyph { Adv = 0.9, Strokes = new[] {
                S(0.171,0.05, 0.171,0.95),
                S(0.171,0.05, 0.557,0.05, 0.686,0.093, 0.729,0.136, 0.771,0.221, 0.771,0.35, 0.729,0.436, 0.686,0.479, 0.557,0.521, 0.171,0.521) } },
            ['Q'] = new Glyph { Adv = 0.943, Strokes = new[] {
                S(0.386,0.05, 0.3,0.093, 0.214,0.179, 0.171,0.264, 0.129,0.393, 0.129,0.607, 0.171,0.736, 0.214,0.821, 0.3,0.907, 0.386,0.95, 0.557,0.95, 0.643,0.907, 0.729,0.821, 0.771,0.736, 0.814,0.607, 0.814,0.393, 0.771,0.264, 0.729,0.179, 0.643,0.093, 0.557,0.05, 0.386,0.05),
                S(0.514,0.779, 0.771,1.036) } },
            ['R'] = new Glyph { Adv = 0.9, Strokes = new[] {
                S(0.171,0.05, 0.171,0.95),
                S(0.171,0.05, 0.557,0.05, 0.686,0.093, 0.729,0.136, 0.771,0.221, 0.771,0.307, 0.729,0.393, 0.686,0.436, 0.557,0.479, 0.171,0.479),
                S(0.471,0.479, 0.771,0.95) } },
            ['S'] = new Glyph { Adv = 0.857, Strokes = new[] {
                S(0.729,0.179, 0.643,0.093, 0.514,0.05, 0.343,0.05, 0.214,0.093, 0.129,0.179, 0.129,0.264, 0.171,0.35, 0.214,0.393, 0.3,0.436, 0.557,0.521, 0.643,0.564, 0.686,0.607, 0.729,0.693, 0.729,0.821, 0.643,0.907, 0.514,0.95, 0.343,0.95, 0.214,0.907, 0.129,0.821) } },
            ['T'] = new Glyph { Adv = 0.686, Strokes = new[] {
                S(0.343,0.05, 0.343,0.95),
                S(0.043,0.05, 0.643,0.05) } },
            ['U'] = new Glyph { Adv = 0.943, Strokes = new[] {
                S(0.171,0.05, 0.171,0.693, 0.214,0.821, 0.3,0.907, 0.429,0.95, 0.514,0.95, 0.643,0.907, 0.729,0.821, 0.771,0.693, 0.771,0.05) } },
            ['V'] = new Glyph { Adv = 0.771, Strokes = new[] {
                S(0.043,0.05, 0.386,0.95),
                S(0.729,0.05, 0.386,0.95) } },
            ['W'] = new Glyph { Adv = 1.029, Strokes = new[] {
                S(0.086,0.05, 0.3,0.95),
                S(0.514,0.05, 0.3,0.95),
                S(0.514,0.05, 0.729,0.95),
                S(0.943,0.05, 0.729,0.95) } },
            ['X'] = new Glyph { Adv = 0.857, Strokes = new[] {
                S(0.129,0.05, 0.729,0.95),
                S(0.729,0.05, 0.129,0.95) } },
            ['Y'] = new Glyph { Adv = 0.771, Strokes = new[] {
                S(0.043,0.05, 0.386,0.479, 0.386,0.95),
                S(0.729,0.05, 0.386,0.479) } },
            ['Z'] = new Glyph { Adv = 0.857, Strokes = new[] {
                S(0.729,0.05, 0.129,0.95),
                S(0.129,0.05, 0.729,0.05),
                S(0.129,0.95, 0.729,0.95) } },
            [' '] = new Glyph { Adv = 0.686, Strokes = new double[0][] },
        };
    }
}
