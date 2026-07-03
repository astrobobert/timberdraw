using System;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;

namespace TimberDraw
{
    // Keeps a managed timber's stored placement frame (TMFrame) and scarf node (TMScarf) in lockstep with
    // its solid whenever the solid is transformed -- so NATIVE MOVE / ROTATE / MIRROR / ALIGN (and rigid
    // grip drags) update the analytic faces TScan/TSpan/TFit rely on, with no special TMove/TRotate.
    //
    // Registered on the Solid3d RXClass in Commands.Initialize and removed in Terminate. Overruling the
    // whole Solid3d class is cheap because ApplyManagedTransform no-ops on any solid that lacks the
    // TMFrame/TMScarf xrecords (legacy TDraw timbers, key blocks, and our own solids DURING construction
    // -- the frame xrecord is written AFTER the build transform, so this fires before it exists).
    public class ManagedTransformOverrule : TransformOverrule
    {
        public static ManagedTransformOverrule Instance { get; private set; }

        public static void Enable()
        {
            if (Instance != null) return;
            Instance = new ManagedTransformOverrule();
            Overrule.AddOverrule(RXObject.GetClass(typeof(Solid3d)), Instance, false);
            Overrule.Overruling = true;
        }

        public static void Disable()
        {
            if (Instance == null) return;
            Overrule.RemoveOverrule(RXObject.GetClass(typeof(Solid3d)), Instance);
            Instance.Dispose();
            Instance = null;
        }

        public override void TransformBy(Entity entity, Matrix3d transform)
        {
            base.TransformBy(entity, transform);   // move the solid as AutoCAD intended

            // Only mirror the move into the stored frame for a RIGID motion (length-preserving): a
            // non-rigid grip stretch would desync W/D/L, and the model is "no grip-edit" by design, so we
            // leave the xrecords untouched rather than corrupt them. A pure rotation/translation/mirror
            // keeps each transformed basis vector unit length.
            CoordinateSystem3d cs = transform.CoordinateSystem3d;
            if (!IsUnit(cs.Xaxis) || !IsUnit(cs.Yaxis) || !IsUnit(cs.Zaxis)) return;

            Database db = entity.Database;
            if (db == null) return;
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                Entity e = (Entity)tr.GetObject(entity.ObjectId, OpenMode.ForWrite);
                ManagedTimber.ApplyManagedTransform(tr, e, transform);
                tr.Commit();
            }
        }

        private static bool IsUnit(Vector3d v) => Math.Abs(v.Length - 1.0) < 1e-6;
    }
}
