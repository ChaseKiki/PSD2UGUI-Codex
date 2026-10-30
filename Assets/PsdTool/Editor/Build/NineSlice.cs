using System;
using UnityEngine;

namespace PsdTool
{
    public struct NineSliceResult
    {
        public Color32[] pixels;
        public int width;
        public int height;

        /// <summary>Unity 顺序：左、下、右、上。</summary>
        public Vector4 border;
        public bool tiled;
        public float avgError;
        public int maxError;
        public string warning;
    }

    /// <summary>
    /// 九宫格与平铺识别。先找可拉伸的纯色（容差内）行列，再找周期性重复。
    /// 结果用拉伸回原尺寸的逐像素误差回验，超标则放弃。
    /// </summary>
    public static class NineSlice
    {
        public const int ChannelTolerance = 2;
        public const int MinStretch = 8;
        public const float MaxAvgError = 1f;
        public const int MaxPixelError = 8;
        const int MinTileRepeats = 3;

        struct Axis
        {
            public int cap0;
            public int cap1;
            public int period;
            public int keep;

            /// <summary>原图中末端边框的起始位置。</summary>
            public int tail;

            public static Axis Identity(int length) => new Axis { period = length, tail = length };

            public static Axis Slice(int length, int start, int run) => new Axis
            {
                cap0 = start,
                cap1 = length - start - run,
                period = 1,
                keep = start,
                tail = start + run,
            };

            public static Axis Tile(int length, int start, int period, int run) => new Axis
            {
                cap0 = start,
                cap1 = length - start - run,
                period = period,
                keep = start,
                tail = start + run,
            };
        }

        /// <param name="forced">@9s 的参数（左,上,右,下）；没有参数时传空字符串；未标记传 null。</param>
        public static bool Try(Color32[] src, int w, int h, string forced, out NineSliceResult result)
        {
            result = default;
            if (src == null || w < 3 || h < 3 || src.Length < w * h) return false;

            if (forced != null)
            {
                if (forced.Length == 0) return TryAuto(src, w, h, out result);
                if (!Parse(forced, out int left, out int top, out int right, out int bottom))
                {
                    result.warning = "九宫参数应为 @9s:左,上,右,下";
                    return false;
                }
                if (left < 0 || top < 0 || right < 0 || bottom < 0 || left + right >= w || top + bottom >= h)
                {
                    result.warning = $"九宫边框 {left},{top},{right},{bottom} 超出图片尺寸 {w}×{h}";
                    return false;
                }
                return Build(src, w, h,
                    new Axis { cap0 = left, cap1 = right, period = 1, keep = left, tail = w - right },
                    new Axis { cap0 = top, cap1 = bottom, period = 1, keep = top, tail = h - bottom },
                    false, true, out result);
            }

            return TryAuto(src, w, h, out result);
        }

        static bool TryAuto(Color32[] src, int w, int h, out NineSliceResult result)
        {
            bool col = TryUniformRun(w, (a, b) => ColumnsEqual(src, w, h, a, b), out int cx, out int clen);
            bool row = TryUniformRun(h, (a, b) => RowsEqual(src, w, a, b), out int ry, out int rlen);
            if ((col || row) && Build(src, w, h,
                    col ? Axis.Slice(w, cx, clen) : Axis.Identity(w),
                    row ? Axis.Slice(h, ry, rlen) : Axis.Identity(h),
                    false, false, out result))
                return true;

            var colHash = Hashes(src, w, h, true);
            var rowHash = Hashes(src, w, h, false);
            bool px = TryPeriod(w, (a, b) => colHash[a] == colHash[b], out int cs, out int cp, out int crun);
            bool py = TryPeriod(h, (a, b) => rowHash[a] == rowHash[b], out int rs, out int rp, out int rrun);
            if ((px || py) && Build(src, w, h,
                    px ? Axis.Tile(w, cs, cp, crun) : Axis.Identity(w),
                    py ? Axis.Tile(h, rs, rp, rrun) : Axis.Identity(h),
                    true, false, out result))
                return true;

            result = default;
            return false;
        }

        static bool Build(Color32[] src, int w, int h, Axis ax, Axis ay, bool tiled, bool keepDespiteError, out NineSliceResult result)
        {
            result = default;
            int mw = ax.cap0 + ax.period + ax.cap1;
            int mh = ay.cap0 + ay.period + ay.cap1;
            if (ax.period < 1 || ay.period < 1 || mw <= 0 || mh <= 0 || mw > w || mh > h) return false;
            if (mw == w && mh == h) return false;
            if (ax.cap0 + ax.cap1 >= mw || ay.cap0 + ay.cap1 >= mh) return false;

            var dst = new Color32[mw * mh];
            for (int y = 0; y < mh; y++)
            {
                int sy = Source(y, ay);
                for (int x = 0; x < mw; x++)
                    dst[y * mw + x] = src[sy * w + Source(x, ax)];
            }

            Measure(src, w, h, dst, mw, ax, ay, out float avg, out int max);
            bool pass = avg <= MaxAvgError && max <= MaxPixelError;
            if (!pass && !keepDespiteError) return false;

            result = new NineSliceResult
            {
                pixels = dst,
                width = mw,
                height = mh,
                border = new Vector4(ax.cap0, ay.cap1, ax.cap1, ay.cap0),
                tiled = tiled,
                avgError = avg,
                maxError = max,
                warning = pass ? null : $"九宫回验误差偏大（平均 {avg:0.##}，最大 {max}），仍按标记使用",
            };
            return true;
        }

