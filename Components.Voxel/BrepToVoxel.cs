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
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Voxels", "VX", "Output Voxels", GH_ParamAccess.list);
            pManager.AddIntegerParameter("Count", "C", "Number of Voxels", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            Brep inputBrep = null;
            double voxelSize = 1.0;

            if (!DA.GetData(0, ref inputBrep)) { return; }
            if (!DA.GetData(1, ref voxelSize)) { return; }

            List<VoxelCell> result = Morpho4D.Models.Voxelizer.createVoxels(inputBrep, voxelSize);

            List<VoxelCellGoo> goos = new List<VoxelCellGoo>();

            int count = 0;
            foreach (VoxelCell v in result)
            {
                VoxelCellGoo package = new VoxelCellGoo(v);
                goos.Add(package);
                count++;
            }

            DA.SetDataList(0, goos);
            DA.SetData(1, count);
        }

        protected override System.Drawing.Bitmap Icon => null;

        public override Guid ComponentGuid => new Guid("01df7804-29cd-4520-98df-a2d2e6835585");
    }
}