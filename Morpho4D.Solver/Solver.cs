using System.Collections.Generic;
using Rhino.Geometry;
using System.Linq;
using System;
using Grasshopper.Kernel.Types;
using System.Drawing;
using Morpho4D.Models;
using _Morpho4D;
using MathNet.Numerics.Optimization;
using MathNet.Numerics.LinearAlgebra;
using System.Numerics;
using Rhino;

namespace Morpho4D.Solver
{
    /// <summary>
    /// Hinge는 중심 복셀과 그 양옆에 존재하는 복셀로 이루어짐(벡터의 합이 0) -> 보통 복셀 1개당 13개의 힌지 관계 생성됨
    /// </summary>
    public class Hinge
    {
        public int centerId;
        public int rightId;
        public int leftId;
        public double hingeAngle;
        public double targetAngle;
        public Hinge(int left, int center, int right)
        {
            this.leftId = left;
            this.centerId = center;
            this.rightId = right;
            this.targetAngle = Math.PI;
        }
    }

    public class VoxelPair
    {
        public int idA;
        public int idB;
        public double pairLength;
        public VoxelPair(VoxelCell a, VoxelCell b)
        {
            this.idA = a.Id;
            this.idB = b.Id;
            this.pairLength = a.initialPoint.DistanceTo(b.initialPoint);
        }
    }

    public class MorphoSolver
    {
        public List<VoxelCell> inputVoxels = new List<VoxelCell>();
        public List<Hinge> allHinges = new List<Hinge>();
        public List<VoxelPair> allPairs = new List<VoxelPair>();

        // 솔버가 목적함수를 평가할 때마다 총 포텐셜 에너지를 기록 (SolverHistoryMonitor 유틸리티용)
        public List<double> energyHistory = new List<double>();

        /// <summary>
        /// Solver 클래스 생성자
        /// </summary>
        /// <param name="voxels"></param>
        public MorphoSolver(List<VoxelCell> voxels)
        {
            this.inputVoxels = voxels;
        }

        /// <summary>
        /// L-BFGS 이전의 전처리를 위한 함수
        /// </summary>
        /// <param name="createdVoxels"></param>
        /// <param name="referenceMesh"></param>
        public void setUp(List<VoxelCell> createdVoxels, Mesh referenceMesh)
        {
            this.inputVoxels = createdVoxels;
            this.buildNeighborConnectivity(referenceMesh);
            this.buildHingeConnectivity();
            this.buildVoxelpairConnectivity(referenceMesh);
        }

