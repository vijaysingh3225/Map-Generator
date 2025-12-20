using UnityEngine;

namespace WorldGen.Core
{
    public sealed class WorldContext
    {
        public int Seed;
        public System.Random Rng;

        public GameObject WorldRoot;

        // Outputs
        public GameObject TerrainGO;
        public Mesh TerrainMesh;
    }
}
