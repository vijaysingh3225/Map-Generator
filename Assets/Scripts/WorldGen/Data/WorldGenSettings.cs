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

        [Header("Elevation Zones (Metaballs / Blobs)")]
        public bool zonesEnabled = true;
        [Min(0)] public int zonesCount = 5;
        [Min(1)] public int radiusMinCells = 20;
        [Min(1)] public int radiusMaxCells = 60;
        [Min(1)] public int zonesSlopeWidthCells = 24;
        public bool zonesBlendEnabled = true;
        [Min(1)] public int zonesBlendWidthCells = 12;
        [Min(1)] public int zonesBlendKernelRadius = 3;
        public float elevationMinMeters = 8f;
        public float elevationMaxMeters = 45f;
        [Tooltip("Threshold on the summed blob field F = sum(Fi). Higher -> fewer/smaller zones.")]
        public float fieldThreshold = 0.9f;
        [Tooltip("Polynomial falloff power for Fi = max(0, 1 - d)^power where d=distance/radius. Higher -> harder-ish edges.")]
        public float falloffPower = 3.0f;

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
