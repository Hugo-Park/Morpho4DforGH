using System;
using System.Collections.Generic;

using Grasshopper;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Morpho4D.Models;

namespace _Morpho4D
{
    /// <summary>
    /// 4D 프린팅 자체 변형 외에 중력이나 상단 적재 하중이 있을 때, 각 복셀 노드에 외력 벡터(appliedLoad)를 주입한다.
    /// 솔버는 이 하중을 포텐셜 에너지 E_ext = -F·x 로 목적 함수에 더한다 (Solver.cs 수정분).
    /// Points가 비어 있으면 모든 복셀에 동일 하중(예: 중력)을 적용하고, Points가 있으면 그 점들에 가장 가까운 복셀에만 적용한다.
    /// SET(덮어쓰기) 시맨틱이라 재실행해도 누적되지 않는다. 하중 제거는 영벡터를 전체에 적용.
    /// </summary>
    public class LoadApplicator : GH_Component
    {
        public LoadApplicator()
          : base("Load Applicator", "Load",
            "Inject external load vectors (e.g. gravity, top load) onto voxels. The solver adds them as potential energy E = -F.x.",
            "Morpho4D", "Solver")
        {
        }

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Voxels", "VX", "Input voxel list", GH_ParamAccess.list);
            pManager.AddVectorParameter("Force", "F", "Force vector applied to each targeted voxel", GH_ParamAccess.item);
            pManager.AddPointParameter("Points", "P", "Optional. Apply force only to voxels nearest these points. If empty, force is applied to ALL voxels (uniform load).", GH_ParamAccess.list);
            pManager[2].Optional = true;
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Voxels", "VX", "Output voxels carrying the applied load", GH_ParamAccess.list);
            pManager.AddTextParameter("Inspection", "?", "Load summary", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            this.ClearRuntimeMessages();

            List<VoxelCellGoo> goos = new List<VoxelCellGoo>();
            Vector3d force = Vector3d.Zero;
            List<Point3d> points = new List<Point3d>();

            if (!DA.GetDataList(0, goos)) { return; }
            if (!DA.GetData(1, ref force)) { return; }
            DA.GetDataList(2, points); // optional

            List<VoxelCell> voxels = new List<VoxelCell>();
            foreach (var goo in goos)
            {
                if (goo != null && goo.Value != null) voxels.Add(goo.Value);
            }
            if (voxels.Count == 0) { this.AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No voxels"); return; }

            int applied = 0;

            if (points == null || points.Count == 0)
            {
                // 균일 하중: 모든 복셀에 동일 적용
                foreach (VoxelCell v in voxels) { v.appliedLoad = force; applied++; }
            }
            else
            {
                // 지정 점에 가장 가까운 복셀에만 적용 (Anchor와 동일한 RTree 방식)
                RTree tree = new RTree();
                for (int i = 0; i < voxels.Count; i++) tree.Insert(voxels[i].currentPoint, i);

                double searchRadius = voxels[0].voxelSize * 1.2;
                if (searchRadius <= 0) searchRadius = 0.5;

                HashSet<int> hit = new HashSet<int>();
                foreach (Point3d p in points)
                {
                    tree.Search(new Sphere(p, searchRadius), (sender, args) => { hit.Add(args.Id); });
                }
                foreach (int id in hit) { voxels[id].appliedLoad = force; applied++; }
            }

            List<string> stat = new List<string>();
            stat.Add(string.Format("Force: ({0:f3})", force));
            stat.Add(string.Format("Loaded voxels: {0} / {1}", applied, voxels.Count));
            if (applied == 0) stat.Add("WARNING: no voxels loaded (check point proximity / voxel size).");

            DA.SetDataList(0, goos);
            DA.SetDataList(1, stat);
        }

        protected override System.Drawing.Bitmap Icon => null;

        public override Guid ComponentGuid => new Guid("5aa88d73-d018-4d48-98f2-9caeb81a473d");
    }
}
