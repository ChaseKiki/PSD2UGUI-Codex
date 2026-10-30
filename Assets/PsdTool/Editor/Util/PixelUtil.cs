using UnityEngine;

namespace PsdTool
{
    /// <summary>像素数组均为从上到下的行顺序（与 PSD 一致）。</summary>
    internal static class PixelUtil
    {
        /// <summary>不透明像素的包围盒；全透明时返回宽高为 0 的矩形。</summary>
        public static RectInt OpaqueBounds(Color32[] px, int w, int h)
        {
            int minX = w, minY = h, maxX = -1, maxY = -1;
            for (int y = 0; y < h; y++)
            {
                int row = y * w;
                for (int x = 0; x < w; x++)
                {
                    if (px[row + x].a == 0) continue;
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
            }
            return maxX < 0 ? new RectInt(0, 0, 0, 0) : new RectInt(minX, minY, maxX - minX + 1, maxY - minY + 1);
        }

        public static Color32[] Crop(Color32[] px, int w, RectInt r)
        {
            var result = new Color32[r.width * r.height];
            for (int y = 0; y < r.height; y++)
                System.Array.Copy(px, (r.y + y) * w + r.x, result, y * r.width, r.width);
            return result;
        }

        public static bool IsSolid(Color32[] px, out Color32 color)
        {
            color = px.Length > 0 ? px[0] : default;
            foreach (var p in px)
                if (p.a != 255 || p.r != color.r || p.g != color.g || p.b != color.b)
                    return false;
            return px.Length > 0;
        }

        /// <summary>64 位差值哈希（9×8），用于相似图预筛选。</summary>
        public static long DHash(Color32[] px, int w, int h)
        {
            ulong bits = 0;
            for (int y = 0; y < 8; y++)
            {
                for (int x = 0; x < 8; x++)
                {
                    float left = Sample(px, w, h, x, y);
                    float right = Sample(px, w, h, x + 1, y);
                    if (left > right) bits |= 1UL << (y * 8 + x);
                }
            }
            return (long)bits;
        }

        public static int Hamming(long a, long b)
        {
            ulong x = (ulong)a ^ (ulong)b;
            int n = 0;
            while (x != 0)
            {
                n += (int)(x & 1);
                x >>= 1;
            }
            return n;
        }

        static float Sample(Color32[] px, int w, int h, int gx, int gy)
        {
            int x0 = gx * w / 9;
            int x1 = Mathf.Max(x0 + 1, (gx + 1) * w / 9);
            int y0 = gy * h / 8;
            int y1 = Mathf.Max(y0 + 1, (gy + 1) * h / 8);
            float sum = 0;
            int count = 0;
            for (int y = y0; y < y1 && y < h; y++)
            for (int x = x0; x < x1 && x < w; x++)
            {
                sum += Luma(px[y * w + x]);
                count++;
            }
            return count == 0 ? 0 : sum / count;
        }

        static float Luma(Color32 p) => (p.r * 299 + p.g * 587 + p.b * 114) / 1000f * (p.a / 255f);

        /// <summary>FNV-1a 64 位哈希，包含宽高。像素为从上到下的行顺序。</summary>
        public static long Hash(Color32[] px, int w, int h)
        {
            unchecked
            {
                const ulong prime = 1099511628211UL;
                ulong hash = 14695981039346656037UL;
                hash = (hash ^ (uint)w) * prime;
                hash = (hash ^ (uint)h) * prime;
                foreach (var p in px)
                {
                    hash = (hash ^ p.r) * prime;
                    hash = (hash ^ p.g) * prime;
                    hash = (hash ^ p.b) * prime;
                    hash = (hash ^ p.a) * prime;
                }
                return (long)hash;
            }
        }

        /// <summary>非预乘 alpha 的 Normal 混合，src 左上角放在 dst 的 (ox, oy)。</summary>
        public static void AlphaOver(Color32[] dst, int dw, int dh, Color32[] src, int sw, int sh, int ox, int oy, float opacity)
        {
            for (int y = 0; y < sh; y++)
            {
                int dy = y + oy;
                if (dy < 0 || dy >= dh) continue;
                for (int x = 0; x < sw; x++)
                {
                    int dx = x + ox;
                    if (dx < 0 || dx >= dw) continue;

                    var s = src[y * sw + x];
                    float sa = s.a / 255f * opacity;
                    if (sa <= 0f) continue;

                    int di = dy * dw + dx;
                    var d = dst[di];
                    float k = d.a / 255f * (1f - sa);
                    float oa = sa + k;
                    dst[di] = new Color32(
                        (byte)Mathf.RoundToInt((s.r * sa + d.r * k) / oa),
                        (byte)Mathf.RoundToInt((s.g * sa + d.g * k) / oa),
                        (byte)Mathf.RoundToInt((s.b * sa + d.b * k) / oa),
                        (byte)Mathf.RoundToInt(oa * 255f));
                }
            }
        }

        public static byte[] EncodePng(Color32[] px, int w, int h)
        {
            var flipped = new Color32[px.Length];
            for (int y = 0; y < h; y++)
                System.Array.Copy(px, y * w, flipped, (h - 1 - y) * w, w);

            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            try
            {
                tex.SetPixels32(flipped);
                tex.Apply(false);
                return tex.EncodeToPNG();
            }
            finally
            {
                Object.DestroyImmediate(tex);
            }
        }
    }
}
