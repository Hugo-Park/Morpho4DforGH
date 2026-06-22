using System;
using System.Collections.Generic;
using System.Drawing;

using Grasshopper;
using Grasshopper.Kernel;
using Rhino.Geometry;

using Morpho4D.Models;
using Morpho4D.Solver;

namespace _Morpho4D
{
    /// <summary>
    /// 타임라인 플레이어 / 프리컴퓨트 캐시.
    /// Times의 모든 시점에 대해 솔버를 '미리' 돌려 변형된 메쉬/좌표를 메모리에 캐싱한 뒤,
    /// Frame 인덱스로 해당 프레임을 즉시 출력한다.
    /// => L-BFGS 연산이 애니메이션 타임라인과 분리되므로, Frame 슬라이더에 라이노 기본 Animate를 걸어도
    ///    솔버가 루프 안에서 돌지 않아 멈춤/프레임 깨짐이 없다. 역재생/점프도 끊김 없음(캐시 조회).
    ///
    /// 재계산: Recompute를 False->True로 토글하면 (재)베이크. 입력(Stimulus/Voxels/Times 값)을 바꾼 뒤엔 토글 필요.
    /// 캐시가 비어있거나 Times 개수가 바뀌면 자동 재베이크.
    /// </summary>
    public class SimulationPlayer : GH_Component
    {
        public SimulationPlayer()
          : base("Simulation Player", "SimPlay",
            "Precompute every time step and cache the deformed geometry, then output a frame by index. Decouples the L-BFGS solve from the animation timeline.",
            "Morpho4D", "Animation")
        {
        }

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddBrepParameter("Base Breps", "BB", "Original reference Brep objects", GH_ParamAccess.list);
            pManager.AddGenericParameter("Voxels", "VX", "Voxel list (with materials assigned)", GH_ParamAccess.list);
            pManager.AddGenericParameter("Stimulus", "S", "Stimulus object", GH_ParamAccess.item);
            pManager.AddNumberParameter("Times", "T", "Time samples to bake (e.g. from Simulation Timer)", GH_ParamAccess.list);
            pManager.AddIntegerParameter("Frame", "F", "Frame index to display (drive this with a slider / native Animate)", GH_ParamAccess.item, 0);
            pManager.AddBooleanParameter("Recompute", "R", "Toggle False->True to (re)bake the cache", GH_ParamAccess.item, false);
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddMeshParameter("Mesh", "M", "Deformed mesh at the current frame", GH_ParamAccess.item);
            pManager.AddPointParameter("Points", "P", "Voxel points at the current frame", GH_ParamAccess.list);
            pManager.AddIntegerParameter("Frame Count", "N", "Number of cached frames", GH_ParamAccess.item);
            pManager.AddNumberParameter("Current Time", "t", "Simulation time of the current frame", GH_ParamAccess.item);
            pManager.AddMeshParameter("All Meshes", "AM", "All cached frame meshes", GH_ParamAccess.list);
        }

        // ---- 캐시 ----
        private List<Mesh> _frameMeshes = new List<Mesh>();
        private List<List<Point3d>> _framePoints = new List<List<Point3d>>();
        private List<double> _frameTimes = new List<double>();
        private bool _lastRecompute = false;

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            this.ClearRuntimeMessages();

            List<Brep> baseBreps = new List<Brep>();
            List<VoxelCellGoo> goos = new List<VoxelCellGoo>();
            Stimulus stim = null;
            List<double> times = new List<double>();
            int frame = 0;
            bool recompute = false;

            if (!DA.GetDataList(0, baseBreps)) { return; }
            if (!DA.GetDataList(1, goos)) { return; }
            if (!DA.GetData(2, ref stim)) { return; }
            if (!DA.GetDataList(3, times)) { return; }
            DA.GetData(4, ref frame);
            DA.GetData(5, ref recompute);

