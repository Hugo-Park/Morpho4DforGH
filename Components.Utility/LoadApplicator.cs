using System;
using System.Collections.Generic;
using System.Drawing;
using Grasshopper.Kernel;
using Morpho4D.Models;
using Rhino.Geometry;

namespace _Morpho4D
{
    /// <summary>
    /// 복셀에 점하중 또는 중력 하중을 적용한다.
    /// VoxelCell.appliedLoad 필드를 설정해 Solver의 calculateTotalEnergy/Gradient에서 -F·x 항으로 반영됨.
    /// </summary>
    public class LoadApplicatorComponent : GH_Component
    {
        public LoadApplicatorComponent()
          : base("Load Applicator", "Load",
              "복셀에 점하중(중력 포함)을 적용한다. Solver에서 퍼텐셜 에너지 -F·x 항으로 반영됨.",
              "Morpho4D", "Utility")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Voxels", "VX", "입력 복셀 리스트", GH_ParamAccess.list);
            pManager.AddVectorParameter("Load Vector", "F",
                "단위 복셀당 힘 벡터 (N). 중력은 (0,0,-mg) 방향.", GH_ParamAccess.item, Vector3d.Zero);
            pManager.AddBrepParameter("Load Region", "R",
                "하중을 적용할 영역 Brep. 미입력 시 전체 복셀에 적용.", GH_ParamAccess.item);
            pManager[2].Optional = true;
            pManager.AddBooleanParameter("Gravity", "G",
                "true이면 Z 방향 중력(9.81 m/s²×질량) 추가 적용", GH_ParamAccess.item, false);
            pManager.AddNumberParameter("Mass Per Voxel", "m",
                "복셀당 질량 (kg). Gravity=true일 때만 사용.", GH_ParamAccess.item, 0.001);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Voxels", "VX", "하중이 적용된 복셀 리스트", GH_ParamAccess.list);
            pManager.AddIntegerParameter("Loaded Count", "n", "하중 적용된 복셀 수", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            var voxelGoos = new List<VoxelCellGoo>();
            Vector3d loadVec = Vector3d.Zero;
            Brep region = null;
            bool gravity = false;
            double massPerVoxel = 0.001;

            if (!DA.GetDataList(0, voxelGoos)) return;
            DA.GetData(1, ref loadVec);
            DA.GetData(2, ref region);
            DA.GetData(3, ref gravity);
            DA.GetData(4, ref massPerVoxel);

            if (gravity) loadVec += new Vector3d(0, 0, -9.81 * massPerVoxel);

            int loaded = 0;
            var outGoos = new List<VoxelCellGoo>();

            foreach (var g in voxelGoos)
            {
                if (g?.Value == null) continue;
                var v = g.Value;

                bool inRegion = (region == null) || region.IsPointInside(v.initialPoint, 0.01, true);
                if (inRegion)
                {
                    v.appliedLoad = loadVec;
                    loaded++;
                }

                outGoos.Add(new VoxelCellGoo(v));
            }

            DA.SetDataList(0, outGoos);
            DA.SetData(1, loaded);
        }

        protected override Bitmap Icon => null;
        public override Guid ComponentGuid => new Guid("AAAAAAA2-1111-2222-3333-444444444444");
    }
}
