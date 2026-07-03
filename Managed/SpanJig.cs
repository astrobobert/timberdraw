using System;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.GraphicsInterface;

namespace TimberDraw
{
    // 1-DOF placement ghost for TSpan. The span fills the perpendicular gap between two facing timbers
    // and is positioned ALONG the connected timbers (the slide rail fa.U). Its position is one rail
    // coordinate s, measured along the rail FROM THE UCS ORIGIN, so s = 0 is the UCS-origin datum (the
    // post base when the UCS origin sits there). The girt's justified face (Center / Bottom / Top) is
    // placed at s.
    //
    // The cursor sets s = (pick - ucsOrigin) . railHat -- a rail projection, so horizontal cursor motion
    // is ignored (the "filter for Y only"): only movement along the rail (the post) changes the height.
    // In an elevation UCS (Bent/Wall, post vertical on screen) the cursor's vertical motion drives s and
    // OSNAP to a feature gives that feature's height. In Plan UCS a free pick lands on the ground plane
    // (s = 0) so the ghost rests at the base -- set the height in an elevation UCS or type it (Height).
    // All geometry is WCS.
    public class SpanJig : DrawJig
    {
        public enum Justify { Center, Bottom, Top }

        private readonly Point3d _o0;       // centered base origin (fixes the fa.V + fa.N placement)
        private readonly Vector3d _len;     // fa.N -- across the gap (member length)
        private readonly Vector3d _u;       // fa.U unit, oriented up -- the slide rail along the timbers
        private readonly Vector3d _v;       // fa.V -- the cross (in-plane, off-rail) axis
        private readonly double _gap;
        private readonly double _railDim;   // section dimension lying ALONG the rail (depth on a post
                                            // rail, width on a girt rail) -- the justify offset
        private readonly double _crossDim;  // section dimension along fa.V
        private readonly Point3d _ucsOrg;   // UCS origin (WCS) -- the rail datum (s = 0)
        private readonly Point3d _floorAnchor; // the s=0 point on the rail -- the pick base point

        private double _sTarget;            // rail coordinate of the placement line (init 0 = datum)
        private bool _locked;               // a typed Height locks the value against pick override
        private Justify _just = Justify.Center;

        public Justify Mode => _just;
        public double LineY => _sTarget;    // height above the datum, for the echo

        public SpanJig(Point3d o0, ManagedTimber.TFace fa, double gap, double railDim, double crossDim, Point3d ucsOrg)
        {
            _o0 = o0; _len = fa.N; _v = fa.V; _gap = gap; _railDim = railDim; _crossDim = crossDim;
            _ucsOrg = ucsOrg;
            Vector3d r = fa.U.GetNormal();
            if (r.DotProduct(Vector3d.ZAxis) < 0.0) r = r.Negate();   // orient up so +s is upward
            _u = r;
            // The s=0 point on the rail = _o0 slid down to the UCS-origin datum. Used as the pick base
            // point so a typed "0" (zero distance from the base) lands on the floor, not the mid-height.
            _floorAnchor = _o0 - _u * ((_o0 - _ucsOrg).DotProduct(_u));
            _sTarget = 0.0;   // start on the UCS-origin datum, not the overlap midpoint
        }

        private double S(Point3d p) => (p - _ucsOrg).DotProduct(_u);

        // Near end-face centre that puts the justified face on the line s == _sTarget. Only the rail
        // component of _o0 moves; fa.V / fa.N stay as set by _o0. Never returns the overlap midpoint.
        public Point3d Origin
        {
            get
            {
                double centerS = _sTarget;                                // Center: axis on the line
                if (_just == Justify.Bottom) centerS += _railDim / 2.0;   // lower face on the line
                else if (_just == Justify.Top) centerS -= _railDim / 2.0; // upper face on the line
                return _o0 + _u * (centerS - S(_o0));
            }
        }

        public void SetJustify(string keyword)
        {
            switch (keyword)
            {
                case "Center": _just = Justify.Center; break;
                case "Bottom": _just = Justify.Bottom; break;
                case "Top": _just = Justify.Top; break;
            }
        }

        public void SetHeight(double s) { _sTarget = s; _locked = true; }

        protected override SamplerStatus Sampler(JigPrompts prompts)
        {
            var opts = new JigPromptPointOptions(
                "\nPick or snap a point for the height (Y only); [Height/Center/Bottom/Top] <" + _just + " " + _sTarget.ToString("0.#") + ">: ")
            {
                UseBasePoint = true,
                BasePoint = _floorAnchor
            };
            opts.Keywords.Add("Height");
            opts.Keywords.Add("Center");
            opts.Keywords.Add("Bottom");
            opts.Keywords.Add("Top");

            PromptPointResult r = prompts.AcquirePoint(opts);
            if (r.Status != PromptStatus.OK) return SamplerStatus.NoChange;   // keyword/cancel surfaced by ed.Drag
            if (_locked) return SamplerStatus.NoChange;

            double s = S(r.Value);   // rail projection: horizontal cursor motion is ignored
            if (Math.Abs(s - _sTarget) < 1e-6) return SamplerStatus.NoChange;
            _sTarget = s;
            return SamplerStatus.OK;
        }

        protected override bool WorldDraw(WorldDraw draw)
        {
            JigGeometry.DrawBoxWire(draw, Origin, _len, _gap, _u, _railDim, _v, _crossDim, 5);
            return true;
        }
    }
}
