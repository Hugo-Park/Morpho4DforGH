using System;
using System.Collections.Generic;

using Grasshopper;
using Grasshopper.Kernel;
using Rhino.Geometry;
using Morpho4D.Models;
using Morpho4D.Solver;
using Grasshopper.Kernel.Geometry.ConvexHull;
using Rhino.Commands;
using Rhino.Render;
using System.Drawing;

namespace _Morpho4D
{
    public class MorphoSolverComponent : GH_Component
    {
        public MorphoSolverComponent()
          : base("Morpho Solver", "MSlv",
            "Simulation solver for Morpho4D",
            "Morpho4D", "04 Solver")
        {
        }

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddBrepParameter("Base Breps", "BB", "Original reference Brep objects", GH_ParamAccess.list);
            pManager.AddGenericParameter("Voxels", "VX", "List of Voxel objects", GH_ParamAccess.list);
            pManager.AddGenericParameter("Stimulus", "S", "External stimulus object (e.g., Temperature, Humidity) that triggers voxel deformation.", GH_ParamAccess.list);
            pManager.AddNumberParameter("Time", "T", "Simulation Time Step", GH_ParamAccess.item);
            pManager.AddBooleanParameter("Continue", "C",
                "If true, continues from current deformed positions instead of resetting to permanent shape. " +
                "Use for Phase 2 (recovery) after applying deformation in Phase 1.",
                GH_ParamAccess.item, false);
            pManager[4].Optional = true;
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddMeshParameter("Mesh", "M", "Deformed geometry after simulation", GH_ParamAccess.item);
            pManager.AddPointParameter("Points", "P", "Optimized voxel center points", GH_ParamAccess.list);
            pManager.AddGenericParameter("Voxels", "VX", "Deformed Voxels", GH_ParamAccess.list);
            pManager.AddTextParameter("Diag", "Diag", "Diagnostic Information", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            this.ClearRuntimeMessages();

            double time = 0;
            List<VoxelCellGoo> voxelGoos = new List<VoxelCellGoo>();
            List<Stimulus> stimulus = new List<Stimulus>();
            List<Brep> baseBreps = new List<Brep>();
            bool continueFromCurrent = false;

            if (!DA.GetDataList(0, baseBreps)) { return; }
            if (!DA.GetDataList(1, voxelGoos)) { return; }
            if (!DA.GetDataList(2, stimulus)) { return; }
            if (!DA.GetData(3, ref time)) { return; }
            DA.GetData(4, ref continueFromCurrent);

            // 입력 필터
            if (stimulus.Count >= 2)
            {
                this.AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Input only one Stimulus object");
                return;
            }

            // VoxelCell 리스트 추출
            List<VoxelCell> voxels = new List<VoxelCell>();
            foreach (VoxelCellGoo goo in voxelGoos)
            {
                if (goo != null && goo.Value != null)
                {
                    voxels.Add(goo.Value);
                }
            }
            
            // Mesh 토폴로지 대신 RTree 기반 그리드 연결성을 사용합니다. (1.1x voxelSize 반경)
            MorphoSolver solver = new MorphoSolver(voxels);
            solver.setUpFromGrid(voxels);

            // [진단 출력] 재질/연결성/하중 통계
            int smpCount = 0, plaCount = 0, otherCount = 0, loadedCount = 0, fixedCount = 0;
            foreach (var v in voxels) {
                if (v.assignedMaterial is SmpMat) smpCount++;
                else if (v.assignedMaterial is PassiveMat) plaCount++;
                else otherCount++;
                if (v.appliedLoad.Length > 1e-9) loadedCount++;
                if (v.isFixed) fixedCount++;
            }
            string diagMsg = $"[Diag] Voxels={voxels.Count} (SMP={smpCount}, PLA={plaCount}, Other={otherCount}) | " +
                             $"Springs={solver.allPairs.Count}, Hinges={solver.allHinges.Count} | " +
                             $"Loaded={loadedCount}, Fixed={fixedCount}";
            this.AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, diagMsg);
            DA.SetData(3, diagMsg);

            solver.execute(time, stimulus[0], continueFromCurrent);

            // 출력 점 리스트 생성
            List<Point3d> resultPoints = solver.getResultPoints();

            // G0: voxel 기반 박스 메쉬 출력 (결함 A 수정 — mesh 인덱스 대응 미보장 문제 해결)
            bool gridSafe = voxels.Count > 0;
            Mesh deformedMesh;

            if (gridSafe)
            {
                deformedMesh = BuildDeformedBoxMesh(voxels, resultPoints);
            }
            else
            {
                // 이론상 도달하지 않음 — 안전망으로 유지
                deformedMesh = new Mesh();
            }

            deformedMesh.Normals.ComputeNormals();
            deformedMesh.FaceNormals.ComputeFaceNormals();

            DA.SetData(0, deformedMesh);
            DA.SetDataList(1, resultPoints);
            DA.SetDataList(2, voxelGoos);
        }

        private Mesh BuildDeformedBoxMesh(List<VoxelCell> voxels, List<Point3d> resultPoints)
        {
            Mesh result = new Mesh();
            if (voxels.Count == 0) return result;
            double size = voxels[0].voxelSize;
            for (int i = 0; i < voxels.Count; i++)
            {
                Point3d center = (i < resultPoints.Count) ? resultPoints[i] : voxels[i].currentPoint;
                Plane pl = new Plane(center, Vector3d.ZAxis);
                Interval iv = new Interval(-size / 2.0, size / 2.0);
                Mesh box = Mesh.CreateFromBox(new Box(pl, iv, iv, iv), 1, 1, 1);
                Color c = voxels[i].assignedMaterial != null
                    ? voxels[i].assignedMaterial.getPreviewColor()
                    : Color.Gray;
                box.VertexColors.Clear();
                for (int k = 0; k < box.Vertices.Count; k++) box.VertexColors.Add(c);
                result.Append(box);
            }
            return result;
        }

        protected override System.Drawing.Bitmap Icon => null;

        public override Guid ComponentGuid => new Guid("F636672F-C5F7-4477-9183-E84C1D473262");
    }
}