using System;
using System.Collections.Generic;

using Grasshopper;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Morpho4D.Models;

namespace _Morpho4D
{
    public class Sigmoid : GH_Component
    {
        public Sigmoid()
          : base("Sigmoid", "SM",
            "Calculate Young's modulus using a sigmoid-based transition model.",
            "Morpho4D", "Material")
        {
        }

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddNumberParameter("GlassTransTemp", "Tg", "Glass Transition Temperature : The critical temperature at which the material transitions from a hard, glassy state to a soft, rubbery state.", GH_ParamAccess.item);
            pManager.AddNumberParameter("GlassyModulus", "Eg", "Glassy Modulus : The maximum Young's Modulus of the material when it is in its stiff, low-temperature state.", GH_ParamAccess.item);
            pManager.AddNumberParameter("RubberyModulus", "Er", "RubberyModulus : The minimum Young's Modulus of the material when it is in its flexible, high-temperature state.", GH_ParamAccess.item);
            pManager.AddNumberParameter("Steepness", "k", "Steepness : A coefficient that determines how rapidly the material stiffness changes around the transition temperature.", GH_ParamAccess.item);
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("SigmoidModel", "SM", "Settings for sigmoid method", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            double tg = 60.0;
            double eg = 2000.0;
            double er = 20.0;
            double k = 1.0;

            if (!DA.GetData(0, ref tg)) { return; }
            if (!DA.GetData(1, ref eg)) { return; }
            if (!DA.GetData(2, ref er)) { return; }
            if (!DA.GetData(3, ref k)) { return; }

            SigmoidModel model = new SigmoidModel(tg, eg, er, k);
            DA.SetData(0, model);
        }

        protected override System.Drawing.Bitmap Icon => null;

        public override Guid ComponentGuid => new Guid("8d29bc3b-2930-4b46-8112-63bc499a8d26");
    }
}