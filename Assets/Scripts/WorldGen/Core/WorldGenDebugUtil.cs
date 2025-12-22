using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace WorldGen.Core
{
    public static class WorldGenDebugUtil
    {
        public struct LayerStats
        {
            public int count;
            public float min;
            public float max;
            public float mean;
            public float p10;
            public float p50;
            public float p90;
            public float coveragePct;
        }

        public static LayerStats ComputeStats(float[] layer, bool[] mask)
        {
            var stats = new LayerStats
            {
                min = float.PositiveInfinity,
                max = float.NegativeInfinity
            };

            if (layer == null || mask == null || layer.Length != mask.Length || layer.Length == 0)
                return stats;

            var values = new List<float>();
            float sum = 0f;
            int inside = 0;

            for (int i = 0; i < layer.Length; i++)
            {
                if (!mask[i]) continue;
                var v = layer[i];
                values.Add(v);
                sum += v;
                inside++;
                if (v < stats.min) stats.min = v;
                if (v > stats.max) stats.max = v;
            }

            stats.count = inside;
            stats.mean = inside > 0 ? (sum / inside) : 0f;
            stats.coveragePct = (mask.Length > 0) ? (inside * 100f / mask.Length) : 0f;

            if (values.Count > 0)
            {
                values.Sort();
                stats.p10 = PercentileSorted(values, 0.10f);
                stats.p50 = PercentileSorted(values, 0.50f);
                stats.p90 = PercentileSorted(values, 0.90f);
            }
            else
            {
                stats.min = 0f;
                stats.max = 0f;
                stats.p10 = 0f;
                stats.p50 = 0f;
                stats.p90 = 0f;
            }

            return stats;
        }

        private static float PercentileSorted(List<float> sorted, float p)
        {
            if (sorted.Count == 0) return 0f;
            p = Mathf.Clamp01(p);
            var idx = p * (sorted.Count - 1);
            var i0 = Mathf.FloorToInt(idx);
            var i1 = Mathf.Min(sorted.Count - 1, i0 + 1);
            var t = idx - i0;
            return Mathf.Lerp(sorted[i0], sorted[i1], t);
        }

        public static Texture2D BuildFloatLayerTexture(int width, int height, float[] layer, bool[] mask, int texSize, out LayerStats stats)
        {
            stats = ComputeStats(layer, mask);
            texSize = Mathf.Clamp(texSize, 32, 4096);

            var tex = new Texture2D(texSize, texSize, TextureFormat.RGBA32, mipChain: false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Point;

            // Normalize based on masked min/max.
            var min = stats.min;
            var max = stats.max;
            var inv = (Mathf.Abs(max - min) > 1e-6f) ? (1f / (max - min)) : 0f;

            var pixels = new Color32[texSize * texSize];

            for (int py = 0; py < texSize; py++)
            {
                var gy = Mathf.Clamp(Mathf.RoundToInt((py / (float)(texSize - 1)) * (height - 1)), 0, height - 1);
                for (int px = 0; px < texSize; px++)
                {
                    var gx = Mathf.Clamp(Mathf.RoundToInt((px / (float)(texSize - 1)) * (width - 1)), 0, width - 1);
                    var gi = gy * width + gx;

                    byte r, g, b, a;
                    if (mask != null && (gi < 0 || gi >= mask.Length || !mask[gi]))
                    {
                        r = g = b = 0;
                        a = 0;
                    }
                    else
                    {
                        var v = (layer != null && gi >= 0 && gi < layer.Length) ? layer[gi] : 0f;
                        var t = Mathf.Clamp01((v - min) * inv);
                        var c = (byte)Mathf.RoundToInt(t * 255f);
                        r = g = b = c;
                        a = 255;
                    }

                    pixels[py * texSize + px] = new Color32(r, g, b, a);
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply(updateMipmaps: false, makeNoLongerReadable: false);
            return tex;
        }

        public static Texture2D BuildMaskTexture(int width, int height, bool[] mask, int texSize, out float coveragePct)
        {
            texSize = Mathf.Clamp(texSize, 32, 4096);

            var tex = new Texture2D(texSize, texSize, TextureFormat.RGBA32, mipChain: false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Point;

            var pixels = new Color32[texSize * texSize];
            int inside = 0;

            for (int py = 0; py < texSize; py++)
            {
                var gy = Mathf.Clamp(Mathf.RoundToInt((py / (float)(texSize - 1)) * (height - 1)), 0, height - 1);
                for (int px = 0; px < texSize; px++)
                {
                    var gx = Mathf.Clamp(Mathf.RoundToInt((px / (float)(texSize - 1)) * (width - 1)), 0, width - 1);
                    var gi = gy * width + gx;

                    var on = (mask != null && gi >= 0 && gi < mask.Length && mask[gi]);
                    if (on) inside++;

                    pixels[py * texSize + px] = on
                        ? new Color32(255, 255, 255, 255)
                        : new Color32(0, 0, 0, 0);
                }
            }

            coveragePct = (mask != null && mask.Length > 0) ? (inside * 100f / (texSize * texSize)) : 0f;
            tex.SetPixels32(pixels);
            tex.Apply(updateMipmaps: false, makeNoLongerReadable: false);
            return tex;
        }

        public static string SavePng(Texture2D tex, string folderRelativeToAssets, string fileNameNoExt)
        {
            if (tex == null) return null;

            var folderAbs = Path.Combine(Application.dataPath, folderRelativeToAssets.Replace("Assets/", "").Replace('\\', '/').TrimStart('/'));
            Directory.CreateDirectory(folderAbs);

            var bytes = tex.EncodeToPNG();
            var pathAbs = Path.Combine(folderAbs, fileNameNoExt + ".png");
            File.WriteAllBytes(pathAbs, bytes);

            // Return project-relative path (Assets/...)
            var projectRel = "Assets/" + folderRelativeToAssets.Replace("Assets/", "").TrimStart('/', '\\') + "/" + fileNameNoExt + ".png";
            projectRel = projectRel.Replace('\\', '/');

#if UNITY_EDITOR
            AssetDatabase.Refresh();
#endif

            return projectRel;
        }

        public static string Timestamp()
        {
            return DateTime.Now.ToString("yyyyMMdd_HHmmss");
        }
    }
}



