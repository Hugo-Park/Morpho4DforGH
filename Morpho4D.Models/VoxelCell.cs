using System.Collections.Generic;
using Rhino.Geometry;
using System.Linq;
using System;
using Grasshopper.Kernel.Types;
using Grasshopper.Kernel;
using System.Drawing;
using Rhino.Display;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Morpho4D.Solver;

namespace Morpho4D.Models
{
    public class VoxelCell
    {
        /*고유 정보 및 위치*/
        public int Id { get; set; } // 고유 ID
        public Point3d initialPoint { get; set; } // 초기 위치
        public Point3d currentPoint { get; set; } // Solver가 실시간으로 이동시킬 좌표
        public Material assignedMaterial { get; set; } // 적용된 재료

        /*전처리 데이터*/
        public double distanceFromSurface { get; set; } // 표면으로부터의 최단 거리
        public double voxelSize { get; set; } // 복셀의 크기
        public List<int> neighborIndices { get; set; } = new List<int>(); // 인접 복셀의 ID 정보
        public List<Hinge> hingeIndices { get; set; } = new List<Hinge>(); // 힌지 복셀의 정보
        public bool isFixed { get; set; } = false; // 고정되어야 하는 복셀인가

        /*실시간 물리적 상태*/
        public double currentTemp { get; set; } = 25.0; // 초기 온도 (Smp 전용)
        public double currentHydration { get; set; } = 0.0; // 초기 흡수율 (Hydrogel 전용)

        /*업데이트 될 물성 변수(L-BGFS Solver에 전달될 값)*/
        public double currentYoungsModulus { get; set; } // 현재 강성
        public double expansionForce { get; set; } // Fick + Osmotic = Hydrogel의 실제 팽창력

        /*Solver 연산용 데이터*/
        public Vector3d gradient { get; set; }

        /*eigenstrain 비등방 활성변형 (G1)*/
        public Vector3d fiberDir { get; set; } = Vector3d.XAxis;
        public double epsMax { get; set; } = 0.0;
        public double activationFraction { get; set; } = 0.0;
        public bool isActive { get; set; } = false;
        public bool isGridVoxel { get; set; } = true;

        /*부가 유틸리티 (G8)*/
        public Vector3d appliedLoad { get; set; } = Vector3d.Zero;

        public VoxelCell(int id, Point3d position)
        {
            this.Id = id;
            this.initialPoint = position;
            this.currentPoint = position;
        }
    }

