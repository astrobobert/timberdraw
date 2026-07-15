using Autodesk.AutoCAD.Geometry;

namespace TimberDraw
{
    // Live SOLID preview for TJoin's knee brace (Robert's call 2026-07-15: no transient wireframes
    // -- previews are real db solids in the current visual style, via SolidGhost). ALL database
    // writes happen in command context: a palette edit fires ManagedBrace/ManagedSection.Changed,
    // whose handler queues SolidGhost's hidden nudge keyword into the pending prompt (a palette
    // handler holds no document lock); the command loop catches the keyword and calls
    // OnPaletteChanged here. Click/Enter places, Flip swaps a Back/Front side, Escape cancels.
    public sealed class BraceGhost : System.IDisposable
    {
        private readonly ManagedTimber.TFace _fa, _fb;
        private readonly Point3d _bodyA, _bodyB;
        private double _d, _w;                 // live: a palette section change re-sections the ghost
        private readonly bool _trackPalette;   // TJoin: live from the palette; Modify: prompted legs stay
        private double _foot, _head;
        private int _placeRaw;                 // palette/seed placement (0 Back / 1 Center / 2 Front)
        private bool _flip;                    // Flip keyword state, applied ON TOP of _placeRaw
        private ManagedTimber.TFrame _frame;
        private readonly SolidGhost _ghost = new SolidGhost();

        public double FootRun => _foot;
        public double HeadRun => _head;
        public double Depth => _d;
        public double Width => _w;
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

        // Re-solve the frame from the current values and rebuild the preview solid. False =
        // degenerate corner (the previous preview stays up, so the user keeps the last good solve).
        public bool Solve()
        {
            if (!ManagedTimber.TryBraceFrame(_fa, _fb, _d, _w, _foot, _head, _bodyA, _bodyB,
                                             Effective(_placeRaw, _flip), out ManagedTimber.TFrame f))
                return false;
            _frame = f;
            _ghost.Update(f);
            return true;
        }

        // COMMAND-context follow-up to a palette nudge: pull the palette's current brace legs /
        // placement AND section (a mid-loop catalog click re-sections the brace); on any change,
        // re-solve + rebuild in place.
        public void OnPaletteChanged()
        {
            if (!_trackPalette) return;
            bool dirty = false;
            if (ManagedBrace.HasCurrent)
            {
                double nf = ManagedBrace.FootRun, nh = ManagedBrace.HeadRun;
                int np = ManagedBrace.Placement;
                if (nf != _foot || nh != _head || np != _placeRaw)
                { _foot = nf; _head = nh; _placeRaw = np; dirty = true; }
            }
            if (ManagedSection.HasCurrent
                && (ManagedSection.Width != _w || ManagedSection.Depth != _d))
            { _w = ManagedSection.Width; _d = ManagedSection.Depth; dirty = true; }
            if (dirty) Solve();
        }

        public void Flip() { _flip = !_flip; Solve(); }

        public void Dispose() => _ghost.Dispose();
    }
}
