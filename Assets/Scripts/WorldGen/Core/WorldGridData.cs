using System.Collections.Generic;
using UnityEngine;

namespace WorldGen.Core
{
    public sealed class WorldGridData
    {
        public int width;
        public int height;
        public float cellSizeWorld;
        public float radiusWorld;
        public Vector3 originWorld; // center

        // Masks / layers (size = width * height)
        public bool[] maskDisk;
        public float[] heightLayer;
        public float[] slope;

        // Optional placeholders for later steps
        public Vector2[] flowDir;
        public float[] flowAccum;
        public float[] moisture;
        public int[] biomeId;

        public int Idx(int x, int y) => (y * width) + x;
        public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < width && y < height;

        public Vector3 GridToWorld(int x, int y)
        {
            // Grid covers [-radius..+radius] in XZ.
            var localX = (-radiusWorld) + (x * cellSizeWorld);
            var localZ = (-radiusWorld) + (y * cellSizeWorld);
            return originWorld + new Vector3(localX, 0f, localZ);
        }

        public void WorldToGrid(Vector3 world, out int x, out int y)
        {
            var local = world - originWorld;
            x = Mathf.RoundToInt((local.x + radiusWorld) / cellSizeWorld);
            y = Mathf.RoundToInt((local.z + radiusWorld) / cellSizeWorld);
        }

        public IEnumerable<int> Neighbors8(int idx)
        {
            var x = idx % width;
            var y = idx / width;

            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0) continue;
                    var nx = x + dx;
                    var ny = y + dy;
                    if (!InBounds(nx, ny)) continue;
                    yield return Idx(nx, ny);
                }
            }
        }

        public void ComputeSlopeFromHeight()
        {
            if (heightLayer == null || slope == null || maskDisk == null) return;
            if (heightLayer.Length != width * height) return;
            if (slope.Length != width * height) return;

            var inv2dx = 1f / (2f * cellSizeWorld);
            var invdx = 1f / cellSizeWorld;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    var i = Idx(x, y);
                    if (!maskDisk[i])
                    {
                        slope[i] = 0f;
                        continue;
                    }

                    var hC = heightLayer[i];

                    // X derivative
                    float dhdx = 0f;
                    var hasL = (x > 0) && maskDisk[Idx(x - 1, y)];
                    var hasR = (x < width - 1) && maskDisk[Idx(x + 1, y)];
                    if (hasL && hasR) dhdx = (heightLayer[Idx(x + 1, y)] - heightLayer[Idx(x - 1, y)]) * inv2dx;
                    else if (hasR) dhdx = (heightLayer[Idx(x + 1, y)] - hC) * invdx;
                    else if (hasL) dhdx = (hC - heightLayer[Idx(x - 1, y)]) * invdx;

                    // Z derivative
                    float dhdz = 0f;
                    var hasD = (y > 0) && maskDisk[Idx(x, y - 1)];
                    var hasU = (y < height - 1) && maskDisk[Idx(x, y + 1)];
                    if (hasD && hasU) dhdz = (heightLayer[Idx(x, y + 1)] - heightLayer[Idx(x, y - 1)]) * inv2dx;
                    else if (hasU) dhdz = (heightLayer[Idx(x, y + 1)] - hC) * invdx;
                    else if (hasD) dhdz = (hC - heightLayer[Idx(x, y - 1)]) * invdx;

                    // slopeAngle = atan(|grad|) in degrees
                    var grad = Mathf.Sqrt(dhdx * dhdx + dhdz * dhdz);
                    slope[i] = Mathf.Atan(grad) * Mathf.Rad2Deg;
                }
            }
        }
    }
}



