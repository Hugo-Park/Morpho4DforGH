using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

using Grasshopper;
using Grasshopper.Kernel;
using Rhino;
using Rhino.Display;

namespace _Morpho4D
{
    /// <summary>
    /// 활성 라이노 뷰포트를 한 프레임씩 PNG로 캡처한다. (보고서/영상 소재용)
    /// 화면 구석에 현재 시뮬레이션 시간/소재 같은 텍스트 주석(Overlay)을 그릴 수 있다.
    /// 시퀀스 캡처 워크플로: Simulation Player의 Frame을 이 컴포넌트의 Frame Index에도 연결한 뒤,
    /// Capture=True로 두고 Frame 슬라이더에 라이노 기본 Animate를 실행 -> 매 프레임 PNG가 저장된다.
    /// (Simulation Player가 프레임을 미리 캐싱하므로 Animate 중 솔버가 돌지 않아 안전/고화질)
    /// 고화질 MP4가 필요하면 이 PNG 시퀀스를 Premiere Pro나 ffmpeg로 합치는 것을 권장(Media Exporter 참고).
    /// </summary>
    public class ViewportRecorder : GH_Component
    {
        public ViewportRecorder()
          : base("Viewport Recorder", "Recorder",
            "Capture the active Rhino viewport to a PNG (with optional overlay text). Drive Frame Index with Animate to record a sequence.",
            "Morpho4D", "Animation")
        {
        }

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("Folder", "Dir", "Output folder for the PNG frames", GH_ParamAccess.item);
            pManager.AddTextParameter("File Prefix", "Pre", "Filename prefix (frames saved as prefix_0000.png)", GH_ParamAccess.item, "frame");
            pManager.AddIntegerParameter("Frame Index", "F", "Frame index used in the filename and overlay", GH_ParamAccess.item, 0);
            pManager.AddIntegerParameter("Width", "W", "Capture width in pixels", GH_ParamAccess.item, 1920);
            pManager.AddIntegerParameter("Height", "H", "Capture height in pixels", GH_ParamAccess.item, 1080);
            pManager.AddTextParameter("Overlay Text", "Txt", "Optional overlay text (e.g. 'Time: 12.4s | Material: Hydrogel')", GH_ParamAccess.item, "");
            pManager.AddBooleanParameter("Capture", "C", "When True, capture this solution's viewport to a PNG", GH_ParamAccess.item, false);
            pManager[5].Optional = true;
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("File Path", "FP", "Path of the written PNG (empty if not captured)", GH_ParamAccess.item);
            pManager.AddTextParameter("Status", "St", "Capture status", GH_ParamAccess.item);
        }

        private string _lastPath = "";

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            this.ClearRuntimeMessages();

            string folder = null, prefix = "frame", overlay = "";
            int frame = 0, w = 1920, h = 1080;
            bool capture = false;

            if (!DA.GetData(0, ref folder)) { return; }
            if (!DA.GetData(1, ref prefix)) { return; }
            if (!DA.GetData(2, ref frame)) { return; }
            if (!DA.GetData(3, ref w)) { return; }
            if (!DA.GetData(4, ref h)) { return; }
            DA.GetData(5, ref overlay);
            if (!DA.GetData(6, ref capture)) { return; }

            if (!capture)
            {
                DA.SetData(0, _lastPath);
                DA.SetData(1, "Idle (set Capture = True to record)");
                return;
            }

            if (string.IsNullOrWhiteSpace(folder)) { this.AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Folder is empty"); return; }
            if (w <= 0 || h <= 0) { this.AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Width/Height must be > 0"); return; }

            RhinoDoc doc = RhinoDoc.ActiveDoc;
            if (doc == null) { this.AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No active Rhino document"); return; }
            RhinoView view = doc.Views.ActiveView;
            if (view == null) { this.AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No active viewport"); return; }

            try
            {
                Directory.CreateDirectory(folder);

                Bitmap bmp = view.CaptureToBitmap(new Size(w, h));
                if (bmp == null) { this.AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "CaptureToBitmap returned null"); return; }

                if (!string.IsNullOrEmpty(overlay))
                {
                    using (Graphics g = Graphics.FromImage(bmp))
                    {
                        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                        float fontSize = Math.Max(12f, h / 50f);
                        using (Font font = new Font("Segoe UI", fontSize, FontStyle.Bold, GraphicsUnit.Pixel))
                        {
                            SizeF textSize = g.MeasureString(overlay, font);
                            float pad = fontSize * 0.5f;
                            float x = pad;
                            float y = h - textSize.Height - pad;
                            using (SolidBrush back = new SolidBrush(Color.FromArgb(140, 0, 0, 0)))
                                g.FillRectangle(back, x - pad * 0.5f, y - pad * 0.25f, textSize.Width + pad, textSize.Height + pad * 0.5f);
                            using (SolidBrush fore = new SolidBrush(Color.White))
                                g.DrawString(overlay, font, fore, x, y);
                        }
                    }
                }

                string fileName = string.Format("{0}_{1:D4}.png", prefix, frame);
                string path = Path.Combine(folder, fileName);
                bmp.Save(path, ImageFormat.Png);
                bmp.Dispose();

                _lastPath = path;
                DA.SetData(0, path);
                DA.SetData(1, string.Format("Saved frame {0} -> {1}", frame, path));
            }
            catch (Exception ex)
            {
                this.AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Capture failed: " + ex.Message);
            }
        }

        protected override System.Drawing.Bitmap Icon => null;

        public override Guid ComponentGuid => new Guid("1eb5344b-b123-46a9-b9a4-d29e18f2197f");
    }
}
