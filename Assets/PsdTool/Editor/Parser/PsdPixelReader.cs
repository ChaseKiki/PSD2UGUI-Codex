using System;
using System.IO;
using System.IO.Compression;
using UnityEngine;

namespace PsdTool.Parser
{
    /// <summary>按需解码图层像素。输出为从上到下的行顺序。</summary>
    public sealed class PsdPixelReader : IDisposable
    {
        readonly PsdDocument _doc;
        readonly FileStream _stream;
        readonly BigEndianReader _r;

        public PsdPixelReader(PsdDocument doc)
        {
            _doc = doc;
            _stream = new FileStream(doc.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16);
            _r = new BigEndianReader(_stream);
        }

        public void Dispose() => _stream.Dispose();

        /// <summary>解码图层像素（已烘焙图层蒙版），尺寸为图层矩形。空图层返回 null。</summary>
        public Color32[] DecodeLayer(PsdLayer layer)
        {
            int w = layer.Width, h = layer.Height;
            if (w <= 0 || h <= 0) return null;

            byte[] red = null, green = null, blue = null, alpha = null, mask = null;
            var m = layer.Mask;
            foreach (var ch in layer.Channels)
            {
                switch (ch.Id)
                {
                    case 0: red = DecodeChannel(ch, w, h); break;
                    case 1: green = DecodeChannel(ch, w, h); break;
                    case 2: blue = DecodeChannel(ch, w, h); break;
                    case -1: alpha = DecodeChannel(ch, w, h); break;
                    case -2:
                        if (m != null && !m.Disabled && m.Width > 0 && m.Height > 0)
                            mask = DecodeChannel(ch, m.Width, m.Height);
                        break;
                }
            }

            var pixels = new Color32[w * h];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    int a = alpha != null ? alpha[i] : 255;
                    if (mask != null)
                    {
                        int gx = x + layer.Left, gy = y + layer.Top;
                        int mv = gx >= m.Left && gx < m.Right && gy >= m.Top && gy < m.Bottom
                            ? mask[(gy - m.Top) * m.Width + (gx - m.Left)]
                            : m.DefaultColor;
                        a = a * mv / 255;
                    }
                    pixels[i] = new Color32(
                        red != null ? red[i] : (byte)0,
                        green != null ? green[i] : (byte)0,
                        blue != null ? blue[i] : (byte)0,
                        (byte)a);
                }
            }
            return pixels;
        }

        /// <summary>
        /// 读取文件末尾的合成图。PS 保存时需勾选“最大兼容”，否则合成图可能为空白。
        /// </summary>
        public Color32[] DecodeComposite()
        {
            if (_doc.CompositeOffset < 0) return null;
            int w = _doc.Width, h = _doc.Height;
            int used = Math.Min(_doc.ChannelCount, 4);
            if (used < 3) return null;

            _r.Position = _doc.CompositeOffset;
            int compression = _r.ReadUInt16();
            var planes = new byte[used][];

            if (compression == 0)
            {
                for (int c = 0; c < used; c++) planes[c] = _r.ReadBytes(w * h);
            }
            else if (compression == 1)
            {
                int rows = _doc.ChannelCount * h;
                var counts = new int[rows];
                for (int i = 0; i < rows; i++) counts[i] = _doc.IsPsb ? (int)_r.ReadUInt32() : _r.ReadUInt16();
                for (int c = 0; c < used; c++)
                {
                    planes[c] = new byte[w * h];
                    for (int y = 0; y < h; y++)
                        UnpackBits(_r.ReadBytes(counts[c * h + y]), planes[c], y * w, w);
                }
            }
            else
            {
                return null;
            }

            var pixels = new Color32[w * h];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = new Color32(planes[0][i], planes[1][i], planes[2][i], used >= 4 ? planes[3][i] : (byte)255);
            return pixels;
        }

        byte[] DecodeChannel(PsdChannel ch, int w, int h)
        {
            var output = new byte[w * h];
            if (ch.Length < 2) return output;

            _r.Position = ch.Offset;
            int compression = _r.ReadUInt16();
            int dataLength = (int)(ch.Length - 2);

            switch (compression)
            {
                case 0:
                    Buffer.BlockCopy(_r.ReadBytes(Math.Min(dataLength, w * h)), 0, output, 0, Math.Min(dataLength, w * h));
                    break;
                case 1:
                {
                    var counts = new int[h];
                    for (int y = 0; y < h; y++) counts[y] = _doc.IsPsb ? (int)_r.ReadUInt32() : _r.ReadUInt16();
                    for (int y = 0; y < h; y++) UnpackBits(_r.ReadBytes(counts[y]), output, y * w, w);
                    break;
                }
                case 2:
                case 3:
                {
                    Inflate(_r.ReadBytes(dataLength), output);
                    if (compression == 3)
                    {
                        for (int y = 0; y < h; y++)
                        {
                            int row = y * w;
                            for (int x = 1; x < w; x++) output[row + x] = (byte)(output[row + x] + output[row + x - 1]);
                        }
                    }
                    break;
                }
                default:
                    throw new PsdException($"未知的像素压缩方式：{compression}");
            }
            return output;
        }

        static void UnpackBits(byte[] src, byte[] dst, int offset, int width)
        {
            int i = 0, o = offset, end = offset + width;
            while (i < src.Length && o < end)
            {
                int n = (sbyte)src[i++];
                if (n >= 0)
                {
                    int count = Math.Min(n + 1, Math.Min(end - o, src.Length - i));
                    Buffer.BlockCopy(src, i, dst, o, count);
                    i += n + 1;
                    o += count;
                }
                else if (n != -128)
                {
                    if (i >= src.Length) break;
                    byte b = src[i++];
                    int count = Math.Min(1 - n, end - o);
                    for (int k = 0; k < count; k++) dst[o++] = b;
                }
            }
        }

        static void Inflate(byte[] zlib, byte[] output)
        {
            if (zlib.Length <= 2) return;
            using (var ms = new MemoryStream(zlib, 2, zlib.Length - 2))
            using (var deflate = new DeflateStream(ms, CompressionMode.Decompress))
            {
                int read = 0;
                while (read < output.Length)
                {
                    int n = deflate.Read(output, read, output.Length - read);
                    if (n <= 0) break;
                    read += n;
                }
            }
        }
    }
}
