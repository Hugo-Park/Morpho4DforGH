using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using Grasshopper.Kernel;
using MathNet.Numerics.LinearAlgebra;
using MathNet.Numerics.LinearAlgebra.Double;
using Morpho4D.Models;
using Rhino.Geometry;

namespace _Morpho4D
{
    /// <summary>
    /// 시뮬레이션 결과와 실측 포인트를 비교한다.
    /// 처리 순서: 호길이 재샘플 → Kabsch 정렬(MathNet SVD) → RMSE/최대편차 계산 → 논문용 CSV 출력
    /// </summary>
    public class ValidationOverlayComponent : GH_Component
    {
        public ValidationOverlayComponent()
          : base("Validation Overlay", "ValOver",
              "시뮬레이션과 실측 결과를 Kabsch 정렬 후 RMSE로 비교한다.",
              "Morpho4D", "Validation")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Sim Voxels", "SimVX", "시뮬레이션 완료 복셀 리스트", GH_ParamAccess.list);
            pManager.AddPointParameter("Measured Points", "MeasPts", "MeasurementImporter 출력 실측 포인트", GH_ParamAccess.list);
            pManager.AddIntegerParameter("Resample N", "N", "정렬 전 재샘플 포인트 수", GH_ParamAccess.item, 20);
            pManager.AddTextParameter("CSV Output Path", "CSV", "논문용 결과 CSV 저장 경로 (비어있으면 미저장)", GH_ParamAccess.item, "");
            pManager.AddBooleanParameter("Run", "Run", "true로 설정하면 계산 실행", GH_ParamAccess.item, false);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddNumberParameter("RMSE", "RMSE", "정렬 후 RMS 편차 (mm)", GH_ParamAccess.item);
            pManager.AddNumberParameter("Max Deviation", "MaxD", "최대 편차 (mm)", GH_ParamAccess.item);
            pManager.AddPointParameter("Aligned Sim Points", "AlignSim", "정렬된 시뮬레이션 포인트", GH_ParamAccess.list);
            pManager.AddPointParameter("Aligned Meas Points", "AlignMeas", "정렬된 실측 포인트", GH_ParamAccess.list);
            pManager.AddTextParameter("Report", "R", "비교 결과 요약", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            bool run = false;
            DA.GetData(4, ref run);
            if (!run)
            {
                DA.SetData(0, 0.0); DA.SetData(1, 0.0);
                DA.SetData(4, "Run=false"); return;
            }

            var voxelGoos = new List<VoxelCellGoo>();
            var measPts = new List<Point3d>();
            int resampleN = 20;
            string csvOut = "";

            if (!DA.GetDataList(0, voxelGoos)) return;
            if (!DA.GetDataList(1, measPts)) return;
            DA.GetData(2, ref resampleN);
            DA.GetData(3, ref csvOut);

            var simPts = new List<Point3d>();
            foreach (var g in voxelGoos) if (g?.Value != null) simPts.Add(g.Value.currentPoint);

            if (simPts.Count < 3 || measPts.Count < 3)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "포인트가 3개 미만입니다.");
                return;
            }

            // 1단계: 호길이 재샘플 (균등 간격으로 N개 선택)
            var simResampled = ResampleArcLength(simPts, resampleN);
            var measResampled = ResampleArcLength(measPts, resampleN);

            // 2단계: Kabsch 정렬 (MathNet SVD)
            List<Point3d> simAligned, measAligned;
            KabschAlign(simResampled, measResampled, out simAligned, out measAligned);

            // 3단계: RMSE 및 최대편차 계산
            double sumSq = 0, maxD = 0;
            for (int i = 0; i < simAligned.Count; i++)
            {
                double d = simAligned[i].DistanceTo(measAligned[i]);
                sumSq += d * d;
                if (d > maxD) maxD = d;
            }
            double rmse = Math.Sqrt(sumSq / simAligned.Count);