        /// <summary>
        /// 인접 복셀과의 연결성을 형성한다. 복셀 1개당 상하좌우대각선 26개의 복셀과의 연결성을 가지게 된다.
        /// 물론 경계점에 있는 복셀은 26개 이하의 복셀과 연결성을 가진다.
        /// 결과적으로 VoxelCell 객체 내의 neighborIndices 변수에 인접 복셀ID를 채워넣는다.
        /// </summary>
        /// <param name="referenceMesh"></param>
        public void buildNeighborConnectivity(Mesh referenceMesh)
        {
            var topology = referenceMesh.TopologyVertices;

            for (int i = 0; i < inputVoxels.Count; i++)
            {
                inputVoxels[i].Id = i;
                inputVoxels[i].neighborIndices.Clear();

                int topoIndex = topology.TopologyVertexIndex(i);

                int[] connectedTopoIndices = topology.ConnectedTopologyVertices(topoIndex);

                if (connectedTopoIndices != null)
                {
                    foreach (int tIndex in connectedTopoIndices)
                    {
                        int[] vIndices = topology.MeshVertexIndices(tIndex);

                        if (vIndices != null && vIndices.Length > 0)
                        {
                            int realVertexIndex = vIndices[0];

                            if (realVertexIndex < inputVoxels.Count)
                            {
                                inputVoxels[i].neighborIndices.Add(realVertexIndex);
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 중심 복셀에 대해 힌지를 이루는 복셀ID를 추출한다.
        /// </summary>
        public void buildHingeConnectivity()
        {
            allHinges.Clear();

            if (inputVoxels == null || inputVoxels.Count == 0) { return; }

            foreach (VoxelCell centerVoxel in inputVoxels)
            {
                centerVoxel.hingeIndices.Clear();

                var neighbors = centerVoxel.neighborIndices;
                if (neighbors.Count < 2) continue;

                for (int i = 0; i < neighbors.Count; i++)
                {
                    for (int j = i + 1; j < neighbors.Count; j++)
                    {
                        int idA = neighbors[i];
                        int idB = neighbors[j];

                        if (idA >= inputVoxels.Count || idB >= inputVoxels.Count) continue;

                        Vector3d vectorA = inputVoxels[idA].initialPoint - centerVoxel.initialPoint;
                        Vector3d vectorB = inputVoxels[idB].initialPoint - centerVoxel.initialPoint;

                        if ((vectorA + vectorB).Length < inputVoxels[0].voxelSize * 0.5)
                        {
                            Hinge newHinge = new Hinge(idA, centerVoxel.Id, idB);
                            centerVoxel.hingeIndices.Add(newHinge);
                            allHinges.Add(newHinge);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// VoxelPair 객체들을 생성한다.
        /// </summary>
        public void buildVoxelpairConnectivity(Mesh referenceMesh)
        {
            allPairs.Clear();

            var topoEdges = referenceMesh.TopologyEdges;
            var topoVertices = referenceMesh.TopologyVertices;

            for (int i = 0; i < topoEdges.Count; i++)
            {
                IndexPair edgeIndices = topoEdges.GetTopologyVertices(i);

                int indexA = topoVertices.MeshVertexIndices(edgeIndices.I)[0];
                int indexB = topoVertices.MeshVertexIndices(edgeIndices.J)[0];

                if (indexA < inputVoxels.Count && indexB < inputVoxels.Count)
                {
                    VoxelCell vA = inputVoxels[indexA];
                    VoxelCell vB = inputVoxels[indexB];

                    allPairs.Add(new VoxelPair(vA, vB));
                }
            }
        }

        /// <summary>
        /// 힌지로 연결된 복셀들의 각도(곡률)를 측정한다.
        /// </summary>
        /// <param name="hinge"></param>
        /// <returns></returns>
        public double calculateHingeAngle(Hinge hinge)
        {
            Vector3d vectorLeft = inputVoxels[hinge.leftId].currentPoint - inputVoxels[hinge.centerId].currentPoint;
            Vector3d vectorRight = inputVoxels[hinge.rightId].currentPoint - inputVoxels[hinge.centerId].currentPoint;

            vectorLeft.Unitize();
            vectorRight.Unitize();

            return Vector3d.VectorAngle(vectorLeft, vectorRight);
        }

        /// <summary>
        /// 초기 시간과 온도를 받아 물성 상태 초기 업데이트를 한 번 실행한다.
        /// </summary>
        /// <param name="time"></param>
        /// <param name="stimulus"></param>
        public void updateAllState(double time, Stimulus stimulus)
        {
            foreach (VoxelCell v in inputVoxels)
            {
                v.assignedMaterial.evaluateState(this, v, stimulus, time); // 타깃곡률 로직 추가, hinge 로직 추가
            }
        }

        /// <summary>
        /// L-BFGS를 사용하여 다음 복셀의 위치를 예상한다
        /// </summary>
        /// <param name="time"></param>
        /// <param name="stimulus"></param>
        public void execute(double time, Stimulus stimulus)
        {
            this.energyHistory.Clear(); // 이번 solve의 수렴 기록 초기화

            // 1. 복셀 상태 업데이트
            // evaluateState()            
            this.updateAllState(time, stimulus);

            // 2. 복셀들의 초기 좌표 추출하여 Vector<double>로 변환(initialGuess 생성)
            double[] initialCoords = new double[inputVoxels.Count * 3];

            for (int i = 0; i < inputVoxels.Count; i++)
            {
                initialCoords[i * 3] = inputVoxels[i].currentPoint.X;
                initialCoords[i * 3 + 1] = inputVoxels[i].currentPoint.Y;
                initialCoords[i * 3 + 2] = inputVoxels[i].currentPoint.Z;
            }

            var initialGuess = MathNet.Numerics.LinearAlgebra.Vector<double>.Build.DenseOfArray(initialCoords);

            // 3. IObjectiveFunction 객체 생성
            // -> IObjectiveFunction Gradient(Func<Vector<double>, double> function, Func<Vector<double>, Vector<double>> gradient) 메서드 사용
            IObjectiveFunction objective = ObjectiveFunction.Gradient(x => calculateTotalEnergy(x), x => calculateTotalGradient(x));

            // 4. LbfgsMinimizer 생성
            // BfgsMinimizer(double gradientTolerance, double parameterTolerance, double functionProgressTolerance, int maximumIterations) 생성자 사용
            var solver = new LimitedMemoryBfgsMinimizer(1e-5, 1e-5, 1e-5, 100);

            // 5. Minimize 호출
            // MinimizationResult FindMinimum(IObjectiveFunction objective, Vector<T> initialGuess)
            MinimizationResult result = solver.FindMinimum(objective, initialGuess);

            // 6. 최종 결과 반영
            var resultCoords = result.MinimizingPoint;

            for (int i = 0; i < inputVoxels.Count; i++)
            {
                // Anchor점 계산 제외
                if (inputVoxels[i].isFixed)
                {
                    continue;
                }

                double x = resultCoords[i * 3];
                double y = resultCoords[i * 3 + 1];
                double z = resultCoords[i * 3 + 2];

                inputVoxels[i].currentPoint = new Point3d(x, y, z);
            }
        }

        /// <summary>
        /// 현재 복셀들의 위치에서 발생하는 모든 에너지를 합산한다.
        /// </summary>
        /// <param name="x"></param>
        /// <returns></returns>
        private double calculateTotalEnergy(MathNet.Numerics.LinearAlgebra.Vector<double> x)
        {
            double totalEnergy = 0;

            for (int i = 0; i < inputVoxels.Count; i++)
            {
                inputVoxels[i].currentPoint = new Point3d(x[i * 3], x[i * 3 + 1], x[i * 3 + 2]);
            }

            foreach (VoxelPair p in allPairs)
            {
                totalEnergy += getSpringEnergy(p);
            }

            foreach (Hinge h in allHinges)
            {
                totalEnergy += getBendingEnergy(h);
            }

            // 외부 하중 포텐셜 에너지 E_ext = -F·x (LoadApplicator로 주입된 voxel.appliedLoad)
            for (int i = 0; i < inputVoxels.Count; i++)
            {
                Vector3d f = inputVoxels[i].appliedLoad;
                if (f.IsZero) continue;
                Point3d p = inputVoxels[i].currentPoint;
                totalEnergy += -(f.X * p.X + f.Y * p.Y + f.Z * p.Z);
            }

            this.energyHistory.Add(totalEnergy); // 수렴 모니터링용 기록
            return totalEnergy;
        }

        /// <summary>
        /// 각 복셀 좌표 변화에 따른 전체 에너지의 변화율을 계산하여 L-BFGS에 전달한다.
        /// </summary>
        /// <param name="x"></param>
        /// <returns></returns>
        private MathNet.Numerics.LinearAlgebra.Vector<double> calculateTotalGradient(MathNet.Numerics.LinearAlgebra.Vector<double> x)
        {
            var gradients = MathNet.Numerics.LinearAlgebra.Vector<double>.Build.Dense(inputVoxels.Count * 3);

            for (int i = 0; i < inputVoxels.Count; i++)
            {
                inputVoxels[i].currentPoint = new Point3d(x[i * 3], x[i * 3 + 1], x[i * 3 + 2]);
            }

            foreach (VoxelPair pair in allPairs)
            {
                Vector3d force = getSpringForce(pair);
                accumulateForce(gradients, pair.idA, force);
                accumulateForce(gradients, pair.idB, -force);
            }

            foreach (Hinge h in allHinges)
            {
                Vector3d[] forces = getBendingForce(h);
                accumulateForce(gradients, h.leftId, forces[0]);
                accumulateForce(gradients, h.centerId, forces[1]);
                accumulateForce(gradients, h.rightId, forces[2]);
            }

            // 외부 하중: 물리적 힘 +F를 누적 (gradients는 물리력 = -dE/dx, 마지막에 -1 곱하면 dE_ext/dx = -F)
            for (int i = 0; i < inputVoxels.Count; i++)
            {
                Vector3d f = inputVoxels[i].appliedLoad;
                if (!f.IsZero) accumulateForce(gradients, i, f);
            }

            for (int i = 0; i < inputVoxels.Count; i++)
            {
                if (inputVoxels[i].isFixed)
                {
                    gradients[i * 3] = 0;
                    gradients[i * 3 + 1] = 0;
                    gradients[i * 3 + 2] = 0;
                }
            }

            return gradients.Multiply(-1.0);
        }

        private void accumulateForce(MathNet.Numerics.LinearAlgebra.Vector<double> gradients, int voxelId, Vector3d force)
        {
            gradients[voxelId * 3] += force.X;
            gradients[voxelId * 3 + 1] += force.Y;
            gradients[voxelId * 3 + 2] += force.Z;
        }

        /// <summary>
        /// calculateTotalEnergy() 내부에서 실행됨
        /// </summary>
        /// <param name="a"></param>
        /// <param name="b"></param>
        /// <returns></returns>
        private double getSpringEnergy(VoxelPair pair)
        {
            double currentDist = inputVoxels[pair.idA].currentPoint.DistanceTo(inputVoxels[pair.idB].currentPoint);
            double initialDist = pair.pairLength;
            // [P0-2] 대칭 처리: 양 끝점 팽창의 평균을 rest length에 반영 (idA만 쓰면 순서 의존 -> bilayer 굽힘이 비대칭/오류)
            double avgExpansion = (inputVoxels[pair.idA].expansionForce + inputVoxels[pair.idB].expansionForce) * 0.5;
            double targetDist = initialDist * avgExpansion;

            double k = (inputVoxels[pair.idA].currentYoungsModulus + inputVoxels[pair.idB].currentYoungsModulus) * 0.5;

            return 0.5 * k * Math.Pow(currentDist - targetDist, 2);
        }

        /// <summary>
        /// calculateTotalEnergy() 내부에서 실행됨
        /// </summary>
        /// <param name="hinge"></param>
        /// <returns></returns>
        private double getBendingEnergy(Hinge hinge)
        {
            double currentAngle = this.calculateHingeAngle(hinge);
            double targetAngle = hinge.targetAngle;
            double k = inputVoxels[hinge.centerId].currentYoungsModulus;

            return 0.5 * k * Math.Pow(currentAngle - hinge.targetAngle, 2);
        }

        /// <summary>
        /// calculateTotalGradient() 내부에서 실행됨
        /// </summary>
        /// <param name="pair"></param>
        /// <returns></returns>
        private Vector3d getSpringForce(VoxelPair pair)
        {
            Vector3d dir = inputVoxels[pair.idB].currentPoint - inputVoxels[pair.idA].currentPoint;
            double currentDist = dir.Length;

            if (currentDist < 1e-9)
                return new Vector3d(0, 0, 0);

            double initialDist = pair.pairLength;
            // [P0-2] 대칭 처리 (getSpringEnergy와 동일해야 gradient 일관성 유지)
            double avgExpansion = (inputVoxels[pair.idA].expansionForce + inputVoxels[pair.idB].expansionForce) * 0.5;
            double targetDist = initialDist * avgExpansion;

            double k = (inputVoxels[pair.idA].currentYoungsModulus + inputVoxels[pair.idB].currentYoungsModulus) * 0.5;

            double forceMagnitude = k * (currentDist - targetDist);

            dir.Unitize();
            return dir * forceMagnitude;
        }

        /// <summary>
        /// calculateTotalGradient() 내부에서 실행됨
        /// </summary>
        /// <param name="hinge"></param>
        /// <returns></returns>
        private Vector3d[] getBendingForce(Hinge hinge)
        {
            Point3d ptLeft = inputVoxels[hinge.leftId].currentPoint;
            Point3d ptCenter = inputVoxels[hinge.centerId].currentPoint;
            Point3d ptRight = inputVoxels[hinge.rightId].currentPoint;

            Vector3d vectorLeft = ptLeft - ptCenter;
            Vector3d vectorRight = ptRight - ptCenter;
            double lenLeft = vectorLeft.Length;
            double lenRight = vectorRight.Length;

            if (lenLeft < 1e-9 || lenRight < 1e-9)
                return new Vector3d[3];

            double currentAngle = Vector3d.VectorAngle(vectorLeft, vectorRight);
            double targetAngle = hinge.targetAngle;
            double k = inputVoxels[hinge.centerId].currentYoungsModulus;
            double torqueMag = k * (currentAngle - targetAngle);

            Vector3d normal = Vector3d.CrossProduct(vectorLeft, vectorRight);
            if (normal.Length < 1e-9)
                return new Vector3d[3];

            Vector3d dirLeft = Vector3d.CrossProduct(normal, vectorLeft);
            Vector3d dirRight = Vector3d.CrossProduct(vectorRight, normal);
            dirLeft.Unitize();
            dirRight.Unitize();

            Vector3d forceLeft = dirLeft * (torqueMag / lenLeft);
            Vector3d forceRight = dirRight * (torqueMag / lenRight);
            Vector3d forceCenter = -(forceLeft + forceRight);

            return new Vector3d[] { forceLeft, forceCenter, forceRight };
        }

        /// <summary>
        /// 최종 시뮬레이션 점 리스트 추출
        /// </summary>
        /// <returns></returns>
        public List<Point3d> getResultPoints()
        {
            List<Point3d> result = new List<Point3d>();

            foreach (VoxelCell v in inputVoxels)
            {
                result.Add(v.currentPoint);
            }

            return result;
        }
    }
}