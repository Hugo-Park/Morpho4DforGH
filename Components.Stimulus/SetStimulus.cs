using System;
using System.Collections.Generic;

using Grasshopper;
using Grasshopper.Kernel;
using Morpho4D.Solver;
using Rhino.Geometry;

namespace _Morpho4D
{
    public class SetStimulus : GH_Component
    {
        /// <summary>
        /// Each implementation of GH_Component must provide a public 
        /// constructor without any arguments.
        /// Category represents the Tab in which the component will appear, 
        /// Subcategory the panel. If you use non-existing tab or panel names, 
        /// new tabs/panels will automatically be created.
        /// </summary>
        public SetStimulus()
          : base("SetStimulusTest(HeatStim)", "Nickname",
            "Stimulus Test(HeatStim)",
            "Morpho4D", "03 Stimulus")
        {
        }

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("Stimulus Name", "N", "Name of Stimulus", GH_ParamAccess.item);
            pManager.AddNumberParameter("Temperature", "T", "Current Temperature of Stimulus", GH_ParamAccess.item);
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Stimulus", "S", "Constructed Stimulus object", GH_ParamAccess.item);
            pManager.AddTextParameter("Inspection", "?", "Inspection of Stimulus", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            string name = null;
            double temp = 25.0;

            if (!DA.GetData(0, ref name)) { return; }
            if (!DA.GetData(1, ref temp)) { return; }

            HeatStim stimulus = new HeatStim(name, temp);

            List<string> statList = new List<string>();
            statList.Add(string.Format("Stimulus Name: {0}", name));
            statList.Add(string.Format("Current Temperature: {0}", temp));

            DA.SetData(0, stimulus);
            DA.SetDataList(1, statList);
        }

        protected override System.Drawing.Bitmap Icon => IconLoader.Get("SetStimulus");

        public override Guid ComponentGuid => new Guid("628e18af-effc-4e34-8861-37691c186fbb");
    }
}