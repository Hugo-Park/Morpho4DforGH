using System;
using System.Collections.Generic;
using System.Drawing;
using Grasshopper.Kernel;
using Morpho4D.Models;
using Morpho4D.Solver;

namespace _Morpho4D
{
    /// <summary>
    /// Reads the L-BFGS convergence energy history of MorphoSolver and outputs it as a curve.
    /// Use by connecting immediately after the execution of MorphoSolverComponent.
    /// </summary>
    public class SolverHistoryMonitorComponent : GH_Component
    {
        public SolverHistoryMonitorComponent()
          : base("Solver History Monitor", "SolvHist",
              "Displays the L-BFGS convergence energy history. For paper figure production.",
              "Morpho4D", "05 Diagnostics")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Voxels", "VX", "List of voxels after MorphoSolverComponent", GH_ParamAccess.list);
            pManager.AddIntegerParameter("Max Steps", "N", "Maximum number of iterations to display (0=all)", GH_ParamAccess.item, 0);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddNumberParameter("Energy History", "E", "List of energy values for each iteration", GH_ParamAccess.list);
            pManager.AddNumberParameter("Min Energy", "Emin", "Minimum energy", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Iteration Count", "N", "Total number of iterations", GH_ParamAccess.item);
            pManager.AddTextParameter("Convergence Status", "S", "Summary of convergence status", GH_ParamAccess.item);
        }

        // Store the last executed solver (GH component data sharing pattern)
        private static MorphoSolver _lastSolver = null;
        public static void RegisterSolver(MorphoSolver s) { _lastSolver = s; }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            var voxelGoos = new List<VoxelCellGoo>();
            int maxN = 0;
            DA.GetDataList(0, voxelGoos);
            DA.GetData(1, ref maxN);

            if (_lastSolver == null || _lastSolver.energyHistory.Count == 0)
            {
                DA.SetDataList(0, new List<double>());
                DA.SetData(1, 0.0);
                DA.SetData(2, 0);
                DA.SetData(3, "No solver history. Please run MorphoSolverComponent first.");
                return;
            }

            var hist = _lastSolver.energyHistory;
            var display = (maxN > 0 && maxN < hist.Count)
                ? hist.GetRange(hist.Count - maxN, maxN)
                : new List<double>(hist);

            double minE = double.MaxValue;
            foreach (var e in hist) if (e < minE) minE = e;

            bool converged = hist.Count >= 2 && Math.Abs(hist[hist.Count - 1] - hist[hist.Count - 2]) < 1e-3;
            string status = converged ? $"Converged ({hist.Count} iterations)" : $"In progress ({hist.Count} iterations)";

            DA.SetDataList(0, display);
            DA.SetData(1, minE);
            DA.SetData(2, hist.Count);
            DA.SetData(3, status);
        }

        protected override Bitmap Icon => null;
        public override Guid ComponentGuid => new Guid("d6613b57-ae15-45c6-80b8-53792ba90ce1");
    }
}
