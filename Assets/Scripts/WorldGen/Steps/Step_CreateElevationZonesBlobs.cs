using System.IO;
using UnityEngine;
using WorldGen.Core;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace WorldGen.Steps
{
    /// <summary>
    /// Creates a small number of "blotchy blob" elevated zones using a metaballs / blob-field implicit surface.
    /// Zones may overlap. Per-cell height delta is taken from the winning blob (argmax contribution) when
    /// the summed field crosses a threshold.
    ///
    /// This step samples the current terrain mesh into a temporary grid, applies height deltas to that grid,
    /// recomputes slope from height, then pushes the updated heights back to the mesh (and wireframe if present).
    /// </summary>
    public sealed class Step_CreateElevationZonesBlobs : MonoBehaviour, IGenerationStep
    {
        public string Name => "Create Elevation Zones (Blobs)";

        public void Generate(WorldGenSettings settings, WorldContext ctx)
        {
            if (ctx == null)
            {
                Debug.LogError($"{nameof(Step_CreateElevationZonesBlobs)}: ctx is null.");
                return;
            }

            if (settings == null)
            {
                Debug.LogError($"{nameof(Step_CreateElevationZonesBlobs)}: settings is null.");
                return;
            }

            if (!settings.zonesEnabled)
                return;

            if (ctx.TerrainMesh == null || ctx.TerrainGO == null)
            {
                Debug.LogError($"{nameof(Step_CreateElevationZonesBlobs)}: missing TerrainMesh/TerrainGO. Run after Step_FlatDiskTerrain.");
                return;
            }

            if (ctx.Rng == null)
            {
                Debug.LogError($"{nameof(Step_CreateElevationZonesBlobs)}: ctx.Rng is null.");
                return;
            }

            var grid = BuildGridFromMesh(settings, ctx);
            if (grid == null || grid.heightLayer == null || grid.maskDisk == null)
            {
                Debug.LogError($"{nameof(Step_CreateElevationZonesBlobs)}: failed to build working grid.");
                return;
            }

            var K = Mathf.Max(0, settings.zonesCount);
            if (K == 0)
            {
                // Still keep the derived slope consistent with the current height foundation.
                grid.ComputeSlopeFromHeight();
                ApplyGridHeightToMesh(settings, ctx, grid);
                return;
            }

            var radiusMin = Mathf.Max(1, Mathf.Min(settings.radiusMinCells, settings.radiusMaxCells));
            var radiusMax = Mathf.Max(radiusMin, settings.radiusMaxCells);

            var elevMin = Mathf.Min(settings.elevationMinMeters, settings.elevationMaxMeters);
            var elevMax = Mathf.Max(settings.elevationMinMeters, settings.elevationMaxMeters);

            var threshold = Mathf.Max(0f, settings.fieldThreshold);
            var falloffPower = Mathf.Max(0.01f, settings.falloffPower);

            // Per-blob parameters (deterministic, arrays only).
            var cx = new int[K];
            var cy = new int[K];
            var radiusCells = new int[K];
            var elevationMeters = new float[K];

            // Scatter centers inside maskDisk.
            for (int i = 0; i < K; i++)
            {
                ScatterCenterInsideDisk(ctx.Rng, grid, out cx[i], out cy[i]);
                radiusCells[i] = NextIntInclusive(ctx.Rng, radiusMin, radiusMax);
                elevationMeters[i] = NextFloat(ctx.Rng, elevMin, elevMax);
            }

            var field = new float[grid.width * grid.height];
            var inside = new bool[grid.width * grid.height];
            var zoneIdNorm = new float[grid.width * grid.height];
            var heightDelta = new float[grid.width * grid.height];
            var zoneId = new int[grid.width * grid.height];

            var invKMinus1 = (K > 1) ? (1f / (K - 1)) : 0f;

            // Compute blob field + thresholded mask + winner.
            for (int y = 0; y < grid.height; y++)
            {
                for (int x = 0; x < grid.width; x++)
                {
                    var idx = grid.Idx(x, y);
                    if (!grid.maskDisk[idx])
                    {
                        field[idx] = 0f;
                        inside[idx] = false;
                        zoneIdNorm[idx] = 0f;
                        heightDelta[idx] = 0f;
                        zoneId[idx] = -1;
                        continue;
                    }

                    float sumF = 0f;
                    float bestF = -1f;
                    int bestI = 0;

                    for (int i = 0; i < K; i++)
                    {
                        var dx = x - cx[i];
                        var dy = y - cy[i];
                        var dist = Mathf.Sqrt((dx * dx) + (dy * dy));
                        var r = Mathf.Max(1e-5f, radiusCells[i]);
                        var d = dist / r; // 0..1 at the blob radius

                        var Fi = ContributionPoly(d, falloffPower);
                        sumF += Fi;

                        if (Fi > bestF)
                        {
                            bestF = Fi;
                            bestI = i;
                        }
                    }

                    field[idx] = sumF;
                    var inZone = sumF >= threshold;
                    inside[idx] = inZone;

                    zoneIdNorm[idx] = (bestF > 0f && K > 1) ? (bestI * invKMinus1) : 0f;
                    zoneId[idx] = bestI;

                    // Apply hard elevation inside the zone mask (no smoothing to outside terrain here).
                    var delta = inZone ? elevationMeters[bestI] : 0f;
                    heightDelta[idx] = delta;
                    grid.heightLayer[idx] += delta;
                }
            }

            // Optional: Smooth ONLY internal edges between different zones, and ONLY inside the zone mask.
            // This preserves:
            // - a hard disk rim (we never smooth due to outside-of-disk neighbors)
            // - a hard zone-vs-non-zone edge (we never blend using non-zone cells)
            float[] internalBoundary01 = null;
            float[] distToInternalBoundary = null;
            float[] blendMask = null;
            float[] heightDeltaBlend = null;

            if (settings.zonesBlendEnabled)
            {
                var blendWidth = Mathf.Max(1, settings.zonesBlendWidthCells);
                var kernelRadius = Mathf.Max(1, settings.zonesBlendKernelRadius);
                var kernelR2 = kernelRadius * kernelRadius;

                // 1) Internal boundary mask: insideZone && neighbor insideZone with different zoneId (neighbor must be inside disk).
                var internalBoundary = new bool[grid.width * grid.height];
                internalBoundary01 = new float[grid.width * grid.height];

                for (int y = 0; y < grid.height; y++)
                {
                    for (int x = 0; x < grid.width; x++)
                    {
                        var idx = grid.Idx(x, y);
                        if (!inside[idx])
                        {
                            internalBoundary[idx] = false;
                            internalBoundary01[idx] = 0f;
                            continue;
                        }

                        var zid = zoneId[idx];
                        var isInternalBoundary =
                            HasDifferentZoneNeighborInsideZone(grid, inside, zoneId, x - 1, y, zid) ||
                            HasDifferentZoneNeighborInsideZone(grid, inside, zoneId, x + 1, y, zid) ||
                            HasDifferentZoneNeighborInsideZone(grid, inside, zoneId, x, y - 1, zid) ||
                            HasDifferentZoneNeighborInsideZone(grid, inside, zoneId, x, y + 1, zid);

                        internalBoundary[idx] = isInternalBoundary;
                        internalBoundary01[idx] = isInternalBoundary ? 1f : 0f;
                    }
                }

                // 2) distToInternalBoundary via multi-source BFS, traversing insideZone cells only.
                var distInt = new int[grid.width * grid.height];
                for (int i = 0; i < distInt.Length; i++) distInt[i] = int.MaxValue;

                var queue = new int[grid.width * grid.height];
                int head = 0, tail = 0;

                for (int i = 0; i < internalBoundary.Length; i++)
                {
                    if (!internalBoundary[i]) continue;
                    distInt[i] = 0;
                    queue[tail++] = i;
                }

                while (head < tail)
                {
                    var cur = queue[head++];
                    var curD = distInt[cur];
                    var cx2 = cur % grid.width;
                    var cy2 = cur / grid.width;

                    TryRelaxInsideZone(grid, inside, distInt, queue, ref tail, cx2 - 1, cy2, curD + 1);
                    TryRelaxInsideZone(grid, inside, distInt, queue, ref tail, cx2 + 1, cy2, curD + 1);
                    TryRelaxInsideZone(grid, inside, distInt, queue, ref tail, cx2, cy2 - 1, curD + 1);
                    TryRelaxInsideZone(grid, inside, distInt, queue, ref tail, cx2, cy2 + 1, curD + 1);
                }

                distToInternalBoundary = new float[grid.width * grid.height];
                blendMask = new float[grid.width * grid.height];
                heightDeltaBlend = new float[grid.width * grid.height];

                var heightBeforeBlend = (float[])grid.heightLayer.Clone();
                var heightAfter = (float[])grid.heightLayer.Clone();

                // 3/4) Blend only near internal boundaries: insideZone cells only, averaging only over insideZone neighbors.
                for (int y = 0; y < grid.height; y++)
                {
                    for (int x = 0; x < grid.width; x++)
                    {
                        var idx = grid.Idx(x, y);
                        if (!inside[idx])
                        {
                            distToInternalBoundary[idx] = 0f;
                            blendMask[idx] = 0f;
                            heightDeltaBlend[idx] = 0f;
                            continue;
                        }

                        var d = distInt[idx];
                        if (d == int.MaxValue) d = blendWidth + 999;
                        distToInternalBoundary[idx] = d;

                        if (d > blendWidth)
                        {
                            blendMask[idx] = 0f;
                            heightDeltaBlend[idx] = 0f;
                            continue;
                        }

                        // local weighted average in a disk neighborhood
                        float sumW = 0f;
                        float sumH = 0f;

                        // Gaussian-like weights based on squared distance in cells.
                        var sigma = kernelRadius * 0.5f + 0.5f;
                        var inv2Sigma2 = 1f / (2f * sigma * sigma);

                        for (int oy = -kernelRadius; oy <= kernelRadius; oy++)
                        {
                            var ny = y + oy;
                            if (ny < 0 || ny >= grid.height) continue;
                            for (int ox = -kernelRadius; ox <= kernelRadius; ox++)
                            {
                                var nx = x + ox;
                                if (nx < 0 || nx >= grid.width) continue;
                                var r2 = (ox * ox) + (oy * oy);
                                if (r2 > kernelR2) continue;

                                var ni = grid.Idx(nx, ny);
                                if (!grid.maskDisk[ni]) continue;
                                if (!inside[ni]) continue; // do NOT blend with non-zone cells

                                var w = Mathf.Exp(-r2 * inv2Sigma2);
                                sumW += w;
                                sumH += heightBeforeBlend[ni] * w;
                            }
                        }

                        var hs = (sumW > 1e-6f) ? (sumH / sumW) : heightBeforeBlend[idx];

                        // blend factor: strongest at boundary, fades to 0 at blend width
                        var t = Mathf.Clamp01(d / (float)blendWidth);
                        var a = 1f - Smoothstep01(t);
                        blendMask[idx] = a;

                        var h0 = heightBeforeBlend[idx];
                        var h1 = Mathf.Lerp(h0, hs, a);
                        heightAfter[idx] = h1;
                        heightDeltaBlend[idx] = h1 - h0;
                    }
                }

                grid.heightLayer = heightAfter;
            }

            // Derived.
            grid.ComputeSlopeFromHeight();

            // Push modified heights to mesh so later steps (including Step_BuildWorldGridData) see the changes.
            ApplyGridHeightToMesh(settings, ctx, grid);

            if (settings.exportDebugTextures)
            {
                ExportDebug(settings, grid, field, inside, zoneIdNorm, heightDelta, internalBoundary01, distToInternalBoundary, blendMask, heightDeltaBlend,
                    null, null);
            }
        }

        private static float ContributionPoly(float d, float falloffPower)
        {
            // Simple smooth polynomial falloff: Fi = max(0, 1 - d)^p, where d is normalized distance (0 at center, 1 at radius).
            var t = 1f - d;
            if (t <= 0f) return 0f;
            return Mathf.Pow(t, falloffPower);
        }

        private static float Smoothstep01(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        private static bool HasDifferentZoneNeighborInsideZone(WorldGridData grid, bool[] insideZone, int[] zoneId, int nx, int ny, int myZoneId)
        {
            if (!grid.InBounds(nx, ny)) return false;
            var ni = grid.Idx(nx, ny);
            if (!grid.maskDisk[ni]) return false;     // do NOT consider outside-of-disk as internal boundary
            if (!insideZone[ni]) return false;        // only smooth between zones (both sides insideZone)
            return zoneId[ni] != myZoneId;
        }

        private static void TryRelaxInsideZone(WorldGridData grid, bool[] insideZone, int[] dist, int[] q, ref int tail, int nx, int ny, int nd)
        {
            if (!grid.InBounds(nx, ny)) return;
            var ni = grid.Idx(nx, ny);
            if (!grid.maskDisk[ni]) return;
            if (!insideZone[ni]) return; // traverse insideZone only
            if (nd >= dist[ni]) return;
            dist[ni] = nd;
            q[tail++] = ni;
        }

        private static int NextIntInclusive(System.Random rng, int minInclusive, int maxInclusive)
        {
            if (maxInclusive < minInclusive)
            {
                var tmp = minInclusive;
                minInclusive = maxInclusive;
                maxInclusive = tmp;
            }

            // Random.Next upper bound is exclusive.
            return rng.Next(minInclusive, maxInclusive + 1);
        }

        private static float NextFloat(System.Random rng, float minInclusive, float maxInclusive)
        {
            if (maxInclusive < minInclusive)
            {
                var tmp = minInclusive;
                minInclusive = maxInclusive;
                maxInclusive = tmp;
            }

            var t = (float)rng.NextDouble(); // [0,1)
            return Mathf.Lerp(minInclusive, maxInclusive, t);
        }

        private static void ScatterCenterInsideDisk(System.Random rng, WorldGridData grid, out int x, out int y)
        {
            // Deterministic rejection sampling; bounded attempts + deterministic fallback.
            const int maxTries = 2048;
            for (int t = 0; t < maxTries; t++)
            {
                var rx = rng.Next(0, grid.width);
                var ry = rng.Next(0, grid.height);
                if (grid.maskDisk[grid.Idx(rx, ry)])
                {
                    x = rx;
                    y = ry;
                    return;
                }
            }

            // Fallback: center cell (or nearest in-bounds).
            x = Mathf.Clamp(grid.width / 2, 0, grid.width - 1);
            y = Mathf.Clamp(grid.height / 2, 0, grid.height - 1);
        }

        private static WorldGridData BuildGridFromMesh(WorldGenSettings settings, WorldContext ctx)
        {
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
                slope = new float[size * size]
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

            // Sample mesh vertices into the grid (robust to flat shading duplication).
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

            for (int i = 0; i < grid.heightLayer.Length; i++)
            {
                if (!grid.maskDisk[i])
                {
                    grid.heightLayer[i] = 0f;
                    continue;
                }

                if (count[i] > 0) grid.heightLayer[i] = sum[i] / count[i];
                else grid.heightLayer[i] = float.NaN;
            }

            FillMissingHeights(grid);
            return grid;
        }

        private static void FillMissingHeights(WorldGridData grid)
        {
            // Same approach as Step_BuildWorldGridData: cheap neighbor averaging to replace NaNs.
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

            for (int i = 0; i < grid.heightLayer.Length; i++)
            {
                if (!grid.maskDisk[i]) continue;
                if (float.IsNaN(grid.heightLayer[i])) grid.heightLayer[i] = 0f;
            }
        }

        private static void ApplyGridHeightToMesh(WorldGenSettings settings, WorldContext ctx, WorldGridData grid)
        {
            var mesh = ctx.TerrainMesh;
            var verts = mesh.vertices;

            var radius = grid.radiusWorld;
            var inv = 1f / grid.cellSizeWorld;

            for (int i = 0; i < verts.Length; i++)
            {
                var v = verts[i];
                var gx = Mathf.RoundToInt((v.x + radius) * inv);
                var gy = Mathf.RoundToInt((v.z + radius) * inv);
                if (!grid.InBounds(gx, gy)) continue;

                var gi = grid.Idx(gx, gy);
                if (!grid.maskDisk[gi]) continue;

                v.y = grid.heightLayer[gi];
                verts[i] = v;
            }

            mesh.vertices = verts;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            ctx.TerrainMesh = mesh;

            // Keep wireframe overlay in sync if present.
            if (ctx.TerrainGO != null)
            {
                var wf = ctx.TerrainGO.transform.Find("Wireframe");
                if (wf != null)
                {
                    var wfMf = wf.GetComponent<MeshFilter>();
                    var wfMesh = (wfMf != null) ? wfMf.sharedMesh : null;
                    if (wfMesh != null && wfMesh.vertexCount == mesh.vertexCount)
                    {
                        wfMesh.vertices = mesh.vertices;
                        wfMesh.RecalculateBounds();
                    }
                }
            }
        }

        private static void ExportDebug(
            WorldGenSettings settings,
            WorldGridData grid,
            float[] field,
            bool[] inside,
            float[] zoneIdNorm,
            float[] heightDelta,
            float[] internalBoundary01,
            float[] distToInternalBoundary,
            float[] blendMask,
            float[] heightDeltaBlend,
            float[] distWarped,
            float[] blendMaskWarped)
        {
            const string folder = "Assets/WorldGen/DebugOutputs";

            // Ensure the folder exists as requested (SavePng also does this, but keep it explicit).
            var folderAbs = Path.Combine(Application.dataPath, "WorldGen/DebugOutputs");
            Directory.CreateDirectory(folderAbs);

            // Build 0/1 mask layer.
            var inside01 = new float[inside.Length];
            for (int i = 0; i < inside.Length; i++)
                inside01[i] = inside[i] ? 1f : 0f;

            // Stats (computed on disk mask only).
            var fieldStats = WorldGenDebugUtil.ComputeStats(field, grid.maskDisk);
            var maskStats = WorldGenDebugUtil.ComputeStats(inside01, grid.maskDisk);
            var zoneStats = WorldGenDebugUtil.ComputeStats(zoneIdNorm, grid.maskDisk);
            var deltaStats = WorldGenDebugUtil.ComputeStats(heightDelta, grid.maskDisk);

            Debug.Log($"[Zones] Debug export folder: {folder}");
            Debug.Log($"[Zones] Field(min/max/mean)={fieldStats.min:0.000}/{fieldStats.max:0.000}/{fieldStats.mean:0.000} | " +
                      $"Mask(mean≈coverage)={maskStats.mean:0.000} | ZoneId(min/max/mean)={zoneStats.min:0.000}/{zoneStats.max:0.000}/{zoneStats.mean:0.000} | " +
                      $"HeightDelta(min/max/mean)={deltaStats.min:0.00}/{deltaStats.max:0.00}/{deltaStats.mean:0.00}");

            // Textures (use disk mask so outside is not drawn, consistent with existing debug outputs).
            var texField = WorldGenDebugUtil.BuildFloatLayerTexture(grid.width, grid.height, field, grid.maskDisk, settings.debugTextureSize, out _);
            var pathField = WorldGenDebugUtil.SavePng(texField, folder, "Step_Zones_Field");

            var texMask = WorldGenDebugUtil.BuildFloatLayerTexture(grid.width, grid.height, inside01, grid.maskDisk, settings.debugTextureSize, out _);
            var pathMask = WorldGenDebugUtil.SavePng(texMask, folder, "Step_Zones_Mask");

            var texZone = WorldGenDebugUtil.BuildFloatLayerTexture(grid.width, grid.height, zoneIdNorm, grid.maskDisk, settings.debugTextureSize, out _);
            var pathZone = WorldGenDebugUtil.SavePng(texZone, folder, "Step_Zones_ZoneId");

            var texDelta = WorldGenDebugUtil.BuildFloatLayerTexture(grid.width, grid.height, heightDelta, grid.maskDisk, settings.debugTextureSize, out _);
            var pathDelta = WorldGenDebugUtil.SavePng(texDelta, folder, "Step_Zones_HeightDelta");

            // New debug outputs for internal zone blending (optional; exported only when arrays provided).
            string pathInternal = null;
            string pathDistInternal = null;
            string pathBlend = null;
            string pathDeltaBlend = null;

            if (internalBoundary01 != null)
            {
                var texInternal = WorldGenDebugUtil.BuildFloatLayerTexture(grid.width, grid.height, internalBoundary01, grid.maskDisk, settings.debugTextureSize, out _);
                pathInternal = WorldGenDebugUtil.SavePng(texInternal, folder, "Step_Zones_InternalBoundary");
            }

            if (distToInternalBoundary != null)
            {
                var texDistInternal = WorldGenDebugUtil.BuildFloatLayerTexture(grid.width, grid.height, distToInternalBoundary, grid.maskDisk, settings.debugTextureSize, out _);
                pathDistInternal = WorldGenDebugUtil.SavePng(texDistInternal, folder, "Step_Zones_DistToBoundary");
            }

            if (blendMask != null)
            {
                var texBlend = WorldGenDebugUtil.BuildFloatLayerTexture(grid.width, grid.height, blendMask, grid.maskDisk, settings.debugTextureSize, out _);
                pathBlend = WorldGenDebugUtil.SavePng(texBlend, folder, "Step_Zones_BlendMask");
            }

            if (heightDeltaBlend != null)
            {
                var texDeltaBlend = WorldGenDebugUtil.BuildFloatLayerTexture(grid.width, grid.height, heightDeltaBlend, grid.maskDisk, settings.debugTextureSize, out _);
                pathDeltaBlend = WorldGenDebugUtil.SavePng(texDeltaBlend, folder, "Step_Zones_HeightDelta_Blend");
            }

            Debug.Log($"[Zones] Exported PNGs: {pathField}, {pathMask}, {pathZone}, {pathDelta}, {pathInternal}, {pathDistInternal}, {pathBlend}, {pathDeltaBlend}");

#if UNITY_EDITOR
            AssetDatabase.Refresh();
#endif
        }
    }
}


