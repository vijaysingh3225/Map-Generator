using UnityEngine;

namespace WorldGen.Core
{
    [CreateAssetMenu(menuName = "WorldGen/WorldGen Settings")]
    public sealed class WorldGenSettings : ScriptableObject
    {
        [Header("Seed")]
        public int seed = 12345;

        [Header("Terrain Disk")]
        [Min(10f)] public float radius = 200f;
        [Range(3, 512)] public int radialSegments = 128;
        [Range(3, 512)] public int ringSegments = 128;

        [Header("Materials")]
        public Material terrainMaterial;

        [Header("Height Noise (FBM)")]
        public float heightAmplitude = 12f;
        public float baseFrequency = 0.01f;
        public int octaves = 5;
        public float persistence = 0.5f;
        public float lacunarity = 2.0f;
        public Vector2 noiseOffset = Vector2.zero;

        [Header("Generation")]
        public bool clearPrevious = true;
        public string worldRootName = "GeneratedWorld";
    }
}
