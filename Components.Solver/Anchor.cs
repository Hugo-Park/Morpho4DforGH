using System;
using System.Collections.Generic;

using Grasshopper;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Morpho4D.Models;
using Morpho4D.Solver;

namespace _Morpho4D
{
    public class Anchor : GH_Component
    {

        public Anchor()
          : base("Anchor", "A",
            "Assigns an 'isFixed' state to specific voxels. Fixed voxels act as rigid anchors that do not move during the simulation process, allowing the rest of the structure to morph or bend around them.",
            "Morpho4D", "04 Solver")
        {
        }

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Voxels", "VX", "Input Voxel List", GH_ParamAccess.list);
            pManager.AddPointParameter("Points", "P", "Points to assign fixed state (The closest voxels to these points will be locked as anchors)", GH_ParamAccess.list);
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Voxels", "VX", "Output Voxels", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            List<Point3d> inputPoints = new List<Point3d>();
            List<VoxelCellGoo> voxelGoos = new List<VoxelCellGoo>();

            if (!DA.GetDataList(0, voxelGoos)) { return; }
            if (!DA.GetDataList(1, inputPoints)) { return; }

            List<VoxelCell> voxels = new List<VoxelCell>();
            foreach (var goo in voxelGoos)
            {
                if (goo != null && goo.Value != null)
                {
                    voxels.Add(goo.Value);
                }
            }

            Rhino.Geometry.RTree tree = new Rhino.Geometry.RTree();
            
            for (int i = 0; i < voxels.Count; i++)
            {
                tree.Insert(voxels[i].currentPoint, i);
            }

            double searchRadius = voxels.Count > 0 ? voxels[0].voxelSize * 1.2 : 0.5;

            foreach(Point3d p in inputPoints)
            {
                tree.Search(new Sphere(p, searchRadius), (sender, args) =>
                {
                    voxels[args.Id].isFixed = true;
                });
            }
            
            DA.SetDataList(0, voxelGoos);
        }

        protected override System.Drawing.Bitmap Icon => IconLoader.Get("Anchor");

        public override Guid ComponentGuid => new Guid("a094b042-81a4-44bd-88f0-6f093ae56328");
    }
}