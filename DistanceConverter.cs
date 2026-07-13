using System;
using System.ComponentModel;
using System.Globalization;
using Autodesk.AutoCAD.Runtime;   // Converter.DistanceToString + DistanceUnitFormat

namespace TimberDraw
{
    // A double TypeConverter for distance fields. IN: accepts architectural input (via UnitInput).
    // OUT: formats with AutoCAD's own converter honoring the drawing's units (LUNITS) + precision
    // (LUPREC), so the cell matches the command line and follows a UNITS change. Applied ONLY to
    // distance fields by the FrameTree property wrappers. Lives at the root (UI units adapter),
    // apart from the pure parser in Frame\UnitInput.cs.
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
