using System.Collections.Generic;
using UnityEngine;
using WorldGen.Core;

namespace WorldGen.Steps
{
    /// <summary>
    /// Builds a canonical WorldGridData right after height has been applied to the terrain mesh.
    /// The grid is a uniform XZ lattice clipped to a disk mask; height[] is initialized by sampling
    /// the current terrain mesh vertex.y values back onto grid cells (robust to flat-shaded duplication).
    /// Also computes slope (simple finite differences) and can export debug textures/logs.
    /// </summary>
    public sealed class Step_BuildWorldGridData : MonoBehaviour, IGenerationStep
    {
        public string Name => "Build World Grid Data";

        public void Generate(WorldGenSettings settings, WorldContext ctx)
        {
            if (ctx == null)
            {
                Debug.LogError($"{nameof(Step_BuildWorldGridData)}: ctx is null.");
                return;
            }

            if (settings == null)
            {
                Debug.LogError($"{nameof(Step_BuildWorldGridData)}: settings is null.");
                return;
            }

            if (ctx.TerrainMesh == null || ctx.TerrainGO == null)
            {
                Debug.LogError($"{nameof(Step_BuildWorldGridData)}: missing TerrainMesh/TerrainGO. Run after Step_FlatDiskTerrain.");
                return;
            }

            var radius = Mathf.Max(0.01f, settings.radius);
            var cell = Mathf.Max(0.1f, settings.cellSizeWorld);

            var steps = Mathf.Max(1, Mathf.CeilToInt((radius * 2f) / cell));
            var size = steps + 1;

            var grid = new WorldGridData
            {
                width = size,
                height = size,
                cellSizeWorld = cell,
                radiusWorld = radius,
                originWorld = ctx.TerrainGO.transform.position,

                maskDisk = new bool[size * size],
                heightLayer = new float[size * size],
                slope = new float[size * size],

                // placeholders (optional)
                flowDir = new Vector2[size * size],
                flowAccum = new float[size * size],
                moisture = new float[size * size],
                biomeId = new int[size * size]
            };

            // Mask based on grid point positions relative to disk radius.
            var r2 = radius * radius;
            for (int y = 0; y < size; y++)
            {
                var z = (-radius) + (y * cell);
                for (int x = 0; x < size; x++)
                {
                    var xx = (-radius) + (x * cell);
                    var i = grid.Idx(x, y);
                    grid.maskDisk[i] = (xx * xx + z * z) <= r2;
                }
            }

            // Sample mesh vertices into the grid (robust to flat shading).
            var verts = ctx.TerrainMesh.vertices;
            var inv = 1f / cell;

            var sum = new float[grid.width * grid.height];
            var count = new int[grid.width * grid.height];

            for (int i = 0; i < verts.Length; i++)
            {
                var v = verts[i];
                var gx = Mathf.RoundToInt((v.x + radius) * inv);
                var gy = Mathf.RoundToInt((v.z + radius) * inv);
                if (!grid.InBounds(gx, gy)) continue;

                var gi = grid.Idx(gx, gy);
                if (!grid.maskDisk[gi]) continue;

                sum[gi] += v.y;
                count[gi] += 1;
            }

            // Assign height layer. If a masked cell has no samples (can happen near clip boundary),
            // fill it from neighbors to avoid holes in the data foundation.
            for (int i = 0; i < grid.heightLayer.Length; i++)
            {
                if (!grid.maskDisk[i])
                {
                    grid.heightLayer[i] = 0f;
                    continue;
                }

                if (count[i] > 0)
                {
                    grid.heightLayer[i] = sum[i] / count[i];
                }
                else
                {
                    grid.heightLayer[i] = float.NaN;
                }
            }

            FillMissingHeights(grid);

            // Compute slope.
            grid.ComputeSlopeFromHeight();

            ctx.Grid = grid;

            // Stats + summary log.
            var hStats = WorldGenDebugUtil.ComputeStats(grid.heightLayer, grid.maskDisk);
            var sStats = WorldGenDebugUtil.ComputeStats(grid.slope, grid.maskDisk);

            Debug.Log($"[WorldGrid] seed={settings.seed}, size={grid.width}x{grid.height}, cellSize={grid.cellSizeWorld:0.###}, radius={grid.radiusWorld:0.###}, " +
                      $"height(min/max/mean)={hStats.min:0.00}/{hStats.max:0.00}/{hStats.mean:0.00}, " +
                      $"slope(min/max/mean)={sStats.min:0.00}/{sStats.max:0.00}/{sStats.mean:0.00}, insideMask={hStats.coveragePct:0.0}%");

            if (settings.enableDebugLogs)
            {
                Debug.Log($"[WorldGrid] height p10/p50/p90={hStats.p10:0.00}/{hStats.p50:0.00}/{hStats.p90:0.00} | slope p10/p50/p90={sStats.p10:0.00}/{sStats.p50:0.00}/{sStats.p90:0.00}");
            }

            if (settings.exportDebugTextures)
            {
                ExportDebugTexture(settings, ctx, grid);
            }
        }

