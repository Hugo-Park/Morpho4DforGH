using System;
using System.Collections.Generic;
using System.Drawing;
using Grasshopper.Kernel;
using Morpho4D.Models;
using Morpho4D.Solver;
using Rhino.Geometry;

namespace _Morpho4D
{
    public class RecoverySolverComponent : GH_Component
    {
        public RecoverySolverComponent()
          : base("Recovery Solver", "ReSlv",
              "Shape Memory Recovery solver. Takes unfolded 2D voxels and folds them back to the original 3D shape using the remembered permanent shape (initialPoint).",
              "Morpho4D", "04 Solver")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Voxels", "VX", "List of Voxel objects (output from MorphoSolver)", GH_ParamAccess.list);
            pManager.AddNumberParameter("Time", "T", "Simulation Time Step", GH_ParamAccess.item);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddMeshParameter("Mesh", "M", "Deformed geometry after simulation", GH_ParamAccess.item);
            pManager.AddPointParameter("Points", "P", "Optimized voxel center points", GH_ParamAccess.list);
            pManager.AddGenericParameter("Voxels", "VX", "Deformed Voxels", GH_ParamAccess.list);
            pManager.AddTextParameter("Diag", "Diag", "Diagnostic Information", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            List<VoxelCellGoo> voxelGoos = new List<VoxelCellGoo>();
            double time = 0.0;

            if (!DA.GetDataList(0, voxelGoos)) { return; }
            if (!DA.GetData(1, ref time)) { return; }

            if (time <= 0) return;

            // 1. VoxelCell 리스트 추출 (복사)
            List<VoxelCell> voxels = new List<VoxelCell>();
            foreach (VoxelCellGoo goo in voxelGoos)
            {
                if (goo != null && goo.Value != null)
                {
                    VoxelCell original = goo.Value;
                    VoxelCell v = new VoxelCell(original.Id, original.initialPoint);
                    v.voxelSize = original.voxelSize;
                    v.currentPoint = original.currentPoint;
                    v.isActive = original.isActive;
                    v.fiberDir = original.fiberDir;
                    v.epsMax = original.epsMax;
                    v.assignedMaterial = original.assignedMaterial;
                    v.isFixed = original.isFixed;
                    v.appliedLoad = original.appliedLoad;
                    voxels.Add(v);
                }
            }

            if (voxels.Count == 0) return;

            // 2. 솔버 초기화 (initialPoint = 3D 상자 기준으로 hinge targetAngle과 spring pairLength 설정)
            MorphoSolver solver = new MorphoSolver(voxels);
            solver.setUpFromGrid(voxels);

            // ===================================================================
            // [Recovery 핵심 로직]
            //
            // MorphoSolver가 상자를 펴는 원리:
            //   열(Stimulus)을 가하면 SMP가 부드러워지고(E↓), activationFraction이 올라가면서
            //   스프링의 aniso(비등방 변형률)가 증가하여 스프링 목표 거리가 늘어나고,
            //   이 늘어난 스프링 힘이 관절을 강제로 펴버림.
            //
            // Recovery에서 다시 접으려면:
            //   - hinge targetAngle은 이미 3D 상자의 초기 각도로 설정되어 있으므로 그대로 유지
            //   - SMP를 부드럽게 만들어서(E↓) 관절이 자유롭게 움직이도록 해야 함
            //   - 하지만 activationFraction=0으로 유지하여 스프링 aniso를 제거 (펴는 힘 차단)
            //   - 이렇게 하면 힌지 토크만 남아서 관절이 초기 3D 각도를 향해 접힌다!
            // ===================================================================

            // 3. SMP 복셀을 부드럽게 만들되, activationFraction은 0으로 유지 (aniso 제거)
            foreach (var v in voxels)
            {
                if (v.assignedMaterial is SmpMat smp)
                {
                    // 부드러운 상태로 만들어 관절이 자유롭게 움직이도록 함
                    v.currentYoungsModulus = smp.rubberyModulus > 0 ? smp.rubberyModulus : 20.0;
                }
                else
                {
                    if (v.currentYoungsModulus <= 0) v.currentYoungsModulus = 2000.0;
                }
                // aniso 제거 + 팽창 없음
                v.activationFraction = 0.0;
                v.expansionForce = 1.0;
            }

            // 4. executeRecovery: updateAllState를 건너뛰고 위에서 세팅한 재료 상태를 그대로 사용하여 C++ 엔진 호출
            solver.executeRecovery(time, voxels);

            // 5. 결과물 래핑 및 메쉬 생성
            List<Point3d> resultPoints = solver.getResultPoints();
            List<VoxelCellGoo> resultGoos = new List<VoxelCellGoo>();
            
            for (int i = 0; i < voxels.Count; i++)
            {
                var v = voxels[i];
                v.currentPoint = resultPoints[i];
                resultGoos.Add(new VoxelCellGoo(v));
            }

            string diagMsg = $"[Recovery] Voxels={voxels.Count}, Springs={solver.allPairs.Count}, Hinges={solver.allHinges.Count}";
            this.AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, diagMsg);

            Mesh mesh = MorphoSolverComponent.BuildDeformedBoxMesh(voxels, resultPoints);
            mesh.Normals.ComputeNormals();
            mesh.FaceNormals.ComputeFaceNormals();
            
            DA.SetData(0, mesh);
            DA.SetDataList(1, resultPoints);
            DA.SetDataList(2, resultGoos);
            DA.SetData(3, diagMsg);
        }

        protected override Bitmap Icon => null;
        public override Guid ComponentGuid => new Guid("a7c51984-2a62-4f7b-91d5-88a2c1f9d443");
    }
}
