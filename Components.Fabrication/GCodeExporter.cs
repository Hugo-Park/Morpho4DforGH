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
    /// Bambu Lab X2D 이중 압출 G-code를 생성한다.
    /// 노즐 전환: M620 S{n}A → T{n} → M621 S{n}A (Bambu 독점 시퀀스)
    /// </summary>
    public class GCodeExporterComponent : GH_Component
    {
        public GCodeExporterComponent()
          : base("GCode Exporter", "GCode",
              "Bilayer 프린팅 경로를 X2D G-code로 변환·저장한다. Bambu Lab X2D 전용 노즐 전환 시퀀스 포함.",
              "Morpho4D", "Fabrication")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddCurveParameter("T0 Paths", "P0", "passive(PLA) 압출 경로", GH_ParamAccess.list);
            pManager.AddCurveParameter("T1 Paths", "P1", "active(SMP) 압출 경로", GH_ParamAccess.list);
            pManager.AddNumberParameter("Layer Height", "LH", "레이어 높이 (mm)", GH_ParamAccess.item, 0.2);
            pManager.AddNumberParameter("Line Width", "LW", "압출선 폭 (mm)", GH_ParamAccess.item, 0.4);
            pManager.AddNumberParameter("Print Speed", "PS", "인쇄 속도 (mm/min)", GH_ParamAccess.item, 2400.0);
            pManager.AddNumberParameter("Filament Diameter", "FD", "필라멘트 직경 (mm)", GH_ParamAccess.item, 1.75);
            pManager.AddTextParameter("Output Path", "Out", "저장할 .gcode 파일 경로", GH_ParamAccess.item, "");
            pManager.AddBooleanParameter("Export", "E", "true로 설정하면 파일 저장", GH_ParamAccess.item, false);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("GCode Preview", "G", "생성된 G-code 미리보기 (첫 100줄)", GH_ParamAccess.list);
            pManager.AddTextParameter("Status", "S", "저장 상태", GH_ParamAccess.item);
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

            string status = "미저장 (Export=false)";
            if (export && !string.IsNullOrWhiteSpace(outPath))
            {
                try
                {
                    File.WriteAllLines(outPath, lines);
                    status = $"저장 완료: {outPath} ({lines.Count}줄)";
                }
                catch (Exception ex)
                {
                    status = $"저장 실패: {ex.Message}";
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

            // 헤더 — 경고 4개 필수 포함
            g.Add("; === Morph4D v4.1 G-code ===");
            g.Add("; WARNING: 이 파일은 Bambu Lab X2D 독점 펌웨어 명령을 포함합니다.");
            g.Add("; WARNING: Bambu Studio로 import하여 검토 후 출력하십시오 (직접 전송 비권장).");
            g.Add("; WARNING: T0 = passive(PLA), T1 = active(SMP). 슬라이서의 필라멘트 매핑을 확인하십시오.");
            g.Add("; WARNING: Cool Mode(챔버 냉각)를 비활성화하면 SMP 활성화 온도에 영향을 줄 수 있습니다.");
            g.Add(";");
            g.Add($"; Layer Height: {lh} mm | Line Width: {lw} mm | Print Speed: {ps} mm/min");
            g.Add("");
            g.Add("G21 ; mm 단위");
            g.Add("G90 ; 절대 좌표");
            g.Add("M82 ; 절대 압출");
            g.Add("G28 ; 원점 복귀");
            g.Add("");

            // 스커트 2루프 (T0으로 시작)
            g.Add("; --- 스커트 (2 루프) ---");
            g.Add(SwitchNozzle(0));
            for (int loop = 0; loop < 2; loop++)
            {
                g.Add($"G1 X5 Y{5 + loop * 2} F{(int)(ps * 2)} ; 스커트 이동");
                g.Add($"G1 X80 Y{5 + loop * 2} E{eAcc + 5.0:F4} F{(int)ps}");
                eAcc += 5.0;
            }
            g.Add("");

            int currentNozzle = 0;

            // T0 레이어
            if (t0Paths.Count > 0)
            {
                g.Add("; --- T0 (PLA passive) 레이어 ---");
                if (currentNozzle != 0) { g.Add(SwitchNozzle(0)); currentNozzle = 0; }
                foreach (var path in t0Paths)
                {
                    var pts = SampleCurve(path, lw);
                    if (pts.Count < 2) continue;
                    g.Add($"G0 X{pts[0].X:F3} Y{pts[0].Y:F3} Z{pts[0].Z:F3} F{(int)(ps * 3)} ; 이동");
                    for (int i = 1; i < pts.Count; i++)
                    {
                        double seg = pts[i - 1].DistanceTo(pts[i]);
                        eAcc += seg * ePerMm;
                        g.Add($"G1 X{pts[i].X:F3} Y{pts[i].Y:F3} Z{pts[i].Z:F3} E{eAcc:F4} F{(int)ps}");
                    }
                }
            }

            // T1 레이어
            if (t1Paths.Count > 0)
            {
                g.Add("");
                g.Add("; --- T1 (SMP active) 레이어 ---");
                if (currentNozzle != 1) { g.Add(SwitchNozzle(1)); currentNozzle = 1; }
                foreach (var path in t1Paths)
                {
                    var pts = SampleCurve(path, lw);
                    if (pts.Count < 2) continue;
                    g.Add($"G0 X{pts[0].X:F3} Y{pts[0].Y:F3} Z{pts[0].Z:F3} F{(int)(ps * 3)} ; 이동");
                    for (int i = 1; i < pts.Count; i++)
                    {
                        double seg = pts[i - 1].DistanceTo(pts[i]);
                        eAcc += seg * ePerMm;
                        g.Add($"G1 X{pts[i].X:F3} Y{pts[i].Y:F3} Z{pts[i].Z:F3} E{eAcc:F4} F{(int)ps}");
                    }
                }
            }

            // 푸터
            g.Add("");
            g.Add("; --- 종료 ---");
            g.Add("G28 X Y ; X Y 원점");
            g.Add("M84     ; 모터 비활성화");
            g.Add("; === 종료 ===");

            return g;
        }

        private static string SwitchNozzle(int n)
        {
            // Bambu Lab X2D 노즐 전환 시퀀스 (독점 펌웨어)
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

        protected override Bitmap Icon => null;
        public override Guid ComponentGuid => new Guid("77777777-8888-9999-aaaa-bbbbbbbbbbbb");
    }
}
