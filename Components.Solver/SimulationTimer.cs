using System;
using System.Collections.Generic;

using Grasshopper;
using Grasshopper.Kernel;

namespace _Morpho4D
{
    /// <summary>
    /// 전체 시뮬레이션 시간을 delta time step으로 쪼갠 시간 샘플 리스트를 만든다.
    /// Hydrogel(Fick 확산)처럼 시간 의존 거동을 여러 시점에서 평가하기 위한 타임라인 소스.
    /// 출력 Times 를 Simulation Player(또는 수동 루프)의 시간 입력으로 사용한다.
    /// </summary>
    public class SimulationTimer : GH_Component
    {
        public SimulationTimer()
          : base("Simulation Timer", "SimTimer",
            "Split a total simulation time into discrete time steps. Feed the output to the Simulation Player to bake frames.",
            "Morpho4D", "Solver")
        {
        }

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddNumberParameter("Total Time", "TT", "Total simulation time", GH_ParamAccess.item, 60.0);
            pManager.AddNumberParameter("Time Step", "dt", "Delta time per step", GH_ParamAccess.item, 5.0);
            pManager.AddBooleanParameter("Include Zero", "Z", "Include t=0 as the first sample", GH_ParamAccess.item, true);
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddNumberParameter("Times", "T", "List of time samples", GH_ParamAccess.list);
            pManager.AddIntegerParameter("Count", "N", "Number of time samples", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            this.ClearRuntimeMessages();

            double total = 60.0, dt = 5.0;
            bool includeZero = true;

            if (!DA.GetData(0, ref total)) { return; }
            if (!DA.GetData(1, ref dt)) { return; }
            if (!DA.GetData(2, ref includeZero)) { return; }

            if (dt <= 0) { this.AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Time Step must be > 0"); return; }
            if (total < 0) { this.AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Total Time must be >= 0"); return; }

            List<double> times = new List<double>();
            double eps = dt * 1e-6;

            double start = includeZero ? 0.0 : dt;
            for (double t = start; t <= total + eps; t += dt)
            {
                times.Add(Math.Min(t, total));
            }
            // 마지막 샘플이 total과 다르면 total을 추가 (스텝이 딱 떨어지지 않는 경우)
            if (times.Count == 0 || Math.Abs(times[times.Count - 1] - total) > eps)
            {
                times.Add(total);
            }

            DA.SetDataList(0, times);
            DA.SetData(1, times.Count);
        }

        protected override System.Drawing.Bitmap Icon => null;

        public override Guid ComponentGuid => new Guid("e844b8ce-0597-4b35-ab9e-f50aa22df41a");
    }
}
