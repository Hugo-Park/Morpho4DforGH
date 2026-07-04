using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace _Morpho4D
{
    /// <summary>
    /// Fiji ImageJ의 CSV 측정 결과를 임포트해 실측 곡률(κ_exp)을 산출한다.
    /// CSV 구분자 자동감지(쉼표/탭/세미콜론), FlipY 옵션, Y→Z 축 변환 옵션 지원.
    /// </summary>
    public class MeasurementImporterComponent : GH_Component
    {
        public MeasurementImporterComponent()
          : base("Measurement Importer", "MeasImp",
              "Fiji CSV 측정값을 임포트해 실측 곡률(κ_exp)을 산출한다.",
              "Morpho4D", "Validation")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("CSV Path", "CSV", "Fiji ImageJ 측정 CSV 파일 경로", GH_ParamAccess.item);
            pManager.AddTextParameter("X Column", "xCol", "X 좌표 열 이름 (대소문자 무관)", GH_ParamAccess.item, "X");
            pManager.AddTextParameter("Y Column", "yCol", "Y 좌표 열 이름 (대소문자 무관)", GH_ParamAccess.item, "Y");
            pManager.AddNumberParameter("Scale", "Sc", "픽셀→mm 변환 스케일", GH_ParamAccess.item, 1.0);
            pManager.AddBooleanParameter("Flip Y", "FY", "Y축 반전 여부 (이미지 좌표계 → 세계 좌표계)", GH_ParamAccess.item, true);
            pManager.AddBooleanParameter("Y to Z", "YZ", "Y 값을 Z 축으로 변환 (평면도→측면도)", GH_ParamAccess.item, false);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddPointParameter("Measured Points", "Pts", "임포트된 실측 포인트", GH_ParamAccess.list);
            pManager.AddNumberParameter("κ_exp", "κ", "실측 곡률 (3점 원 피팅, 1/mm)", GH_ParamAccess.item);
            pManager.AddNumberParameter("R_exp", "R", "실측 곡률 반지름 (mm)", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Point Count", "n", "임포트된 포인트 수", GH_ParamAccess.item);
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
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"파일을 찾을 수 없습니다: {csvPath}");
                return;
            }

            var pts = ParseCsv(csvPath, xCol, yCol, scale, flipY, yToZ);
            if (pts.Count < 3)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"포인트가 3개 미만입니다 ({pts.Count}개). 곡률 계산 불가.");
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

            // 구분자 자동감지
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
                    $"열 이름을 찾을 수 없습니다. X='{xCol}', Y='{yCol}'. 헤더: {lines[0]}");
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
        public override Guid ComponentGuid => new Guid("8A1C4E72-3F5B-4D91-A6E8-1B2C3D4E5F60");
    }
}
