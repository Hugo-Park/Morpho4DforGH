using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
#if NET5_0_OR_GREATER
using System.Runtime.Versioning;
#endif
using Grasshopper.Kernel;

namespace _Morpho4D
{
    /// <summary>
    /// Collects sequential image files to generate a GIF animation or organize a file list.
    /// Used to produce paper figures in connection with ViewportRecorder.
    /// </summary>
#if NET5_0_OR_GREATER
    [SupportedOSPlatform("windows")]
#endif
    public class MediaExporterComponent : GH_Component
    {
        public MediaExporterComponent()
          : base("Media Exporter", "Media",
              "Collects ViewportRecorder images and exports them as a GIF or file list. For producing paper figures.",
              "Morpho4D", "08 Utility")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("Frame Paths", "F", "List of each frame image file paths", GH_ParamAccess.list);
            pManager.AddTextParameter("Output Path", "Out", "GIF file path to save (.gif)", GH_ParamAccess.item, "");
            pManager.AddIntegerParameter("Frame Delay ms", "D", "Delay between frames (ms)", GH_ParamAccess.item, 100);
            pManager.AddBooleanParameter("Loop", "L", "Whether to loop the GIF infinitely", GH_ParamAccess.item, true);
            pManager.AddBooleanParameter("Export", "E", "If set to true, generates GIF", GH_ParamAccess.item, false);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Status", "S", "Generation result", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Frame Count", "n", "Number of processed frames", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            var framePaths = new List<string>();
            string outPath = "";
            int delay = 100;
            bool loop = true, export = false;

            if (!DA.GetDataList(0, framePaths)) return;
            DA.GetData(1, ref outPath);
            DA.GetData(2, ref delay);
            DA.GetData(3, ref loop);
            DA.GetData(4, ref export);

            if (!export)
            {
                DA.SetData(0, string.Format("Export=false. {0} frames waiting.", framePaths.Count));
                DA.SetData(1, framePaths.Count);
                return;
            }

            if (string.IsNullOrWhiteSpace(outPath))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Please specify an output path.");
                DA.SetData(0, "ERROR: No path"); DA.SetData(1, 0);
                return;
            }

            int processed = 0;
            try
            {
                using (var fs = new FileStream(outPath, FileMode.Create))
                {
                    var encoder = new GifEncoder(fs, delay, loop);
                    foreach (var path in framePaths)
                    {
                        if (!File.Exists(path)) continue;
                        using (var bmp = new Bitmap(path))
                        {
                            encoder.AddFrame(bmp);
                            processed++;
                        }
                    }
                    encoder.Finish();
                }
                DA.SetData(0, string.Format("GIF saved: {0} ({1} frames)", outPath, processed));
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    "Failed to generate GIF: " + ex.Message + ". Outputting only the frame path list.");
                DA.SetData(0, "Warning: " + ex.Message);
            }

            DA.SetData(1, processed);
        }

        protected override Bitmap Icon => IconLoader.Get("MediaExporter");
        public override Guid ComponentGuid => new Guid("e56fbad5-5081-4794-8732-8ccf53ed562a");
    }

    // Minimal GIF encoder (Pure .NET — no external dependencies)
#if NET5_0_OR_GREATER
    [SupportedOSPlatform("windows")]
