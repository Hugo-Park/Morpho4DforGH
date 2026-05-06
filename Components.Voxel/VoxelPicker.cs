using System;
using System.Collections.Generic;

using Grasshopper;
using Grasshopper.Kernel;
using Morpho4D.Models;
using Rhino.Geometry;

namespace _Morpho4D
{
    public class VoxelPicker : GH_Component
    {

        public VoxelPicker()
          : base("Voxel Picker", "vxPick",
            "Pick and Retrive a Specific Voxel from a List by its Unique ID for Detailed Inspection",
            "Morpho4D", "Voxel")
        {
        }

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Voxels", "VX", "List of Voxels", GH_ParamAccess.list);
            pManager.AddIntegerParameter("Voxel ID", "vxID", "Specific Voxel ID", GH_ParamAccess.item, 0);
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Inspection", "?", "Inspection of One Specific Voxel", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            int voxelID = 0;
            List<VoxelCellGoo> goos = new List<VoxelCellGoo>();

            if (!DA.GetDataList(0, goos)) { return; }
            if (!DA.GetData(1, ref voxelID)) { return; }

            List<String> statList = new List<String>();
            statList.Add(string.Format("Voxel ID: {0}", voxelID));
            statList.Add(string.Format("Initial Point: {0:f3}", goos[voxelID].Value.initialPoint));
            statList.Add(string.Format("Current Point: {0:f3}", goos[voxelID].Value.currentPoint));
            // 이후 추가적으로 어떤 속성을 출력할지는 정해야 됨

            DA.SetDataList(0, statList);
        }

        protected override System.Drawing.Bitmap Icon => null;

        public override Guid ComponentGuid => new Guid("20B11023-DA29-4A23-8803-C6D4AF86F7CE");
    }
}