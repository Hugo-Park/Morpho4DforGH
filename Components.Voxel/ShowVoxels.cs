using System;
using System.Collections.Generic;
using FourDSim.Models;
using Grasshopper;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace _4DPrintSim
{
    public class ShowVoxels : GH_Component
    {
        public ShowVoxels()
          : base("Show Voxels", "SV",
            "Show converted Voxels",
            "4DPrintSim", "Voxel")
        {
        }

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Voxels", "V", "Input Vocels", GH_ParamAccess.list);
            pManager.AddNumberParameter("Size", "S", "Size of visual Voxel", GH_ParamAccess.item);
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddMeshParameter("Mesh", "M", "Ouput Voxels(Mesh)", GH_ParamAccess.item); // Mesh 객체 하나로 합쳐서 내보냄
            pManager.AddPointParameter("Point", "P", "Output Voxels(Point)", GH_ParamAccess.list); // Voxel의 중심점
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            List<VoxelCellGoo> goos = new List<VoxelCellGoo>();
            List<Point3d> voxel_point = new List<Point3d>();
            double size = 1.0;

            if (!DA.GetDataList(0, goos)) { return; }
            if (!DA.GetData(1, ref size)) { return; }

            List<VoxelCell> voxel_list = new List<VoxelCell>();

            foreach (VoxelCellGoo goo in goos)
            {
                if (goo != null && goo.Value != null)
                {
                    voxel_list.Add(goo.Value);
                    voxel_point.Add(goo.Value.initial_point);
                }
            }

            Mesh visual_mesh = FourDSim.Models.Voxelizer.showVoxels(voxel_list, size);

            DA.SetData(0, visual_mesh);
            DA.SetDataList(1, voxel_point);

        }

        protected override System.Drawing.Bitmap Icon => null;

        public override Guid ComponentGuid => new Guid("7C721802-72FB-4BCE-B06E-B3BBAE441DA8");
    }
}