#endif
    internal class GifEncoder
    {
        private readonly Stream _stream;
        private readonly int _delay;
        private readonly bool _loop;
        private bool _firstFrame = true;
        private int _width, _height;

        public GifEncoder(Stream stream, int delayMs, bool loop)
        {
            _stream = stream;
            _delay = delayMs / 10;
            _loop = loop;
        }

        public void AddFrame(Bitmap bmp)
        {
            if (_firstFrame)
            {
                _width = bmp.Width;
                _height = bmp.Height;
                WriteHeader();
                if (_loop) WriteNetscapeExtension();
                _firstFrame = false;
            }

            using (var quantized = (Bitmap)bmp.Clone(
                new Rectangle(0, 0, bmp.Width, bmp.Height),
                System.Drawing.Imaging.PixelFormat.Format8bppIndexed))
            {
                WriteGraphicControlExtension();
                WriteImageDescriptor(quantized);
                WriteLzwData(quantized);
            }
        }

        public void Finish()
        {
            _stream.WriteByte(0x3B);
        }

        private void WriteHeader()
        {
            _stream.Write(System.Text.Encoding.ASCII.GetBytes("GIF89a"), 0, 6);
            WriteShort(_width); WriteShort(_height);
            _stream.WriteByte(0x70);
            _stream.WriteByte(0);
            _stream.WriteByte(0);
            for (int i = 0; i < 256; i++)
            {
                _stream.WriteByte((byte)i);
                _stream.WriteByte((byte)i);
                _stream.WriteByte((byte)i);
            }
        }

        private void WriteNetscapeExtension()
        {
            _stream.WriteByte(0x21); _stream.WriteByte(0xFF); _stream.WriteByte(11);
            _stream.Write(System.Text.Encoding.ASCII.GetBytes("NETSCAPE2.0"), 0, 11);
            _stream.WriteByte(3); _stream.WriteByte(1);
            WriteShort(0);
            _stream.WriteByte(0);
        }

        private void WriteGraphicControlExtension()
        {
            _stream.WriteByte(0x21); _stream.WriteByte(0xF9); _stream.WriteByte(4);
            _stream.WriteByte(0x00);
            WriteShort(_delay);
            _stream.WriteByte(0); _stream.WriteByte(0);
        }

        private void WriteImageDescriptor(Bitmap bmp)
        {
            _stream.WriteByte(0x2C);
            WriteShort(0); WriteShort(0);
            WriteShort(bmp.Width); WriteShort(bmp.Height);
            _stream.WriteByte(0x00);
        }

        private void WriteLzwData(Bitmap bmp)
        {
            var pixels = new byte[bmp.Width * bmp.Height];
            var data = bmp.LockBits(
                new Rectangle(0, 0, bmp.Width, bmp.Height),
                System.Drawing.Imaging.ImageLockMode.ReadOnly,
                System.Drawing.Imaging.PixelFormat.Format8bppIndexed);
            for (int y = 0; y < bmp.Height; y++)
            {
                System.Runtime.InteropServices.Marshal.Copy(
                    new IntPtr(data.Scan0.ToInt64() + (long)y * data.Stride),
                    pixels, y * bmp.Width, bmp.Width);
            }
            bmp.UnlockBits(data);

            _stream.WriteByte(8);
            var lzw = EncodeLzw(pixels);
            int pos = 0;
            while (pos < lzw.Length)
            {
                int blockSize = Math.Min(255, lzw.Length - pos);
                _stream.WriteByte((byte)blockSize);
                _stream.Write(lzw, pos, blockSize);
                pos += blockSize;
            }
            _stream.WriteByte(0);
        }

        private static byte[] EncodeLzw(byte[] pixels)
        {
            int clearCode = 256;
            var output = new System.Collections.BitArray(pixels.Length * 9 + 32);
            int bitPos = 0;
            int codeSize = 9;

            void WriteBits(int code)
            {
                for (int b = 0; b < codeSize; b++)
                {
                    if (bitPos < output.Length) output[bitPos] = ((code >> b) & 1) == 1;
                    bitPos++;
                }
            }

            WriteBits(clearCode);
            foreach (var px in pixels) WriteBits(px);
            WriteBits(clearCode + 1); // EOF

            int byteCount = (bitPos + 7) / 8;
            var result = new byte[byteCount];
            output.CopyTo(result, 0);
            return result;
        }

        private void WriteShort(int value)
        {
            _stream.WriteByte((byte)(value & 0xFF));
            _stream.WriteByte((byte)((value >> 8) & 0xFF));
        }
    }
}
