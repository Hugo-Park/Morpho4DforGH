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
    /// Captures the Rhino viewport and saves it to a file. Useful for producing thesis figures.
    /// Uses RhinoView.CaptureToBitmap.
    /// </summary>
#if NET5_0_OR_GREATER
    [SupportedOSPlatform("windows")]
#endif
    public class ViewportRecorderComponent : GH_Component
    {
        public ViewportRecorderComponent()
          : base("Viewport Recorder", "VPRec",
              "Captures and saves the current Rhino viewport as an image. Useful for producing thesis figures.",
              "Morpho4D", "08 Utility")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("Output Path", "Out",
                "Image file path to save (.png/.jpg/.bmp)", GH_ParamAccess.item, "");
            pManager.AddTextParameter("View Name", "V",
                "Name of the view to capture (e.g., 'Perspective', 'Top', 'Front', 'Right')", GH_ParamAccess.item, "Perspective");
            pManager.AddIntegerParameter("Width", "W", "Image width (px)", GH_ParamAccess.item, 1920);
            pManager.AddIntegerParameter("Height", "H", "Image height (px)", GH_ParamAccess.item, 1080);
            pManager.AddBooleanParameter("Capture", "Cap", "Set to true to execute capture", GH_ParamAccess.item, false);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Status", "S", "Capture result", GH_ParamAccess.item);
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
                DA.SetData(0, "Capture=false. Set to true to capture.");
                return;
            }

            if (string.IsNullOrWhiteSpace(outPath))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Please specify a save path.");
                DA.SetData(0, "ERROR: No path specified");
                return;
            }

            var doc = RhinoDoc.ActiveDoc;
            if (doc == null) { DA.SetData(0, "ERROR: No RhinoDoc found"); return; }

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
                    $"View '{viewName}' not found. Using the current active view.");
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
                DA.SetData(0, $"Saved successfully: {outPath} ({w}×{h}px)");
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message);
                DA.SetData(0, $"ERROR: {ex.Message}");
            }
        }

        protected override Bitmap Icon => null;
        public override Guid ComponentGuid => new Guid("5fe6803d-d2f3-474d-b8c2-1aa09bac39fa");
    }
}
