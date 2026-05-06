using System.Collections.Generic;
using Rhino.Geometry;
using System.Linq;
using System;
using Grasshopper.Kernel.Types;
using Grasshopper.Kernel;
using System.Drawing;
using Rhino.Display;

namespace Morpho4D.Models
{
    public class VoxelCell
    {
        /*고유 정보 및 위치*/
        public int Id { get; set; } // 고유 ID
        public Point3d initialPoint { get; set; } // 초기 위치
        public Point3d currentPoint {get; set;} // Solver가 실시간으로 이동시킬 좌표
        public Material assignedMaterial { get; set; } // 적용된 재료

        /*전처리 데이터*/   
        public double distanceFromSurface { get; set; } // 표면으로부터의 최단 거리
        public double volume { get; set; } // 복셀의 부피
        public List<int> neighborIndices { get; set; } = new List<int>(); // 인접 복셀의 ID 정보
        public bool isFixed { get; set; } = false; // 고정되어야 하는 복셀인가

        /*실시간 물리적 상태*/
        public double currentTemp { get; set; } = 25.0; // 초기 온도 (Smp 전용)
        public double currentHydration { get; set; } = 0.0; // 초기 흡수율 (Hydrogel 전용)

        /*업데이트 될 물성 변수(L-BGFS Solver에 전달될 값)*/
        public double currentYoungsModulus { get; set; } // 현재 강성
        public double expansionForce { get; set; } // Fick + Osmotic = Hydrogel의 실제 팽창력
        public double currentPoissonRatio { get; set; } // 현재 포아송 비

        /*Solver 연산용 데이터*/
        public Vector3d gradient { get; set; }

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
        /// <param name="inpuBrep">입력 Brep</param>
        /// <param name="size">복셀의 크기 (클수록 점의 개수 줄어듦)</param>
        /// <returns>List&lt;VoxelCell&gt; result</returns>
        public static List<VoxelCell> createVoxels(Brep inputBrep, double size)
        {
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

        public static void drawPreviewMesh(IGH_PreviewArgs args, Mesh mesh, DisplayMaterial prevColor)
        {
            args.Display.DrawMeshShaded(mesh, prevColor);
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