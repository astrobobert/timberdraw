using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.GraphicsInterface;

namespace TimberDraw
{
    // Live wire ghost for TJoin's knee brace, as a RETAINED TRANSIENT (replacing the short-lived
    // BraceJig -- a DrawJig's sampler only runs on drawing-input events, so palette edits didn't
    // show until the cursor re-entered the drawing; Robert's catch). The command holds a plain
    // GetPoint prompt while this ghost lives; the palette's ManagedBrace.Changed event calls
    // OnPaletteChanged, which re-solves and redraws IMMEDIATELY (transients repaint from any
    // thread-of-UI event, no drawing input needed). Click/Enter places, Flip swaps a Back/Front
    // side, Escape cancels -- all handled by the command loop. Wireframe look matches TSpan.
    public sealed class BraceGhost : System.IDisposable
    {
        private readonly ManagedTimber.TFace _fa, _fb;
        private readonly Point3d _bodyA, _bodyB;
        private readonly double _d, _w;
        private readonly bool _trackPalette;   // TJoin: live from ManagedBrace; Modify: prompted legs stay
        private double _foot, _head;
        private int _placeRaw;                 // palette/seed placement (0 Back / 1 Center / 2 Front)
        private bool _flip;                    // Flip keyword state, applied ON TOP of _placeRaw
        private ManagedTimber.TFrame _frame;
        private bool _solved;
        private readonly List<Line> _lines = new List<Line>();

        public double FootRun => _foot;
        public double HeadRun => _head;
        public int Placement => Effective(_placeRaw, _flip);
        public ManagedTimber.TFrame Frame => _frame;

        public BraceGhost(ManagedTimber.TFace fa, ManagedTimber.TFace fb, double depth, double width,
            double footRun, double headRun, int placement, Point3d bodyA, Point3d bodyB, bool trackPalette)
        {
            _fa = fa; _fb = fb; _bodyA = bodyA; _bodyB = bodyB; _d = depth; _w = width;
            _foot = footRun; _head = headRun; _placeRaw = placement; _trackPalette = trackPalette;
        }

        // Flip only swaps a Back/Front side; Center has no side. Kept SEPARATE from _placeRaw so a
        // palette re-read (which restores the palette's own value) doesn't silently undo a Flip.
        private static int Effective(int raw, bool flip) => flip && raw != 1 ? (raw == 0 ? 2 : 0) : raw;

        // Re-solve the frame from the current values and redraw. False = degenerate corner (the
        // previous ghost stays up, so the user keeps seeing the last good solve).
        public bool Solve()
        {
            if (!ManagedTimber.TryBraceFrame(_fa, _fb, _d, _w, _foot, _head, _bodyA, _bodyB,
                                             Effective(_placeRaw, _flip), out ManagedTimber.TFrame f))
                return false;
            _frame = f;
            _solved = true;
            Refresh();
            return true;
        }

        // The ManagedBrace.Changed handler: pull the palette's current values; on any change,
        // re-solve + redraw in place.
        public void OnPaletteChanged()
        {
            if (!_trackPalette || !ManagedBrace.HasCurrent) return;
            double nf = ManagedBrace.FootRun, nh = ManagedBrace.HeadRun;
            int np = ManagedBrace.Placement;
            if (nf == _foot && nh == _head && np == _placeRaw) return;
            _foot = nf; _head = nh; _placeRaw = np;
            Solve();
        }

        public void Flip() { _flip = !_flip; Solve(); }

        private void Refresh()
        {
            Erase();
            if (!_solved) return;
            ManagedTimber.TFace[] caps = ManagedTimber.Faces(_frame);
            TransientManager tm = TransientManager.CurrentTransientManager;
            foreach ((Point3d A, Point3d B) seg in
                     JigGeometry.MiteredWireSegments(caps[0], caps[1], _frame.X, _frame.Y))
            {
                var ln = new Line(seg.A, seg.B) { ColorIndex = 5 };   // blue, colour-blind safe
                tm.AddTransient(ln, TransientDrawingMode.DirectShortTerm, 128, new IntegerCollection());
                _lines.Add(ln);
            }
        }

        private void Erase()
        {
            TransientManager tm = TransientManager.CurrentTransientManager;
            foreach (Line ln in _lines)
            {
                tm.EraseTransient(ln, new IntegerCollection());
                ln.Dispose();
            }
            _lines.Clear();
        }

        public void Dispose() => Erase();
    }
}
