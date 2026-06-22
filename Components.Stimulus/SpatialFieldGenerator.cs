using System;
using System.Collections.Generic;

using Grasshopper;
using Grasshopper.Kernel;
using Rhino.Geometry;

using Morpho4D.Models;
using Morpho4D.Solver;

namespace _Morpho4D
{
    /// <summary>
    /// 자극(현재는 온도)이 공간적으로 불균일하게 가해질 때, 복셀 격자의 바운딩 박스를 기준으로
    /// 자극 세기 그라디언트를 만들어 Stimulus에 주입한다. (Stimulus.temperatureField)
    /// 두 가지 모드: (1) Direction 축을 따라 선형 그라디언트, (2) Source Point가 연결되면 그 점 기준 방사형.
    /// 주의: 현재 물성 모델에서 '온도'는 SMP의 sigmoid 모듈러스 전이에 작용한다(Hydrogel 확산은 온도 비의존).
    ///       Hydrogel용 습도 필드는 확산 모델 확장이 필요(향후).
    /// </summary>
    public class SpatialFieldGenerator : GH_Component
    {
        public SpatialFieldGenerator()
          : base("Spatial Field Generator", "SpatField",
            "Generate a spatially-varying stimulus (temperature) field over the voxel bounding box and inject it into a Stimulus. Affects SMP (sigmoid).",
            "Morpho4D", "Stimulus")
        {
        }

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Voxels", "VX", "Voxel list (used to compute the bounding box)", GH_ParamAccess.list);
            pManager.AddTextParameter("Name", "N", "Stimulus name", GH_ParamAccess.item, "SpatialHeat");
            pManager.AddNumberParameter("Min Value", "Min", "Temperature at the low end of the gradient", GH_ParamAccess.item, 25.0);
            pManager.AddNumberParameter("Max Value", "Max", "Temperature at the high end of the gradient (or at the source point)", GH_ParamAccess.item, 80.0);
            pManager.AddVectorParameter("Direction", "D", "Gradient direction for the linear mode", GH_ParamAccess.item, Vector3d.ZAxis);
            pManager.AddPointParameter("Source Point", "SP", "Optional. If connected, uses a radial field (hottest at this point) instead of the linear gradient.", GH_ParamAccess.item);
            pManager[5].Optional = true;
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Stimulus", "S", "Stimulus carrying the spatial temperature field", GH_ParamAccess.item);
            pManager.AddTextParameter("Inspection", "?", "Field summary", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            this.ClearRuntimeMessages();

            List<VoxelCellGoo> goos = new List<VoxelCellGoo>();
            string name = "SpatialHeat";
            double minVal = 25.0, maxVal = 80.0;
            Vector3d direction = Vector3d.ZAxis;
            Point3d source = Point3d.Origin;

            if (!DA.GetDataList(0, goos)) { return; }
            if (!DA.GetData(1, ref name)) { return; }
            if (!DA.GetData(2, ref minVal)) { return; }
            if (!DA.GetData(3, ref maxVal)) { return; }
            if (!DA.GetData(4, ref direction)) { return; }
            bool useRadial = DA.GetData(5, ref source);

            List<Point3d> pts = new List<Point3d>();
            foreach (var goo in goos)
            {
                if (goo != null && goo.Value != null) pts.Add(goo.Value.currentPoint);
            }
            if (pts.Count == 0) { this.AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No voxels"); return; }

            BoundingBox bb = new BoundingBox(pts);
            Point3d bbMin = bb.Min;

            Func<Point3d, double> field;
            string modeText;

            if (useRadial)
            {
                double diag = bb.Diagonal.Length;
                if (diag < 1e-9) diag = 1.0;
                Point3d src = source;
                double lo = minVal, hi = maxVal;
                field = p =>
                {
                    double tt = p.DistanceTo(src) / diag;
                    if (tt < 0) tt = 0; if (tt > 1) tt = 1;
                    return hi + (lo - hi) * tt; // 소스에서 가장 뜨겁고 멀어질수록 낮아짐
                };
                modeText = string.Format("Radial from ({0:f2})", src);
            }
            else
            {
                Vector3d dir = direction;
                if (!dir.Unitize()) dir = Vector3d.ZAxis;
                Vector3d diagV = bb.Max - bb.Min;
                double extent = Math.Abs(diagV.X * dir.X + diagV.Y * dir.Y + diagV.Z * dir.Z);
                if (extent < 1e-9) extent = 1.0;
                Vector3d d = dir;
                Point3d origin = bbMin;
                double lo = minVal, hi = maxVal, ext = extent;
                field = p =>
                {
                    double proj = (p.X - origin.X) * d.X + (p.Y - origin.Y) * d.Y + (p.Z - origin.Z) * d.Z;
                    double tt = proj / ext;
                    if (tt < 0) tt = 0; if (tt > 1) tt = 1;
                    return lo + (hi - lo) * tt;
                };
                modeText = string.Format("Linear along ({0:f2})", dir);
            }

            HeatStim stim = new HeatStim(name, (minVal + maxVal) * 0.5);
            stim.temperatureField = field;

            List<string> stat = new List<string>();
            stat.Add(string.Format("Stimulus: {0}", name));
            stat.Add(string.Format("Mode: {0}", modeText));
            stat.Add(string.Format("Range: {0:f2} ~ {1:f2}", minVal, maxVal));
            stat.Add("Note: temperature affects SMP (sigmoid). Hydrogel diffusion is temperature-independent.");

            DA.SetData(0, stim);
            DA.SetDataList(1, stat);
        }

        protected override System.Drawing.Bitmap Icon => null;

        public override Guid ComponentGuid => new Guid("48ff90d7-67d9-423e-b62c-c6f7882eb324");
    }
}
