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
              "Assigns material to voxels based on a Brep region. Used to assign multiple materials by region.",
              "Morpho4D", "02 Material")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Voxels", "VX", "Input voxel list", GH_ParamAccess.list);
            pManager.AddGenericParameter("Material", "M", "Material to assign", GH_ParamAccess.item);
            pManager.AddBrepParameter("Region", "R",
                "Brep region to assign material. If not provided, applies to all voxels.",
                GH_ParamAccess.item);
            pManager[2].Optional = true;
            pManager.AddBooleanParameter("Active", "A",
                "Whether to mark this material as an active (SMP) material", GH_ParamAccess.item, false);
            pManager[3].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Voxels", "VX", "List of voxels with material assigned", GH_ParamAccess.list);
            pManager.AddIntegerParameter("Assigned Count", "n", "Number of assigned voxels", GH_ParamAccess.item);
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

        protected override Bitmap Icon => IconLoader.Get("AssignMaterial");
        public override Guid ComponentGuid => new Guid("762b5bda-2599-4cc2-b1d8-3e61f5b0685a");
    }
}
