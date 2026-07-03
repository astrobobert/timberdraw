using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
namespace TimberDraw
{
    // Generates a mortise void that mirrors TenonGenerator constituent-for-constituent.
    // Every part of the tenon (tongue, housing, shoulder) gets a matching void cut into
    // the receiver, so the receiver holds the complete negative of the giver joint.
    //
    // Tongue void: TenonWidth wide, 4" deep, origin shifted by HousingDepth when housing present.
    // Housing void (HousingDepth > 0): full Width wide, full Depth tall, HousingDepth deep.
    //   BoolUnited into tongue void so the combined solid subtracts all voids at once.
    // Shoulder void (ShoulderDepth > 0): triangle, full Width wide.
    //   BoolUnited into tongue void.
    // Slope (Pitch > 0): top face tilts at -Pitch, matching TenonGenerator slope exactly.
    //
    // Winding: same LateralDir x FaceNormal Z-sign check as TenonGenerator.
    public class MortiseGenerator : IJointGenerator
    {
        public string[] RequiredExtras => System.Array.Empty<string>();

        public JointResult Generate(JointParams p)
        {
            double mortLen      = 4.0;
            double topFaceSlope = -p.Pitch;

            // Same lateralSpan formula as TenonGenerator so tongue height matches exactly.
            double lateralSpan = p.Depth - p.TopRelish + p.HousingDepth * topFaceSlope;

            // Winding: reverse when LateralDir x FaceNormal has negative Z (right-side joints).
            bool reverse = p.LateralDir.CrossProduct(p.FaceNormal).Z < 0;

            // Tongue origin: shifts inward by HousingDepth when housing is present.
            Point3d tongueOrigin = p.HousingDepth > 0
                ? p.Origin + p.FaceNormal * p.HousingDepth
                : p.Origin;

            // Tongue void: matches TenonGenerator tongue parallelogram exactly.
            Point3d t0 = tongueOrigin;
            Point3d t1 = tongueOrigin + p.LateralDir * lateralSpan;
            Point3d t2 = t1 + p.FaceNormal * mortLen + p.LateralDir * (mortLen * topFaceSlope);
            Point3d t3 = tongueOrigin + p.FaceNormal * mortLen;

            Point3dCollection tPts = reverse
                ? new Point3dCollection { t3, t2, t1, t0 }
                : new Point3dCollection { t0, t1, t2, t3 };

            ObjectId mortId = Module1.DrawElement(tPts, p.TenonWidth, "Mortise", p.BentNumber, p.Designation);

            // Housing void: full Width x full Depth x HousingDepth -- same profile as TenonGenerator housing.
            if (p.HousingDepth > 0)
            {
                double baseZ = Module1.Make3D ? p.Origin.Z - (p.Width - 2.0) / 2.0 : p.Origin.Z;
                Point3d h0 = new Point3d(p.Origin.X, p.Origin.Y, baseZ);
                Point3d h1 = h0 + p.LateralDir * p.Depth;
                Point3d h2 = h1 + p.FaceNormal * p.HousingDepth + p.LateralDir * (p.HousingDepth * topFaceSlope);
                Point3d h3 = h0 + p.FaceNormal * p.HousingDepth;

                Point3dCollection hPts = reverse
                    ? new Point3dCollection { h3, h2, h1, h0 }
                    : new Point3dCollection { h0, h1, h2, h3 };

                ObjectId housingVoidId = Module1.DrawElement(hPts, p.Width, "Mortise", p.BentNumber, p.Designation);
                Module1.AddJoint(mortId, housingVoidId, Module1.Joint.Tenon);
                Module1.DeleteJoint(housingVoidId);
            }

            // Shoulder void: triangle, full Width wide -- matches TenonGenerator shoulder.
            if (p.ShoulderDepth > 0)
            {
                double baseZ = Module1.Make3D ? p.Origin.Z - (p.Width - 2.0) / 2.0 : p.Origin.Z;
                Point3d s0 = new Point3d(p.Origin.X, p.Origin.Y, baseZ);
                Point3d s1 = s0 + p.LateralDir * p.Depth;
                Point3d s2 = s0 + p.FaceNormal * p.ShoulderDepth;

                Point3dCollection sPts = reverse
                    ? new Point3dCollection { s2, s1, s0 }
                    : new Point3dCollection { s0, s1, s2 };

                ObjectId shoulderVoidId = Module1.DrawElement(sPts, p.Width, "Mortise", p.BentNumber, p.Designation);
                Module1.AddJoint(mortId, shoulderVoidId, Module1.Joint.Tenon);
                Module1.DeleteJoint(shoulderVoidId);
            }

            return new JointResult
            {
                JointId          = mortId,
                AdditionalSolids = new List<ObjectId>(),
                Pegs             = new List<ObjectId>()
            };
        }
    }
}