        static int Source(int m, Axis a)
        {
            if (m < a.cap0) return m;
            if (m < a.cap0 + a.period) return a.keep + (m - a.cap0);
            return a.tail + (m - a.cap0 - a.period);
        }

        static int Map(int p, Axis a, int length)
        {
            if (p < a.cap0) return p;
            int end = length - a.cap1;
            if (p >= end) return a.cap0 + a.period + (p - end);
            int rel = p - a.cap0;
            return a.cap0 + (a.period <= 1 ? 0 : rel % a.period);
        }

        static void Measure(Color32[] src, int w, int h, Color32[] dst, int mw, Axis ax, Axis ay, out float avg, out int max)
        {
            long sum = 0;
            max = 0;
            for (int y = 0; y < h; y++)
            {
                int my = Map(y, ay, h);
                for (int x = 0; x < w; x++)
                {
                    int mx = Map(x, ax, w);
                    sum += Delta(src[y * w + x], dst[my * mw + mx], ref max);
                }
            }
            avg = (float)sum / (w * h * 4f);
        }

        static int Delta(Color32 a, Color32 b, ref int max)
        {
            int dr = Math.Abs(a.r - b.r);
            int dg = Math.Abs(a.g - b.g);
            int db = Math.Abs(a.b - b.b);
            int da = Math.Abs(a.a - b.a);
            max = Math.Max(max, Math.Max(Math.Max(dr, dg), Math.Max(db, da)));
            return dr + dg + db + da;
        }

        static bool TryUniformRun(int length, Func<int, int, bool> equal, out int start, out int len)
        {
            start = 0;
            len = 0;
            if (length < MinStretch) return false;
            int bestS = 0, bestL = 0, runS = 0;
            for (int i = 1; i <= length; i++)
            {
                if (i < length && equal(runS, i)) continue;
                int l = i - runS;
                if (l > bestL)
                {
                    bestL = l;
                    bestS = runS;
                }
                runS = i;
            }
            if (bestL < MinStretch) return false;
            start = bestS;
            len = bestL;
            return true;
        }

        static bool TryPeriod(int length, Func<int, int, bool> equal, out int start, out int period, out int run)
        {
            start = period = run = 0;
            int maxP = length / MinTileRepeats;
            int bestLen = 0, bestP = 0, bestS = 0;
            for (int p = 2; p <= maxP; p++)
            {
                int streakStart = -1;
                for (int i = p; i <= length; i++)
                {
                    bool match = i < length && equal(i - p, i);
                    if (match)
                    {
                        if (streakStart < 0) streakStart = i;
                        continue;
                    }
                    if (streakStart >= 0)
                    {
                        Consider(streakStart, i, p, ref bestLen, ref bestP, ref bestS);
                        streakStart = -1;
                    }
                }
            }
            if (bestP == 0) return false;
            start = bestS;
            period = bestP;
            run = bestLen;
            return true;
        }

        static void Consider(int firstMatch, int endExclusive, int period, ref int bestLen, ref int bestP, ref int bestS)
        {
            int regionStart = firstMatch - period;
            int aligned = (endExclusive - regionStart) / period * period;
            if (aligned < period * MinTileRepeats) return;
            if (aligned < bestLen || (aligned == bestLen && period >= bestP)) return;
            bestLen = aligned;
            bestP = period;
            bestS = regionStart;
        }

        static bool ColumnsEqual(Color32[] px, int w, int h, int a, int b)
        {
            for (int y = 0; y < h; y++)
                if (!Near(px[y * w + a], px[y * w + b])) return false;
            return true;
        }

        static bool RowsEqual(Color32[] px, int w, int a, int b)
        {
            for (int x = 0; x < w; x++)
                if (!Near(px[a * w + x], px[b * w + x])) return false;
            return true;
        }

        static bool Near(Color32 a, Color32 b) =>
            Math.Abs(a.r - b.r) <= ChannelTolerance && Math.Abs(a.g - b.g) <= ChannelTolerance
            && Math.Abs(a.b - b.b) <= ChannelTolerance && Math.Abs(a.a - b.a) <= ChannelTolerance;

        static long[] Hashes(Color32[] px, int w, int h, bool columns)
        {
            int n = columns ? w : h;
            var hashes = new long[n];
            for (int i = 0; i < n; i++) hashes[i] = LineHash(px, w, h, i, columns);
            return hashes;
        }

        static long LineHash(Color32[] px, int w, int h, int index, bool column)
        {
            unchecked
            {
                const ulong prime = 1099511628211UL;
                ulong hash = 14695981039346656037UL;
                int count = column ? h : w;
                for (int i = 0; i < count; i++)
                {
                    var p = column ? px[i * w + index] : px[index * w + i];
                    hash = (hash ^ p.r) * prime;
                    hash = (hash ^ p.g) * prime;
                    hash = (hash ^ p.b) * prime;
                    hash = (hash ^ p.a) * prime;
                }
                return (long)hash;
            }
        }

        static bool Parse(string arg, out int left, out int top, out int right, out int bottom)
        {
            left = top = right = bottom = 0;
            var parts = arg.Split(new[] { ',', '，' });
            return parts.Length == 4
                   && int.TryParse(parts[0].Trim(), out left)
                   && int.TryParse(parts[1].Trim(), out top)
                   && int.TryParse(parts[2].Trim(), out right)
                   && int.TryParse(parts[3].Trim(), out bottom);
        }
    }
}
