using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using TimberFrameSuite.Standards;
namespace TimberDraw
{
    // Generates a tenon with optional shoulder and optional housing.
    // All geometry is BoolUnited into a single solid returned as JointId.
    //
    // Winding: LateralDir x FaceNormal gives the extrusion normal (+Z = correct).
    // When Z < 0 the polygon is CW; reverse vertex order to get CCW (+Z extrusion).
    // This flips all profiles when FaceNormal points in +X (right-end joints).
    //
    // Housing (when HousingDepth > 0): 4-point rectangle, Width wide, full Depth tall.
    //   h0(face-bot), h1(face-top), h2(back-top), h3(back-bot)
    //   Tenon origin shifts to the housing back face so total projection =
    //   HousingDepth + 4". Housing is BoolUnited into tenon.
    //
    // Tenon: plain rectangle, TenonWidth wide, starts at housing back (or face if no housing).
    //   Normal order: p0(bot), p1(top), p2(back-top), p3(back-bot)
    //   Reversed:     p3, p2, p1, p0
    //
    // Shoulder (when ShoulderDepth > 0): 3-point triangle, Width wide.
    //   s0(face-bot), s1(face-top), s2(seat-bot = ShoulderDepth into post)
    //   Diagonal bearing seat. BoolUnited into tenon.
    //   Reversed: s2, s1, s0
    //
    // Pegs (when GeneratePegs): 1.75" from the tenon near face (= housing back when
    //   housing is present), per TFGPegStandards.
    //
    // Caller: AddJoint(timberId, tenonId, Joint.Tenon) and AddConnection() wiring.
    public class TenonGenerator : IJointGenerator
    {
        public string[] RequiredExtras => new[] { "tenonWidth", "tenonRelish", "housingDepth", "Pitch" };

