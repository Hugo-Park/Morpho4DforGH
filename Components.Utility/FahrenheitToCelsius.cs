using System;
using System.Collections.Generic;

using Grasshopper;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace _Morpho4D
{
    public class FahrenheitToCelsius : GH_Component
    {
        public FahrenheitToCelsius()
          : base("\u00B0F to \u00B0C", "F-C",
            "Description of component",
            "Morpho4D", "08 Utility")
        {
        }

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddNumberParameter("\u00B0F", "\u00B0F", "Temperature in Fahrenheit", GH_ParamAccess.item, 0.0);
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddNumberParameter("\u00B0C", "\u00B0C", "Temperature in Celsius", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            double fahTemp = 0.0;

            if (!DA.GetData(0, ref fahTemp)) { return; }

            double selTemp = (fahTemp - 32.0) * (5.0 / 9.0);

            DA.SetData(0, Math.Round(selTemp, 2));
        }

        protected override System.Drawing.Bitmap Icon => null;

        public override Guid ComponentGuid => new Guid("6ABC5074-64ED-4F9C-A630-65D7B1629E24");
    }
}