    public static class Voxelizer
    {
        /// <summary>
        /// createVoxels(Brep inputBrep, double size)
        /// BoundingBox 생성 후 정한 size 만큼의 간격을 건너뛰며 점을 생성
        /// -> 3중 루프를 돌며 IsPointInside 로 Brep 내의 점인지 확인 -> 맞으면 VoxelCell 객체 생성
        /// </summary>
        /// <param name="inputBrep">입력 Brep</param>
        /// <param name="size">복셀의 크기 (클수록 점의 개수 줄어듦)</param>
        /// <returns>List&lt;VoxelCell&gt; result</returns>
        public static List<VoxelCell> createVoxels(Brep inputBrep, double size)
        {
            MeshingParameters mParams = MeshingParameters.Default;
            mParams.MaximumEdgeLength = size * 0.85;
            mParams.MinimumEdgeLength = size * 0.85;
            mParams.GridAspectRatio = 1.0;

            Mesh[] meshes = Mesh.CreateFromBrep(inputBrep, mParams);
            if (meshes == null || meshes.Length == 0) return new List<VoxelCell>();

            Mesh combinedMesh = new Mesh();
            foreach (Mesh m in meshes) combinedMesh.Append(m);
            combinedMesh.Vertices.CombineIdentical(true, true);
            combinedMesh.Weld(0.1);

            // [Point Culling Patch] 표면 복셀 최적화
            // 피라미드 꼭짓점처럼 점들이 밀집되는 특이점(Singularity)에서 
            // 점들이 겹쳐 물리 엔진의 강성이 무한대로 폭발하는 것을 방지합니다.
            Rhino.Geometry.RTree tree = new Rhino.Geometry.RTree();
            List<Point3d> culledPoints = new List<Point3d>();
            double cullDistance = size * 0.5;

            for (int i = 0; i < combinedMesh.Vertices.Count; i++)
            {
                Point3d p = new Point3d(combinedMesh.Vertices[i]);
                bool tooClose = false;
                tree.Search(new Sphere(p, cullDistance), (sender, args) =>
                {
                    tooClose = true;
                });

                if (!tooClose)
                {
                    culledPoints.Add(p);
                    tree.Insert(p, culledPoints.Count - 1);
                }
            }

            List<VoxelCell> result = new List<VoxelCell>();

            for (int i = 0; i < culledPoints.Count; i++)
            {
                Point3d voxelPos = culledPoints[i];
                VoxelCell newCell = new VoxelCell(i, voxelPos);
                newCell.voxelSize = size;

                Point3d closestPoint;
                Rhino.Geometry.ComponentIndex ci;
                double s, t;
                Vector3d normal;

                if (inputBrep.ClosestPoint(voxelPos, out closestPoint, out ci, out s, out t, 0.0, out normal))
                {
                    newCell.distanceFromSurface = voxelPos.DistanceTo(closestPoint);
                }
                else
                {
                    newCell.distanceFromSurface = 0.0;
                }
                result.Add(newCell);
            }

            return result;

            /*
            List<VoxelCell> result = new List<VoxelCell>();
            BoundingBox bbox = inputBrep.GetBoundingBox(true);
            int curId = 0;

            for (double x = bbox.Min.X; x <= bbox.Max.X; x += size)
            {
                for (double y = bbox.Min.Y; y <= bbox.Max.Y; y += size)
                {
                    for (double z = bbox.Min.Z; z <= bbox.Max.Z; z += size)
                    {
                        Point3d curPoint = new Point3d(x, y, z);
                        if (inputBrep.IsPointInside(curPoint, 0.01, true))
                        {
                            VoxelCell newCell = new VoxelCell(curId++, curPoint);

                            result.Add(newCell);
                        }
                    }
                }
            }

            return result;
            */

            /*병렬처리로 대체*/
            /*
            ConcurrentBag<VoxelCell> resultBag = new ConcurrentBag<VoxelCell>();
            BoundingBox bbox = inputBrep.GetBoundingBox(true);

            List<Point3d> pointsToTest = new List<Point3d>();
            for (double x = bbox.Min.X; x <= bbox.Max.X; x += size)
            {
                for (double y = bbox.Min.Y; y <= bbox.Max.Y; y += size)
                {
                    for (double z = bbox.Min.Z; z <= bbox.Max.Z; z += size)
                    {
                        pointsToTest.Add(new Point3d(x, y, z));
                    }
                }
            }

            Parallel.ForEach(pointsToTest, curPoint =>
            {
                if (inputBrep.IsPointInside(curPoint, 0.01, true))
                {
                    resultBag.Add(new VoxelCell(0, curPoint));
                }
            });

            List<VoxelCell> finalResult = resultBag.ToList();

            finalResult = finalResult.OrderBy(v => v.initialPoint.X).ThenBy(v => v.initialPoint.Y).ThenBy(v => v.initialPoint.Z).ToList();

            for (int i = 0; i < finalResult.Count; i++)
            {
                finalResult[i].Id = i;
            }

            return finalResult;
            */
        }

        /// <summary>
        /// showVoxels(List&lt;VoxelCell&gt; voxelList, double size)
        /// VoxelCell 객체 리스트를 받아서 중심점을 기준으로 size 만큼 mesh box를 만든다.
        /// </summary>
        /// <param name="voxelList">VoxelCell 리스트</param>
        /// <param name="size">정육면체 한 변의 길이</param>
        /// <returns>Mesh -> 데이터가 합쳐진 메쉬</returns>
        public static Mesh showVoxels(List<VoxelCell> voxelList, double size)
        {
            Mesh result = new Mesh(); // 최종 Mesh (하나의 Mesh로 합쳐짐)

            foreach (VoxelCell v in voxelList)
            {
                Plane plane = Plane.WorldXY;
                plane.Origin = v.initialPoint;

                Interval interval = new Interval(-size / 2, size / 2); // size를 절반씩 해서 중심점을 기준으로 박스 생성
                Box box = new Box(plane, interval, interval, interval);

                Mesh tempMesh = Mesh.CreateFromBox(box, 1, 1, 1);

                result.Append(tempMesh); // Append를 통해 하나의 Mesh로 합침
            }

            return result;
        }

    }

    /// <summary>
    /// VoxelCell 데이터를 Grasshopper로 전송하기 위한 일종의 포장지 class
    /// IGH_Goo를 사용하여 커스텀 데이터를 효율적으로 사용 가능
    /// </summary>
    public class VoxelCellGoo : GH_Goo<VoxelCell>
    {
        public VoxelCellGoo() { }
        public VoxelCellGoo(VoxelCell v) : base(v) { }

        public override bool IsValid => Value != null;
        public override string TypeName => "VoxelCell";
        public override string TypeDescription => "4D Printing Simulation Voxel Cells";
        public override IGH_Goo Duplicate()
        {
            return new VoxelCellGoo(Value);
        }
        public override string ToString()
        {
            return this.GetType().FullName;
        }
    }
}