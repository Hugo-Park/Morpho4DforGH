using System;
using System.Collections.Generic;
using System.Drawing;
using Grasshopper.Kernel;
using Morpho4D.Models;
using Morpho4D.Solver;
using MathNet.Numerics.LinearAlgebra;
using Rhino.Geometry;

namespace _Morpho4D
{
    public class GradientCheckComponent : GH_Component
    {
        public GradientCheckComponent()
          : base("Gradient Check", "GradChk",
              "유한차분법으로 energy gradient의 수치적 정확성을 검증한다. G(-1) 수정 후 세 가지 케이스 전부 PASS 확인 필수.",
              "Morpho4D", "Diagnostics")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddIntegerParameter("Test Case", "TC",
                "0=Spring단독, 1=Bending단독, 2=혼합(3x3격자), 3=Spring+비등방(G1 이후)", GH_ParamAccess.item, 0);
            pManager.AddNumberParameter("Epsilon", "Eps", "중심차분 섭동 크기 (default 1e-6)", GH_ParamAccess.item, 1e-6);
            pManager.AddNumberParameter("Threshold", "Thr", "PASS 판정 최대 상대오차 (default 1e-4)", GH_ParamAccess.item, 1e-4);
            pManager.AddBooleanParameter("Run", "Run", "true로 설정하면 검증 실행", GH_ParamAccess.item, false);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Report", "R", "검증 결과 리포트", GH_ParamAccess.item);
            pManager.AddNumberParameter("Max Relative Error", "MaxErr", "최대 성분별 상대오차", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Failed DOF Count", "Fail", "임계값 초과 DOF 개수", GH_ParamAccess.item);
            pManager.AddBooleanParameter("Pass", "Pass", "PASS 여부", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            bool run = false;
            int testCase = 0;
            double eps = 1e-6;
            double threshold = 1e-4;

            DA.GetData(0, ref testCase);
            DA.GetData(1, ref eps);
            DA.GetData(2, ref threshold);
            DA.GetData(3, ref run);

            if (!run)
            {
                DA.SetData(0, "Run 입력을 true로 설정하면 검증을 시작합니다.");
                DA.SetData(1, 0.0);
                DA.SetData(2, 0);
                DA.SetData(3, false);
                return;
            }

            try
            {
                var (solver, voxels) = BuildTestCase(testCase);
                var result = RunGradientCheck(solver, voxels, eps, threshold);

                DA.SetData(0, result.Report);
                DA.SetData(1, result.MaxRelativeError);
                DA.SetData(2, result.FailedDOFCount);
                DA.SetData(3, result.Pass);
            }
            catch (Exception ex)
            {
                this.AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
                DA.SetData(0, $"ERROR: {ex.Message}");
                DA.SetData(1, double.NaN);
                DA.SetData(2, -1);
                DA.SetData(3, false);
            }
        }

        private (MorphoSolver, List<VoxelCell>) BuildTestCase(int testCase)
        {
            double size = 1.0;
            var mat = new SmpMat(
                new Material.MaterialBase("SMP_test", Color.Red, 1.0, 0.3),
                0.0, 0.0,
                new SigmoidModel(60.0, 1.0, 0.01, 0.5));

            switch (testCase)
            {
                case 0: return BuildSpringCase(size, mat);
                case 1: return BuildBendingCase(size, mat);
                case 2: return BuildMixedCase(size, mat);
                case 3: return BuildAnisoSpringCase(size, mat);
                default: throw new ArgumentException($"알 수 없는 테스트 케이스: {testCase}");
            }
        }

        private (MorphoSolver, List<VoxelCell>) BuildSpringCase(double size, SmpMat mat)
        {
            var v0 = MakeVoxel(0, 0, 0, 0, size, mat, expansionForce: 1.2);
            var v1 = MakeVoxel(1, size, 0, 0, size, mat, expansionForce: 1.2);
            var voxels = new List<VoxelCell> { v0, v1 };

            var solver = new MorphoSolver(voxels);
            solver.allPairs.Add(new VoxelPair(v0, v1));
            // hinge 없음
            return (solver, voxels);
        }

        private (MorphoSolver, List<VoxelCell>) BuildBendingCase(double size, SmpMat mat)
        {
            var v0 = MakeVoxel(0, 0, 0, 0, size, mat);
            var v1 = MakeVoxel(1, size, 0, 0.1, size, mat);  // targetAngle != currentAngle 보장을 위해 약간 변위
            var v2 = MakeVoxel(2, size * 2, 0, 0, size, mat);
            var voxels = new List<VoxelCell> { v0, v1, v2 };

            v1.neighborIndices.Add(0);
            v1.neighborIndices.Add(2);

            var hinge = new Hinge(0, 1, 2);
            hinge.referenceNormal = Vector3d.ZAxis;
            hinge.targetAngle = Math.PI;
            v1.hingeIndices.Add(hinge);

            var solver = new MorphoSolver(voxels);
            solver.allHinges.Add(hinge);
            return (solver, voxels);
        }

        private (MorphoSolver, List<VoxelCell>) BuildMixedCase(double size, SmpMat mat)
        {
            // 3×3 평면 격자
            var voxels = new List<VoxelCell>();
            for (int ix = 0; ix < 3; ix++)
                for (int iy = 0; iy < 3; iy++)
                {
                    int id = ix * 3 + iy;
                    voxels.Add(MakeVoxel(id, ix * size, iy * size, 0, size, mat, expansionForce: 1.0));
                }

            var solver = new MorphoSolver(voxels);

            // spring pairs: 인접(거리=size)과 대각(거리=size*√2)
            double tol = size * 0.05;
            for (int a = 0; a < voxels.Count; a++)
                for (int b = a + 1; b < voxels.Count; b++)
                {
                    double d = voxels[a].initialPoint.DistanceTo(voxels[b].initialPoint);
                    if (d < size * Math.Sqrt(2.0) + tol)
                        solver.allPairs.Add(new VoxelPair(voxels[a], voxels[b]));
                    if (Math.Abs(d - size) < tol)
                    {
                        voxels[a].neighborIndices.Add(b);
                        voxels[b].neighborIndices.Add(a);
                    }
                }

            // hinges: 정반대 쌍
            foreach (var center in voxels)
            {
                var nb = center.neighborIndices;
                for (int a = 0; a < nb.Count; a++)
                    for (int b2 = a + 1; b2 < nb.Count; b2++)
                    {
                        Vector3d va = voxels[nb[a]].initialPoint - center.initialPoint;
                        Vector3d vb = voxels[nb[b2]].initialPoint - center.initialPoint;
                        if ((va + vb).Length < size * 0.1)
                        {
                            var h = new Hinge(nb[a], center.Id, nb[b2]);
                            h.referenceNormal = Vector3d.ZAxis;
                            h.targetAngle = Math.PI;
                            center.hingeIndices.Add(h);
                            solver.allHinges.Add(h);
                        }
                    }
            }

            return (solver, voxels);
        }

        private (MorphoSolver, List<VoxelCell>) BuildAnisoSpringCase(double size, SmpMat mat)
        {
            var v0 = MakeVoxel(0, 0, 0, 0, size, mat, expansionForce: 1.0);
            var v1 = MakeVoxel(1, size, 0, 0, size, mat, expansionForce: 1.0);

            // 비등방 설정
            v0.isActive = true;
            v0.fiberDir = Vector3d.XAxis;
            v0.epsMax = 0.05;
            v0.activationFraction = 0.8;

            v1.isActive = true;
            v1.fiberDir = Vector3d.XAxis;
            v1.epsMax = 0.05;
            v1.activationFraction = 0.8;

            var voxels = new List<VoxelCell> { v0, v1 };
            var solver = new MorphoSolver(voxels);
            solver.allPairs.Add(new VoxelPair(v0, v1));
            return (solver, voxels);
        }

        private VoxelCell MakeVoxel(int id, double x, double y, double z, double size, Material mat,
            double expansionForce = 1.0, double youngsModulus = 1.0)
        {
            var v = new VoxelCell(id, new Point3d(x, y, z));
            v.currentPoint = new Point3d(x, y, z);
            v.voxelSize = size;
            v.assignedMaterial = mat;
            v.expansionForce = expansionForce;
            v.currentYoungsModulus = youngsModulus;
            return v;
        }

        private struct CheckResult
        {
            public string Report;
            public double MaxRelativeError;
            public int FailedDOFCount;
            public bool Pass;
        }

        private CheckResult RunGradientCheck(MorphoSolver solver, List<VoxelCell> voxels, double eps, double threshold)
        {
            int n = voxels.Count * 3;
            var x0 = Vector<double>.Build.Dense(n);
            for (int i = 0; i < voxels.Count; i++)
            {
                x0[i * 3] = voxels[i].currentPoint.X;
                x0[i * 3 + 1] = voxels[i].currentPoint.Y;
                x0[i * 3 + 2] = voxels[i].currentPoint.Z;
            }
                
            var analytic = solver.DebugTotalGradient(x0);

            // 수치 gradient
            var numeric = Vector<double>.Build.Dense(n);
            for (int i = 0; i < n; i++)
            {
                var xp = x0.Clone(); xp[i] += eps;
                var xm = x0.Clone(); xm[i] -= eps;
                numeric[i] = (solver.DebugTotalEnergy(xp) - solver.DebugTotalEnergy(xm)) / (2.0 * eps);
            }

            // 성분별 상대오차
            double maxErr = 0.0;
            int failCount = 0;
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"[GradientCheck] n_dof={n}, eps={eps:E1}, threshold={threshold:E1}");
            sb.AppendLine($"{"DOF",-6} {"Analytic",-16} {"Numeric",-16} {"RelErr",-12}");

            for (int i = 0; i < n; i++)
            {
                double a = analytic[i];
                double num = numeric[i];
                double denom = Math.Max(Math.Abs(num), 1e-8);
                double relErr = Math.Abs(a - num) / denom;
                if (relErr > maxErr) maxErr = relErr;
                if (relErr > threshold) failCount++;

                if (n <= 30 || relErr > threshold)
                    sb.AppendLine($"{i,-6} {a,-16:E6} {num,-16:E6} {relErr,-12:E4}");
            }

            bool pass = failCount == 0;
            sb.AppendLine($"\n최대 상대오차: {maxErr:E4}  |  초과 DOF: {failCount}  |  판정: {(pass ? "PASS ✓" : "FAIL ✗")}");

            return new CheckResult
            {
                Report = sb.ToString(),
                MaxRelativeError = maxErr,
                FailedDOFCount = failCount,
                Pass = pass
            };
        }

        protected override Bitmap Icon => null;

        public override Guid ComponentGuid => new Guid("BBBBBBB1-1111-2222-3333-444444444444");
    }
}