        public JointResult Generate(JointParams p)
        {
            double tenLen = 4.0;
            // topFaceSlope: rise of the top face per unit of FaceNormal projection.
            // Negative for all sloped timbers -- the top surface descends as the tenon
            // extends into the receiver away from the timber body.
            // p.Pitch = 0 for horizontal members (girts); = Module1.Pitch for rafters.
            double topFaceSlope = -p.Pitch;

            // When housing is present the tenon origin sits HousingDepth into the post.
            // The top surface has already dropped by HousingDepth * Pitch at that point,
            // so lateralSpan is reduced accordingly.
            double lateralSpan = p.Depth - p.TopRelish + p.HousingDepth * topFaceSlope;

            // Detect winding: if LateralDir x FaceNormal has negative Z the polygon
            // would be CW -- reverse vertex order to make it CCW (correct +Z extrusion).
            bool reverse = p.LateralDir.CrossProduct(p.FaceNormal).Z < 0;

            // Housing: full-Width rectangle from the timber face to HousingDepth into post.
            // Drawn first; tenon origin is shifted to the housing back face.
            Point3d tenonOrigin = p.HousingDepth > 0
                ? p.Origin + p.FaceNormal * p.HousingDepth
                : p.Origin;

            // Tenon: parallelogram -- bottom stays horizontal, top face slopes at topFaceSlope.
            // p2 (top-far) is pulled down by tenLen * Pitch relative to p1 (top-near).
            Point3d p0 = tenonOrigin;
            Point3d p1 = tenonOrigin + p.LateralDir * lateralSpan;
            Point3d p2 = p1 + p.FaceNormal * tenLen + p.LateralDir * (tenLen * topFaceSlope);
            Point3d p3 = tenonOrigin + p.FaceNormal * tenLen;

            Point3dCollection pts = reverse
                ? new() { p3, p2, p1, p0 }
                : new() { p0, p1, p2, p3 };

            ObjectId tenonId = Module1.DrawElement(pts, p.TenonWidth, "Tenon", p.BentNumber, p.Designation);

            // Housing: 4-point rectangle, Width wide, full Depth, HousingDepth deep.
            //   h0(face-bot), h1(face-top), h2(back-top), h3(back-bot)
            // BoolUnite into tenon so they act as one solid.
            if (p.HousingDepth > 0)
            {
                double baseZ = Module1.Make3D ? p.Origin.Z - (p.Width - 2.0) / 2.0 : p.Origin.Z;
                Point3d h0 = new(p.Origin.X, p.Origin.Y, baseZ);
                Point3d h1 = h0 + p.LateralDir * p.Depth;
                Point3d h2 = h1 + p.FaceNormal * p.HousingDepth + p.LateralDir * (p.HousingDepth * topFaceSlope);
                Point3d h3 = h0 + p.FaceNormal * p.HousingDepth;

                Point3dCollection hPts = reverse
                    ? new() { h3, h2, h1, h0 }
                    : new() { h0, h1, h2, h3 };
                ObjectId housingId = Module1.DrawElement(hPts, p.Width, "Tenon", p.BentNumber, p.Designation);
                Module1.AddJoint(tenonId, housingId, Module1.Joint.Tenon);
                Module1.DeleteJoint(housingId);
            }

            // Shoulder: 3-point triangle, Width wide, always at the timber face (Origin).
            //   s0(face-bot), s1(face-top), s2(seat-bot) -- reversed when winding is CW.
            // BoolUnite into the tenon solid so all geometry acts as one joint entity.
            // baseZ reconstructed from tenonZ so the prism spans the full girt Width.
            if (p.ShoulderDepth > 0)
            {
                double baseZ = Module1.Make3D ? p.Origin.Z - (p.Width - 2.0) / 2.0 : p.Origin.Z;
                Point3d s0 = new(p.Origin.X, p.Origin.Y, baseZ);
                Point3d s1 = s0 + p.LateralDir * p.Depth;
                Point3d s2 = s0 + p.FaceNormal * p.ShoulderDepth;

                Point3dCollection sPts = reverse
                    ? new() { s2, s1, s0 }
                    : new() { s0, s1, s2 };
                ObjectId shoulderId = Module1.DrawElement(sPts, p.Width, "Tenon", p.BentNumber, p.Designation);
                Module1.AddJoint(tenonId, shoulderId, Module1.Joint.Tenon);
                Module1.DeleteJoint(shoulderId);
            }

            var result = new JointResult
            {
                JointId          = tenonId,   // combined housing+tenon+shoulder (if any)
                AdditionalSolids = new List<ObjectId>(),
                Pegs             = new List<ObjectId>()
            };

            if (p.GeneratePegs)
            {
                var preset    = TFGPegStandards.GetPresetForTenonThickness(p.TenonWidth);
                double r      = preset.DiameterInches / 2.0;
                double pegLen = p.Width + 1.5;
                double maxPegPos = p.Depth - preset.FirstPegSetbackInches;
                // Pegs extrude along the timber width axis (global +Z for girts).
                // p.Origin.Z is the tenon center (tenonZ); reconstruct the timber base Z.
                double baseZ = Module1.Make3D ? p.Origin.Z - (p.Width - 2.0) / 2.0 : p.Origin.Z;
                double[] ys =
                {
                    preset.FirstPegSetbackInches,
                    preset.FirstPegSetbackInches + preset.CalculatedSpacingInches,
                    preset.FirstPegSetbackInches + 2 * preset.CalculatedSpacingInches
                };
                // Pegs are 1.75" from the tenon near face (housing back when housing present).
                // LateralDir has no Z component for girts, so explicit baseZ overrides Z.
                Point3d facePt = tenonOrigin + p.FaceNormal * 1.75;
                for (int i = 0; i < ys.Length; i++)
                {
                    if (i > 0 && ys[i] > maxPegPos) continue; // first peg always; rest if they fit
                    Point3d c       = facePt + p.LateralDir * ys[i];
                    Point3d pegBase = new Point3d(c.X, c.Y, baseZ - 0.75);
                    result.Pegs.Add(Module1.DrawPeg(pegBase, r, pegLen, "Peg", "", "", ""));
                }
            }
            return result;
        }
    }
}
