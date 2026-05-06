using System;
using System.Collections.Generic;

using Grasshopper;
using Grasshopper.Kernel;
using Rhino.Geometry;

using Morpho4D.Models;
using Rhino.Commands;

namespace _Morpho4D
{
    public class BrepToVoxel : GH_Component
    {
        public BrepToVoxel()
          : base("Brep to Voxel", "B/V",
            "Convert Brep objects to Voxels",
            "Morpho4D", "Voxel")
        {
        }

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddBrepParameter("Brep", "B", "Input Brep", GH_ParamAccess.item);
            pManager.AddNumberParameter("Size", "S", "Size of Voxel", GH_ParamAccess.item);
            pManager.AddGenericParameter("Material", "M", "Material to Assign", GH_ParamAccess.item);
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Voxels", "VX", "Output Voxels", GH_ParamAccess.list);
            pManager.AddIntegerParameter("Count", "N", "Number of Voxels", GH_ParamAccess.item);
            pManager.AddTextParameter("Inspection", "?", "Inspection of Voxels", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            Brep inputBrep = null;
            Material assignedMat = null;
            double voxelSize = 1.0;

            if (!DA.GetData(0, ref inputBrep)) { return; }
            if (!DA.GetData(1, ref voxelSize)) { return; }
            if (!DA.GetData(2, ref assignedMat)) { return; }


            List<VoxelCell> result = Morpho4D.Models.Voxelizer.createVoxels(inputBrep, voxelSize);

            List<VoxelCellGoo> goos = new List<VoxelCellGoo>();

            int count = 0;
            foreach (VoxelCell v in result)
            {
                v.assignedMaterial = assignedMat;
                VoxelCellGoo package = new VoxelCellGoo(v);
                goos.Add(package);
                count++;
            }

            List<string> statList = new List<string>();
            statList.Add(string.Format("Assigned Material: {0}", assignedMat.getMaterialName()));
            statList.Add(string.Format("Voxel Count: {0}", count));

            DA.SetDataList(0, goos);
            DA.SetData(1, count);
            DA.SetDataList(2, statList);
        }

        protected override System.Drawing.Bitmap Icon => null;

        public override Guid ComponentGuid => new Guid("01df7804-29cd-4520-98df-a2d2e6835585");
    }
}