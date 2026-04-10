using System.Collections.Generic;
using Rhino.Geometry;
using System.Linq;
using System;
using Grasshopper.Kernel.Types;

namespace FourDSim.Models
{
    public class VoxelCell
    {
        public int Id { get; set; } // 고유 ID
        public Point3d initial_point { get; set; } // 초기 위치
        public Vector3d expected_move { get; set; } // 예상 변형 (벡터)
        public VoxelCell(int id, Point3d position)
        {
            Id = id;
            initial_point = position;
            expected_move = new Vector3d(0, 0, 0);
        }
    }

    public static class Voxelizer
    {
        /// <summary>
        /// CreateVoxels(Brep input_brep, double size)
        /// BoundingBox 생성 후 정한 size 만큼의 간격을 건너뛰며 점을 생성
        /// -> 3중 루프를 돌며 IsPointInside 로 Brep 내의 점인지 확인 -> 맞으면 VoxelCell 객체 생성
        /// </summary>
        /// <param name="input_brep">입력 Brep</param>
        /// <param name="size">복셀의 크기 (클수록 점의 개수 줄어듦)</param>
        /// <returns>List&lt;VoxelCell&gt; result</returns>
        public static List<VoxelCell> createVoxels(Brep input_brep, double size)
        {
            List<VoxelCell> result = new List<VoxelCell>();
            BoundingBox bbox = input_brep.GetBoundingBox(true);
            int cur_id = 0;

            for (double x = bbox.Min.X; x <= bbox.Max.X; x += size)
            {
                for (double y = bbox.Min.Y; y <= bbox.Max.Y; y += size)
                {
                    for (double z = bbox.Min.Z; z <= bbox.Max.Z; z += size)
                    {
                        Point3d cur_point = new Point3d(x, y, z);
                        if (input_brep.IsPointInside(cur_point, 0.01, true))
                        {
                            VoxelCell new_cell = new VoxelCell(cur_id++, cur_point);

                            result.Add(new_cell);
                        }
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// showVoxels(List&lt;VoxelCell&gt; voxel_list, double size)
        /// VoxelCell 객체 리스트를 받아서 중심점을 기준으로 size 만큼 mesh box를 만든다.
        /// </summary>
        /// <param name="voxel_list">VoxelCell 리스트</param>
        /// <param name="size">정육면체 한 변의 길이</param>
        /// <returns>Mesh -> 데이터가 합쳐진 메쉬</returns>
        public static Mesh showVoxels(List<VoxelCell> voxel_list, double size)
        {
            Mesh result = new Mesh(); // 최종 Mesh (하나의 Mesh로 합쳐짐)

            foreach (VoxelCell v in voxel_list)
            {
                Plane plane = Plane.WorldXY;
                plane.Origin = v.initial_point;

                Interval interval = new Interval(-size / 2, size / 2); // size를 절반씩 해서 중심점을 기준으로 박스 생성
                Box box = new Box(plane, interval, interval, interval);

                Mesh temp_mesh = Mesh.CreateFromBox(box, 1, 1, 1);

                result.Append(temp_mesh); // Append를 통해 하나의 Mesh로 합침
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