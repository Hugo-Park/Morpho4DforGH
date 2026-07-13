using System;
using System.Collections.Generic;

using Grasshopper;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Morpho4D.Models;

namespace _Morpho4D
{
    public class ConstructSMP : GH_Component
    {
        public ConstructSMP()
          : base("Construct SMP", "SMP",
            "Create SMP material by defining variables",
            "Morpho4D", "02 Material")
        {
        }

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Material Base", "mB", "Output from 'Material Base' component", GH_ParamAccess.item);
            pManager.AddNumberParameter("Max Swelling Ratio", "MaxS", "Maximum swelling ratio of SMP material", GH_ParamAccess.item, 1.0);
            pManager.AddNumberParameter("Min Swelling Ratio", "MinS", "Minimum swelling ratio of SMP material", GH_ParamAccess.item, 1.0);
            pManager.AddGenericParameter("Sigmoid Model", "SM", "Output from 'Define Sigmoid' component", GH_ParamAccess.item);
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("SMP", "SMP", "Constructed SMP material", GH_ParamAccess.item);
            pManager.AddTextParameter("Inspection", "?", "Inspection of SMP", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            double maxS = 1.0;
            double minS = 1.0;
            SigmoidModel model = null;
            Material.MaterialBase mBase = new Material.MaterialBase();


            if (!DA.GetData(0, ref mBase)) { return; }
            if (!DA.GetData(1, ref maxS)) { return; }
            if (!DA.GetData(2, ref minS)) { return; }
            if (!DA.GetData(3, ref model)) { return; }

            SmpMat smpMat = new SmpMat(mBase, maxS, minS, model);

            List<string> statList = new List<string>();
            statList.Add(string.Format("Material Name: {0}", mBase.materialName));
            statList.Add(string.Format("Preview Color: {0}", mBase.previewColor));
            statList.Add(string.Format("Young's Modulus: {0}", mBase.youngsModulus));
            statList.Add(string.Format("Poisson's Ratio: {0}", mBase.poissonRatio));
            statList.Add(string.Format("Max Swelling Ratio: {0}", maxS));
            statList.Add(string.Format("Min Swelling Ratio: {0}", minS));
            statList.Add(string.Format("Glass Transition Temperature: {0}", model.Tg));
            statList.Add(string.Format("Glassy Modulus: {0}", model.Eg));
            statList.Add(string.Format("Rubbery Modulus: {0}", model.Er));
            statList.Add(string.Format("Steepness: {0}", model.k));

            DA.SetData(0, smpMat);
            DA.SetDataList(1, statList);
        }

        protected override System.Drawing.Bitmap Icon => IconLoader.Get("ConstructSMP");

        public override Guid ComponentGuid => new Guid("E471F8AC-99A8-4D56-9FC1-F6F0A7F3F34C");
    }
}