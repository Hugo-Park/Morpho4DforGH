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
using MathNet.Numerics.Random;

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
        public Vector3d referenceNormal;
        public double cachedK;
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
        public double cachedTargetDist;
        public double cachedK;
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

        // G8: SolverHistoryMonitor가 읽는 수렴 에너지 기록
        public List<double> energyHistory { get; } = new List<double>();

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
            this.buildHingeConnectivity(referenceMesh);
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
        public void buildHingeConnectivity(Mesh referenceMesh)
        {
            allHinges.Clear();
            if (inputVoxels == null || inputVoxels.Count == 0) { return; }

            if (referenceMesh.Normals.Count == 0)
            {
                referenceMesh.Normals.ComputeNormals();
            }

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

                            if (referenceMesh.Normals.Count > centerVoxel.Id)
                            {
                                newHinge.referenceNormal = (Vector3d)referenceMesh.Normals[centerVoxel.Id];
                            }
                            else
                            {
                                newHinge.referenceNormal = Vector3d.ZAxis;
                            }

                            centerVoxel.hingeIndices.Add(newHinge);
                            newHinge.targetAngle = this.calculateInitialHingeAngle(newHinge);
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

            double tolerance = 1e-4;
            Rhino.Geometry.RTree tree = new Rhino.Geometry.RTree();

            for (int i = 0; i < inputVoxels.Count; i++)
            {
                tree.Insert(inputVoxels[i].initialPoint, i);
            }

            for (int i = 0; i < inputVoxels.Count; i++)
            {
                Point3d pt = inputVoxels[i].initialPoint;
                int currentId = i;

                tree.Search(new Sphere(pt, tolerance), (sender, args) =>
                {
                    int neighborId = args.Id;
                    if (neighborId > currentId)
                    {
                        VoxelCell vA = inputVoxels[currentId];
                        VoxelCell vB = inputVoxels[neighborId];

                        allPairs.Add(new VoxelPair(vA, vB));
                    }
                });
            }
        }

        /// <summary>
        /// G3: 체적 격자 전용 setup — mesh 없이 RTree 기반으로 연결성을 구성한다. (결함 C 해결)
        /// </summary>
        public void setUpFromGrid(List<VoxelCell> createdVoxels)
        {
            this.inputVoxels = createdVoxels;
            this.buildGridConnectivity();
        }

        /// <summary>
        /// G3: RTree 기반 격자 연결성 빌더. 기존 mesh 기반 buildNeighborConnectivity 3개와 병행.
        /// </summary>
        public void buildGridConnectivity()
        {
            allHinges.Clear();
            allPairs.Clear();
            if (inputVoxels == null || inputVoxels.Count == 0) return;

            double size = inputVoxels[0].voxelSize;
            double tol = size * 0.05;
            Rhino.Geometry.RTree tree = new Rhino.Geometry.RTree();

            for (int i = 0; i < inputVoxels.Count; i++)
            {
                inputVoxels[i].Id = i;
                inputVoxels[i].neighborIndices.Clear();
                inputVoxels[i].hingeIndices.Clear();
                tree.Insert(inputVoxels[i].initialPoint, i);
            }

            double r18 = size * Math.Sqrt(2.0) + tol;
            for (int i = 0; i < inputVoxels.Count; i++)
            {
                Point3d pi = inputVoxels[i].initialPoint;
                int curId = i;
                tree.Search(new Sphere(pi, r18), (sender, args) =>
                {
                    int j = args.Id;
                    if (j <= curId) return;
                    double d = pi.DistanceTo(inputVoxels[j].initialPoint);
                    if (Math.Abs(d - size) < tol)
                    {
                        inputVoxels[curId].neighborIndices.Add(j);
                        inputVoxels[j].neighborIndices.Add(curId);
                        allPairs.Add(new VoxelPair(inputVoxels[curId], inputVoxels[j]));
                    }
                    else if (Math.Abs(d - size * Math.Sqrt(2.0)) < tol)
                    {
                        allPairs.Add(new VoxelPair(inputVoxels[curId], inputVoxels[j]));
                    }
                });
            }

            // 힌지: axis-aligned 정반대쌍만 생성
            foreach (VoxelCell center in inputVoxels)
            {
                var nb = center.neighborIndices;
                for (int a = 0; a < nb.Count; a++)
                    for (int b = a + 1; b < nb.Count; b++)
                    {
                        Vector3d va = inputVoxels[nb[a]].initialPoint - center.initialPoint;
                        Vector3d vb = inputVoxels[nb[b]].initialPoint - center.initialPoint;
                        if ((va + vb).Length < size * 0.1)
                        {
                            Hinge h = new Hinge(nb[a], center.Id, nb[b]);
                            Vector3d rn = Vector3d.CrossProduct(va, Vector3d.ZAxis);
                            h.referenceNormal = (rn.Length < 1e-6) ? Vector3d.YAxis : rn;
                            h.referenceNormal.Unitize();
                            h.targetAngle = Math.PI;
                            center.hingeIndices.Add(h);
                            allHinges.Add(h);
                        }
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

            if (vectorLeft.Length < 1e-9 || vectorRight.Length < 1e-9)
            {
                return hinge.targetAngle;
            }
            vectorLeft.Unitize();
            vectorRight.Unitize();

            double angle = Vector3d.VectorAngle(vectorLeft, vectorRight);

            Vector3d cross = Vector3d.CrossProduct(vectorLeft, vectorRight);
            if (Vector3d.Multiply(cross, hinge.referenceNormal) < 0)
            {
                angle = 2.0 * Math.PI - angle;
            }

            return angle;

        }

        public double calculateInitialHingeAngle(Hinge hinge)
        {
            Vector3d vectorLeft = inputVoxels[hinge.leftId].initialPoint - inputVoxels[hinge.centerId].initialPoint;
            Vector3d vectorRight = inputVoxels[hinge.rightId].initialPoint - inputVoxels[hinge.centerId].initialPoint;

            if (vectorLeft.Length < 1e-9 || vectorRight.Length < 1e-9)
            {
                return hinge.targetAngle;
            }
            vectorLeft.Unitize();
            vectorRight.Unitize();

            double angle = Vector3d.VectorAngle(vectorLeft, vectorRight);

            Vector3d cross = Vector3d.CrossProduct(vectorLeft, vectorRight);
            if (Vector3d.Multiply(cross, hinge.referenceNormal) < 0)
            {
                angle = 2.0 * Math.PI - angle;
            }

            return angle;

        }

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
            energyHistory.Clear();

            foreach (VoxelCell v in inputVoxels)
            {
                v.currentPoint = v.initialPoint;
            }

            if (time <= 0) return;

            int steps = 5;

            Random rnd = new Random(42);
            for (int i = 0; i < inputVoxels.Count; i++)
            {
                double noiseZ = (rnd.NextDouble() - 0.5) * 1e-5;
                inputVoxels[i].currentPoint = new Point3d(
                    inputVoxels[i].currentPoint.X, 
                    inputVoxels[i].currentPoint.Y, 
                    inputVoxels[i].currentPoint.Z + noiseZ);
            }

            for (int step = 1; step <= steps; step++)
            {
                // 2차 곡선 시간 쪼개기
                double ratio = (double)step / steps;
                double currentTime = time * Math.Pow(ratio, 2.0); 

                this.updateAllState(currentTime, stimulus);

                // [최적화] 스프링 및 힌지 강성/목표길이 캐싱
                foreach(var pair in allPairs) {
                    VoxelCell va = inputVoxels[pair.idA];
                    VoxelCell vb = inputVoxels[pair.idB];
                    
                    Vector3d pairDir = vb.initialPoint - va.initialPoint;
                    if (pairDir.Length > 1e-9) pairDir.Unitize();
                    double aniso = 0.0;
                    if (va.isActive) { double d = pairDir * va.fiberDir; aniso += 0.5 * va.epsMax * va.activationFraction * d * d; }
                    if (vb.isActive) { double d = pairDir * vb.fiberDir; aniso += 0.5 * vb.epsMax * vb.activationFraction * d * d; }
                    
                    double avgExpansion = (va.expansionForce + vb.expansionForce) * 0.5;
                    pair.cachedTargetDist = pair.pairLength * (avgExpansion + aniso);
                    
                    double E = (va.currentYoungsModulus + vb.currentYoungsModulus) * 0.5;
                    pair.cachedK = (E * va.voxelSize * va.voxelSize) / pair.pairLength;
                }

                foreach(var hinge in allHinges) {
                    double E = inputVoxels[hinge.centerId].currentYoungsModulus;
                    double s = inputVoxels[hinge.centerId].voxelSize;
                    hinge.cachedK = E * Math.Pow(s, 3) / 12.0;
                }
                double[] initialCoords = new double[inputVoxels.Count * 3];
                for (int i = 0; i < inputVoxels.Count; i++)
                {
                    initialCoords[i * 3] = inputVoxels[i].currentPoint.X;
                    initialCoords[i * 3 + 1] = inputVoxels[i].currentPoint.Y;
                    initialCoords[i * 3 + 2] = inputVoxels[i].currentPoint.Z;
                }

                var initialGuess = MathNet.Numerics.LinearAlgebra.Vector<double>.Build.DenseOfArray(initialCoords);

                MathNet.Numerics.LinearAlgebra.Vector<double> bestX = null;
                double minEnergy = double.MaxValue;

                IObjectiveFunction objective = ObjectiveFunction.Gradient(x => {
                    double energy = calculateTotalEnergy(x);
                    energyHistory.Add(energy);  // G8: 수렴 기록
                    if (energy < minEnergy) {
                        minEnergy = energy;
                        bestX = x.Clone();
                    }
                    return energy;
                }, x => calculateTotalGradient(x));

                // 허용 오차를 조금 낮춰서(1e-2) 정답을 대충 찾아도 바로 넘어가게 하고, 최대 30번만 계산하게 하여 속도 대폭 향상
                var solver = new BfgsMinimizer(1e-2, 1e-2, 1e-2, 30);

                try 
                {
                    MinimizationResult result = solver.FindMinimum(objective, initialGuess);
                    bestX = result.MinimizingPoint;
                }
                catch (Exception)
                {
                    // 에러 발생 시 무시하고 진행된 곳까지만 반영
                }

                var resultCoords = bestX ?? initialGuess;

                for (int i = 0; i < inputVoxels.Count; i++)
                {
                    if (inputVoxels[i].isFixed) continue;

                    double x = resultCoords[i * 3];
                    double y = resultCoords[i * 3 + 1];
                    double z = resultCoords[i * 3 + 2];

                    inputVoxels[i].currentPoint = new Point3d(x, y, z);
                }
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
                if (inputVoxels[i].isFixed) continue;
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

            // G8: 외부 하중 포텐셜 에너지 -F·x
            foreach (var v in inputVoxels)
            {
                if (v.isFixed) continue;
                totalEnergy -= v.appliedLoad.X * v.currentPoint.X
                             + v.appliedLoad.Y * v.currentPoint.Y
                             + v.appliedLoad.Z * v.currentPoint.Z;
            }

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
                if (inputVoxels[i].isFixed) continue;
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

            // G8: 외부 하중 gradient (∂(-F·x)/∂x = -F, 하중 방향으로 gradient 누적)
            foreach (var v in inputVoxels)
            {
                if (v.isFixed) continue;
                gradients[v.Id * 3]     -= v.appliedLoad.X;
                gradients[v.Id * 3 + 1] -= v.appliedLoad.Y;
                gradients[v.Id * 3 + 2] -= v.appliedLoad.Z;
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
        /// <param name="pair"></param>
        /// <returns></returns>
        private double getSpringEnergy(VoxelPair pair)
        {
            VoxelCell va = inputVoxels[pair.idA];
            VoxelCell vb = inputVoxels[pair.idB];
            double currentDist = va.currentPoint.DistanceTo(vb.currentPoint);
            
            if (pair.pairLength < 1e-9) return 0.0;

            return 0.5 * pair.cachedK * Math.Pow(currentDist - pair.cachedTargetDist, 2);
        }

        /// <summary>
        /// calculateTotalEnergy() 내부에서 실행됨
        /// </summary>
        /// <param name="hinge"></param>
        /// <returns></returns>
        private double getBendingEnergy(Hinge hinge)
        {
            double currentAngle = this.calculateHingeAngle(hinge);

            return 0.5 * hinge.cachedK * Math.Pow(currentAngle - hinge.targetAngle, 2);
        }

        /// <summary>
        /// calculateTotalGradient() 내부에서 실행됨
        /// </summary>
        /// <param name="pair"></param>
        /// <returns></returns>
        private Vector3d getSpringForce(VoxelPair pair)
        {
            VoxelCell va = inputVoxels[pair.idA];
            VoxelCell vb = inputVoxels[pair.idB];
            Vector3d dir = vb.currentPoint - va.currentPoint;
            double currentDist = dir.Length;

            if (currentDist < 1e-9)
            {
                dir = vb.initialPoint - va.initialPoint;
                currentDist = 0.0;
            }

            if (pair.pairLength < 1e-9) return new Vector3d(0, 0, 0);

            double forceMagnitude = pair.cachedK * (currentDist - pair.cachedTargetDist);

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

            double currentAngle = this.calculateHingeAngle(hinge);
            double targetAngle = hinge.targetAngle;

            double torqueMag = hinge.cachedK * (currentAngle - targetAngle);

            Vector3d normal = Vector3d.CrossProduct(vectorLeft, vectorRight);
            if (normal.Length < 1e-4)
            {
                normal = hinge.referenceNormal;
            }
            else
            {
                normal.Unitize();
                if (Vector3d.Multiply(normal, hinge.referenceNormal) < 0)
                {
                    normal = -normal;
                }
            }

            Vector3d dirLeft = Vector3d.CrossProduct(normal, vectorLeft);
            Vector3d dirRight = Vector3d.CrossProduct(vectorRight, normal);
            dirLeft.Unitize();
            dirRight.Unitize();

            Vector3d forceLeft = dirLeft * (torqueMag / lenLeft);
            Vector3d forceRight = dirRight * (torqueMag / lenRight);
            Vector3d forceCenter = -(forceLeft + forceRight);

            return new Vector3d[] { forceLeft, forceCenter, forceRight };
        }

        /// <summary> [테스트 전용] calculateTotalEnergy의 public 래퍼. GradientCheck에서만 사용. </summary>
        public double DebugTotalEnergy(MathNet.Numerics.LinearAlgebra.Vector<double> x) => calculateTotalEnergy(x);

        /// <summary> [테스트 전용] calculateTotalGradient의 public 래퍼. GradientCheck에서만 사용. </summary>
        public MathNet.Numerics.LinearAlgebra.Vector<double> DebugTotalGradient(MathNet.Numerics.LinearAlgebra.Vector<double> x) => calculateTotalGradient(x);

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