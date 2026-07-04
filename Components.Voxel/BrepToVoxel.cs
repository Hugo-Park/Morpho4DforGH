using System;
using System.Collections.Generic;

using Grasshopper;
using Grasshopper.Kernel;
using Rhino.Geometry;

using Morpho4D.Models;
using Rhino.Commands;
using Rhino.Display;

namespace _Morpho4D
{
    public class BrepToVoxel : GH_Component
    {
        public BrepToVoxel()
          : base("Brep to Voxel", "B/V",
            "Convert Brep objects to Voxels",
            "Morpho4D", "01 Voxel")
        {
        }

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddBrepParameter("Brep", "B", "Input Brep (Only One Brep Allowed)", GH_ParamAccess.list);
            pManager.AddNumberParameter("Size", "S", "Size of Voxel", GH_ParamAccess.item);
            pManager.AddGenericParameter("Material", "M", "Material to Assign (Only One Material Allowed)", GH_ParamAccess.list);
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Voxels", "VX", "Output Voxels", GH_ParamAccess.list);
            pManager.AddTextParameter("Inspection", "?", "Inspection of Voxels", GH_ParamAccess.list);
        }

        private Brep previewBrep = null;
        private Material previewMat = null;
        private DisplayMaterial prevColor = null;
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            this.ClearRuntimeMessages();

            double voxelSize = 0.0;
            List<Brep> inputBrep = new List<Brep>();
            List<Material> inputMat = new List<Material>();

            if (!DA.GetDataList(0, inputBrep)) { return; }
            if (!DA.GetData(1, ref voxelSize)) { return; }
            if (!DA.GetDataList(2, inputMat)) { return; }

            // Input Only One Brep Check
            if(inputBrep.Count >= 2)
            {
                this.AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Input Only One Brep");
                previewBrep = null;
                return;
            }

            // Input Only One Material Check
            if(inputMat.Count >= 2)
            {
                this.AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Input Only One Material");
                previewMat = null;
                return ;
            }

            if(inputBrep.Count == 1 && inputMat.Count == 1)
            {
                previewBrep = inputBrep[0];
                previewMat = inputMat[0];

                List<VoxelCell> result = Morpho4D.Models.Voxelizer.createVoxels(inputBrep[0], voxelSize);
                List<VoxelCellGoo> goos = new List<VoxelCellGoo>();

                int count = 0;
                foreach (VoxelCell v in result)
                {
                    v.assignedMaterial = inputMat[0];
                    VoxelCellGoo package = new VoxelCellGoo(v);
                    goos.Add(package);
                    count++;
                }

                List<string> statList = new List<string>();
                statList.Add(string.Format("Assigned Material: {0}", inputMat[0].getMaterialName()));
                statList.Add(string.Format("Voxel Count: {0}", count));

                prevColor = new DisplayMaterial(inputMat[0].getPreviewColor());

                DA.SetDataList(0, goos);
                DA.SetDataList(1, statList);
            }            
        }

        public override void DrawViewportMeshes(IGH_PreviewArgs args)
        {
            if(previewBrep != null && prevColor != null)
            {
                args.Display.DrawBrepShaded(previewBrep, prevColor);
            }
        }

        public override void DrawViewportWires(IGH_PreviewArgs args)
        {
            if(previewBrep != null && previewMat != null)
            {
                args.Display.DrawBrepWires(previewBrep, previewMat.getPreviewColor(), -1);
            }
        }
        protected override System.Drawing.Bitmap Icon => null;

        public override Guid ComponentGuid => new Guid("01df7804-29cd-4520-98df-a2d2e6835585");
    }
}