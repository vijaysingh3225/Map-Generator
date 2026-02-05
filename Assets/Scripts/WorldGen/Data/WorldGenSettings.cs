using UnityEngine;

namespace WorldGen.Core
{
    [CreateAssetMenu(menuName = "WorldGen/WorldGen Settings")]
    public sealed class WorldGenSettings : ScriptableObject
    {
        [Header("Terrain Disk")]
        [Min(10f)] public float radius = 200f;

        [Header("Terrain Grid (Uniform Polygons)")]
        [Min(0.1f)] public float cellSizeWorld = 2f;
        public bool clipToCircle = true;

        [Header("Shading")]
        public bool useFlatShading = true;

        [Header("Materials")]
        public Material terrainMaterial;

        [Header("Generation")]
        public bool clearPrevious = true;
        public string worldRootName = "GeneratedWorld";
    }
}
