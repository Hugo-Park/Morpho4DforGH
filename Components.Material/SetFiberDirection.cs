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
              "isActive=true인 복셀에 섬유 방향(fiberDir)과 최대 eigenstrain(epsMax)을 지정한다.",
              "Morpho4D", "Material")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Voxels", "VX", "BilayerMaterial을 통과한 복셀 리스트", GH_ParamAccess.list);
            pManager.AddVectorParameter("Fiber Direction", "D",
                "섬유 방향 단위벡터. 자동으로 정규화됨.", GH_ParamAccess.item, Vector3d.XAxis);
            pManager.AddNumberParameter("Eps Max", "ε",
                "최대 eigenstrain (활성화 완료 시 최대 변형률). 수축이면 음수.", GH_ParamAccess.item, 0.0);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Voxels", "VX", "섬유 방향이 지정된 복셀 리스트", GH_ParamAccess.list);
            pManager.AddIntegerParameter("Updated Count", "n", "업데이트된 active 복셀 수", GH_ParamAccess.item);
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
                this.AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Fiber Direction이 영벡터입니다.");
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

        protected override Bitmap Icon => null;
        public override Guid ComponentGuid => new Guid("33333333-4444-5555-6666-777777777777");
    }
}
