using System;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.GraphicsInterface;

namespace TimberDraw
{
    // 1-DOF placement ghost for TScarf. The scarf occupies a region of length _len centred on a point
    // that slides ALONG the beam's length axis (f.Z). The cursor's projection onto that axis sets the
    // centre _xc; horizontal cursor motion off the beam is ignored. _xc is clamped so the whole scarf
    // stays on the beam. The "Length" keyword resizes the region mid-drag. All geometry is WCS.
    public class ScarfJig : DrawJig
    {
        private readonly ManagedTimber.TFrame _f;
        private readonly Vector3d _zHat;   // unit beam length axis
        private double _len;               // scarf length
        private double _xc;                // centre along the beam (distance from f.O along _zHat)

        public double Xc => _xc;
        public double Len => _len;

        public ScarfJig(ManagedTimber.TFrame f, double initialLen)
        {
            _f = f;
            _zHat = f.Z.GetNormal();
            _len = initialLen <= 0 ? 1.0 : initialLen;
            _xc = ClampXc(_f.L / 2.0);   // start centred on the beam
        }

        // Keep the whole scarf on the beam: centre within [_len/2 + 0.5, L - _len/2 - 0.5].
        private double ClampXc(double xc)
        {
            double lo = _len / 2.0 + 0.5, hi = _f.L - _len / 2.0 - 0.5;
            if (lo > hi) return _f.L / 2.0;   // beam too short -- guarded by the command before the jig
            return Math.Max(lo, Math.Min(hi, xc));
        }

        public void SetLength(double len)
        {
            if (len > 1e-6) _len = len;
            _xc = ClampXc(_xc);
        }

        protected override SamplerStatus Sampler(JigPrompts prompts)
        {
            var opts = new JigPromptPointOptions(
                "\nScarf centre on the beam (drag or snap a point); [Length] <" + _len.ToString("0.#") + ">: ")
            {
                UseBasePoint = true,
                BasePoint = _f.O
            };
            opts.Keywords.Add("Length");

            PromptPointResult r = prompts.AcquirePoint(opts);
            if (r.Status != PromptStatus.OK) return SamplerStatus.NoChange;   // keyword/cancel surfaced by ed.Drag

            double xc = ClampXc((r.Value - _f.O).DotProduct(_zHat));   // project onto the beam axis
            if (Math.Abs(xc - _xc) < 1e-6) return SamplerStatus.NoChange;
            _xc = xc;
            return SamplerStatus.OK;
        }

        protected override bool WorldDraw(WorldDraw draw)
        {
            Point3d nearCenter = _f.O + _f.Z * (_xc - _len / 2.0);
            JigGeometry.DrawBoxWire(draw, nearCenter, _f.Z, _len, _f.Y, _f.D, _f.X, _f.W, 5);
            return true;
        }
    }
}
