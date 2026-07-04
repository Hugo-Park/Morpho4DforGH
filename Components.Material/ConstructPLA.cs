using System;
using System.Collections.Generic;
using System.Drawing;
using Grasshopper.Kernel;
using Morpho4D.Models;

namespace _Morpho4D
{
    public class ConstructPLAComponent : GH_Component
    {
        public ConstructPLAComponent()
          : base("Construct PLA", "PLA",
              "PLA 패시브 레이어 재료를 생성한다. BilayerMaterial의 passive 레이어로 사용.",
              "Morpho4D", "Material")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Material Base", "mB", "MaterialBaseComponent 출력값", GH_ParamAccess.item);
            pManager.AddNumberParameter("Thermal Softening", "TS",
                "단위 온도당 강성 감소량 (MPa/°C). 0이면 온도 무관 rigid.", GH_ParamAccess.item, 0.0);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("PLA Material", "M", "PassiveMat 재료 객체", GH_ParamAccess.item);
            pManager.AddTextParameter("Inspection", "?", "재료 정보", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            Material.MaterialBase mBase = new Material.MaterialBase();
            double ts = 0.0;

            if (!DA.GetData(0, ref mBase)) return;
            DA.GetData(1, ref ts);

            var mat = new PassiveMat(mBase, ts);

            var info = new List<string>
            {
                $"Material Name: {mBase.materialName}",
                $"Young's Modulus: {mBase.youngsModulus} MPa",
                $"Thermal Softening: {ts} MPa/°C",
                $"Preview Color: {mBase.previewColor}"
            };

            DA.SetData(0, mat);
            DA.SetDataList(1, info);
        }

        protected override Bitmap Icon => null;
        public override Guid ComponentGuid => new Guid("AAAAAAA9-1111-2222-3333-444444444444");
    }
}
