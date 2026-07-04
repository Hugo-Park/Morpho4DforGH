using System;
using System.Collections.Generic;
using System.Drawing;
using Grasshopper.Kernel;
using Morpho4D.Models;
using Rhino.Geometry;

namespace _Morpho4D
{
    public class BilayerMaterialComponent : GH_Component
    {
        public BilayerMaterialComponent()
          : base("Bilayer Material", "Bilayer",
              "Splits the voxel list by a plane to assign top=active(SMP) and bottom=passive(PLA) materials.",
              "Morpho4D", "02 Material")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Voxels", "VX", "Input voxel list", GH_ParamAccess.list);
            pManager.AddGenericParameter("Active Material", "AM", "Top (active) material - SmpMat recommended", GH_ParamAccess.item);
            pManager.AddGenericParameter("Passive Material", "PM", "Bottom (passive) material - PassiveMat(PLA) recommended", GH_ParamAccess.item);
            pManager.AddPlaneParameter("Split Plane", "P",
                "Split plane. Above this plane in the normal direction is active. Auto-set to Z center if unset (AutoCenter).",
                GH_ParamAccess.item);
            pManager[3].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Voxels", "VX", "Voxel list with materials assigned", GH_ParamAccess.list);
            pManager.AddIntegerParameter("Active Count", "nA", "Active voxel count", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Passive Count", "nP", "Passive voxel count", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            var voxelGoos = new List<VoxelCellGoo>();
            Material activeMat = null;
            Material passiveMat = null;
            Plane splitPlane = Plane.Unset;

            if (!DA.GetDataList(0, voxelGoos)) return;
            if (!DA.GetData(1, ref activeMat)) return;
            if (!DA.GetData(2, ref passiveMat)) return;
            DA.GetData(3, ref splitPlane);

            var voxels = new List<VoxelCell>();
            foreach (var g in voxelGoos)
                if (g?.Value != null) voxels.Add(g.Value);

            if (voxels.Count == 0) return;

            if (!voxels[0].isGridVoxel)
                this.AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    "BilayerMaterial expects volumetric voxels (default in this base).");

            // AutoCenter: Auto-set to Z center if no plane is specified
            if (splitPlane == Plane.Unset)
            {
                double zMin = double.MaxValue, zMax = double.MinValue;
                foreach (var v in voxels)
                {
                    if (v.initialPoint.Z < zMin) zMin = v.initialPoint.Z;
                    if (v.initialPoint.Z > zMax) zMax = v.initialPoint.Z;
                }
                double zMid = (zMin + zMax) * 0.5;
                splitPlane = new Plane(new Point3d(0, 0, zMid), Vector3d.ZAxis);
            }

            int nA = 0, nP = 0;
            var outGoos = new List<VoxelCellGoo>();

            foreach (var v in voxels)
            {
                double dot = (v.initialPoint - splitPlane.Origin) * splitPlane.Normal;
                if (dot >= 0)
                {
                    v.assignedMaterial = activeMat;
                    v.isActive = true;
                    nA++;
                }
                else
                {
                    v.assignedMaterial = passiveMat;
                    v.isActive = false;
                    nP++;
                }
                outGoos.Add(new VoxelCellGoo(v));
            }

            DA.SetDataList(0, outGoos);
            DA.SetData(1, nA);
            DA.SetData(2, nP);
        }

        protected override Bitmap Icon => null;
        public override Guid ComponentGuid => new Guid("1bf4606b-2bf4-4b2d-9057-6f13c79411f7");
    }
}
