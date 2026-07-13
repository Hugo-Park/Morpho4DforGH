using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using Grasshopper.Kernel;
using Morpho4D.Models;
using Rhino.Geometry;

namespace _Morpho4D
{
    /// <summary>
    /// Generates Bambu Lab X2D dual extrusion G-code.
    /// Nozzle switch: M620 S{n}A → T{n} → M621 S{n}A (Bambu proprietary sequence)
    /// </summary>
    public class GCodeExporterComponent : GH_Component
    {
        public GCodeExporterComponent()
          : base("GCode Exporter", "GCode",
              "Converts and saves the Bilayer printing path to X2D G-code. Includes Bambu Lab X2D dedicated nozzle switch sequence.",
              "Morpho4D", "07 Fabrication")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddCurveParameter("T0 Paths", "P0", "passive(PLA) extrusion path", GH_ParamAccess.list);
            pManager.AddCurveParameter("T1 Paths", "P1", "active(SMP) extrusion path", GH_ParamAccess.list);
            pManager.AddNumberParameter("Layer Height", "LH", "Layer height (mm)", GH_ParamAccess.item, 0.2);
            pManager.AddNumberParameter("Line Width", "LW", "Line width (mm)", GH_ParamAccess.item, 0.4);
            pManager.AddNumberParameter("Print Speed", "PS", "Print speed (mm/min)", GH_ParamAccess.item, 2400.0);
            pManager.AddNumberParameter("Filament Diameter", "FD", "Filament diameter (mm)", GH_ParamAccess.item, 1.75);
            pManager.AddTextParameter("Output Path", "Out", ".gcode file path to save", GH_ParamAccess.item, "");
            pManager.AddBooleanParameter("Export", "E", "Save file if set to true", GH_ParamAccess.item, false);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("GCode Preview", "G", "Generated G-code preview (first 100 lines)", GH_ParamAccess.list);
            pManager.AddTextParameter("Status", "S", "Save status", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            var t0Paths = new List<Curve>();
            var t1Paths = new List<Curve>();
            double lh = 0.2, lw = 0.4, ps = 2400.0, fd = 1.75;
            string outPath = "";
            bool export = false;

            if (!DA.GetDataList(0, t0Paths)) return;
            if (!DA.GetDataList(1, t1Paths)) return;
            DA.GetData(2, ref lh);
            DA.GetData(3, ref lw);
            DA.GetData(4, ref ps);
            DA.GetData(5, ref fd);
            DA.GetData(6, ref outPath);
            DA.GetData(7, ref export);

            var lines = BuildGCode(t0Paths, t1Paths, lh, lw, ps, fd);

            var preview = new List<string>();
            for (int i = 0; i < Math.Min(100, lines.Count); i++)
                preview.Add(lines[i]);

            string status = "Not saved (Export=false)";
            if (export && !string.IsNullOrWhiteSpace(outPath))
            {
                try
                {
                    File.WriteAllLines(outPath, lines);
                    status = $"Save complete: {outPath} ({lines.Count} lines)";
                }
                catch (Exception ex)
                {
                    status = $"Save failed: {ex.Message}";
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, status);
                }
            }

            DA.SetDataList(0, preview);
            DA.SetData(1, status);
        }

