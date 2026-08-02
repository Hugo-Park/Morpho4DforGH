using System;
using System.Collections.Generic;
using System.Drawing;
using Grasshopper.Kernel;
using Morpho4D.Models;
using Rhino.Geometry;

namespace _Morpho4D
{
    /// <summary>
    /// Plans the X2D dual extrusion printing path on a voxel lattice.
    /// T0 = passive(PLA), T1 = active(SMP)
    /// </summary>
    public class PrintPathPlannerComponent : GH_Component
    {
        public PrintPathPlannerComponent()
          : base("Print Path Planner", "PathPlan",
              "Plans the X2D (dual extrusion) printing path on a Bilayer voxel lattice. T0=passive, T1=active.",
              "Morpho4D", "07 Fabrication")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Voxels", "VX", "List of voxels with completed BilayerMaterial + SetFiberDirection", GH_ParamAccess.list);
            pManager.AddNumberParameter("Layer Height", "LH", "Layer height (mm)", GH_ParamAccess.item, 0.2);
            pManager.AddNumberParameter("Line Width", "LW", "Extrusion line width (mm)", GH_ParamAccess.item, 0.4);
            pManager.AddNumberParameter("Print Speed", "PS", "Print speed (mm/min)", GH_ParamAccess.item, 2400.0);
            pManager.AddNumberParameter("Travel Speed", "TS", "Travel speed (mm/min)", GH_ParamAccess.item, 7200.0);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddCurveParameter("T0 Paths", "P0", "passive(PLA) extrusion path (T0)", GH_ParamAccess.list);
            pManager.AddCurveParameter("T1 Paths", "P1", "active(SMP) extrusion path (T1)", GH_ParamAccess.list);
            pManager.AddIntegerParameter("Layer Count", "nL", "Number of layers", GH_ParamAccess.item);
            pManager.AddTextParameter("Stats", "?", "Path statistics", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            var voxelGoos = new List<VoxelCellGoo>();
            double lh = 0.2, lw = 0.4, ps = 2400.0, ts = 7200.0;

            if (!DA.GetDataList(0, voxelGoos)) return;
            DA.GetData(1, ref lh);
            DA.GetData(2, ref lw);
            DA.GetData(3, ref ps);
            DA.GetData(4, ref ts);

            var voxels = new List<VoxelCell>();
            foreach (var g in voxelGoos) if (g?.Value != null) voxels.Add(g.Value);
            if (voxels.Count == 0) return;

            // Group layers in Z direction
            var layerMap = new SortedDictionary<double, List<VoxelCell>>();
            double tol = voxels[0].voxelSize * 0.1;

            foreach (var v in voxels)
            {
                double z = Math.Round(v.initialPoint.Z / tol) * tol;
                if (!layerMap.ContainsKey(z)) layerMap[z] = new List<VoxelCell>();
                layerMap[z].Add(v);
            }

            var t0Paths = new List<Curve>();
            var t1Paths = new List<Curve>();

            foreach (var kvp in layerMap)
            {
                var layer = kvp.Value;

                // Separate active/passive
                var t0Pts = new List<Point3d>();
                var t1Pts = new List<Point3d>();

                layer.Sort((a, b) => {
                    int xc = a.initialPoint.X.CompareTo(b.initialPoint.X);
                    return xc != 0 ? xc : a.initialPoint.Y.CompareTo(b.initialPoint.Y);
                });

                foreach (var v in layer)
                {
                    if (v.isActive) t1Pts.Add(v.initialPoint);
                    else t0Pts.Add(v.initialPoint);
                }

                if (t0Pts.Count >= 2)
                    t0Paths.Add(new PolylineCurve(t0Pts));
                if (t1Pts.Count >= 2)
                    t1Paths.Add(new PolylineCurve(t1Pts));
            }

            var stats = new List<string>
            {
                $"Number of layers: {layerMap.Count}",
                $"Number of T0(PLA) paths: {t0Paths.Count}",
                $"Number of T1(SMP) paths: {t1Paths.Count}",
                $"Layer height: {lh} mm",
                $"Extrusion line width: {lw} mm",
                $"Print speed: {ps} mm/min",
                $"Travel speed: {ts} mm/min"
            };

            DA.SetDataList(0, t0Paths);
            DA.SetDataList(1, t1Paths);
            DA.SetData(2, layerMap.Count);
            DA.SetDataList(3, stats);
        }
        protected override System.Drawing.Bitmap Icon => _Morpho4D.IconLoader.Get("PrintPathPlanner");
        public override Guid ComponentGuid => new Guid("04ad844c-bb0d-4b96-a8c1-7e5b30c4b09c");
    }
}

