using UnityEngine;
using WorldGen.Core;

namespace WorldGen.Steps
{
    /// <summary>
    /// Deforms the existing terrain mesh by applying deterministic FBM (fractal Perlin) height noise over (x,z).
    /// Requires ctx.TerrainMesh to be created by a prior terrain step (e.g., Step_FlatDiskTerrain).
    /// </summary>
    public sealed class Step_ApplyHeightFbm : MonoBehaviour, IGenerationStep
    {
        public string Name => "Apply Height (FBM)";

        public void Generate(WorldGenSettings settings, WorldContext ctx)
        {
            if (ctx == null)
            {
                Debug.LogError($"{nameof(Step_ApplyHeightFbm)}: ctx is null.");
                return;
            }

            if (ctx.TerrainMesh == null)
            {
                Debug.LogError($"{nameof(Step_ApplyHeightFbm)}: ctx.TerrainMesh is null. Ensure a terrain mesh step runs first.");
                return;
            }

            if (settings == null)
            {
                Debug.LogError($"{nameof(Step_ApplyHeightFbm)}: settings is null.");
                return;
            }

            var mesh = ctx.TerrainMesh;
            var verts = mesh.vertices;

            // Seeded offset so the same seed always generates the same heights.
            // We combine this with the user-provided noiseOffset for controllable shifting.
            var seeded = CreateSeedOffset(settings.seed);
            var offset = settings.noiseOffset + seeded;

            var baseFreq = Mathf.Max(0.000001f, settings.baseFrequency);
            var octaves = Mathf.Clamp(settings.octaves, 1, 12);
            var persistence = Mathf.Clamp01(settings.persistence);
            var lacunarity = Mathf.Max(1.0f, settings.lacunarity);
            var heightAmp = settings.heightAmplitude;

            for (int i = 0; i < verts.Length; i++)
            {
                var v = verts[i];
                var h = FbmSigned(v.x + offset.x, v.z + offset.y, baseFreq, octaves, persistence, lacunarity);
                v.y = h * heightAmp;
                verts[i] = v;
            }

            mesh.vertices = verts;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            // Same instance is fine; set back for clarity.
            ctx.TerrainMesh = mesh;

            // If wireframe overlay exists, keep it in sync with the terrain vertex positions.
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

        private static Vector2 CreateSeedOffset(int seed)
        {
            // Large deterministic offset to avoid obvious symmetry / repeating patterns across seeds.
            // System.Random is deterministic across platforms for a given seed in .NET/Mono.
            var rng = new System.Random(seed);
            var ox = (float)(rng.NextDouble() * 20000.0 - 10000.0);
            var oy = (float)(rng.NextDouble() * 20000.0 - 10000.0);
            return new Vector2(ox, oy);
        }

        private static float FbmSigned(float x, float z, float baseFrequency, int octaves, float persistence, float lacunarity)
        {
            float sum = 0f;
            float amp = 1f;
            float freq = baseFrequency;

            for (int o = 0; o < octaves; o++)
            {
                // Mathf.PerlinNoise returns [0,1]. Remap to [-1,1] to allow hills + valleys around 0.
                var n = Mathf.PerlinNoise(x * freq, z * freq) * 2f - 1f;
                sum += n * amp;

                amp *= persistence;
                freq *= lacunarity;
            }

            return sum;
        }
    }
}


