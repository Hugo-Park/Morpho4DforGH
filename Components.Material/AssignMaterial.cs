using System;
using System.Collections.Generic;

using Grasshopper;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Morpho4D.Models;

namespace _Morpho4D
{
    /// <summary>
    /// [P0-1 다재료] 입력된 영역(Region Brep) 안에 들어오는 복셀들에 대해 assignedMaterial 을 덮어쓴다.
    /// BrepToVoxel 로 기본 재료를 깐 뒤, 이 컴포넌트를 체인으로 연결해 부분 영역에 다른 재료를 지정할 수 있다.
    /// (VoxelCell 은 참조 타입이므로 동일 객체를 통과시키며 mutate 한다 - Anchor 와 동일한 패턴)
    /// </summary>
    public class AssignMaterial : GH_Component
    {
        public AssignMaterial()
          : base("Assign Material", "AsgnMat",
            "Override the material of voxels that fall inside a region Brep. Chain after 'Brep to Voxel' to build multi-material bodies.",
            "Morpho4D", "Material")
        {
        }

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Voxels", "VX", "Input voxel list", GH_ParamAccess.list);
            pManager.AddGenericParameter("Material", "M", "Material to assign to voxels inside the region", GH_ParamAccess.item);
            pManager.AddBrepParameter("Region", "R", "Closed Brep region; voxels whose center is inside get the material", GH_ParamAccess.item);
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Voxels", "VX", "Output voxels (material overridden inside the region)", GH_ParamAccess.list);
            pManager.AddTextParameter("Inspection", "?", "Assignment summary", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            this.ClearRuntimeMessages();

            List<VoxelCellGoo> goos = new List<VoxelCellGoo>();
            Material mat = null;
            Brep region = null;

            if (!DA.GetDataList(0, goos)) { return; }
            if (!DA.GetData(1, ref mat)) { return; }
            if (!DA.GetData(2, ref region)) { return; }

            if (mat == null) { this.AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Material is null"); return; }
            if (region == null || !region.IsSolid)
            {
                this.AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Region Brep is not closed/solid; IsPointInside may be unreliable.");
            }

            List<VoxelCell> voxels = new List<VoxelCell>();
            foreach (var goo in goos)
            {
                if (goo != null && goo.Value != null) voxels.Add(goo.Value);
            }

            double tol = voxels.Count > 0 ? voxels[0].voxelSize * 0.01 : 0.001;
            if (tol <= 0) tol = 0.001;

            int assigned = 0;
            foreach (VoxelCell v in voxels)
            {
                if (region != null && region.IsPointInside(v.currentPoint, tol, false))
                {
                    v.assignedMaterial = mat;
                    assigned++;
                }
            }

            List<string> stat = new List<string>();
            stat.Add(string.Format("Assigned Material: {0}", mat.getMaterialName()));
            stat.Add(string.Format("Voxels in region: {0} / {1}", assigned, voxels.Count));

            DA.SetDataList(0, goos);
            DA.SetDataList(1, stat);
        }

        protected override System.Drawing.Bitmap Icon => null;

        public override Guid ComponentGuid => new Guid("91e1a414-6b41-405d-8ad9-e2bbffbdb929");
    }
}
