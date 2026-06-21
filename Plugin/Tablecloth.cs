using System;
using System.Collections.Generic;
using System.IO;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace LeagueTablecloth
{
    // here's where quadrant composition happens
    // given the four absolute-seat nicknames and the viewer's seat, it resolves each seat's triangle,
    // composes the base + four triangles onto the fixed quadrant geometry,
    // and rotates the result into a single reused display texture for the viewer's perspective
    // state is per-match and single-threaded
    internal static class Tablecloth
    {
        // each triangle source is 1568x786, 90/-90 rotations swap it to 786x1568
        // nicknames arrive in absolute seat order E/S/W/N
        private const int SIZE = 2048;
        private const int TRI_W = 1568;
        private const int TRI_H = 786;

        private readonly struct Quadrant
        {
            public readonly int X, Y, Rot;
            public Quadrant(int x, int y, int rot) { X = x; Y = y; Rot = rot; }
        }

        private static readonly Quadrant[] Quadrants =
        {
            new Quadrant(240, 1020, 0), // east
            new Quadrant(1020, 240, 90), // south
            new Quadrant(235, 240, 180), // west
            new Quadrant(240, 240, -90), // north
        };

        private const int ROTATION_SIGN = -1;

        private static string _assetDir = "";
        private static Dictionary<string, string> _nicknameMap = new Dictionary<string, string>();

        private static Rgba? _baseRgba;
        private static string? _baseKey;

        // live GPU texture the material shows (base rotated for the current seat)
        private static Texture2D? _displayTex;

        // a decoded source layer plus the file's last-write time when it was decoded
        private readonly struct CachedLayer
        {
            public readonly Rgba Data;
            public readonly DateTime Stamp;
            public CachedLayer(Rgba data, DateTime stamp) { Data = data; Stamp = stamp; }
        }

        // decoded source layers by absolute path and validated against mtime
        private static readonly Dictionary<string, CachedLayer> DecodeCache =
            new Dictionary<string, CachedLayer>();

        public static void Init(string assetDir)
        {
            _assetDir = assetDir;
            Log($"asset directory: {_assetDir}");
            _nicknameMap = Identity.LoadNicknameMap(Path.Combine(_assetDir, "players.json"));
        }

        // resolve each seat's triangle from nicknames (E/S/W/N order), compose or reuse the base for that tuple,
        // rotate it for viewerSeat into the single reused display texture, return it
        public static Texture2D? Compose(string[] nicknames, int viewerSeat)
        {
            var triangles = new string[4];
            var keyParts = new string[4];
            for (int seat = 0; seat < 4; seat++)
            {
                string nick = seat < nicknames.Length ? (nicknames[seat] ?? "") : "";
                triangles[seat] = Identity.ResolveTriangle(_nicknameMap, nick);
                keyParts[seat] = nick.Trim();
                Log($"seat {seat + 1} nickname=\"{nick}\" -> {triangles[seat]}");
            }
            string key = string.Join("\u001f", keyParts);

            if (_baseRgba == null || _baseKey != key)
            {
                var composed = ComposeBase(triangles);
                if (composed == null)
                {
                    Log("compose failed (missing/corrupt asset); leaving vanilla cloth untouched");
                    return null;
                }
                _baseRgba = composed;
                _baseKey = key;
                Log("composed base for new player tuple");
            }
            else
            {
                Log("reusing cached base composite (same players)");
            }

            if (viewerSeat < 1 || viewerSeat > 4) viewerSeat = 1;
            int k = (((ROTATION_SIGN * (viewerSeat - 1)) % 4) + 4) % 4; // 0..3 quarter-turns CCW
            var rotated = Compositor.Rotate(_baseRgba, k * 90);
            _displayTex = WriteTexture(rotated, _displayTex);
            Log($"oriented cloth for seat {viewerSeat} (rotation k={k})");
            return _displayTex;
        }

        // free our display texture and drop the cached base so leaving a match accumulates nothing
        public static void Release()
        {
            if (_displayTex != null)
            {
                try { UnityEngine.Object.Destroy(_displayTex); }
                catch (Exception e) { Log($"failed to destroy display texture (continuing): {e.Message}"); }
                _displayTex = null;
            }
            _baseRgba = null;
            _baseKey = null;
        }

        // decode the base + each seat's resolved triangle and compose them onto the fixed quadrant geometry
        private static Rgba? ComposeBase(string[] triangles)
        {
            var baseImg = Decode(Path.Combine(_assetDir, "base.png"));
            if (baseImg == null) return null;

            var layers = new List<Layer>(5)
            {
                new Layer { Source = baseImg, X = 0, Y = 0, W = SIZE, H = SIZE, Rot = 0 },
            };
            for (int seat = 0; seat < 4; seat++)
            {
                var q = Quadrants[seat];
                var img = Decode(Path.Combine(_assetDir, triangles[seat]));
                if (img == null) return null;
                layers.Add(new Layer { Source = img, X = q.X, Y = q.Y, W = TRI_W, H = TRI_H, Rot = q.Rot });
            }
            return Compositor.Compose(SIZE, layers.ToArray());
        }

        // decode a PNG to a top-left-origin RGBA buffer, caching successful decodes by (path, mtime)
        private static Rgba? Decode(string path)
        {
            if (!File.Exists(path))
            {
                Log($"layer file not found: {path}");
                return null;
            }

            DateTime stamp;
            try { stamp = File.GetLastWriteTimeUtc(path); }
            catch { stamp = DateTime.MinValue; }

            if (stamp != DateTime.MinValue
                && DecodeCache.TryGetValue(path, out var cached)
                && cached.Stamp == stamp)
            {
                return cached.Data;
            }

            Rgba? result = null;
            try
            {
                var bytes = File.ReadAllBytes(path);
                // RGBA32, linear:false (sRGB colour data, not a linear/normal map)
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false, false);
                bool ok = ImageConversion.LoadImage(tex, (Il2CppStructArray<byte>)bytes, false);
                if (!ok)
                    Log($"failed to decode PNG: {path}");
                else
                    result = ToTopLeftRgba(tex);
                UnityEngine.Object.Destroy(tex);
            }
            catch (Exception e)
            {
                Log($"error decoding {path}: {e.Message}");
                result = null;
            }

            if (result != null && stamp != DateTime.MinValue)
                DecodeCache[path] = new CachedLayer(result, stamp);
            return result;
        }

        // Unity's GetPixels32 returns bottom-left, row-major
        // the compositor work top-left, so flip rows on the way in
        private static Rgba ToTopLeftRgba(Texture2D tex)
        {
            int w = tex.width, h = tex.height;
            var px = tex.GetPixels32();
            var img = new Rgba(w, h);
            byte[] d = img.Pixels;
            for (int r = 0; r < h; r++)
            {
                int srcRow = (h - 1 - r) * w; // bottom-left row matching this top-left row
                int dstRow = r * w * 4;
                for (int c = 0; c < w; c++)
                {
                    var p = px[srcRow + c];
                    int di = dstRow + c * 4;
                    d[di] = p.r;
                    d[di + 1] = p.g;
                    d[di + 2] = p.b;
                    d[di + 3] = p.a;
                }
            }
            return img;
        }

        // build (or refill) a Texture2D from a top-left RGBA buffer
        [ThreadStatic] private static byte[]? _rowFlipScratch;

        private static Texture2D WriteTexture(Rgba img, Texture2D? dest)
        {
            int w = img.Width, h = img.Height;
            byte[] d = img.Pixels;
            int total = w * h * 4;
            byte[] raw = _rowFlipScratch != null && _rowFlipScratch.Length == total
                ? _rowFlipScratch
                : (_rowFlipScratch = new byte[total]);
            int rowBytes = w * 4;
            for (int r = 0; r < h; r++)
            {
                int srcRow = r * rowBytes;
                int dstRow = (h - 1 - r) * rowBytes;
                Array.Copy(d, srcRow, raw, dstRow, rowBytes);
            }

            Texture2D tex;
            if (dest != null && dest.width == w && dest.height == h && dest.format == TextureFormat.RGBA32)
            {
                tex = dest;
            }
            else
            {
                tex = new Texture2D(w, h, TextureFormat.RGBA32, false, false);
                tex.filterMode = FilterMode.Bilinear;
                tex.wrapMode = TextureWrapMode.Clamp;
                tex.hideFlags = HideFlags.HideAndDontSave;
            }

            tex.LoadRawTextureData((Il2CppStructArray<byte>)raw);
            tex.Apply(false, false);
            return tex;
        }

        private static void Log(string msg) => Plugin.Instance?.Log.LogInfo($"[tablecloth] {msg}");
    }
}