        private List<string> BuildGCode(
            List<Curve> t0Paths, List<Curve> t1Paths,
            double lh, double lw, double ps, double fd)
        {
            var g = new List<string>();
            double ePerMm = (lh * lw) / (Math.PI * (fd / 2.0) * (fd / 2.0));
            double eAcc = 0.0;

            // Header — must include 4 warnings
            g.Add("; === Morph4D v4.1 G-code ===");
            g.Add("; WARNING: This file contains Bambu Lab X2D proprietary firmware commands.");
            g.Add("; WARNING: Import into Bambu Studio to review before printing (direct sending not recommended).");
            g.Add("; WARNING: T0 = passive(PLA), T1 = active(SMP). Check filament mapping in the slicer.");
            g.Add("; WARNING: Disabling Cool Mode (chamber cooling) may affect SMP activation temperature.");
            g.Add(";");
            g.Add($"; Layer Height: {lh} mm | Line Width: {lw} mm | Print Speed: {ps} mm/min");
            g.Add("");
            g.Add("G21 ; mm units");
            g.Add("G90 ; Absolute coordinates");
            g.Add("M82 ; Absolute extrusion");
            g.Add("G28 ; Auto home");
            g.Add("");

            // Skirt 2 loops (Starts with T0)
            g.Add("; --- Skirt (2 loops) ---");
            g.Add(SwitchNozzle(0));
            for (int loop = 0; loop < 2; loop++)
            {
                g.Add($"G1 X5 Y{5 + loop * 2} F{(int)(ps * 2)} ; Skirt move");
                g.Add($"G1 X80 Y{5 + loop * 2} E{eAcc + 5.0:F4} F{(int)ps}");
                eAcc += 5.0;
            }
            g.Add("");

            int currentNozzle = 0;

            // T0 layer
            if (t0Paths.Count > 0)
            {
                g.Add("; --- T0 (PLA passive) layer ---");
                if (currentNozzle != 0) { g.Add(SwitchNozzle(0)); currentNozzle = 0; }
                foreach (var path in t0Paths)
                {
                    var pts = SampleCurve(path, lw);
                    if (pts.Count < 2) continue;
                    g.Add($"G0 X{pts[0].X:F3} Y{pts[0].Y:F3} Z{pts[0].Z:F3} F{(int)(ps * 3)} ; Move");
                    for (int i = 1; i < pts.Count; i++)
                    {
                        double seg = pts[i - 1].DistanceTo(pts[i]);
                        eAcc += seg * ePerMm;
                        g.Add($"G1 X{pts[i].X:F3} Y{pts[i].Y:F3} Z{pts[i].Z:F3} E{eAcc:F4} F{(int)ps}");
                    }
                }
            }

            // T1 layer
            if (t1Paths.Count > 0)
            {
                g.Add("");
                g.Add("; --- T1 (SMP active) layer ---");
                if (currentNozzle != 1) { g.Add(SwitchNozzle(1)); currentNozzle = 1; }
                foreach (var path in t1Paths)
                {
                    var pts = SampleCurve(path, lw);
                    if (pts.Count < 2) continue;
                    g.Add($"G0 X{pts[0].X:F3} Y{pts[0].Y:F3} Z{pts[0].Z:F3} F{(int)(ps * 3)} ; Move");
                    for (int i = 1; i < pts.Count; i++)
                    {
                        double seg = pts[i - 1].DistanceTo(pts[i]);
                        eAcc += seg * ePerMm;
                        g.Add($"G1 X{pts[i].X:F3} Y{pts[i].Y:F3} Z{pts[i].Z:F3} E{eAcc:F4} F{(int)ps}");
                    }
                }
            }

            // Footer
            g.Add("");
            g.Add("; --- End ---");
            g.Add("G28 X Y ; X Y Home");
            g.Add("M84     ; Disable motors");
            g.Add("; === End ===");

            return g;
        }

        private static string SwitchNozzle(int n)
        {
            // Bambu Lab X2D nozzle switch sequence (proprietary firmware)
            return $"M620 S{n}A\nT{n}\nM621 S{n}A";
        }

        private static List<Point3d> SampleCurve(Curve crv, double spacing)
        {
            var pts = new List<Point3d>();
            if (crv == null) return pts;
            double len = crv.GetLength();
            if (len < 1e-6) return pts;
            int n = Math.Max(2, (int)(len / spacing) + 1);
            for (int i = 0; i <= n; i++)
            {
                double t;
                crv.LengthParameter(len * i / n, out t);
                pts.Add(crv.PointAt(t));
            }
            return pts;
        }

        protected override Bitmap Icon => IconLoader.Get("GCodeExporter");
        public override Guid ComponentGuid => new Guid("09be29b1-c1f1-422e-929e-279e37e11c73");
    }
}
