using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Runtime;

[assembly: CommandClass(typeof(TimberDraw.DiagCommands))]

namespace TimberDraw
{
    // Session diagnostics for MUST-NOT-VANISH failures: geometry features silently dropped by
    // the solid builder, and persistence reads/writes that silently fall back. Shared
    // infrastructure (like Module1) callable from the generator and editor folders alike --
    // deliberately in neither boundary token list. LOGGING ONLY: every instrumented catch keeps
    // its existing fallback behavior exactly; Warn adds a command-line echo + a ring-buffer
    // entry that TDiag dumps on demand.
    public static class Diag
    {
        private const int Cap = 200;
        private static readonly object Gate = new object();

        private sealed class Entry
        {
            public string Stamp, Context, Detail;
            public int Count = 1;
        }

        private static readonly List<Entry> Ring = new List<Entry>();

        public static void Warn(string context, System.Exception ex)
            => Warn(context, ex == null ? "(no detail)" : ex.Message);

        public static void Warn(string context, string detail)
        {
            bool echo = true;
            lock (Gate)
            {
                // Collapse consecutive duplicates: the solid builder also runs per DRAG FRAME in
                // transient ghost previews, so one bad feature would otherwise flood the command
                // line and the ring.
                Entry last = Ring.Count > 0 ? Ring[Ring.Count - 1] : null;
                if (last != null && last.Context == context && last.Detail == detail)
                {
                    last.Count++;
                    echo = false;
                }
                else
                {
                    Ring.Add(new Entry
                    {
                        Stamp = DateTime.Now.ToString("HH:mm:ss"),
                        Context = context,
                        Detail = detail
                    });
                    if (Ring.Count > Cap) Ring.RemoveAt(0);
                }
            }
            if (!echo) return;
            try
            {
                // A logger must never throw. No active document (startup, palette teardown,
                // background thread) -> the ring still has it; TDiag surfaces it later.
                Application.DocumentManager.MdiActiveDocument?
                    .Editor.WriteMessage("\n[TD] " + context + ": " + detail);
            }
            catch { }
        }

        internal static string[] Snapshot()
        {
            lock (Gate)
            {
                var lines = new List<string>(Ring.Count);
                foreach (Entry e in Ring)
                    lines.Add(e.Stamp + "  " + e.Context + ": " + e.Detail
                              + (e.Count > 1 ? "  (x" + e.Count + ")" : ""));
                return lines.ToArray();
            }
        }
    }

    // TDiag -- dump the session's diagnostic ring (the TScribeProbe idiom: one line per entry
    // + a summary). Read-only; the ring is session-scoped and capped at 200 entries.
    public class DiagCommands
    {
        [CommandMethod("TDiag")]
        public static void DumpDiagnostics()
        {
            var ed = Application.DocumentManager.MdiActiveDocument?.Editor;
            if (ed == null) return;
            string[] lines = Diag.Snapshot();
            if (lines.Length == 0)
            {
                ed.WriteMessage("\nTDiag: no warnings this session.");
                return;
            }
            foreach (string l in lines) ed.WriteMessage("\n  " + l);
            ed.WriteMessage("\nTDiag: " + lines.Length + " warning(s) this session (ring cap 200).");
        }
    }
}
