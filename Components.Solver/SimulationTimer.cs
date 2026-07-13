using System;
using System.Collections.Generic;
using System.Drawing;
using Grasshopper.Kernel;

namespace _Morpho4D
{
    /// <summary>
    /// Generates simulation timesteps. Provides a time sequence for animation in conjunction with SimulationPlayer.
    /// </summary>
    public class SimulationTimerComponent : GH_Component
    {
        public SimulationTimerComponent()
          : base("Simulation Timer", "SimTimer",
              "Generates a simulation time sequence. Connect with SimulationPlayer to implement animation.",
              "Morpho4D", "04 Solver")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddNumberParameter("Start Time", "T0", "Start time", GH_ParamAccess.item, 0.0);
            pManager.AddNumberParameter("End Time", "T1", "End time", GH_ParamAccess.item, 1.0);
            pManager.AddIntegerParameter("Steps", "N", "Total number of steps", GH_ParamAccess.item, 10);
            pManager.AddIntegerParameter("Current Frame", "F", "Current frame index (0-based)", GH_ParamAccess.item, 0);
            pManager.AddBooleanParameter("Quadratic", "Q",
                "Quadratic time distribution (dense at start, sparse at end)", GH_ParamAccess.item, false);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddNumberParameter("Current Time", "T", "Time value of the current frame", GH_ParamAccess.item);
            pManager.AddNumberParameter("All Times", "Ts", "Complete time sequence", GH_ParamAccess.list);
            pManager.AddNumberParameter("Progress", "%", "Progress (0~1)", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            double t0 = 0, t1 = 1;
            int steps = 10, frame = 0;
            bool quadratic = false;

            DA.GetData(0, ref t0);
            DA.GetData(1, ref t1);
            DA.GetData(2, ref steps);
            DA.GetData(3, ref frame);
            DA.GetData(4, ref quadratic);

            if (steps < 2) steps = 2;
            frame = Math.Max(0, Math.Min(frame, steps - 1));

            var times = new List<double>();
            for (int i = 0; i < steps; i++)
            {
                double ratio = (double)i / (steps - 1);
                if (quadratic) ratio = ratio * ratio;
                times.Add(t0 + (t1 - t0) * ratio);
            }

            double progress = (double)frame / (steps - 1);
            DA.SetData(0, times[frame]);
            DA.SetDataList(1, times);
            DA.SetData(2, progress);
        }

        protected override Bitmap Icon => IconLoader.Get("SimulationTimer");
        public override Guid ComponentGuid => new Guid("a27ac3c8-91ea-4a57-849e-752db859785b");
    }
}
