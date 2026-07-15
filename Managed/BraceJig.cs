using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.GraphicsInterface;

namespace TimberDraw
{
    // Live placement ghost for TJoin's knee brace (Robert's call, 2026-07-15: the TSpan feel,
    // replacing the static transient + Yes/No prompt). The brace is fully determined by the two
    // picked faces + the Brace spec, so the cursor doesn't position anything -- the jig exists so
    // the ghost RE-SOLVES as the palette changes: with trackPalette on (TJoin), every sampler tick
    // re-reads ManagedBrace, so editing Foot/Head/Angle or Placement on the palette moves the ghost
    // on the next cursor move. Click or Enter places, Flip swaps a Back/Front placement side,
    // Escape cancels. Renders the REAL mitered solid (BuildFramedSolid), rebuilt only when the
    // inputs actually change.
    public class BraceJig : DrawJig
    {
        private readonly ManagedTimber.TFace _fa, _fb;
        private readonly Point3d _bodyA, _bodyB;
        private readonly double _d, _w;
        private readonly bool _trackPalette;   // TJoin: live from ManagedBrace; Modify: prompted legs stay
        private readonly string _prompt;
        private double _foot, _head;
        private int _placeRaw;                 // palette/seed placement (0 Back / 1 Center / 2 Front)
        private bool _flip;                    // Flip keyword state, applied ON TOP of _placeRaw
        private ManagedTimber.TFrame _frame;
        private Solid3d _ghost;

        public double FootRun => _foot;
        public double HeadRun => _head;
        public int Placement => Effective(_placeRaw, _flip);
        public ManagedTimber.TFrame Frame => _frame;

        public BraceJig(ManagedTimber.TFace fa, ManagedTimber.TFace fb, double depth, double width,
            double footRun, double headRun, int placement, Point3d bodyA, Point3d bodyB,
            bool trackPalette, string prompt)
        {
            _fa = fa; _fb = fb; _bodyA = bodyA; _bodyB = bodyB; _d = depth; _w = width;
            _foot = footRun; _head = headRun; _placeRaw = placement;
            _trackPalette = trackPalette; _prompt = prompt;
        }

        // Flip only swaps a Back/Front side; Center has no side. Kept SEPARATE from _placeRaw so a
        // palette re-read (which restores the palette's own value) doesn't silently undo a Flip.
        private static int Effective(int raw, bool flip) => flip && raw != 1 ? (raw == 0 ? 2 : 0) : raw;

        // Re-solve the frame + rebuild the ghost solid from the current values. False = degenerate
        // corner (an existing ghost is kept, so the jig keeps rendering the last good solve).
        public bool Solve()
        {
            if (!ManagedTimber.TryBraceFrame(_fa, _fb, _d, _w, _foot, _head, _bodyA, _bodyB,
                                             Effective(_placeRaw, _flip), out ManagedTimber.TFrame f))
                return false;
            _frame = f;
            _ghost?.Dispose();
            _ghost = ManagedTimber.BuildFramedSolid(_frame);
            _ghost.ColorIndex = 5;   // blue, colour-blind safe
            return true;
        }

        public void Flip() { _flip = !_flip; Solve(); }

        // The ghost is a non-database solid owned by the jig -- the command's finally disposes it.
        public void DisposeGhost() { _ghost?.Dispose(); _ghost = null; }

        protected override SamplerStatus Sampler(JigPrompts prompts)
        {
            // STATIC prompt (no live values in the text -- the command line must not scroll-update).
            var opts = new JigPromptPointOptions(_prompt)
            { UserInputControls = UserInputControls.NullResponseAccepted };
            if (Effective(_placeRaw, _flip) != 1) opts.Keywords.Add("Flip");
            PromptPointResult r = prompts.AcquirePoint(opts);
            if (r.Status != PromptStatus.OK) return SamplerStatus.NoChange;

            if (!_trackPalette) return SamplerStatus.NoChange;
            double nf = ManagedBrace.HasCurrent ? ManagedBrace.FootRun : _foot;
            double nh = ManagedBrace.HasCurrent ? ManagedBrace.HeadRun : _head;
            int np = ManagedBrace.HasCurrent ? ManagedBrace.Placement : _placeRaw;
            if (nf == _foot && nh == _head && np == _placeRaw) return SamplerStatus.NoChange;
            _foot = nf; _head = nh; _placeRaw = np;
            return Solve() ? SamplerStatus.OK : SamplerStatus.NoChange;
        }

        protected override bool WorldDraw(WorldDraw draw)
        {
            if (_ghost != null) draw.Geometry.Draw(_ghost);
            return true;
        }
    }
}
