using System;
using System.Collections.Generic;
using System.Drawing;
using Grasshopper.Kernel;
using Morpho4D.Models;
using Morpho4D.Solver;
using Rhino.Geometry;

namespace _Morpho4D
{
    public class CalibrationSolverComponent : GH_Component
    {
        public CalibrationSolverComponent()
          : base("Calibration Solver", "Calib",
              "실측 곡률(κ*)로부터 epsMax를 역산한다. Golden-Section Search 사용.",
              "Morpho4D", "Analysis")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Voxels", "VX", "BilayerMaterial + SetFiberDirection이 완료된 복셀 리스트", GH_ParamAccess.list);
            pManager.AddGenericParameter("Stimulus", "S", "시뮬레이션에 사용할 자극 객체", GH_ParamAccess.item);
            pManager.AddNumberParameter("Target Curvature κ*", "κ*", "실측 곡률 (1/mm)", GH_ParamAccess.item, 0.02);
            pManager.AddNumberParameter("Time", "T", "시뮬레이션 시간", GH_ParamAccess.item, 1.0);
            pManager.AddNumberParameter("Eps Search Min", "eMin", "epsMax 탐색 범위 하한", GH_ParamAccess.item, -0.2);
            pManager.AddNumberParameter("Eps Search Max", "eMax", "epsMax 탐색 범위 상한", GH_ParamAccess.item, 0.2);
            pManager.AddIntegerParameter("Max Iterations", "N", "Golden-Section 최대 반복 수", GH_ParamAccess.item, 50);
            pManager.AddBooleanParameter("Run", "Run", "true로 설정하면 최적화 실행", GH_ParamAccess.item, false);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddNumberParameter("Optimal epsMax", "ε*", "역산된 최대 eigenstrain", GH_ParamAccess.item);
            pManager.AddNumberParameter("Simulated κ", "κ_sim", "최적 epsMax로 시뮬레이션한 곡률", GH_ParamAccess.item);
            pManager.AddNumberParameter("Error %", "Err%", "κ* 대비 오차 (%)", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Iterations Used", "N", "실제 반복 횟수", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            bool run = false;
            DA.GetData(7, ref run);
            if (!run)
            {
                DA.SetData(0, 0.0); DA.SetData(1, 0.0); DA.SetData(2, 0.0); DA.SetData(3, 0);
                return;
            }

            var voxelGoos = new List<VoxelCellGoo>();
            Stimulus stim = null;
            double kTarget = 0.02, time = 1.0, eMin = -0.2, eMax = 0.2;
            int maxIter = 50;

            if (!DA.GetDataList(0, voxelGoos)) return;
            if (!DA.GetData(1, ref stim)) return;
            DA.GetData(2, ref kTarget);
            DA.GetData(3, ref time);
            DA.GetData(4, ref eMin);
            DA.GetData(5, ref eMax);
            DA.GetData(6, ref maxIter);

            var voxels = new List<VoxelCell>();
            foreach (var g in voxelGoos) if (g?.Value != null) voxels.Add(g.Value);
            if (voxels.Count < 3) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "복셀이 3개 미만입니다."); return; }

            // Golden-Section Search
            double phi = (Math.Sqrt(5) - 1) / 2.0;
            double a = eMin, b = eMax;
            int iter = 0;

            double c = b - phi * (b - a);
            double d = a + phi * (b - a);

            Func<double, double> cost = (eps) =>
            {
                double kSim = SimulateAndMeasure(voxels, stim, time, eps);
                return Math.Abs(kSim - kTarget);
            };

            double fc = cost(c), fd = cost(d);

            while (Math.Abs(b - a) > 1e-6 && iter < maxIter)
            {
                if (fc < fd)
                {
                    b = d; d = c; fd = fc;
                    c = b - phi * (b - a);
                    fc = cost(c);
                }
                else
                {
                    a = c; c = d; fc = fd;
                    d = a + phi * (b - a);
                    fd = cost(d);
                }
                iter++;
            }

            double epsOpt = (a + b) * 0.5;
            double kFinal = SimulateAndMeasure(voxels, stim, time, epsOpt);
            double errPct = (Math.Abs(kTarget) > 1e-9) ? Math.Abs(kFinal - kTarget) / Math.Abs(kTarget) * 100.0 : 0.0;

            DA.SetData(0, epsOpt);
            DA.SetData(1, kFinal);
            DA.SetData(2, errPct);
            DA.SetData(3, iter);
        }

        private double SimulateAndMeasure(List<VoxelCell> voxels, Stimulus stim, double time, double epsMax)
        {
            // epsMax를 active 복셀에 임시 적용
            foreach (var v in voxels)
                if (v.isActive) v.epsMax = epsMax;

            // 복셀 상태 초기화
            foreach (var v in voxels)
                v.currentPoint = v.initialPoint;

            var solver = new MorphoSolver(voxels);
            solver.setUpFromGrid(voxels);
            solver.execute(time, stim);

            // 곡률 측정: X 방향으로 정렬 후 3점 원 피팅
            var pts = solver.getResultPoints();
            if (pts.Count < 3) return 0.0;

            pts.Sort((a, b) => a.X.CompareTo(b.X));
            Point3d p0 = pts[0], p1 = pts[pts.Count / 2], p2 = pts[pts.Count - 1];

            Point3d center; double R;
            if (!Circle3Pt(p0, p1, p2, out center, out R)) return 0.0;
            return (R > 1e-9) ? 1.0 / R : 0.0;
        }

        private static bool Circle3Pt(Point3d a, Point3d b, Point3d c, out Point3d center, out double R)
        {
            center = Point3d.Origin; R = 0;
            Vector3d ab = b - a, ac = c - a;
            Vector3d n = Vector3d.CrossProduct(ab, ac);
            if (n.Length < 1e-9) return false;
            Plane pl = new Plane(a, n);
            double u1, v1, u2, v2;
            pl.ClosestParameter(b, out u1, out v1);
            pl.ClosestParameter(c, out u2, out v2);
            double bx = u1, by = v1, cx = u2, cy = v2;
            double d = 2 * (bx * cy - by * cx);
            if (Math.Abs(d) < 1e-12) return false;
            double ux = (cy * (bx * bx + by * by) - by * (cx * cx + cy * cy)) / d;
            double uy = (bx * (cx * cx + cy * cy) - cx * (bx * bx + by * by)) / d;
            center = pl.PointAt(ux, uy);
            R = center.DistanceTo(a);
            return true;
        }

        protected override Bitmap Icon => null;
        public override Guid ComponentGuid => new Guid("55555555-6666-7777-8888-999999999999");
    }
}
