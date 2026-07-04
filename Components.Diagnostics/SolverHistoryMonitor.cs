using System;
using System.Collections.Generic;
using System.Drawing;
using Grasshopper.Kernel;
using Morpho4D.Models;
using Morpho4D.Solver;

namespace _Morpho4D
{
    /// <summary>
    /// MorphoSolver의 L-BFGS 수렴 에너지 기록을 읽어 곡선으로 출력한다.
    /// MorphoSolverComponent 실행 직후에 연결하여 사용.
    /// </summary>
    public class SolverHistoryMonitorComponent : GH_Component
    {
        public SolverHistoryMonitorComponent()
          : base("Solver History Monitor", "SolvHist",
              "L-BFGS 수렴 에너지 기록을 표시한다. 논문 figure 생산용.",
              "Morpho4D", "Diagnostics")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Voxels", "VX", "MorphoSolverComponent 이후 복셀 리스트", GH_ParamAccess.list);
            pManager.AddIntegerParameter("Max Steps", "N", "표시할 최대 반복 수 (0=전체)", GH_ParamAccess.item, 0);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddNumberParameter("Energy History", "E", "각 반복의 에너지 값 목록", GH_ParamAccess.list);
            pManager.AddNumberParameter("Min Energy", "Emin", "최소 에너지", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Iteration Count", "N", "총 반복 수", GH_ParamAccess.item);
            pManager.AddTextParameter("Convergence Status", "S", "수렴 상태 요약", GH_ParamAccess.item);
        }

        // 마지막 실행된 solver를 저장 (GH 컴포넌트 간 데이터 공유 패턴)
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
                DA.SetData(3, "Solver 기록 없음. MorphoSolverComponent를 먼저 실행하세요.");
                return;
            }

            var hist = _lastSolver.energyHistory;
            var display = (maxN > 0 && maxN < hist.Count)
                ? hist.GetRange(hist.Count - maxN, maxN)
                : new List<double>(hist);

            double minE = double.MaxValue;
            foreach (var e in hist) if (e < minE) minE = e;

            bool converged = hist.Count >= 2 && Math.Abs(hist[hist.Count - 1] - hist[hist.Count - 2]) < 1e-3;
            string status = converged ? $"수렴 완료 ({hist.Count}회)" : $"진행 중 ({hist.Count}회)";

            DA.SetDataList(0, display);
            DA.SetData(1, minE);
            DA.SetData(2, hist.Count);
            DA.SetData(3, status);
        }

        protected override Bitmap Icon => null;
        public override Guid ComponentGuid => new Guid("AAAAAAA1-1111-2222-3333-444444444444");
    }
}
