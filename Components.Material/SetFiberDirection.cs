using System;
using System.Collections.Generic;
using System.Drawing;
using Grasshopper.Kernel;
using Morpho4D.Models;
using Rhino.Geometry;

namespace _Morpho4D
{
    public class SetFiberDirectionComponent : GH_Component
    {
        public SetFiberDirectionComponent()
          : base("Set Fiber Direction", "Fiber",
              "Assigns fiber direction (fiberDir) and maximum eigenstrain (epsMax) to voxels with isActive=true.",
              "Morpho4D", "02 Material")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Voxels", "VX", "List of voxels passed through BilayerMaterial", GH_ParamAccess.list);
            pManager.AddVectorParameter("Fiber Direction", "D",
                "Fiber direction unit vector. Normalized automatically.", GH_ParamAccess.item, Vector3d.XAxis);
            pManager.AddNumberParameter("Eps Max", "ε",
                "Maximum eigenstrain (maximum strain when activation is complete). Negative for shrinkage.", GH_ParamAccess.item, 0.0);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Voxels", "VX", "List of voxels with fiber direction assigned", GH_ParamAccess.list);
            pManager.AddIntegerParameter("Updated Count", "n", "Number of updated active voxels", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            var voxelGoos = new List<VoxelCellGoo>();
            Vector3d fiberDir = Vector3d.XAxis;
            double epsMax = 0.0;

            if (!DA.GetDataList(0, voxelGoos)) return;
            DA.GetData(1, ref fiberDir);
            DA.GetData(2, ref epsMax);

            if (fiberDir.Length < 1e-9)
            {
                this.AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Fiber Direction is a zero vector.");
                return;
            }
            fiberDir.Unitize();

            int updated = 0;
            var outGoos = new List<VoxelCellGoo>();

            foreach (var g in voxelGoos)
            {
                if (g?.Value == null) continue;
                var v = g.Value;
                if (v.isActive)
                {
                    v.fiberDir = fiberDir;
                    v.epsMax = epsMax;
                    updated++;
                }
                outGoos.Add(new VoxelCellGoo(v));
            }

            DA.SetDataList(0, outGoos);
            DA.SetData(1, updated);
        }

        protected override Bitmap Icon => IconLoader.Get("SetFiberDirection");
        public override Guid ComponentGuid => new Guid("5fb060cd-157c-4abb-9f82-4e83c1cba00a");
    }
}
