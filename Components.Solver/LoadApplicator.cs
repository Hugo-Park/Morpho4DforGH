using System;
using System.Collections.Generic;
using System.Drawing;
using Grasshopper.Kernel;
using Morpho4D.Models;
using Rhino.Geometry;

namespace _Morpho4D
{
    /// <summary>
    /// Applies point loads or gravity loads to voxels.
    /// By setting the VoxelCell.appliedLoad field, it is reflected as an -F·x term in the Solver's calculateTotalEnergy/Gradient.
    /// </summary>
    public class LoadApplicatorComponent : GH_Component
    {
        public LoadApplicatorComponent()
          : base("Load Applicator", "Load",
              "Applies point loads (including gravity) to voxels. Reflected as a potential energy -F·x term in the Solver.",
              "Morpho4D", "04 Solver")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Voxels", "VX", "Input voxel list", GH_ParamAccess.list);
            pManager.AddVectorParameter("Load Vector", "F",
                "Force vector per voxel (N). Gravity is in the (0,0,-mg) direction.", GH_ParamAccess.item, Vector3d.Zero);
            pManager.AddBrepParameter("Load Region", "R",
                "Brep region to apply the load. If not provided, applies to all voxels.", GH_ParamAccess.item);
            pManager[2].Optional = true;
            pManager.AddBooleanParameter("Gravity", "G",
                "If true, additionally applies Z-direction gravity (9.81 m/s² × mass)", GH_ParamAccess.item, false);
            pManager.AddNumberParameter("Mass Per Voxel", "m",
                "Mass per voxel (kg). Only used when Gravity=true.", GH_ParamAccess.item, 0.001);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Voxels", "VX", "List of voxels with applied loads", GH_ParamAccess.list);
            pManager.AddIntegerParameter("Loaded Count", "n", "Number of loaded voxels", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            var voxelGoos = new List<VoxelCellGoo>();
            Vector3d loadVec = Vector3d.Zero;
            Brep region = null;
            bool gravity = false;
            double massPerVoxel = 0.001;

            if (!DA.GetDataList(0, voxelGoos)) return;
            DA.GetData(1, ref loadVec);
            DA.GetData(2, ref region);
            DA.GetData(3, ref gravity);
            DA.GetData(4, ref massPerVoxel);

            if (gravity) loadVec += new Vector3d(0, 0, -9.81 * massPerVoxel);

            int loaded = 0;
            var outGoos = new List<VoxelCellGoo>();

            foreach (var g in voxelGoos)
            {
                if (g?.Value == null) continue;
                var v = g.Value;

                bool inRegion = (region == null) || region.IsPointInside(v.initialPoint, 0.01, false);
                if (inRegion)
                {
                    v.appliedLoad = loadVec;
                    loaded++;
                }

                outGoos.Add(new VoxelCellGoo(v));
            }

            DA.SetDataList(0, outGoos);
            DA.SetData(1, loaded);
        }

        protected override Bitmap Icon => null;
        public override Guid ComponentGuid => new Guid("c12d2c78-1a85-49f9-9cd1-24257fb77bf5");
    }
}
