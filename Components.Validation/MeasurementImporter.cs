using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace _Morpho4D
{
    /// <summary>
    /// Imports Fiji ImageJ CSV measurement results to calculate the measured curvature (κ_exp).
    /// Supports automatic CSV delimiter detection (comma/tab/semicolon), FlipY option, and Y→Z axis conversion option.
    /// </summary>
    public class MeasurementImporterComponent : GH_Component
    {
        public MeasurementImporterComponent()
          : base("Measurement Importer", "MeasImp",
              "Imports Fiji CSV measurements to calculate the measured curvature (κ_exp).",
              "Morpho4D", "06 Validation")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("CSV Path", "CSV", "Fiji ImageJ measurement CSV file path", GH_ParamAccess.item);
            pManager.AddTextParameter("X Column", "xCol", "X coordinate column name (case-insensitive)", GH_ParamAccess.item, "X");
            pManager.AddTextParameter("Y Column", "yCol", "Y coordinate column name (case-insensitive)", GH_ParamAccess.item, "Y");
            pManager.AddNumberParameter("Scale", "Sc", "Pixel→mm conversion scale", GH_ParamAccess.item, 1.0);
            pManager.AddBooleanParameter("Flip Y", "FY", "Whether to flip Y axis (Image coordinate system → World coordinate system)", GH_ParamAccess.item, true);
            pManager.AddBooleanParameter("Y to Z", "YZ", "Convert Y values to Z axis (Plan view→Side view)", GH_ParamAccess.item, false);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddPointParameter("Measured Points", "Pts", "Imported measured points", GH_ParamAccess.list);
            pManager.AddNumberParameter("κ_exp", "κ", "Measured curvature (3-point circle fitting, 1/mm)", GH_ParamAccess.item);
            pManager.AddNumberParameter("R_exp", "R", "Measured radius of curvature (mm)", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Point Count", "n", "Number of imported points", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            string csvPath = "";
            string xCol = "X", yCol = "Y";
            double scale = 1.0;
            bool flipY = true, yToZ = false;

            if (!DA.GetData(0, ref csvPath)) return;
            DA.GetData(1, ref xCol);
            DA.GetData(2, ref yCol);
            DA.GetData(3, ref scale);
            DA.GetData(4, ref flipY);
            DA.GetData(5, ref yToZ);

            if (!File.Exists(csvPath))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"File not found: {csvPath}");
                return;
            }

            var pts = ParseCsv(csvPath, xCol, yCol, scale, flipY, yToZ);
            if (pts.Count < 3)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Less than 3 points ({pts.Count} points). Cannot calculate curvature.");
                DA.SetDataList(0, pts);
                DA.SetData(1, 0.0); DA.SetData(2, 0.0); DA.SetData(3, pts.Count);
                return;
            }

            Point3d center; double R;
            bool ok = Circle3Pt(pts[0], pts[pts.Count / 2], pts[pts.Count - 1], out center, out R);
            double kappa = (ok && R > 1e-9) ? 1.0 / R : 0.0;

            DA.SetDataList(0, pts);
            DA.SetData(1, kappa);
            DA.SetData(2, R);
            DA.SetData(3, pts.Count);
        }

        private List<Point3d> ParseCsv(string path, string xCol, string yCol, double scale, bool flipY, bool yToZ)
        {
            var pts = new List<Point3d>();
            var lines = File.ReadAllLines(path);
            if (lines.Length < 2) return pts;

            // Automatic delimiter detection
            char delim = DetectDelimiter(lines[0]);

            var header = lines[0].Split(new char[] { delim });
            int xi = -1, yi = -1;
            for (int i = 0; i < header.Length; i++)
            {
                if (string.Equals(header[i].Trim(), xCol, StringComparison.OrdinalIgnoreCase)) xi = i;
                if (string.Equals(header[i].Trim(), yCol, StringComparison.OrdinalIgnoreCase)) yi = i;
            }

            if (xi < 0 || yi < 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    $"Column names not found. X='{xCol}', Y='{yCol}'. Header: {lines[0]}");
                return pts;
            }

            double yMax = 0;
            if (flipY)
            {
                for (int i = 1; i < lines.Length; i++)
                {
                    var cols = lines[i].Split(new char[] { delim });
                    if (cols.Length <= Math.Max(xi, yi)) continue;
                    if (double.TryParse(cols[yi].Trim(), out double yv) && yv > yMax) yMax = yv;
                }
            }

            for (int i = 1; i < lines.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i])) continue;
                var cols = lines[i].Split(new char[] { delim });
                if (cols.Length <= Math.Max(xi, yi)) continue;

                if (!double.TryParse(cols[xi].Trim(), out double xv)) continue;
                if (!double.TryParse(cols[yi].Trim(), out double yv)) continue;

                double px = xv * scale;
                double py = flipY ? (yMax - yv) * scale : yv * scale;

                pts.Add(yToZ ? new Point3d(px, 0, py) : new Point3d(px, py, 0));
            }

            return pts;
        }

        private static char DetectDelimiter(string header)
        {
            if (header.IndexOf('\t') >= 0) return '\t';
            if (header.IndexOf(';') >= 0) return ';';
            return ',';
        }

        private static bool Circle3Pt(Point3d a, Point3d b, Point3d c, out Point3d center, out double R)
        {
            center = Point3d.Origin; R = 0;
            Vector3d ab = b - a, ac = c - a;
            Vector3d n = Vector3d.CrossProduct(ab, ac);
            if (n.Length < 1e-9) return false;
            Plane pl = new Plane(a, n);
            double u1, v1, u2, v2;
            pl.ClosestParameter(b, out u1, out v1);
            pl.ClosestParameter(c, out u2, out v2);
            double bx = u1, by = v1, cx = u2, cy = v2;
            double d = 2 * (bx * cy - by * cx);
            if (Math.Abs(d) < 1e-12) return false;
            double ux = (cy * (bx * bx + by * by) - by * (cx * cx + cy * cy)) / d;
            double uy = (bx * (cx * cx + cy * cy) - cx * (bx * bx + by * by)) / d;
            center = pl.PointAt(ux, uy);
            R = center.DistanceTo(a);
            return true;
        }

        protected override Bitmap Icon => null;
        public override Guid ComponentGuid => new Guid("29b4b1a3-9e8d-4239-a7dd-339aa93dcd8d");
    }
}
