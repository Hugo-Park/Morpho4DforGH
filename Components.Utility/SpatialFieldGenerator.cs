using System;
using System.Collections.Generic;
using System.Drawing;
using Grasshopper.Kernel;
using Morpho4D.Solver;
using Rhino.Geometry;

namespace _Morpho4D
{
    /// <summary>
    /// 공간 온도장을 생성한다. 특정 점과의 거리를 기반으로 온도를 계산하며,
    /// Stimulus.temperatureField로 주입해 SmpMat이 위치별 온도를 반영할 수 있다.
    /// </summary>
    public class SpatialFieldGeneratorComponent : GH_Component
    {
        public SpatialFieldGeneratorComponent()
          : base("Spatial Field Generator", "SpatField",
              "공간 위치 기반 온도장을 생성한다. Stimulus에 주입해 위치별 활성화를 구현.",
              "Morpho4D", "Utility")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Stimulus", "S", "기존 Stimulus 객체", GH_ParamAccess.item);
            pManager.AddTextParameter("Field Type", "FT",
                "온도장 유형: 'uniform'(균일), 'gradient_x'(X선형), 'gradient_z'(Z선형), 'radial'(방사형)",
                GH_ParamAccess.item, "uniform");
            pManager.AddNumberParameter("T Min", "Tmin", "최소 온도 (°C)", GH_ParamAccess.item, 25.0);
            pManager.AddNumberParameter("T Max", "Tmax", "최대 온도 (°C)", GH_ParamAccess.item, 80.0);
            pManager.AddPointParameter("Origin", "O", "방사형 필드 중심점", GH_ParamAccess.item, Point3d.Origin);
            pManager.AddNumberParameter("Radius", "R", "방사형 필드 반경 (mm)", GH_ParamAccess.item, 50.0);
            pManager.AddNumberParameter("Bounding Min", "BMin",
                "선형 필드 좌표 범위 최솟값", GH_ParamAccess.item, 0.0);
            pManager.AddNumberParameter("Bounding Max", "BMax",
                "선형 필드 좌표 범위 최댓값", GH_ParamAccess.item, 100.0);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Stimulus with Field", "S", "temperatureField가 주입된 Stimulus", GH_ParamAccess.item);
            pManager.AddTextParameter("Field Info", "?", "필드 설정 정보", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            Stimulus stim = null;
            string fieldType = "uniform";
            double tMin = 25, tMax = 80, radius = 50, bMin = 0, bMax = 100;
            Point3d origin = Point3d.Origin;

            if (!DA.GetData(0, ref stim)) return;
            DA.GetData(1, ref fieldType);
            DA.GetData(2, ref tMin);
            DA.GetData(3, ref tMax);
            DA.GetData(4, ref origin);
            DA.GetData(5, ref radius);
            DA.GetData(6, ref bMin);
            DA.GetData(7, ref bMax);

            double tRange = tMax - tMin;
            double bRange = bMax - bMin;

            Func<Point3d, double> field;
            string info;

            switch (fieldType.ToLower())
            {
                case "gradient_x":
                    field = pt => tMin + tRange * Math.Max(0, Math.Min(1, (pt.X - bMin) / Math.Max(bRange, 1e-9)));
                    info = $"X 선형 구배: [{bMin},{bMax}] → [{tMin},{tMax}] °C";
                    break;
                case "gradient_z":
                    field = pt => tMin + tRange * Math.Max(0, Math.Min(1, (pt.Z - bMin) / Math.Max(bRange, 1e-9)));
                    info = $"Z 선형 구배: [{bMin},{bMax}] → [{tMin},{tMax}] °C";
                    break;
                case "radial":
                    field = pt =>
                    {
                        double d = pt.DistanceTo(origin);
                        double t = 1.0 - Math.Max(0, Math.Min(1, d / Math.Max(radius, 1e-9)));
                        return tMin + tRange * t;
                    };
                    info = $"방사형: 중심 {origin} 반경 {radius} mm, {tMax}°C → {tMin}°C";
                    break;
                default: // uniform
                    field = pt => stim.temperature;
                    info = $"균일: {stim.temperature}°C";
                    break;
            }

            stim.temperatureField = field;

            DA.SetData(0, stim);
            DA.SetData(1, info);
        }

        protected override Bitmap Icon => null;
        public override Guid ComponentGuid => new Guid("AAAAAAA3-1111-2222-3333-444444444444");
    }
}
