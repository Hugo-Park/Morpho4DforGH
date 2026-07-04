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
    /// 여러 타임스텝의 시뮬레이션을 실행하고 프레임 캐시를 관리한다.
    /// BuildDeformedBoxMesh(G0)를 재사용해 각 프레임의 메쉬를 생성.
    /// </summary>
    public class SimulationPlayerComponent : GH_Component
    {
        private readonly Dictionary<int, (List<Point3d> pts, Mesh mesh)> _cache
            = new Dictionary<int, (List<Point3d>, Mesh)>();

        public SimulationPlayerComponent()
          : base("Simulation Player", "SimPlay",
              "타임스텝별 시뮬레이션 결과를 캐시하고 재생한다. SimulationTimer와 연결.",
              "Morpho4D", "Utility")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Voxels", "VX", "BilayerMaterial + SetFiberDirection 완료 복셀", GH_ParamAccess.list);
            pManager.AddGenericParameter("Stimulus", "S", "자극 객체", GH_ParamAccess.item);
            pManager.AddNumberParameter("Time", "T", "현재 시간 (SimulationTimer 출력)", GH_ParamAccess.item, 0.0);
            pManager.AddBooleanParameter("Clear Cache", "C", "true이면 캐시 초기화", GH_ParamAccess.item, false);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddMeshParameter("Frame Mesh", "M", "현재 프레임의 변형 메쉬", GH_ParamAccess.item);
            pManager.AddPointParameter("Frame Points", "P", "현재 프레임의 복셀 중심점", GH_ParamAccess.list);
            pManager.AddIntegerParameter("Cached Frames", "nC", "캐시된 프레임 수", GH_ParamAccess.item);
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

            // 시간을 100단위로 양자화해 캐시 키 생성
            int cacheKey = (int)(time * 100);
            if (!_cache.ContainsKey(cacheKey))
            {
                // 복셀 상태 초기화 후 시뮬레이션
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

        protected override Bitmap Icon => null;
        public override Guid ComponentGuid => new Guid("AAAAAAA5-1111-2222-3333-444444444444");
    }
}
