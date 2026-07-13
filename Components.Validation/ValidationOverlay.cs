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
    /// Compares simulation results with measured points.
    /// Processing sequence: Arc length resampling -> Kabsch alignment (MathNet SVD) -> RMSE/Max deviation calculation -> CSV output for paper
    /// </summary>
    public class ValidationOverlayComponent : GH_Component
    {
        public ValidationOverlayComponent()
          : base("Validation Overlay", "ValOver",
              "Compares simulation and measured results using RMSE after Kabsch alignment.",
              "Morpho4D", "06 Validation")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Sim Voxels", "SimVX", "List of simulated voxels", GH_ParamAccess.list);
            pManager.AddPointParameter("Measured Points", "MeasPts", "Measured points from MeasurementImporter", GH_ParamAccess.list);
            pManager.AddIntegerParameter("Resample N", "N", "Number of points to resample before alignment", GH_ParamAccess.item, 20);
            pManager.AddTextParameter("CSV Output Path", "CSV", "Path to save result CSV for paper (empty if not saving)", GH_ParamAccess.item, "");
            pManager.AddBooleanParameter("Run", "Run", "Execute calculation if set to true", GH_ParamAccess.item, false);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddNumberParameter("RMSE", "RMSE", "RMS deviation after alignment (mm)", GH_ParamAccess.item);
            pManager.AddNumberParameter("Max Deviation", "MaxD", "Maximum deviation (mm)", GH_ParamAccess.item);
            pManager.AddPointParameter("Aligned Sim Points", "AlignSim", "Aligned simulation points", GH_ParamAccess.list);
            pManager.AddPointParameter("Aligned Meas Points", "AlignMeas", "Aligned measured points", GH_ParamAccess.list);
            pManager.AddTextParameter("Report", "R", "Comparison result summary", GH_ParamAccess.item);
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
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Less than 3 points.");
                return;
            }

            // Step 1: Arc length resample (select N points with uniform spacing)
            var simResampled = ResampleArcLength(simPts, resampleN);
            var measResampled = ResampleArcLength(measPts, resampleN);

            // Step 2: Kabsch alignment (MathNet SVD)
            List<Point3d> simAligned, measAligned;
            KabschAlign(simResampled, measResampled, out simAligned, out measAligned);

            // Step 3: Calculate RMSE and maximum deviation
            double sumSq = 0, maxD = 0;
            for (int i = 0; i < simAligned.Count; i++)
            {
                double d = simAligned[i].DistanceTo(measAligned[i]);
                sumSq += d * d;
                if (d > maxD) maxD = d;
            }
            double rmse = Math.Sqrt(sumSq / simAligned.Count);

            // Step 4: Save CSV
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
                    report += $" | CSV saved: {csvOut}";
                }
                catch (Exception ex)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Failed to save CSV: {ex.Message}");
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

        // Kabsch alignment: Optimal rotation and translation alignment of simPts to measPts
        private static void KabschAlign(
            List<Point3d> simPts, List<Point3d> measPts,
            out List<Point3d> simAligned, out List<Point3d> measAligned)
        {
            int n = Math.Min(simPts.Count, measPts.Count);

            // Calculate centroid
            Point3d cSim = Centroid(simPts, n);
            Point3d cMeas = Centroid(measPts, n);

            // Center points
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

            // Covariance matrix H = P^T * Q
            var H = P.TransposeThisAndMultiply(Q);

            // SVD
            var svd = H.Svd();
            var U = svd.U;
            var Vt = svd.VT;

            // Rotation matrix R = V * U^T (including reflection correction)
            var R = Vt.Transpose().Multiply(U.Transpose());
            if (R.Determinant() < 0)
            {
                var Vm = Vt.Transpose();
                for (int i = 0; i < 3; i++) Vm[i, 2] = -Vm[i, 2];
                R = Vm.Multiply(U.Transpose());
            }

            // Create aligned points
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

        protected override Bitmap Icon => IconLoader.Get("ValidationOverlay");
        public override Guid ComponentGuid => new Guid("3c10865b-6880-472b-867d-d7243c6d43f2");
    }
}
