using System;
using System.Collections.Generic;

using Grasshopper;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace _Morpho4D
{
    /// <summary>
    /// 솔버가 목적 함수를 평가할 때마다 기록한 총 포텐셜 에너지(Energy History)를 받아
    /// 수렴이 제대로 되고 있는지 텍스트 로그/통계/그래프 포인트로 출력한다.
    /// Morpho Solver 컴포넌트의 'Energy History(EH)' 출력에 연결해서 사용.
    /// 주의: 기록 단위는 L-BFGS 이터레이션이 아니라 '목적함수 평가(line search 포함)' 단위이므로 값이 톱니처럼 보일 수 있다.
    /// </summary>
    public class SolverHistoryMonitor : GH_Component
    {
        public SolverHistoryMonitor()
          : base("Solver History Monitor", "SlvMon",
            "Diagnose L-BFGS convergence from the solver's energy history. Connect the Morpho Solver 'Energy History' output.",
            "Morpho4D", "Solver")
        {
        }

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddNumberParameter("Energy History", "EH", "Total potential energy per solver evaluation", GH_ParamAccess.list);
            pManager.AddNumberParameter("Convergence Tol", "tol", "Relative change below which the run is considered converged", GH_ParamAccess.item, 1e-4);
            pManager.AddIntegerParameter("Max Log Lines", "L", "Maximum number of log lines (history is sampled if longer)", GH_ParamAccess.item, 40);
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Log", "Log", "Readable convergence log", GH_ParamAccess.list);
            pManager.AddNumberParameter("Initial Energy", "E0", "Energy at first evaluation", GH_ParamAccess.item);
            pManager.AddNumberParameter("Final Energy", "Ef", "Energy at last evaluation", GH_ParamAccess.item);
            pManager.AddNumberParameter("Reduction %", "R%", "Percent energy reduction from initial to final", GH_ParamAccess.item);
            pManager.AddBooleanParameter("Converged", "OK", "Heuristic: energy decreased and last-step relative change < tol", GH_ParamAccess.item);
            pManager.AddPointParameter("Graph Points", "G", "Points (evaluation index, energy, 0) for plotting", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            this.ClearRuntimeMessages();

            List<double> hist = new List<double>();
            double tol = 1e-4;
            int maxLines = 40;

            if (!DA.GetDataList(0, hist)) { return; }
            DA.GetData(1, ref tol);
            DA.GetData(2, ref maxLines);

            if (hist.Count == 0)
            {
                this.AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Energy history is empty (run the solver first).");
                return;
            }

            int n = hist.Count;
            double e0 = hist[0];
            double ef = hist[n - 1];
            double denom = Math.Abs(e0) > 1e-12 ? Math.Abs(e0) : 1.0;
            double reduction = (e0 - ef) / denom * 100.0;

            double lastDelta = n >= 2 ? Math.Abs(hist[n - 1] - hist[n - 2]) : 0.0;
            double lastRel = lastDelta / (Math.Abs(ef) > 1e-12 ? Math.Abs(ef) : 1.0);
            bool converged = (ef <= e0 + 1e-12) && (lastRel < tol);

            // 로그 (너무 길면 샘플링)
            List<string> log = new List<string>();
            log.Add(string.Format("Evaluations: {0}", n));
            log.Add(string.Format("E0 = {0:e4}  ->  Ef = {1:e4}", e0, ef));
            log.Add(string.Format("Reduction: {0:f2}%   Converged: {1}", reduction, converged));
            log.Add("----");

            int budget = Math.Max(5, maxLines);
            if (n <= budget)
            {
                for (int i = 0; i < n; i++) log.Add(string.Format("[{0}] {1:e6}", i, hist[i]));
            }
            else
            {
                int step = (int)Math.Ceiling((double)n / budget);
                for (int i = 0; i < n; i += step) log.Add(string.Format("[{0}] {1:e6}", i, hist[i]));
                if ((n - 1) % step != 0) log.Add(string.Format("[{0}] {1:e6}", n - 1, hist[n - 1]));
            }

            List<Point3d> graph = new List<Point3d>();
            for (int i = 0; i < n; i++) graph.Add(new Point3d(i, hist[i], 0));

            DA.SetDataList(0, log);
            DA.SetData(1, e0);
            DA.SetData(2, ef);
            DA.SetData(3, reduction);
            DA.SetData(4, converged);
            DA.SetDataList(5, graph);
        }

        protected override System.Drawing.Bitmap Icon => null;

        public override Guid ComponentGuid => new Guid("e6c8fc16-e82b-441e-99d7-7afc6a9c51f9");
    }
}
