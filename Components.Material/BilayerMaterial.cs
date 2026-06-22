using System;
using System.Collections.Generic;

using Grasshopper;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Morpho4D.Models;

namespace _Morpho4D
{
    /// <summary>
    /// [P0-1 다재료 / P0-2 굽힘] 한 평면을 기준으로 복셀을 위/아래 두 그룹으로 나누고 각각 다른 재료를 지정한다.
    /// 이것이 4D 프린팅의 정석 케이스인 'bilayer'를 만든다. 위층(예: 팽창 hydrogel)과 아래층(예: 비팽창)의
    /// 차등 팽창 + (P0-2 대칭 스프링) 으로 시트가 휘어진다.
    /// 사용법: 얇은 판형 Brep을 복셀화 -> 두께 중앙에 Base Plane을 놓고 위=능동재료, 아래=수동재료.
    /// </summary>
    public class BilayerMaterial : GH_Component
    {
        public BilayerMaterial()
          : base("Bilayer Material", "Bilayer",
            "Split voxels by a plane and assign a top and bottom material. Creates a bilayer whose differential expansion produces bending.",
            "Morpho4D", "Material")
        {
        }

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Voxels", "VX", "Input voxel list", GH_ParamAccess.list);
            pManager.AddPlaneParameter("Base Plane", "Pl", "Split plane. Voxels on the +normal side get the Top material, the others get Bottom.", GH_ParamAccess.item, Plane.WorldXY);
            pManager.AddGenericParameter("Top Material", "Mt", "Material for voxels on the +normal side of the plane", GH_ParamAccess.item);
            pManager.AddGenericParameter("Bottom Material", "Mb", "Material for voxels on the -normal side of the plane", GH_ParamAccess.item);
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Voxels", "VX", "Output voxels with bilayer material assignment", GH_ParamAccess.list);
            pManager.AddTextParameter("Inspection", "?", "Split summary", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            this.ClearRuntimeMessages();

            List<VoxelCellGoo> goos = new List<VoxelCellGoo>();
            Plane plane = Plane.WorldXY;
            Material topMat = null;
            Material botMat = null;

            if (!DA.GetDataList(0, goos)) { return; }
            if (!DA.GetData(1, ref plane)) { return; }
            if (!DA.GetData(2, ref topMat)) { return; }
            if (!DA.GetData(3, ref botMat)) { return; }

            if (topMat == null || botMat == null)
            {
                this.AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Both Top and Bottom material are required");
                return;
            }

            List<VoxelCell> voxels = new List<VoxelCell>();
            foreach (var goo in goos)
            {
                if (goo != null && goo.Value != null) voxels.Add(goo.Value);
            }

            int top = 0, bottom = 0;
            foreach (VoxelCell v in voxels)
            {
                // Plane.DistanceTo는 부호 있는 거리(법선 방향이 +)를 반환
                double signed = plane.DistanceTo(v.currentPoint);
                if (signed >= 0.0) { v.assignedMaterial = topMat; top++; }
                else { v.assignedMaterial = botMat; bottom++; }
            }

            List<string> stat = new List<string>();
            stat.Add(string.Format("Top ({0}): {1} voxels", topMat.getMaterialName(), top));
            stat.Add(string.Format("Bottom ({0}): {1} voxels", botMat.getMaterialName(), bottom));
            stat.Add(string.Format("Plane origin: ({0:f3})", plane.Origin));
            if (top == 0 || bottom == 0)
                stat.Add("WARNING: one side is empty - move the plane into the body's thickness.");

            DA.SetDataList(0, goos);
            DA.SetDataList(1, stat);
        }

        protected override System.Drawing.Bitmap Icon => null;

        public override Guid ComponentGuid => new Guid("261e353b-f65e-422d-b2c8-d5175aa255b7");
    }
}
