using System;

namespace LeagueTablecloth
{
    internal sealed class Rgba
    {
        public readonly int Width;
        public readonly int Height;
        public readonly byte[] Pixels;

        public Rgba(int width, int height)
        {
            Width = width;
            Height = height;
            Pixels = new byte[width * height * 4];
        }

        public Rgba(int width, int height, byte[] pixels)
        {
            if (pixels.Length != width * height * 4)
                throw new ArgumentException("pixel buffer size does not match dimensions");
            Width = width;
            Height = height;
            Pixels = pixels;
        }
    }

    internal struct Layer
    {
        public Rgba Source;
        public int X, Y;
        public int W, H;
        public int Rot;
    }

    internal static class Compositor
    {
        public static Rgba Compose(int size, Layer[] layers)
        {
            var dst = new Rgba(size, size);
            foreach (var layer in layers)
            {
                var src = layer.Source;
                if (layer.Rot != 0)
                    src = Rotate(src, layer.Rot);
                CompositeOver(dst, src, layer.X, layer.Y);
            }
            return dst;
        }

        private static byte Blend(int dst, int src, int a)
        {
            return (byte)((dst * (255 - a) + src * a + 127) / 255);
        }

        public static void CompositeOver(Rgba dst, Rgba src, int dstX, int dstY)
        {
            int x0 = Math.Max(0, dstX);
            int y0 = Math.Max(0, dstY);
            int x1 = Math.Min(dst.Width, dstX + src.Width);
            int y1 = Math.Min(dst.Height, dstY + src.Height);
            if (x1 <= x0 || y1 <= y0) return;

            byte[] d = dst.Pixels;
            byte[] s = src.Pixels;
            int dStride = dst.Width * 4;
            int sStride = src.Width * 4;
            for (int y = y0; y < y1; y++)
            {
                int sy = y - dstY;
                int di = y * dStride + x0 * 4;
                int si = sy * sStride + (x0 - dstX) * 4;
                for (int x = x0; x < x1; x++, di += 4, si += 4)
                {
                    int a = s[si + 3];
                    if (a == 0) continue;
                    if (a == 255)
                    {
                        d[di] = s[si];
                        d[di + 1] = s[si + 1];
                        d[di + 2] = s[si + 2];
                        d[di + 3] = 255;
                        continue;
                    }
                    d[di] = Blend(d[di], s[si], a);
                    d[di + 1] = Blend(d[di + 1], s[si + 1], a);
                    d[di + 2] = Blend(d[di + 2], s[si + 2], a);
                    d[di + 3] = Blend(d[di + 3], a, a);
                }
            }
        }

        public static Rgba Rotate(Rgba src, int rot)
        {
            int r = ((rot % 360) + 360) % 360;
            if (r == 0) return src;

            int sw = src.Width, sh = src.Height;
            byte[] s = src.Pixels;

            bool swap = r != 180;
            int dw = swap ? sh : sw;
            int dh = swap ? sw : sh;
            var outImg = new Rgba(dw, dh);
            byte[] o = outImg.Pixels;
            for (int dy = 0; dy < dh; dy++)
            for (int dx = 0; dx < dw; dx++)
            {
                int sx, sy;
                if (r == 180)
                {
                    sx = sw - 1 - dx;
                    sy = sh - 1 - dy;
                }
                else if (r == 90)
                {
                    sx = sw - 1 - dy;
                    sy = dx;
                }
                else
                {
                    sx = dy;
                    sy = sh - 1 - dx;
                }
                Copy(s, (sy * sw + sx) * 4, o, (dy * dw + dx) * 4);
            }
            return outImg;
        }

        private static void Copy(byte[] src, int si, byte[] dst, int di)
        {
            dst[di] = src[si];
            dst[di + 1] = src[si + 1];
            dst[di + 2] = src[si + 2];
            dst[di + 3] = src[si + 3];
        }
    }
}
