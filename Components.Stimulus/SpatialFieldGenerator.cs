using System;
using System.Collections.Generic;
using System.Drawing;
using Grasshopper.Kernel;
using Morpho4D.Solver;
using Rhino.Geometry;

namespace _Morpho4D
{
    /// <summary>
    /// Generates a spatial temperature field. Calculates temperature based on distance from a specific point,
    /// and injects it into Stimulus.temperatureField so SmpMat can reflect location-specific temperatures.
    /// </summary>
    public class SpatialFieldGeneratorComponent : GH_Component
    {
        public SpatialFieldGeneratorComponent()
          : base("Spatial Field Generator", "SpatField",
              "Generates a spatial location-based temperature field. Injects into Stimulus to implement location-specific activation.",
              "Morpho4D", "03 Stimulus")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Stimulus", "S", "Existing Stimulus object", GH_ParamAccess.item);
            pManager.AddTextParameter("Field Type", "FT",
                "Temperature field type: 'uniform', 'gradient_x', 'gradient_z', 'radial'",
                GH_ParamAccess.item, "uniform");
            pManager.AddNumberParameter("T Min", "Tmin", "Minimum temperature (°C)", GH_ParamAccess.item, 25.0);
            pManager.AddNumberParameter("T Max", "Tmax", "Maximum temperature (°C)", GH_ParamAccess.item, 80.0);
            pManager.AddPointParameter("Origin", "O", "Radial field origin point", GH_ParamAccess.item, Point3d.Origin);
            pManager.AddNumberParameter("Radius", "R", "Radial field radius (mm)", GH_ParamAccess.item, 50.0);
            pManager.AddNumberParameter("Bounding Min", "BMin",
                "Minimum value of linear field coordinate range", GH_ParamAccess.item, 0.0);
            pManager.AddNumberParameter("Bounding Max", "BMax",
                "Maximum value of linear field coordinate range", GH_ParamAccess.item, 100.0);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Stimulus with Field", "S", "Stimulus with injected temperatureField", GH_ParamAccess.item);
            pManager.AddTextParameter("Field Info", "?", "Field configuration info", GH_ParamAccess.item);
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
                    info = $"X linear gradient: [{bMin},{bMax}] → [{tMin},{tMax}] °C";
                    break;
                case "gradient_z":
                    field = pt => tMin + tRange * Math.Max(0, Math.Min(1, (pt.Z - bMin) / Math.Max(bRange, 1e-9)));
                    info = $"Z linear gradient: [{bMin},{bMax}] → [{tMin},{tMax}] °C";
                    break;
                case "radial":
                    field = pt =>
                    {
                        double d = pt.DistanceTo(origin);
                        double t = 1.0 - Math.Max(0, Math.Min(1, d / Math.Max(radius, 1e-9)));
                        return tMin + tRange * t;
                    };
                    info = $"Radial: origin {origin} radius {radius} mm, {tMax}°C → {tMin}°C";
                    break;
                default: // uniform
                    field = pt => stim.temperature;
                    info = $"Uniform: {stim.temperature}°C";
                    break;
            }

            stim.temperatureField = field;

            DA.SetData(0, stim);
            DA.SetData(1, info);
        }
        protected override System.Drawing.Bitmap Icon => _Morpho4D.IconLoader.Get("SpatialFieldGenerator");
        public override Guid ComponentGuid => new Guid("e30bd584-67e0-4469-b96f-b749b83f4fc4");
    }
}

