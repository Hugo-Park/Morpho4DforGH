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
    /// 연속 이미지 파일을 수집해 GIF 애니메이션을 생성하거나 파일 목록을 정리한다.
    /// ViewportRecorder와 연결해 논문 figure 생산에 사용.
    /// </summary>
#if NET5_0_OR_GREATER
    [SupportedOSPlatform("windows")]
#endif
    public class MediaExporterComponent : GH_Component
    {
        public MediaExporterComponent()
          : base("Media Exporter", "Media",
              "ViewportRecorder 이미지를 수집해 GIF 또는 파일 목록으로 내보낸다. 논문 figure 생산용.",
              "Morpho4D", "Utility")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("Frame Paths", "F", "각 프레임 이미지 파일 경로 리스트", GH_ParamAccess.list);
            pManager.AddTextParameter("Output Path", "Out", "저장할 GIF 파일 경로 (.gif)", GH_ParamAccess.item, "");
            pManager.AddIntegerParameter("Frame Delay ms", "D", "프레임 간 지연 (ms)", GH_ParamAccess.item, 100);
            pManager.AddBooleanParameter("Loop", "L", "GIF 무한 반복 여부", GH_ParamAccess.item, true);
            pManager.AddBooleanParameter("Export", "E", "true로 설정하면 GIF 생성", GH_ParamAccess.item, false);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Status", "S", "생성 결과", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Frame Count", "n", "처리된 프레임 수", GH_ParamAccess.item);
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
                DA.SetData(0, string.Format("Export=false. {0}개 프레임 대기 중.", framePaths.Count));
                DA.SetData(1, framePaths.Count);
                return;
            }

            if (string.IsNullOrWhiteSpace(outPath))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "출력 경로를 지정하세요.");
                DA.SetData(0, "ERROR: 경로 없음"); DA.SetData(1, 0);
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
                DA.SetData(0, string.Format("GIF 저장 완료: {0} ({1}프레임)", outPath, processed));
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    "GIF 생성 실패: " + ex.Message + ". 프레임 경로 목록만 출력합니다.");
                DA.SetData(0, "경고: " + ex.Message);
            }

            DA.SetData(1, processed);
        }

        protected override Bitmap Icon => null;
        public override Guid ComponentGuid => new Guid("AAAAAAA7-1111-2222-3333-444444444444");
    }

    // 최소 GIF 인코더 (순수 .NET — 외부 의존성 없음)
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
