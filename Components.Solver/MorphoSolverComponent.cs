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
            "Morpho4D", "Solver")
        {
        }

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddBrepParameter("Base Breps", "BB", "Original reference Brep objects", GH_ParamAccess.list);
            pManager.AddGenericParameter("Voxels", "VX", "List of Voxel objects", GH_ParamAccess.list);
            pManager.AddGenericParameter("Stimulus", "S", "External stimulus object (e.g., Temperature, Humidity) that triggers voxel deformation.", GH_ParamAccess.list);
            pManager.AddNumberParameter("Time", "T", "Simulation Time Step", GH_ParamAccess.item);
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddMeshParameter("Mesh", "M", "Deformed geometry after simulation", GH_ParamAccess.item);
            pManager.AddPointParameter("Points", "P", "Optimized voxel center points", GH_ParamAccess.list);
            pManager.AddNumberParameter("Energy History", "EH", "Total potential energy per solver evaluation (feed to Solver History Monitor)", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            this.ClearRuntimeMessages();

            double time = 0;
            List<VoxelCellGoo> voxelGoos = new List<VoxelCellGoo>();
            List<Stimulus> stimulus = new List<Stimulus>();
            List<Brep> baseBreps = new List<Brep>();

            if (!DA.GetDataList(0, baseBreps)) { return; }
            if (!DA.GetDataList(1, voxelGoos)) { return; }
            if (!DA.GetDataList(2, stimulus)) { return; }
            if (!DA.GetData(3, ref time)) { return; }

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

            // reference 메쉬 생성
            if (baseBreps.Count == 0 || baseBreps[0] == null) return;
            Mesh referenceMesh = new Mesh();
            foreach(Brep b in baseBreps)
            {
                MeshingParameters mParams = MeshingParameters.Default;
                mParams.MaximumEdgeLength = voxels[0].voxelSize;
                mParams.MinimumEdgeLength = voxels[0].voxelSize * 0.5;
                mParams.GridAspectRatio = 1.0;
                if (b == null) continue;
                Mesh[] meshes = Mesh.CreateFromBrep(b, mParams);
                if(meshes != null)
                {
                    foreach (Mesh m in meshes)
                    {
                        referenceMesh.Append(m);
                    }
                }
            }
            
            // 솔버 시작
            MorphoSolver solver = new MorphoSolver(voxels);
            solver.setUp(voxels, referenceMesh);
            solver.execute(time, stimulus[0]);

            // 출력 점 리스트 생성
            List<Point3d> resultPoints = solver.getResultPoints();

            // 출력 메쉬 생성
            Mesh deformedMesh = referenceMesh.DuplicateMesh();
            
            deformedMesh.VertexColors.Clear();
            for (int i = 0; i < deformedMesh.Vertices.Count; i++)
            {
                if (i < resultPoints.Count)
                {
                    deformedMesh.Vertices.SetVertex(i, (Point3f)resultPoints[i]);

                    Color previewCOlor = voxels[i].assignedMaterial.getPreviewColor();
                    deformedMesh.VertexColors.Add(previewCOlor);
                }
            }

            deformedMesh.Normals.ComputeNormals();
            deformedMesh.FaceNormals.ComputeFaceNormals();

            DA.SetData(0, deformedMesh);
            DA.SetDataList(1, resultPoints);
            DA.SetDataList(2, solver.energyHistory);
        }

        protected override System.Drawing.Bitmap Icon => null;

        public override Guid ComponentGuid => new Guid("F636672F-C5F7-4477-9183-E84C1D473262");
    }
}