            // 4단계: CSV 저장
            var report = $"RMSE = {rmse:F4} mm | MaxDev = {maxD:F4} mm | N = {simAligned.Count}";
            if (!string.IsNullOrWhiteSpace(csvOut))
            {
                try
                {
                    var csvLines = new List<string> { "Index,Sim_X,Sim_Y,Sim_Z,Meas_X,Meas_Y,Meas_Z,Deviation_mm" };
                    for (int i = 0; i < simAligned.Count; i++)
                    {
                        double d = simAligned[i].DistanceTo(measAligned[i]);
                        csvLines.Add($"{i},{simAligned[i].X:F4},{simAligned[i].Y:F4},{simAligned[i].Z:F4}," +
                                     $"{measAligned[i].X:F4},{measAligned[i].Y:F4},{measAligned[i].Z:F4},{d:F4}");
                    }
                    csvLines.Add($",,,,,,RMSE,{rmse:F4}");
                    csvLines.Add($",,,,,,MaxDev,{maxD:F4}");
                    File.WriteAllLines(csvOut, csvLines);
                    report += $" | CSV 저장됨: {csvOut}";
                }
                catch (Exception ex)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"CSV 저장 실패: {ex.Message}");
                }
            }

            DA.SetData(0, rmse);
            DA.SetData(1, maxD);
            DA.SetDataList(2, simAligned);
            DA.SetDataList(3, measAligned);
            DA.SetData(4, report);
        }

        private static List<Point3d> ResampleArcLength(List<Point3d> pts, int n)
        {
            if (pts.Count <= n) return new List<Point3d>(pts);
            var result = new List<Point3d>();
            double totalLen = 0;
            for (int i = 1; i < pts.Count; i++) totalLen += pts[i - 1].DistanceTo(pts[i]);

            result.Add(pts[0]);
            double step = totalLen / (n - 1);
            double acc = 0, nextTarget = step;
            for (int i = 1; i < pts.Count && result.Count < n; i++)
            {
                double seg = pts[i - 1].DistanceTo(pts[i]);
                while (acc + seg >= nextTarget && result.Count < n)
                {
                    double t = (nextTarget - acc) / seg;
                    result.Add(pts[i - 1] + (pts[i] - pts[i - 1]) * t);
                    nextTarget += step;
                }
                acc += seg;
            }
            if (result.Count < n) result.Add(pts[pts.Count - 1]);
            return result;
        }

        // Kabsch 정렬: simPts를 measPts에 최적 회전·평행이동 정렬
        private static void KabschAlign(
            List<Point3d> simPts, List<Point3d> measPts,
            out List<Point3d> simAligned, out List<Point3d> measAligned)
        {
            int n = Math.Min(simPts.Count, measPts.Count);

            // 무게중심 계산
            Point3d cSim = Centroid(simPts, n);
            Point3d cMeas = Centroid(measPts, n);

            // 중심화
            var P = Matrix<double>.Build.Dense(n, 3);
            var Q = Matrix<double>.Build.Dense(n, 3);
            for (int i = 0; i < n; i++)
            {
                P[i, 0] = simPts[i].X - cSim.X;
                P[i, 1] = simPts[i].Y - cSim.Y;
                P[i, 2] = simPts[i].Z - cSim.Z;
                Q[i, 0] = measPts[i].X - cMeas.X;
                Q[i, 1] = measPts[i].Y - cMeas.Y;
                Q[i, 2] = measPts[i].Z - cMeas.Z;
            }

            // 공분산 행렬 H = P^T * Q
            var H = P.TransposeThisAndMultiply(Q);

            // SVD
            var svd = H.Svd();
            var U = svd.U;
            var Vt = svd.VT;

            // 회전 행렬 R = V * U^T (반사 보정 포함)
            var R = Vt.Transpose().Multiply(U.Transpose());
            if (R.Determinant() < 0)
            {
                var Vm = Vt.Transpose();
                for (int i = 0; i < 3; i++) Vm[i, 2] = -Vm[i, 2];
                R = Vm.Multiply(U.Transpose());
            }

            // 정렬된 포인트 생성
            simAligned = new List<Point3d>();
            for (int i = 0; i < n; i++)
            {
                double[] p = { simPts[i].X - cSim.X, simPts[i].Y - cSim.Y, simPts[i].Z - cSim.Z };
                double rx = R[0, 0] * p[0] + R[0, 1] * p[1] + R[0, 2] * p[2] + cMeas.X;
                double ry = R[1, 0] * p[0] + R[1, 1] * p[1] + R[1, 2] * p[2] + cMeas.Y;
                double rz = R[2, 0] * p[0] + R[2, 1] * p[1] + R[2, 2] * p[2] + cMeas.Z;
                simAligned.Add(new Point3d(rx, ry, rz));
            }

            measAligned = new List<Point3d>(measPts.GetRange(0, n));
        }

        private static Point3d Centroid(List<Point3d> pts, int n)
        {
            double x = 0, y = 0, z = 0;
            for (int i = 0; i < n; i++) { x += pts[i].X; y += pts[i].Y; z += pts[i].Z; }
            return new Point3d(x / n, y / n, z / n);
        }

        protected override Bitmap Icon => null;
        public override Guid ComponentGuid => new Guid("9B2D5F83-4A6C-4EA2-B7F9-2C3D4E5F6071");
    }
}
