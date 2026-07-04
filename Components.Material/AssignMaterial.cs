using System;
using System.Collections.Generic;
using System.Drawing;
using Grasshopper.Kernel;
using Morpho4D.Models;
using Rhino.Geometry;

namespace _Morpho4D
{
    public class AssignMaterialComponent : GH_Component
    {
        public AssignMaterialComponent()
          : base("Assign Material", "Assign",
              "Brep 영역 기반으로 복셀에 재료를 할당한다. 여러 재료를 지역별로 지정할 때 사용.",
              "Morpho4D", "Material")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Voxels", "VX", "입력 복셀 리스트", GH_ParamAccess.list);
            pManager.AddGenericParameter("Material", "M", "지정할 재료", GH_ParamAccess.item);
            pManager.AddBrepParameter("Region", "R",
                "재료를 지정할 Brep 영역. 미입력 시 전체 복셀에 적용.",
                GH_ParamAccess.item);
            pManager[2].Optional = true;
            pManager.AddBooleanParameter("Active", "A",
                "이 재료를 active(SMP) 재료로 표시할지 여부", GH_ParamAccess.item, false);
            pManager[3].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Voxels", "VX", "재료가 지정된 복셀 리스트", GH_ParamAccess.list);
            pManager.AddIntegerParameter("Assigned Count", "n", "지정된 복셀 수", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            var voxelGoos = new List<VoxelCellGoo>();
            Material mat = null;
            Brep region = null;
            bool isActive = false;

            if (!DA.GetDataList(0, voxelGoos)) return;
            if (!DA.GetData(1, ref mat)) return;
            DA.GetData(2, ref region);
            DA.GetData(3, ref isActive);

            int assigned = 0;
            var outGoos = new List<VoxelCellGoo>();

            foreach (var g in voxelGoos)
            {
                if (g?.Value == null) continue;
                var v = g.Value;

                bool inRegion = (region == null)
                    || region.IsPointInside(v.initialPoint, 0.01, true);

                if (inRegion)
                {
                    v.assignedMaterial = mat;
                    v.isActive = isActive;
                    assigned++;
                }

                outGoos.Add(new VoxelCellGoo(v));
            }

            DA.SetDataList(0, outGoos);
            DA.SetData(1, assigned);
        }

        protected override Bitmap Icon => null;
        public override Guid ComponentGuid => new Guid("AAAAAAA8-1111-2222-3333-444444444444");
    }
}