            List<VoxelCell> voxels = new List<VoxelCell>();
            foreach (var goo in goos)
            {
                if (goo != null && goo.Value != null) voxels.Add(goo.Value);
            }
            if (voxels.Count == 0) { this.AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No voxels"); return; }
            if (times.Count == 0) { this.AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No time samples (connect Simulation Timer)"); return; }
            if (baseBreps.Count == 0 || baseBreps[0] == null) { this.AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No base Brep"); return; }
            foreach (VoxelCell v in voxels)
            {
                if (v.assignedMaterial == null) { this.AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Some voxels have no material assigned"); return; }
            }

            // 베이크 필요 여부 판단
            bool needBake = _frameMeshes == null || _frameMeshes.Count == 0
                            || _frameTimes == null || _frameTimes.Count != times.Count
                            || (recompute && !_lastRecompute);
            _lastRecompute = recompute;

            if (needBake)
            {
                try { BakeAll(baseBreps, voxels, stim, times); }
                catch (Exception ex)
                {
                    this.AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Bake failed: " + ex.Message);
                    return;
                }
            }

            int count = _frameMeshes.Count;
            if (count == 0) { this.AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No frames baked"); return; }

            int idx = frame;
            if (idx < 0) idx = 0;
            if (idx >= count) idx = count - 1;

            DA.SetData(0, _frameMeshes[idx]);
            DA.SetDataList(1, _framePoints[idx]);
            DA.SetData(2, count);
            DA.SetData(3, _frameTimes[idx]);
            DA.SetDataList(4, _frameMeshes);
        }

        /// <summary>
        /// 모든 시점에 대해 솔버를 순차 실행하며(이전 형상에서 warm-start) 결과를 캐싱.
        /// </summary>
        private void BakeAll(List<Brep> baseBreps, List<VoxelCell> voxels, Stimulus stim, List<double> times)
        {
            _frameMeshes = new List<Mesh>();
            _framePoints = new List<List<Point3d>>();
            _frameTimes = new List<double>();

            // reference 메쉬 (MorphoSolverComponent와 동일 방식)
            Mesh referenceMesh = new Mesh();
            double vsize = voxels[0].voxelSize;
            foreach (Brep b in baseBreps)
            {
                if (b == null) continue;
                MeshingParameters mp = MeshingParameters.Default;
                mp.MaximumEdgeLength = vsize;
                mp.MinimumEdgeLength = vsize * 0.5;
                mp.GridAspectRatio = 1.0;
                Mesh[] ms = Mesh.CreateFromBrep(b, mp);
                if (ms != null) foreach (Mesh m in ms) referenceMesh.Append(m);
            }

            // 결정적 시작을 위해 초기 형상으로 리셋
            foreach (VoxelCell v in voxels) v.currentPoint = v.initialPoint;

            MorphoSolver solver = new MorphoSolver(voxels);
            solver.setUp(voxels, referenceMesh);

            foreach (double t in times)
            {
                solver.execute(t, stim); // 이전 프레임 형상에서 이어서 최적화(progressive)

                // 좌표 스냅샷 (Point3d는 값 타입이라 복사됨)
                List<Point3d> pts = solver.getResultPoints();
                List<Point3d> snap = new List<Point3d>(pts);

                // 변형 메쉬
                Mesh deformed = referenceMesh.DuplicateMesh();
                deformed.VertexColors.Clear();
                for (int i = 0; i < deformed.Vertices.Count; i++)
                {
                    if (i < snap.Count)
                    {
                        deformed.Vertices.SetVertex(i, (Point3f)snap[i]);
                        Color c = voxels[i].assignedMaterial.getPreviewColor();
                        deformed.VertexColors.Add(c);
                    }
                }
                deformed.Normals.ComputeNormals();
                deformed.FaceNormals.ComputeFaceNormals();

                _frameMeshes.Add(deformed);
                _framePoints.Add(snap);
                _frameTimes.Add(t);
            }
        }

        protected override System.Drawing.Bitmap Icon => null;

        public override Guid ComponentGuid => new Guid("148ff164-3eaf-49fb-be1a-307e185a61a4");
    }
}
