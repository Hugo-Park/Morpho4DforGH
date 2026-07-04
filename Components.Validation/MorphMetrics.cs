using System;
using System.Collections.Generic;
using System.Drawing;
using Grasshopper.Kernel;
using Morpho4D.Models;
using Rhino.Geometry;

namespace _Morpho4D
{
    public class MorphoMetricsComponent : GH_Component
    {
        public MorphoMetricsComponent()
          : base("Morpho Metrics", "Metrics",
              "Measures curvature (κ) from the deformed voxel list. Uses 3-point circle fitting (fallback: custom implementation).",
              "Morpho4D", "06 Validation")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Voxels", "VX", "Voxel list after simulation", GH_ParamAccess.list);
            pManager.AddVectorParameter("Curvature Axis", "Ax",
                "Axis direction to measure curvature (slicing points along this direction)", GH_ParamAccess.item, Vector3d.XAxis);
            pManager.AddIntegerParameter("Sample Count", "N",
                "Number of sample points to use for circle fitting (3 or more)", GH_ParamAccess.item, 5);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddNumberParameter("Curvature κ", "κ", "Average curvature (1/mm)", GH_ParamAccess.item);
            pManager.AddNumberParameter("Radius R", "R", "Radius of the fitted circle (mm). 0 if flat.", GH_ParamAccess.item);
            pManager.AddPointParameter("Fitted Center", "C", "Center point of the fitted circle", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            var voxelGoos = new List<VoxelCellGoo>();
            Vector3d axis = Vector3d.XAxis;
            int sampleN = 5;

            if (!DA.GetDataList(0, voxelGoos)) return;
            DA.GetData(1, ref axis);
            DA.GetData(2, ref sampleN);

            if (sampleN < 3) sampleN = 3;
            if (axis.Length < 1e-9) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Axis is a zero vector."); return; }
            axis.Unitize();

            var voxels = new List<VoxelCell>();
            foreach (var g in voxelGoos) if (g?.Value != null) voxels.Add(g.Value);
            if (voxels.Count < 3) { AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Less than 3 voxels."); return; }

            // Sort along the axis direction and uniformly sample
            voxels.Sort((a, b) =>
            {
                double da = a.currentPoint.X * axis.X + a.currentPoint.Y * axis.Y + a.currentPoint.Z * axis.Z;
                double db = b.currentPoint.X * axis.X + b.currentPoint.Y * axis.Y + b.currentPoint.Z * axis.Z;
                return da.CompareTo(db);
            });

            var samples = new List<Point3d>();
            double step = (voxels.Count - 1.0) / (sampleN - 1);
            for (int i = 0; i < sampleN; i++)
            {
                int idx = (int)Math.Round(i * step);
                idx = Math.Max(0, Math.Min(idx, voxels.Count - 1));
                samples.Add(voxels[idx].currentPoint);
            }

            // 3-point circle fitting (middle-trisection selection)
            Point3d center = Point3d.Origin;
            double R = 0.0;

            if (samples.Count >= 3)
            {
                Point3d p0 = samples[0];
                Point3d p1 = samples[samples.Count / 2];
                Point3d p2 = samples[samples.Count - 1];

                bool ok = Circle3Pt(p0, p1, p2, out center, out R);
                if (!ok)
                {
                    DA.SetData(0, 0.0);
                    DA.SetData(1, 0.0);
                    DA.SetData(2, Point3d.Origin);
                    return;
                }
            }

            double kappa = (R > 1e-9) ? 1.0 / R : 0.0;
            DA.SetData(0, kappa);
            DA.SetData(1, R);
            DA.SetData(2, center);
        }

        // 3-point circumcircle custom implementation (fallback — works without RhinoCommon Circle.TryFitCircleToPoints)
        private static bool Circle3Pt(Point3d a, Point3d b, Point3d c, out Point3d center, out double R)
        {
            center = Point3d.Origin; R = 0;
            Vector3d ab = b - a, ac = c - a;
            Vector3d n = Vector3d.CrossProduct(ab, ac);
            if (n.Length < 1e-9) return false;
            Plane pl = new Plane(a, n);
            double u1, v1, u2, v2;
            pl.ClosestParameter(b, out u1, out v1);
            pl.ClosestParameter(c, out u2, out v2);
            double bx = u1, by = v1, cx = u2, cy = v2;
            double d = 2 * (bx * cy - by * cx);
            if (Math.Abs(d) < 1e-12) return false;
            double ux = (cy * (bx * bx + by * by) - by * (cx * cx + cy * cy)) / d;
            double uy = (bx * (cx * cx + cy * cy) - cx * (bx * bx + by * by)) / d;
            center = pl.PointAt(ux, uy);
            R = center.DistanceTo(a);
            return true;
        }

        protected override Bitmap Icon => null;
        public override Guid ComponentGuid => new Guid("bac78788-0141-4874-b1d6-ffa0555dd378");
    }
}
