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
              "복셀 리스트를 평면으로 분할해 상단=active(SMP), 하단=passive(PLA) 재료를 지정한다.",
              "Morpho4D", "Material")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Voxels", "VX", "입력 복셀 리스트", GH_ParamAccess.list);
            pManager.AddGenericParameter("Active Material", "AM", "상단(active) 재료 — SmpMat 권장", GH_ParamAccess.item);
            pManager.AddGenericParameter("Passive Material", "PM", "하단(passive) 재료 — PassiveMat(PLA) 권장", GH_ParamAccess.item);
            pManager.AddPlaneParameter("Split Plane", "P",
                "분할 평면. 이 평면보다 법선 방향 위가 active. 미입력 시 Z 중앙으로 자동 설정(AutoCenter).",
                GH_ParamAccess.item);
            pManager[3].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Voxels", "VX", "재료가 지정된 복셀 리스트", GH_ParamAccess.list);
            pManager.AddIntegerParameter("Active Count", "nA", "active 복셀 수", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Passive Count", "nP", "passive 복셀 수", GH_ParamAccess.item);
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

            // AutoCenter: 지정 평면 없으면 Z 중앙 분할
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
        public override Guid ComponentGuid => new Guid("22222222-3333-4444-5555-666666666666");
    }
}
