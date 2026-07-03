using System;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.GraphicsInterface;

namespace TimberDraw
{
    // Two-phase placement ghost for TPlace. All geometry is WCS (the base point + UCS axes are
    // passed in already resolved to WCS).
    //   Phase Roll   : the W x D cross-section rectangle is drawn flat in the UCS at the base point
    //                  and spun about the base point (rotation about the UCS Z extrusion axis) to set
    //                  the section roll. The user drags or types an angle.
    //   Phase Length : the full box ghosts along UCS Z; the user drags or types the length.
    public class PlaceJig : DrawJig
    {
        public enum JigPhase { Roll, Length }

        private readonly Point3d _baseWcs;            // near end-face centre (WCS)
        private readonly Vector3d _ux, _uy, _uz;      // UCS axes in WCS; Z = extrusion / length
        private readonly double _w, _d;
        private double _angle;                        // current roll about uz (radians)
        private double _length;                       // current length along uz

        public JigPhase Phase;
        public double Angle => _angle;
        public double Length => _length;

        public PlaceJig(Point3d baseWcs, Vector3d ux, Vector3d uy, Vector3d uz,
                        double w, double d, double initialLength)
        {
            _baseWcs = baseWcs; _ux = ux; _uy = uy; _uz = uz; _w = w; _d = d;
            _length = initialLength <= 0 ? 1.0 : initialLength;
        }

        protected override SamplerStatus Sampler(JigPrompts prompts)
        {
            if (Phase == JigPhase.Roll)
            {
                var opts = new JigPromptAngleOptions("\nRoll the section (drag or type angle): ")
                {
                    UseBasePoint = true,
                    BasePoint = _baseWcs
                };
                PromptDoubleResult r = prompts.AcquireAngle(opts);
                if (r.Status != PromptStatus.OK) return SamplerStatus.Cancel;
                if (Math.Abs(r.Value - _angle) < 1e-6) return SamplerStatus.NoChange;
                _angle = r.Value;
                return SamplerStatus.OK;
            }
            else
            {
                var opts = new JigPromptDistanceOptions("\nLength (drag or type): ")
                {
                    UseBasePoint = true,
                    BasePoint = _baseWcs
                };
                PromptDoubleResult r = prompts.AcquireDistance(opts);
                if (r.Status != PromptStatus.OK) return SamplerStatus.Cancel;
                double len = r.Value;
                if (len <= 1e-6) len = _length;
                if (Math.Abs(len - _length) < 1e-6) return SamplerStatus.NoChange;
                _length = len;
                return SamplerStatus.OK;
            }
        }

        protected override bool WorldDraw(WorldDraw draw)
        {
            // Section axes after the roll about UCS Z. In the roll phase length is 0 (rectangle only);
            // in the length phase the full box ghosts along UCS Z. Blue (ACI 5) = colour-blind safe.
            Vector3d wAxis = _ux * Math.Cos(_angle) + _uy * Math.Sin(_angle);
            Vector3d dAxis = _ux * (-Math.Sin(_angle)) + _uy * Math.Cos(_angle);
            double len = (Phase == JigPhase.Length) ? _length : 0.0;
            JigGeometry.DrawBoxWire(draw, _baseWcs, _uz, len, dAxis, _d, wAxis, _w, 5);
            return true;
        }
    }
}
