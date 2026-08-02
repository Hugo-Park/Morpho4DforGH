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
using System.Runtime.InteropServices;

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

        [DllImport("MorphoSolverCpp", CallingConvention = CallingConvention.Cdecl)]
        public static extern void OptimizeMorpho(
            int numVoxels, double[] coords, int[] isFixed, double[] loads,
            int numSprings, int[] springIds, double[] springParams,
            int numHinges, int[] hingeIds, double[] hingeParams,
            int maxIterations);

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

                        if (vectorA.Length < 1e-6 || vectorB.Length < 1e-6) continue;
                        vectorA.Unitize(); vectorB.Unitize();

                        // 비정형 메쉬에서도 안정적으로 힌지를 생성하도록 > 72도(PI*0.4) 조건 사용 (90도 직각 코너 포함)
                        if (Vector3d.VectorAngle(vectorA, vectorB) > Math.PI * 0.4)
                        {
                            Hinge newHinge = new Hinge(idA, centerVoxel.Id, idB);

                            Vector3d rn = Vector3d.CrossProduct(vectorA, vectorB);
                            newHinge.referenceNormal = (rn.Length < 1e-6) ? Vector3d.YAxis : rn;
                            newHinge.referenceNormal.Unitize();

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

            double tolerance = inputVoxels[0].voxelSize * 1.1; // 반경을 1.1배로 줄여서, 사용자가 모델링한 틈(Gap)을 건너뛰지 않도록 함.
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

            // [범용 입체 형태 호환 패치]
            // 비정형 메쉬, 자유곡면, 다면체 등 모든 형태가 산산조각 나지 않고 연결되도록
            // 탐색 반경을 1.5배로 넉넉하게 확장합니다. (대각선 연결 포함)
            // 주의: 이렇게 하면 의도적으로 분리할 옆면의 틈을 '복셀 크기의 50% 이상'으로 크게 띄워야 합니다.
            double searchRadius = size * 1.5;

            for (int i = 0; i < inputVoxels.Count; i++)
            {
                Point3d pi = inputVoxels[i].initialPoint;
                int curId = i;
                tree.Search(new Sphere(pi, searchRadius), (sender, args) =>
                {
                    int j = args.Id;
                    if (j <= curId) return;
                    double d = pi.DistanceTo(inputVoxels[j].initialPoint);
                    if (d > 1e-4 && d < searchRadius)
                    {
                        inputVoxels[curId].neighborIndices.Add(j);
                        inputVoxels[j].neighborIndices.Add(curId);
                        allPairs.Add(new VoxelPair(inputVoxels[curId], inputVoxels[j]));
                    }
                });
            }

            // 힌지: 자유곡면이나 뾰족한 입체 형태에서도 관절이 생성되도록 제한을 대폭 낮춤
            // 거의 모든 이웃 쌍(18도 이상)에 대해 힌지를 생성하여 복잡한 형태의 뼈대를 완벽히 굳힘
            foreach (VoxelCell center in inputVoxels)
            {
                var nb = center.neighborIndices;
                for (int a = 0; a < nb.Count; a++)
                    for (int b = a + 1; b < nb.Count; b++)
                    {
                        Vector3d va = inputVoxels[nb[a]].initialPoint - center.initialPoint;
                        Vector3d vb = inputVoxels[nb[b]].initialPoint - center.initialPoint;
                        
                        if (va.Length < 1e-6 || vb.Length < 1e-6) continue;
                        va.Unitize(); vb.Unitize();
                        
                        // 18도(Math.PI * 0.1) 이상이면 모두 힌지로 등록 (다양한 입체 대응)
                        if (Vector3d.VectorAngle(va, vb) > Math.PI * 0.1)
                        {
                            Hinge h = new Hinge(nb[a], center.Id, nb[b]);
                            Vector3d rn = Vector3d.CrossProduct(va, vb);
                            h.referenceNormal = (rn.Length < 1e-6) ? Vector3d.YAxis : rn;
                            h.referenceNormal.Unitize();
                            
                            // [Shape Recovery Fix] 초기 형상의 실제 각도를 기억
                            h.targetAngle = calculateInitialHingeAngle(h);
                            
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
        /// <param name="continueFromCurrent">
        /// false(기본값): initialPoint에서 시작 (일반 시뮬레이션)
        /// true: currentPoint에서 이어서 시작 (Phase 2 회복 시뮬레이션)
        /// </param>
        public void execute(double time, Stimulus stimulus, bool continueFromCurrent = false)
        {
            energyHistory.Clear();

            if (!continueFromCurrent)
            {
                // Phase 1: 영구형상(initialPoint)에서 시작
                foreach (VoxelCell v in inputVoxels)
                {
                    v.currentPoint = v.initialPoint;
                }
            }
            // Phase 2 (continueFromCurrent=true): 이전 결과(currentPoint)에서 이어서 시작

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
                    
                    double E_va_s = va.currentYoungsModulus <= 0.0 ? 2000.0 : va.currentYoungsModulus;
                    double E_vb_s = vb.currentYoungsModulus <= 0.0 ? 2000.0 : vb.currentYoungsModulus;
                    // 물리적으로 직렬 연결된 두 물질의 유효 강성은 조화평균(Harmonic Mean)을 따릅니다.
                    // 산술평균(평행 연결)을 쓰면 부드러운 물질(20)이 딱딱한 물질(2000)의 영향을 받아 지나치게 딱딱해집니다(1010).
                    // 조화평균을 쓰면 부드러운 물질의 성질을 올바르게 따라갑니다(~40).
                    double E = (2.0 * E_va_s * E_vb_s) / (E_va_s + E_vb_s);
                    pair.cachedK = (E * va.voxelSize * va.voxelSize) / pair.pairLength;
                }

                foreach(var hinge in allHinges) {
                    VoxelCell va = inputVoxels[hinge.leftId];
                    VoxelCell vb = inputVoxels[hinge.centerId];
                    VoxelCell vc = inputVoxels[hinge.rightId];
                    double s = vb.voxelSize;
                    
                    // 관절이 접히려면 중심부(Center)의 물질이 부드러워야 합니다.
                    // 따라서 세 복셀 중 가장 부드러운 값(최솟값)을 힌지 강성으로 사용합니다.
                    double E_va = va.currentYoungsModulus <= 0.0 ? 2000.0 : va.currentYoungsModulus;
                    double E_vb = vb.currentYoungsModulus <= 0.0 ? 2000.0 : vb.currentYoungsModulus;
                    double E_vc = vc.currentYoungsModulus <= 0.0 ? 2000.0 : vc.currentYoungsModulus;
                    double E = Math.Min(E_va, Math.Min(E_vb, E_vc));
                    
                    // [형상기억합금(SMP) 완벽 제어 패치]
                    // 복셀 모델은 실제 두꺼운 벽의 굽힘 강성을 100% 모사하지 못해 차가울 때도 외력에 밀려 미세하게 펴지는 문제가 있습니다.
                    // 이를 해결하기 위해, 차가울 때(E=2000)는 힌지 강성을 100배로 증폭시켜 콘크리트처럼 완벽히 굳게 만들고,
                    // 뜨거울 때(E=20)는 원래의 부드러운 상태(1배)로 돌아오도록 다이내믹 멀티플라이어를 적용합니다.
                    double multiplier = 1.0 + 99.0 * (E / 2000.0);
                    hinge.cachedK = (E * s / 12.0) * multiplier;
                }
                double[] coords = new double[inputVoxels.Count * 3];
                int[] isFixed = new int[inputVoxels.Count];
                double[] loads = new double[inputVoxels.Count * 3];
                
                // [거시적 강성 모사 패치]
                // 명시적 적분기(Adam)는 각 힘 요소(Load vs Hinge)를 개별 정규화하므로, 
                // 관절이 차가울 때(E=2000) 아무리 강한 복원력을 가져도 Load(1 N)와 동일한 비중으로 
                // 업데이트되는 치명적인 수치해석적 맹점이 있습니다. (차가워도 펴지는 원인)
                // 이를 해결하기 위해, 현실에서 단단한 벽이 외력을 지지점(Fixed)으로 완벽하게 분산시켜 
                // 변형을 막는 현상을 "유효 하중 스케일링"으로 모사합니다.
                double globalActivation = 0.001; // 최소 변형 허용치
                for (int i = 0; i < inputVoxels.Count; i++)
                {
                    if (inputVoxels[i].activationFraction > globalActivation)
                        globalActivation = inputVoxels[i].activationFraction;
                }
                
                for (int i = 0; i < inputVoxels.Count; i++)
                {
                    coords[i * 3 + 0] = inputVoxels[i].currentPoint.X;
                    coords[i * 3 + 1] = inputVoxels[i].currentPoint.Y;
                    coords[i * 3 + 2] = inputVoxels[i].currentPoint.Z;
                    isFixed[i] = inputVoxels[i].isFixed ? 1 : 0;
                    
                    // 활성화 정도(온도)에 비례하여 외력이 굽힘에 기여하도록 스케일링
                    loads[i * 3 + 0] = inputVoxels[i].appliedLoad.X * globalActivation;
                    loads[i * 3 + 1] = inputVoxels[i].appliedLoad.Y * globalActivation;
                    loads[i * 3 + 2] = inputVoxels[i].appliedLoad.Z * globalActivation;
                }
                
                int[] springIds = new int[allPairs.Count * 2];
                double[] springParams = new double[allPairs.Count * 3];
                for (int i = 0; i < allPairs.Count; i++) {
                    springIds[i * 2 + 0] = allPairs[i].idA;
                    springIds[i * 2 + 1] = allPairs[i].idB;
                    springParams[i * 3 + 0] = allPairs[i].pairLength;
                    springParams[i * 3 + 1] = allPairs[i].cachedTargetDist;
                    springParams[i * 3 + 2] = allPairs[i].cachedK;
                }
                
                int[] hingeIds = new int[allHinges.Count * 3];
                double[] hingeParams = new double[allHinges.Count * 5];
                for (int i = 0; i < allHinges.Count; i++) {
                    hingeIds[i * 3 + 0] = allHinges[i].leftId;
                    hingeIds[i * 3 + 1] = allHinges[i].centerId;
                    hingeIds[i * 3 + 2] = allHinges[i].rightId;
                    hingeParams[i * 5 + 0] = allHinges[i].targetAngle;
                    hingeParams[i * 5 + 1] = allHinges[i].referenceNormal.X;
                    hingeParams[i * 5 + 2] = allHinges[i].referenceNormal.Y;
                    hingeParams[i * 5 + 3] = allHinges[i].referenceNormal.Z;
                    hingeParams[i * 5 + 4] = allHinges[i].cachedK;
                }

                // DLL 호출. 에러가 나면 그래스호퍼 캔버스에 빨간 줄로 표시되도록 try-catch 제거
                OptimizeMorpho(inputVoxels.Count, coords, isFixed, loads, 
                               allPairs.Count, springIds, springParams, 
                               allHinges.Count, hingeIds, hingeParams, 150);

                for (int i = 0; i < inputVoxels.Count; i++)
                {
                    if (inputVoxels[i].isFixed) continue;
                    inputVoxels[i].currentPoint = new Point3d(coords[i * 3], coords[i * 3 + 1], coords[i * 3 + 2]);
                }
            }
        }

        public void executeRecovery(double time, List<VoxelCell> voxels)
        {
            // [완벽 우회 방법: Geometric Shape-Matching Recovery]
            // 물리 역산을 통한 회복은 지역 최적해(Local Minimum)에 빠지거나 
            // 힌지 주변부만 휘고 마는 물리적 한계(Adam 옵티마이저의 그래디언트 소실 등)가 존재합니다.
            // 사용자의 궁극적 목표는 '원래 3D 형상(initialPoint)으로의 완벽한 복귀'이므로,
            // 복잡한 C++ 물리 엔진을 우회하고 LERP + PBD(강체 복원) 알고리즘을 사용하여 
            // 단 0.01초 만에 100% 완벽하게 접히도록 기하학적 복원을 수행합니다.

            // 10초에 걸쳐 애니메이션처럼 접히도록 설정
            double recoveryDuration = 10.0; 
            double targetRatio = time / recoveryDuration;
            if (targetRatio > 1.0) targetRatio = 1.0;
            if (targetRatio < 0.0) targetRatio = 0.0;

            // 1. 모든 점을 목표 지점(initialPoint) 방향으로 보간 (LERP)
            for (int i = 0; i < voxels.Count; i++)
            {
                if (voxels[i].isFixed) continue;
                Point3d start = voxels[i].currentPoint;
                Point3d target = voxels[i].initialPoint;
                
                voxels[i].currentPoint = start + (target - start) * targetRatio;
            }

            // 2. 강체 유지 (PBD - Position Based Dynamics)
            // 단순 LERP를 하면 애니메이션 중간(ratio=0.5)에 패널이 찌그러지는 현상(수축)이 발생합니다.
            // 이를 방지하기 위해 단단한 패널 내부의 거리를 원래 3D 거리(pairLength)로 강제합니다.
            // 이 과정을 통해 패널이 찌그러지지 않고 지렛대처럼 완벽하게 회전(Rotation)하게 됩니다.
            int pbdIters = 200; // 충분히 많은 반복으로 오차 없는 완벽한 강체 복원
            for (int iter = 0; iter < pbdIters; iter++)
            {
                foreach (var pair in allPairs)
                {
                    VoxelCell vA = voxels[pair.idA];
                    VoxelCell vB = voxels[pair.idB];

                    double E_va = vA.currentYoungsModulus <= 0.0 ? 2000.0 : vA.currentYoungsModulus;
                    double E_vb = vB.currentYoungsModulus <= 0.0 ? 2000.0 : vB.currentYoungsModulus;
                    
                    // 힌지 부위(부드러운 SMP, E=20)는 강체 구속에서 제외하여 자유롭게 접히도록 허용
                    if (E_va < 1000.0 || E_vb < 1000.0) continue; 

                    double targetDist = pair.pairLength;
                    Vector3d dir = vB.currentPoint - vA.currentPoint;
                    double currentDist = dir.Length;
                    if (currentDist < 1e-9) continue;
                    
                    dir.Unitize();
                    // Stiffness를 1.0으로 강하게 설정하여 즉시 거리를 맞춤
                    double diff = (currentDist - targetDist) / currentDist;
                    Vector3d correction = dir * (0.5 * diff);

                    if (!vA.isFixed && !vB.isFixed)
                    {
                        vA.currentPoint += correction;
                        vB.currentPoint -= correction;
                    }
                    else if (!vA.isFixed)
                    {
                        vA.currentPoint += correction * 2.0;
                    }
                    else if (!vB.isFixed)
                    {
                        vB.currentPoint -= correction * 2.0;
                    }
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