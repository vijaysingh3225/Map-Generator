using UnityEngine;

namespace WorldGen.Core
{
    public enum DebugLayer
    {
        Height,
        Slope,
        MaskDisk,
        FlowAccum,
        Moisture,
        Biome
    }

    [CreateAssetMenu(menuName = "WorldGen/WorldGen Settings")]
    public sealed class WorldGenSettings : ScriptableObject
    {
        [Header("Seed")]
        public int seed = 12345;

        [Header("Terrain Disk")]
        [Min(10f)] public float radius = 200f;
        // Legacy polar settings (kept, but NOT used by current mesh generation):
        [Range(3, 512)] public int radialSegments = 128;
        [Range(3, 512)] public int ringSegments = 128;

        [Header("Terrain Grid (Uniform Polygons)")]
        [Min(0.1f)] public float cellSizeWorld = 2f;
        public bool clipToCircle = true;

        [Header("Polygon Visualization")]
        public bool useFlatShading = true;
        public bool showWireframe = false;
        public Material wireframeMaterial;

        [Header("Materials")]
        public Material terrainMaterial;

        [Header("Height Noise (FBM)")]
        public float heightAmplitude = 12f;
        public float baseFrequency = 0.01f;
        public int octaves = 5;
        public float persistence = 0.5f;
        public float lacunarity = 2.0f;
        public Vector2 noiseOffset = Vector2.zero;

        [Header("Debug Settings")]
        public bool enableDebugLogs = true;
        public bool exportDebugTextures = false;
        public int debugTextureSize = 512;
        public DebugLayer debugLayer = DebugLayer.Height;

        [Header("Generation")]
        public bool clearPrevious = true;
        public string worldRootName = "GeneratedWorld";
    }
}
