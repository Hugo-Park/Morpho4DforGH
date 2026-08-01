using System;
using System.Collections.Generic;
using Morpho4D.Models;
using Grasshopper;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Grasshopper.GUI.SettingsControls;
using Rhino.Display;

namespace _Morpho4D
{
    public class ShowVoxels : GH_Component
    {
        public ShowVoxels()
          : base("Show Voxels", "sVX",
            "Show converted Voxels",
            "Morpho4D", "01 Voxel")
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
            pManager.AddPointParameter("Points", "Ps", "Output Voxels(Point)", GH_ParamAccess.list); // Voxel의 중심점
        }

        private Mesh visualMesh;
        private DisplayMaterial prevColor;
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            List<VoxelCellGoo> goos = new List<VoxelCellGoo>();
            List<Point3d> voxelPoint = new List<Point3d>();
            double size = 0.0;

            if (!DA.GetDataList(0, goos)) { return; }
            if (!DA.GetData(1, ref size)) { return; }

            List<VoxelCell> voxelList = new List<VoxelCell>();

            foreach (VoxelCellGoo goo in goos)
            {
                if (goo != null && goo.Value != null)
                {
                    voxelList.Add(goo.Value);
                    voxelPoint.Add(goo.Value.initialPoint);
                }
            }

            visualMesh = Morpho4D.Models.Voxelizer.showVoxels(voxelList, size);

            if (voxelList.Count > 0 && voxelList[0].assignedMaterial != null)
            {
                prevColor = new DisplayMaterial(voxelList[0].assignedMaterial.getPreviewColor());
            }

            DA.SetData(0, visualMesh);
            DA.SetDataList(1, voxelPoint);
        }

        /// <summary>
        /// GH_Component 클래스 함수 override
        /// </summary>
        /// <param name="args"></param>
        public override void DrawViewportMeshes(IGH_PreviewArgs args)
        {
            args.Display.DrawMeshShaded(visualMesh, prevColor);
        }
        protected override System.Drawing.Bitmap Icon => _Morpho4D.IconLoader.Get("ShowVoxels");

        public override Guid ComponentGuid => new Guid("7C721802-72FB-4BCE-B06E-B3BBAE441DA8");
    }
}
