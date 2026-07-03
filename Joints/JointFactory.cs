using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
namespace TimberDraw
{
    // Registry and dispatch for all joint geometry generators.
    //
    // Adding a new joint type:
    //   1. Implement IJointGenerator in a new .cs file in this folder.
    //   2. Add a Register() call in RegisterDefaults() below.
    //   No other changes needed.
    //
    // Called from Commands.cs Initialize() so generators are ready before TDraw.
    public static class JointFactory
    {
        private static readonly Dictionary<Module1.JointType, IJointGenerator> _generators
            = new Dictionary<Module1.JointType, IJointGenerator>();

        // Register all built-in generators. Called once at plugin load.
        public static void RegisterDefaults()
        {
            Register(Module1.JointType.Tenon,              new TenonGenerator());
            Register(Module1.JointType.Mortise,            new MortiseGenerator());
            Register(Module1.JointType.Butt,               new ButtGenerator());
            Register(Module1.JointType.ButtHousing,        new ButtHousingGenerator());
            Register(Module1.JointType.Dovetail,           new DovetailGenerator());
            Register(Module1.JointType.DovetailHousing,    new DovetailHousingGenerator());
            Register(Module1.JointType.Birdmouth,          new BirdmouthGenerator());
            Register(Module1.JointType.BirdmouthHousing,   new BirdmouthHousingGenerator());
            Register(Module1.JointType.ScarfA,             new ScarfAGenerator());
            Register(Module1.JointType.ScarfB,             new ScarfBGenerator());
            Register(Module1.JointType.Spline,             new SplineGenerator());
            Register(Module1.JointType.SplineHousing,      new SplineHousingGenerator());
            Register(Module1.JointType.Shoulder,           new ShoulderGenerator());
            Register(Module1.JointType.Polygon,            new PolygonGenerator());
        }

        // Register (or replace) a generator for the given joint type.
        public static void Register(Module1.JointType type, IJointGenerator generator)
        {
            _generators[type] = generator;
        }

        // Draw a joint and return its ObjectId (ObjectId.Null for no-geometry joints).
        public static ObjectId Create(Module1.JointType type, JointParams p)
            => CreateWithPegs(type, p).JointId;

        public static JointResult CreateWithPegs(Module1.JointType type, JointParams p)
        {
            if (_generators.TryGetValue(type, out IJointGenerator gen))
                return gen.Generate(p);
            throw new InvalidOperationException("No generator registered for JointType." + type);
        }

        // Returns the Extra keys required by the generator for this joint type.
        // Used by TimberTag to show context-specific input fields.
        public static string[] RequiredExtras(Module1.JointType type)
        {
            if (_generators.TryGetValue(type, out IJointGenerator gen))
                return gen.RequiredExtras;
            return Array.Empty<string>();
        }

        public static bool HasGenerator(Module1.JointType type)
            => _generators.ContainsKey(type);
    }
}
