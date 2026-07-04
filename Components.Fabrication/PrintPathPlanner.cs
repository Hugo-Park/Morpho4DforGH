using System;
using System.Collections.Generic;
using System.Drawing;
using Grasshopper.Kernel;
using Morpho4D.Models;
using Rhino.Geometry;

namespace _Morpho4D
{
    /// <summary>
    /// 복셀 격자에서 X2D 이중 압출 프린팅 경로를 계획한다.
    /// T0 = passive(PLA), T1 = active(SMP)
    /// </summary>
    public class PrintPathPlannerComponent : GH_Component
    {
        public PrintPathPlannerComponent()
          : base("Print Path Planner", "PathPlan",
              "Bilayer 복셀 격자에서 X2D(이중 압출) 프린팅 경로를 계획한다. T0=passive, T1=active.",
              "Morpho4D", "Fabrication")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Voxels", "VX", "BilayerMaterial + SetFiberDirection이 완료된 복셀 리스트", GH_ParamAccess.list);
            pManager.AddNumberParameter("Layer Height", "LH", "레이어 높이 (mm)", GH_ParamAccess.item, 0.2);
            pManager.AddNumberParameter("Line Width", "LW", "압출선 폭 (mm)", GH_ParamAccess.item, 0.4);
            pManager.AddNumberParameter("Print Speed", "PS", "인쇄 속도 (mm/min)", GH_ParamAccess.item, 2400.0);
            pManager.AddNumberParameter("Travel Speed", "TS", "이동 속도 (mm/min)", GH_ParamAccess.item, 7200.0);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddCurveParameter("T0 Paths", "P0", "passive(PLA) 압출 경로 (T0)", GH_ParamAccess.list);
            pManager.AddCurveParameter("T1 Paths", "P1", "active(SMP) 압출 경로 (T1)", GH_ParamAccess.list);
            pManager.AddIntegerParameter("Layer Count", "nL", "레이어 수", GH_ParamAccess.item);
            pManager.AddTextParameter("Stats", "?", "경로 통계", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            var voxelGoos = new List<VoxelCellGoo>();
            double lh = 0.2, lw = 0.4, ps = 2400.0, ts = 7200.0;

            if (!DA.GetDataList(0, voxelGoos)) return;
            DA.GetData(1, ref lh);
            DA.GetData(2, ref lw);
            DA.GetData(3, ref ps);
            DA.GetData(4, ref ts);

            var voxels = new List<VoxelCell>();
            foreach (var g in voxelGoos) if (g?.Value != null) voxels.Add(g.Value);
            if (voxels.Count == 0) return;

            // Z 방향으로 레이어 그룹화
            var layerMap = new SortedDictionary<double, List<VoxelCell>>();
            double tol = voxels[0].voxelSize * 0.1;

            foreach (var v in voxels)
            {
                double z = Math.Round(v.initialPoint.Z / tol) * tol;
                if (!layerMap.ContainsKey(z)) layerMap[z] = new List<VoxelCell>();
                layerMap[z].Add(v);
            }

            var t0Paths = new List<Curve>();
            var t1Paths = new List<Curve>();

            foreach (var kvp in layerMap)
            {
                var layer = kvp.Value;

                // active/passive 분리
                var t0Pts = new List<Point3d>();
                var t1Pts = new List<Point3d>();

                layer.Sort((a, b) => {
                    int xc = a.initialPoint.X.CompareTo(b.initialPoint.X);
                    return xc != 0 ? xc : a.initialPoint.Y.CompareTo(b.initialPoint.Y);
                });

                foreach (var v in layer)
                {
                    if (v.isActive) t1Pts.Add(v.initialPoint);
                    else t0Pts.Add(v.initialPoint);
                }

                if (t0Pts.Count >= 2)
                    t0Paths.Add(new PolylineCurve(t0Pts));
                if (t1Pts.Count >= 2)
                    t1Paths.Add(new PolylineCurve(t1Pts));
            }

            var stats = new List<string>
            {
                $"레이어 수: {layerMap.Count}",
                $"T0(PLA) 경로 수: {t0Paths.Count}",
                $"T1(SMP) 경로 수: {t1Paths.Count}",
                $"레이어 높이: {lh} mm",
                $"압출선 폭: {lw} mm",
                $"인쇄 속도: {ps} mm/min",
                $"이동 속도: {ts} mm/min"
            };

            DA.SetDataList(0, t0Paths);
            DA.SetDataList(1, t1Paths);
            DA.SetData(2, layerMap.Count);
            DA.SetDataList(3, stats);
        }

        protected override Bitmap Icon => null;
        public override Guid ComponentGuid => new Guid("66666666-7777-8888-9999-aaaaaaaaaaaa");
    }
}
