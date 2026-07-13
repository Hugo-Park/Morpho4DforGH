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
              "Constructs PLA passive layer material. Used as a passive layer for BilayerMaterial.",
              "Morpho4D", "02 Material")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Material Base", "mB", "MaterialBaseComponent output", GH_ParamAccess.item);
            pManager.AddNumberParameter("Thermal Softening", "TS",
                "Stiffness reduction per unit temperature (MPa/°C). If 0, it is rigid regardless of temperature.", GH_ParamAccess.item, 0.0);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("PLA Material", "M", "PassiveMat material object", GH_ParamAccess.item);
            pManager.AddTextParameter("Inspection", "?", "Material information", GH_ParamAccess.list);
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

        protected override Bitmap Icon => IconLoader.Get("ConstructPLA");
        public override Guid ComponentGuid => new Guid("7c5ed220-a983-4ade-a54f-e0c488ffeadb");
    }
}
