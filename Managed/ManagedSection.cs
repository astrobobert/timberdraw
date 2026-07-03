namespace TimberDraw
{
    // Sticky "current section" set by the managed-timber palette (TPanel). When a section
    // is active, the managed placement verbs (TPlace / TSpan / TJoin) take Width / Depth /
    // Type from here instead of prompting at the command line -- so the user sets the section
    // once and places many. When no section is active (HasCurrent == false), the verbs prompt
    // exactly as they did before, so plain command-line use is unchanged.
    //
    // The palette sets this synchronously before firing a verb via SendStringToExecute, so the
    // value is in place by the time the queued command runs.
    public static class ManagedSection
    {
        public static bool HasCurrent;
        public static string Type;
        public static double Width;
        public static double Depth;

        public static void Set(string type, double width, double depth)
        {
            Type = string.IsNullOrWhiteSpace(type) ? "Timber" : type;
            Width = width;
            Depth = depth;
            HasCurrent = true;
        }

        public static void Clear() { HasCurrent = false; }
    }
}
