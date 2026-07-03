using System;
using System.ComponentModel;
using System.Globalization;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;

namespace TimberDraw
{
    // Parses AutoCAD-style architectural / engineering distance input into decimal inches, so the
    // property grid accepts "1'2-1/2"" (= 14.5) as well as plain "14.5". Inches throughout; this does
    // NOT consult the drawing UNITS sysvar -- it just understands feet ('), inches ("), the
    // foot/inch separators (dash or space), and fractions (a/b).
    public static class UnitInput
    {
        public static double ParseDistance(string s)
        {
            if (!TryParseDistance(s, out double v))
                throw new FormatException("Cannot parse distance: '" + s + "'. Try 14.5 or 1'2-1/2\".");
            return v;
        }

        public static bool TryParseDistance(string s, out double inches)
        {
            inches = 0.0;
            if (string.IsNullOrWhiteSpace(s)) return false;
            s = s.Trim().Replace("\"", "").Trim();   // drop inch marks
            if (s.Length == 0) return false;

            bool neg = false;
            if (s[0] == '-') { neg = true; s = s.Substring(1).Trim(); }   // leading sign only

            double feet = 0.0;
            string inchPart = s;
            int fi = s.IndexOf('\'');
            if (fi >= 0)
            {
                string feetStr = s.Substring(0, fi).Trim();
                if (feetStr.Length > 0 &&
                    !double.TryParse(feetStr, NumberStyles.Any, CultureInfo.InvariantCulture, out feet))
                    return false;
                inchPart = s.Substring(fi + 1).Trim();
            }

            // A leading dash/space after the foot mark ("1'-2 1/2") is a separator, not a sign.
            inchPart = inchPart.TrimStart('-', ' ');
            double inch = 0.0;
            if (inchPart.Length > 0)
            {
                string[] toks = inchPart.Replace('-', ' ')
                    .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (toks.Length == 1)
                {
                    if (!ParseInchToken(toks[0], out inch)) return false;
                }
                else if (toks.Length == 2)   // whole + fraction
                {
                    if (!double.TryParse(toks[0], NumberStyles.Any, CultureInfo.InvariantCulture, out double whole)) return false;
                    if (!ParseFraction(toks[1], out double frac)) return false;
                    inch = whole + frac;
                }
                else return false;
            }

            inches = feet * 12.0 + inch;
            if (neg) inches = -inches;
            return true;
        }

        private static bool ParseInchToken(string t, out double v)
        {
            if (t.IndexOf('/') >= 0) return ParseFraction(t, out v);
            return double.TryParse(t, NumberStyles.Any, CultureInfo.InvariantCulture, out v);
        }

        private static bool ParseFraction(string t, out double v)
        {
            v = 0.0;
            int sl = t.IndexOf('/');
            if (sl <= 0 || sl >= t.Length - 1) return false;
            if (!double.TryParse(t.Substring(0, sl), NumberStyles.Any, CultureInfo.InvariantCulture, out double num)) return false;
            if (!double.TryParse(t.Substring(sl + 1), NumberStyles.Any, CultureInfo.InvariantCulture, out double den) || den == 0.0) return false;
            v = num / den;
            return true;
        }
    }

    // A double TypeConverter for distance fields. IN: accepts architectural input (via UnitInput).
    // OUT: formats with AutoCAD's own converter honoring the drawing's units (LUNITS) + precision
    // (LUPREC), so the cell matches the command line and follows a UNITS change. Applied ONLY to
    // distance fields by the FrameTree property wrappers.
    public class DistanceConverter : DoubleConverter
    {
        public override object ConvertFrom(ITypeDescriptorContext context, CultureInfo culture, object value)
        {
            if (value is string s)
            {
                if (UnitInput.TryParseDistance(s, out double d)) return d;
                throw new FormatException("Invalid distance: '" + s + "'. Try 14.5 or 1'2-1/2\".");
            }
            return base.ConvertFrom(context, culture, value);
        }

        public override object ConvertTo(ITypeDescriptorContext context, CultureInfo culture, object value, Type destinationType)
        {
            if (destinationType == typeof(string) && value is double d)
            {
                // DistanceUnitFormat.Current = honor LUNITS; precision -1 = honor LUPREC. Needs an
                // active document, so fall back to the default (decimal) at design time / if it throws.
                try { return Converter.DistanceToString(d, DistanceUnitFormat.Current, -1); }
                catch { /* fall through to base */ }
            }
            return base.ConvertTo(context, culture, value, destinationType);
        }
    }
}
