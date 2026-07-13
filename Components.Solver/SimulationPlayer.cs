using System;
using System.Collections.Generic;
using System.Drawing;
using Grasshopper.Kernel;
using Morpho4D.Models;
using Morpho4D.Solver;
using Rhino.Geometry;

namespace _Morpho4D
{
    /// <summary>
    /// Executes simulations for multiple time steps and manages the frame cache.
    /// Reuses BuildDeformedBoxMesh(G0) to create a mesh for each frame.
    /// </summary>
    public class SimulationPlayerComponent : GH_Component
    {
        private readonly Dictionary<int, (List<Point3d> pts, Mesh mesh)> _cache
            = new Dictionary<int, (List<Point3d>, Mesh)>();

        public SimulationPlayerComponent()
          : base("Simulation Player", "SimPlay",
              "Caches and plays back simulation results per time step. Connects to SimulationTimer.",
              "Morpho4D", "04 Solver")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Voxels", "VX", "Completed voxels with BilayerMaterial + SetFiberDirection", GH_ParamAccess.list);
            pManager.AddGenericParameter("Stimulus", "S", "Stimulus object", GH_ParamAccess.item);
            pManager.AddNumberParameter("Time", "T", "Current time (SimulationTimer output)", GH_ParamAccess.item, 0.0);
            pManager.AddBooleanParameter("Clear Cache", "C", "If true, clears the cache", GH_ParamAccess.item, false);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddMeshParameter("Frame Mesh", "M", "Deformed mesh of the current frame", GH_ParamAccess.item);
            pManager.AddPointParameter("Frame Points", "P", "Voxel center points of the current frame", GH_ParamAccess.list);
            pManager.AddIntegerParameter("Cached Frames", "nC", "Number of cached frames", GH_ParamAccess.item);
            pManager.AddGenericParameter("Voxels", "VX", "Deformed Voxels", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            var voxelGoos = new List<VoxelCellGoo>();
            Stimulus stim = null;
            double time = 0.0;
            bool clearCache = false;

            if (!DA.GetDataList(0, voxelGoos)) return;
            if (!DA.GetData(1, ref stim)) return;
            DA.GetData(2, ref time);
            DA.GetData(3, ref clearCache);

            if (clearCache) _cache.Clear();

            var voxels = new List<VoxelCell>();
            foreach (var g in voxelGoos) if (g?.Value != null) voxels.Add(g.Value);
            if (voxels.Count == 0) return;

            // Quantize time to 100 units to create a cache key
            int cacheKey = (int)(time * 100);
            if (!_cache.ContainsKey(cacheKey))
            {
                // Simulate after initializing voxel states
                foreach (var v in voxels) v.currentPoint = v.initialPoint;
                var solver = new MorphoSolver(voxels);
                solver.setUpFromGrid(voxels);
                solver.execute(time, stim);

                var resultPts = solver.getResultPoints();
                var mesh = BuildDeformedBoxMesh(voxels, resultPts);
                _cache[cacheKey] = (resultPts, mesh);
            }

            var (pts, frameMesh) = _cache[cacheKey];
            DA.SetData(0, frameMesh);
            DA.SetDataList(1, pts);
            DA.SetData(2, _cache.Count);
            DA.SetDataList(3, voxelGoos);
        }

        private Mesh BuildDeformedBoxMesh(List<VoxelCell> voxels, List<Point3d> resultPoints)
        {
            Mesh result = new Mesh();
            if (voxels.Count == 0) return result;
            double size = voxels[0].voxelSize;
            for (int i = 0; i < voxels.Count; i++)
            {
                Point3d center = (i < resultPoints.Count) ? resultPoints[i] : voxels[i].currentPoint;
                Plane pl = new Plane(center, Vector3d.ZAxis);
                Interval iv = new Interval(-size / 2.0, size / 2.0);
                Mesh box = Mesh.CreateFromBox(new Box(pl, iv, iv, iv), 1, 1, 1);
                Color c = voxels[i].assignedMaterial?.getPreviewColor() ?? Color.Gray;
                box.VertexColors.Clear();
                for (int k = 0; k < box.Vertices.Count; k++) box.VertexColors.Add(c);
                result.Append(box);
            }
            return result;
        }

        protected override Bitmap Icon => IconLoader.Get("SimulationPlayer");
        public override Guid ComponentGuid => new Guid("8519efd0-7634-41bc-843d-84bf4d7a33d4");
    }
}
