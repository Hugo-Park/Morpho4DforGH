using System;
using System.Drawing;
using System.IO;
#if NET5_0_OR_GREATER
using System.Runtime.Versioning;
#endif
using Grasshopper.Kernel;
using Rhino;
using Rhino.Display;

namespace _Morpho4D
{
    /// <summary>
    /// Rhino 뷰포트를 캡처해 파일로 저장한다. 논문 figure 생산용.
    /// RhinoView.CaptureToBitmap 사용.
    /// </summary>
#if NET5_0_OR_GREATER
    [SupportedOSPlatform("windows")]
#endif
    public class ViewportRecorderComponent : GH_Component
    {
        public ViewportRecorderComponent()
          : base("Viewport Recorder", "VPRec",
              "현재 Rhino 뷰포트를 이미지로 캡처·저장한다. 논문 figure 생산용.",
              "Morpho4D", "Utility")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("Output Path", "Out",
                "저장할 이미지 파일 경로 (.png/.jpg/.bmp)", GH_ParamAccess.item, "");
            pManager.AddTextParameter("View Name", "V",
                "캡처할 뷰 이름 ('Perspective','Top','Front','Right' 등)", GH_ParamAccess.item, "Perspective");
            pManager.AddIntegerParameter("Width", "W", "이미지 너비 (px)", GH_ParamAccess.item, 1920);
            pManager.AddIntegerParameter("Height", "H", "이미지 높이 (px)", GH_ParamAccess.item, 1080);
            pManager.AddBooleanParameter("Capture", "Cap", "true로 설정하면 캡처 실행", GH_ParamAccess.item, false);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Status", "S", "캡처 결과", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            string outPath = "", viewName = "Perspective";
            int w = 1920, h = 1080;
            bool capture = false;

            DA.GetData(0, ref outPath);
            DA.GetData(1, ref viewName);
            DA.GetData(2, ref w);
            DA.GetData(3, ref h);
            DA.GetData(4, ref capture);

            if (!capture)
            {
                DA.SetData(0, "Capture=false. true로 설정하면 캡처합니다.");
                return;
            }

            if (string.IsNullOrWhiteSpace(outPath))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "저장 경로를 지정하세요.");
                DA.SetData(0, "ERROR: 경로 없음");
                return;
            }

            var doc = RhinoDoc.ActiveDoc;
            if (doc == null) { DA.SetData(0, "ERROR: RhinoDoc 없음"); return; }

            RhinoView targetView = null;
            foreach (var v in doc.Views)
            {
                if (string.Equals(v.MainViewport.Name, viewName, StringComparison.OrdinalIgnoreCase))
                {
                    targetView = v;
                    break;
                }
            }

            if (targetView == null)
            {
                targetView = doc.Views.ActiveView;
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    $"뷰 '{viewName}'을 찾지 못해 현재 활성 뷰를 사용합니다.");
            }

            try
            {
                var size = new System.Drawing.Size(w, h);
                Bitmap bmp = targetView.CaptureToBitmap(size);

                string ext = Path.GetExtension(outPath).ToLower();
                System.Drawing.Imaging.ImageFormat fmt = System.Drawing.Imaging.ImageFormat.Png;
                if (ext == ".jpg" || ext == ".jpeg") fmt = System.Drawing.Imaging.ImageFormat.Jpeg;
                else if (ext == ".bmp") fmt = System.Drawing.Imaging.ImageFormat.Bmp;

                bmp.Save(outPath, fmt);
                DA.SetData(0, $"저장 완료: {outPath} ({w}×{h}px)");
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
                DA.SetData(0, $"ERROR: {ex.Message}");
            }
        }

        protected override Bitmap Icon => null;
        public override Guid ComponentGuid => new Guid("AAAAAAA6-1111-2222-3333-444444444444");
    }
}
