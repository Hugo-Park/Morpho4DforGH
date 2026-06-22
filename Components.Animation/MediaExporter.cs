using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

using Grasshopper;
using Grasshopper.Kernel;

namespace _Morpho4D
{
    /// <summary>
    /// Viewport Recorder가 저장한 PNG 시퀀스를 GIF/MP4로 합치는 백엔드 유틸리티.
    /// 자체 인코더를 만들지 않고 ffmpeg 실행파일을 호출한다 (전문 영상툴을 재발명하지 않는다는 판단).
    /// => ffmpeg가 설치되어 있어야 한다(PATH에 있거나 FFmpeg Path로 경로 지정). 없으면 PNG 시퀀스를
    ///    Premiere Pro의 이미지 시퀀스 기능으로 합치는 것이 더 견고하다.
    /// 주의: 인코딩이 끝날 때까지 GH 솔브 스레드가 블로킹된다(수동 Export 버튼이므로 허용 범위).
    /// </summary>
    public class MediaExporter : GH_Component
    {
        public MediaExporter()
          : base("Media Exporter", "Export",
            "Assemble a PNG sequence into a GIF or MP4 by calling ffmpeg. Requires ffmpeg installed. For best MP4 quality, Premiere Pro on the PNG sequence also works.",
            "Morpho4D", "Animation")
        {
        }

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("Frame Folder", "Dir", "Folder containing the PNG sequence", GH_ParamAccess.item);
            pManager.AddTextParameter("File Prefix", "Pre", "Prefix used by Viewport Recorder (prefix_0000.png)", GH_ParamAccess.item, "frame");
            pManager.AddTextParameter("Output Path", "Out", "Output file path (e.g. C:/out/sim.mp4 or C:/out/sim.gif)", GH_ParamAccess.item);
            pManager.AddTextParameter("Format", "Fmt", "Output format: 'mp4' or 'gif'", GH_ParamAccess.item, "mp4");
            pManager.AddIntegerParameter("FPS", "FPS", "Frames per second", GH_ParamAccess.item, 24);
            pManager.AddTextParameter("FFmpeg Path", "FF", "Path to ffmpeg executable (default: 'ffmpeg' on PATH)", GH_ParamAccess.item, "ffmpeg");
            pManager.AddBooleanParameter("Export", "E", "Set True to run the export", GH_ParamAccess.item, false);
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Status", "St", "Export status / ffmpeg result", GH_ParamAccess.item);
            pManager.AddTextParameter("Command", "Cmd", "The ffmpeg command used (for transparency / manual run)", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            this.ClearRuntimeMessages();

            string folder = null, prefix = "frame", outPath = null, format = "mp4", ffmpeg = "ffmpeg";
            int fps = 24;
            bool export = false;

            if (!DA.GetData(0, ref folder)) { return; }
            if (!DA.GetData(1, ref prefix)) { return; }
            if (!DA.GetData(2, ref outPath)) { return; }
            if (!DA.GetData(3, ref format)) { return; }
            if (!DA.GetData(4, ref fps)) { return; }
            if (!DA.GetData(5, ref ffmpeg)) { return; }
            if (!DA.GetData(6, ref export)) { return; }

            if (fps <= 0) fps = 24;
            format = (format ?? "mp4").Trim().ToLowerInvariant();

            string pattern = Path.Combine(folder ?? "", string.Format("{0}_%04d.png", prefix));

            string args;
            if (format == "gif")
            {
                args = string.Format("-y -framerate {0} -i \"{1}\" -vf \"fps={0},scale=trunc(iw/2)*2:-1:flags=lanczos\" \"{2}\"",
                                     fps, pattern, outPath);
            }
            else // mp4 (default)
            {
                args = string.Format("-y -framerate {0} -i \"{1}\" -c:v libx264 -pix_fmt yuv420p -vf \"scale=trunc(iw/2)*2:trunc(ih/2)*2\" \"{2}\"",
                                     fps, pattern, outPath);
            }

            string fullCmd = string.Format("\"{0}\" {1}", ffmpeg, args);

            if (!export)
            {
                DA.SetData(0, "Idle (set Export = True). You can also run the command below manually.");
                DA.SetData(1, fullCmd);
                return;
            }

            if (string.IsNullOrWhiteSpace(folder) || string.IsNullOrWhiteSpace(outPath))
            {
                this.AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Frame Folder and Output Path are required");
                DA.SetData(1, fullCmd);
                return;
            }

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = ffmpeg,
                    Arguments = args,
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };

                using (Process proc = Process.Start(psi))
                {
                    string stderr = proc.StandardError.ReadToEnd();
                    proc.WaitForExit();
                    int code = proc.ExitCode;

                    if (code == 0)
                    {
                        DA.SetData(0, string.Format("Success ({0}) -> {1}", format, outPath));
                    }
                    else
                    {
                        string tail = stderr;
                        if (!string.IsNullOrEmpty(tail) && tail.Length > 500) tail = tail.Substring(tail.Length - 500);
                        this.AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "ffmpeg exited with code " + code);
                        DA.SetData(0, "ffmpeg error (code " + code + "):\n" + tail);
                    }
                }
            }
            catch (System.ComponentModel.Win32Exception)
            {
                this.AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "ffmpeg not found. Install ffmpeg or set FFmpeg Path. Alternatively import the PNG sequence into Premiere Pro.");
                DA.SetData(0, "ffmpeg not found. Use the PNG sequence in Premiere Pro, or install ffmpeg.");
            }
            catch (Exception ex)
            {
                this.AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Export failed: " + ex.Message);
                DA.SetData(0, "Export failed: " + ex.Message);
            }

            DA.SetData(1, fullCmd);
        }

        protected override System.Drawing.Bitmap Icon => null;

        public override Guid ComponentGuid => new Guid("ffd3ff6e-8227-46bd-81cb-4b97fb8218a6");
    }
}