        private static void FillMissingHeights(WorldGridData grid)
        {
            // One cheap pass that replaces NaNs with average of available neighbors (masked only).
            // Repeat a few times to propagate values inward if needed.
            for (int pass = 0; pass < 3; pass++)
            {
                var changed = 0;
                for (int i = 0; i < grid.heightLayer.Length; i++)
                {
                    if (!grid.maskDisk[i]) continue;
                    if (!float.IsNaN(grid.heightLayer[i])) continue;

                    float sum = 0f;
                    int cnt = 0;
                    foreach (var n in grid.Neighbors8(i))
                    {
                        if (!grid.maskDisk[n]) continue;
                        var v = grid.heightLayer[n];
                        if (float.IsNaN(v)) continue;
                        sum += v;
                        cnt++;
                    }

                    if (cnt > 0)
                    {
                        grid.heightLayer[i] = sum / cnt;
                        changed++;
                    }
                }

                if (changed == 0) break;
            }

            // Any remaining NaNs become 0.
            for (int i = 0; i < grid.heightLayer.Length; i++)
            {
                if (!grid.maskDisk[i]) continue;
                if (float.IsNaN(grid.heightLayer[i])) grid.heightLayer[i] = 0f;
            }
        }

        private static void ExportDebugTexture(WorldGenSettings settings, WorldContext ctx, WorldGridData grid)
        {
            var layer = settings.debugLayer;
            var baseName = $"WorldGrid_{layer}_seed{settings.seed}_{WorldGenDebugUtil.Timestamp()}";

            if (layer == DebugLayer.MaskDisk)
            {
                var tex = WorldGenDebugUtil.BuildMaskTexture(grid.width, grid.height, grid.maskDisk, settings.debugTextureSize, out var cov);
                var path = WorldGenDebugUtil.SavePng(tex, "Assets/WorldGen/DebugOutputs", baseName);
                Debug.Log($"[WorldGrid] Exported {layer} PNG: {path} (coverage≈{cov:0.0}%)");
                TryApplyTextureToTerrain(ctx, tex);
                return;
            }

            float[] floatLayer = null;
            switch (layer)
            {
                case DebugLayer.Height: floatLayer = grid.heightLayer; break;
                case DebugLayer.Slope: floatLayer = grid.slope; break;
                case DebugLayer.FlowAccum: floatLayer = grid.flowAccum; break;
                case DebugLayer.Moisture: floatLayer = grid.moisture; break;
                default: floatLayer = grid.heightLayer; break;
            }

            var tex2 = WorldGenDebugUtil.BuildFloatLayerTexture(grid.width, grid.height, floatLayer, grid.maskDisk, settings.debugTextureSize, out var stats);
            var path2 = WorldGenDebugUtil.SavePng(tex2, "Assets/WorldGen/DebugOutputs", baseName);
            Debug.Log($"[WorldGrid] Exported {layer} PNG: {path2} (min/max/mean={stats.min:0.00}/{stats.max:0.00}/{stats.mean:0.00})");
            TryApplyTextureToTerrain(ctx, tex2);
        }

        private static void TryApplyTextureToTerrain(WorldContext ctx, Texture2D tex)
        {
            if (ctx.TerrainGO == null) return;
            var mr = ctx.TerrainGO.GetComponent<MeshRenderer>();
            if (mr == null) return;

            var mat = mr.sharedMaterial;
            if (mat == null) return;

            // URP Lit uses _BaseMap; legacy uses _MainTex.
            if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
            else if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", tex);
        }
    }
}


