using System.Collections.Generic;

namespace TimberDraw
{
    // Plain-language tooltips for the Joints pane, mirroring GLOSSARY.md sections C (joint parts) + E (canonical
    // params). Keyed by the (now-uniform) param name and the element kind. Pure UI text -- ASCII only, no
    // geometry and no generator types. Keep in step with GLOSSARY.md (the single source of truth).
    internal static class JointGlossary
    {
        private static readonly Dictionary<string, string> Params = new Dictionary<string, string>
        {
            { "Seat",           "Let-in depth -- how far the housing / shoulder / seat recesses into the host." },
            { "ShoulderTop",    "Inset from the top face that forms the bearing step (depth axis); 0 = flush." },
            { "ShoulderBottom", "Inset from the bottom face that forms the bearing step (depth axis); 0 = flush." },
            { "ShoulderSide1",  "Housing inset from one side face that forms a bearing step (width axis); 0 = flush." },
            { "ShoulderSide2",  "Housing inset from the other side face that forms a bearing step (width axis); 0 = flush." },
            { "Thickness",      "Tenon width, absolute; 0 = full section width." },
            { "Offset",         "Lateral shift of the tenon across the width; 0 = centered, pushed to a face = barefaced." },
            { "Length",         "How far the tenon projects into the mortise (or the dovetail tongue past the housing)." },
            { "Heel",           "Birdsmouth heel: plumb-cut let-in inside the heel face (resists down-slope thrust)." },
            { "Count",          "Number of pegs, stacked across the tongue depth." },
            { "Diameter",       "Peg bore diameter (~3/4 to 1 in)." },
            { "Setback",        "Distance from the shoulder / floor to the peg, into the tongue." },
            { "Spacing",        "Center-to-center distance between stacked pegs." },
            { "Bore",           "Full = bore straight through; Blind = stop short of the far face." },
            { "BlindDepth",     "How far a blind bore stops past the tenon (Blind only)." },
            { "BlindFlip",      "Which face a blind bore enters from." },
            { "Width",          "Dovetail tongue width." },
            { "Depth",          "Dovetail housing band depth into the host's back." },
            { "Angle",          "Dovetail flare (taper) angle that resists pull-out." },
        };

        // Tooltip for a param by its (uniform) name; "" when unknown (no tooltip attached).
        public static string ParamTip(string name)
            => name != null && Params.TryGetValue(name, out string t) ? t : "";

        // Tooltip for an element header by its kind.
        public static string ElementTip(ElementKind kind)
        {
            switch (kind)
            {
                case ElementKind.Tenon:    return "The reduced tongue that seats in the mortise.";
                case ElementKind.Housing:  return "A full-section let-in recess the member end beds into for bearing.";
                case ElementKind.Shoulder: return "A bearing notch the member seats against.";
                case ElementKind.Dovetail: return "A flared drop-in end that locks against withdrawal.";
                case ElementKind.Pegs:     return "Pins through the joint (bore the host cheeks; field-bore the tongue).";
                default:                   return "";
            }
        }
    }
}
