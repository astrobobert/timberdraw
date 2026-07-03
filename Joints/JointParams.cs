using System.Collections.Generic;
using Autodesk.AutoCAD.Geometry;
namespace TimberDraw
{
    // All parameters needed by a joint generator to produce joint geometry.
    // Standard fields cover the common case; Extra holds type-specific values.
    //
    // Coordinate convention (matches existing DrawElement usage):
    //   Origin   -- face center of the joint on the timber surface
    //   FaceNormal -- unit vector pointing INTO the receiving timber (tenon projects this way)
    //   LateralDir -- unit vector along the timber Depth direction
    //   Width    -- timber section width (extrusion direction; = TenonWidth for tenons)
    //   Depth    -- timber section depth (lateral span of tenon profile)
    //
    // CustomPts path (JointType.Polygon):
    //   When CustomPts is non-null the standard geometry fields are ignored.
    //   PolygonGenerator extrudes the polygon (bent-local coordinates) by Width.
    //   Factory method: JointParams.ForPolygon(pts, width, bentNumber, designation)
    public struct JointParams
    {
        public Module1.JointType JointType;
        public Autodesk.AutoCAD.Geometry.Point3d Origin;
        public Autodesk.AutoCAD.Geometry.Vector3d FaceNormal;  // direction tenon/notch projects
        public Autodesk.AutoCAD.Geometry.Vector3d LateralDir;  // along timber Depth
        public double Width;       // section width -- extrusion depth for this joint
        public double Depth;       // section depth -- lateral extent of joint profile
        public double TenonWidth;  // tenon thickness: 2.0 standard, 1.5 braces
        // Top relish: wood from the top face of the tenon to the top face of the timber.
        // TenonGenerator insets the tenon profile by this amount from the far end of LateralDir.
        // 0 = tenon runs full depth (flush with both faces) -- legacy / default.
        // bottomRelish is implicitly (Depth - TopRelish - tenon lateral height); currently 0
        // (origin sits on the bottom face).  Add a BottomRelish field here when needed.
        public double TopRelish;
        // Seat depth (in): how far the shouldered tenon's near-bottom corner is pulled
        // into the receiving member along FaceNormal. 0 = no shoulder (plain tenon).
        public double ShoulderDepth;
        // Housing depth (in): a full-Width x full-Depth rectangular housing block that
        // extends HousingDepth into the receiver before the tenon begins. 0 = no housing.
        // When > 0 the tenon origin shifts by HousingDepth so total projection =
        // HousingDepth + 4". The housing is BoolUnited into the tenon solid.
        public double HousingDepth;
        // When true, a peg-aware generator (TenonGenerator) also draws and returns this
        // joint's pegs in JointResult.Pegs. Legacy callers leave this false (no pegs drawn).
        public bool GeneratePegs;
        public string BentNumber;
        public string Designation;
        // Timber's own rise/run slope (0 for horizontal members; Module1.Pitch for rafters).
        // TenonGenerator and MortiseGenerator use this to tilt the top face of the joint so
        // the tenon/housing top aligns with the timber top edge at each depth into the receiver.
        // Default 0 -- all existing callers that omit this get flat tops unchanged.
        public double Pitch;
        // Context-specific values required by some generators:
        //   BirdmouthGenerator:     "Pitch" (rise/run ratio), "SeatDepth" (in)
        //   DovetailGenerator:      "TaperAngle" (default 0.125), "TenonLength" (in)
        //   ScarfAGenerator:        "ScarfLength" (in, typically 3x Depth)
        public Dictionary<string, double> Extra;

        // Custom polygon profile (JointType.Polygon only).
        // Bent-local coordinates; same convention as Origin.
        // null = use standard JF geometry driven by the fields above.
        public Point3d[] CustomPts;

        public JointParams(Module1.JointType jointType, Point3d origin, Vector3d faceNormal, Vector3d lateralDir,
            double width, double depth, double tenonWidth, string bentNumber, string designation,
            double topRelish = 0, double shoulderDepth = 0, bool generatePegs = false,
            double housingDepth = 0, double pitch = 0)
        {
            JointType = jointType;
            Origin = origin;
            FaceNormal = faceNormal;
            LateralDir = lateralDir;
            Width = width;
            Depth = depth;
            TenonWidth = tenonWidth;
            TopRelish = topRelish;
            ShoulderDepth = shoulderDepth;
            GeneratePegs = generatePegs;
            HousingDepth = housingDepth;
            Pitch = pitch;
            BentNumber = bentNumber;
            Designation = designation;
            Extra = new Dictionary<string, double>();
            CustomPts = null;
        }

        // Factory: create a Polygon joint from an explicit point array.
        // pts must be in bent-local coordinates (same convention as Origin).
        // Width is the extrusion depth (section width).
        public static JointParams ForPolygon(Point3d[] pts, double width,
            string bentNumber = "", string designation = "")
            => new JointParams
            {
                JointType   = Module1.JointType.Polygon,
                CustomPts   = pts,
                Width       = width,
                BentNumber  = bentNumber,
                Designation = designation,
                Extra       = new Dictionary<string, double>()
            };
    }
}
