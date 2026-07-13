using System;
using System.Collections.Generic;

using Grasshopper;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Morpho4D.Models;

namespace _Morpho4D
{
    public class DefineFicks : GH_Component
    {
        public DefineFicks()
          : base("Define Fick's", "F's",
            "Calculate hydrational level using Fick's law of diffusion",
            "Morpho4D", "02 Material")
        {
        }

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddNumberParameter("Diffusion Coefficient", "D", "A physical constant that determines the speed at which moisture spreads through the hydrogel material(mm^2/s).", GH_ParamAccess.item, 0.05);
            pManager.AddNumberParameter("Max Hydration", "Hmax", "The maximum capacity of moisture the hydrogel can absorb, serving as the upper limit for the swelling simulation.", GH_ParamAccess.item, 1.0);
            pManager.AddNumberParameter("Saturation Limit", "Cs", "The fixed concentration level at the material's surface, representing external environmental stimuli (e.g., 1.0 for immersion in water).", GH_ParamAccess.item, 1.0);
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Fick's Model", "FM", "Settings for Fick's law of diffusion", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            double D = 0.05;
            double HMax = 1.0;
            double Cs = 1.0;

            if (!DA.GetData(0, ref D)) { return; }
            if (!DA.GetData(1, ref HMax)) { return; }
            if (!DA.GetData(2, ref Cs)) { return; }

            FicksModel model = new FicksModel(D, HMax, Cs);
            DA.SetData(0, model);
        }

        protected override System.Drawing.Bitmap Icon => IconLoader.Get("DefineFicks");

        public override Guid ComponentGuid => new Guid("25EC8DE4-CD5C-4099-A03C-F9B056389EA5");
    }
}