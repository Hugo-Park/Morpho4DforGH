using System;
using System.Collections.Generic;

using Grasshopper;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Morpho4D.Models;
using Eto.Forms;

namespace _Morpho4D
{
    public class ConstructHydrogel : GH_Component
    {
        public ConstructHydrogel()
          : base("Construct Hydrogel", "Hydrogel",
            "Create hydrogel material by defining variables",
            "Morpho4D", "Material")
        {
        }

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Material Base", "mB", "Output from 'Material Base' component", GH_ParamAccess.item);
            pManager.AddNumberParameter("Max Swelling Ratio", "MaxS", "Maximum swelling ratio of Hydrogel material", GH_ParamAccess.item, 1.0);
            pManager.AddNumberParameter("Min Swelling Ratio", "MinS", "Minimum swelling ratio of Hydrogel material", GH_ParamAccess.item, 1.0);
            pManager.AddNumberParameter("Osmotic Pressure", "OP", "Osmotic Pressure of Hydrogel material", GH_ParamAccess.item, 50.0);
            pManager.AddGenericParameter("Fick's Model", "FM", "Output from 'Define Fick's' component", GH_ParamAccess.item);
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Hydrogel", "HYD", "Constructed Hydrogel material", GH_ParamAccess.item);
            pManager.AddTextParameter("Inspection", "?", "Inspection of Hydrogel", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            double maxS = 1.0;
            double minS = 1.0;
            FicksModel model = null;
            double osmoticP = 50.0;
            Material.MaterialBase mBase = new Material.MaterialBase();

            if (!DA.GetData(0, ref mBase)) { return; }
            if (!DA.GetData(1, ref maxS)) { return; }
            if (!DA.GetData(2, ref minS)) { return; }
            if (!DA.GetData(3, ref osmoticP)) { return; }
            if (!DA.GetData(4, ref model)) { return; }

            HydrogelMat hydrogelMat = new HydrogelMat(mBase, maxS, minS, osmoticP, model);

            List<string> statList = new List<string>();
            statList.Add(string.Format("Material Name: {0}", mBase.materialName));
            statList.Add(string.Format("Preview Color: {0}", mBase.previewColor));
            statList.Add(string.Format("Young's Modulus: {0}", mBase.youngsModulus));
            statList.Add(string.Format("Poisson's Ratio: {0}", mBase.poissonRatio));
            statList.Add(string.Format("Max Swelling Ratio: {0}", maxS));
            statList.Add(string.Format("Min Swelling Ratio: {0}", minS));
            statList.Add(string.Format("Osmotic Pressure: {0}", osmoticP));
            statList.Add(string.Format("Diffusion Coefficient: {0}", model.D));
            statList.Add(string.Format("Max Hydration: {0}", model.HMax));
            statList.Add(string.Format("Saturation Limit: {0}", model.Cs));

            DA.SetData(0, hydrogelMat);
            DA.SetDataList(1, statList);
        }

        protected override System.Drawing.Bitmap Icon => null;

        public override Guid ComponentGuid => new Guid("7B47BC4F-C0A1-4CA7-A864-C2EA168B50EE");
    }
}