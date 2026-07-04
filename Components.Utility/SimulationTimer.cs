using System;
using System.Collections.Generic;
using System.Drawing;
using Grasshopper.Kernel;

namespace _Morpho4D
{
    /// <summary>
    /// 시뮬레이션 타임스텝을 생성한다. SimulationPlayer와 함께 애니메이션용 시간 시퀀스를 제공.
    /// </summary>
    public class SimulationTimerComponent : GH_Component
    {
        public SimulationTimerComponent()
          : base("Simulation Timer", "SimTimer",
              "시뮬레이션 시간 시퀀스를 생성한다. SimulationPlayer와 연결해 애니메이션 구현.",
              "Morpho4D", "Utility")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddNumberParameter("Start Time", "T0", "시작 시간", GH_ParamAccess.item, 0.0);
            pManager.AddNumberParameter("End Time", "T1", "종료 시간", GH_ParamAccess.item, 1.0);
            pManager.AddIntegerParameter("Steps", "N", "총 스텝 수", GH_ParamAccess.item, 10);
            pManager.AddIntegerParameter("Current Frame", "F", "현재 프레임 인덱스 (0-based)", GH_ParamAccess.item, 0);
            pManager.AddBooleanParameter("Quadratic", "Q",
                "2차 곡선 시간 분배 (초반 촘촘, 후반 성김)", GH_ParamAccess.item, false);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddNumberParameter("Current Time", "T", "현재 프레임의 시간값", GH_ParamAccess.item);
            pManager.AddNumberParameter("All Times", "Ts", "전체 시간 시퀀스", GH_ParamAccess.list);
            pManager.AddNumberParameter("Progress", "%", "진행률 (0~1)", GH_ParamAccess.item);
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

        protected override Bitmap Icon => null;
        public override Guid ComponentGuid => new Guid("AAAAAAA4-1111-2222-3333-444444444444");
    }
}
