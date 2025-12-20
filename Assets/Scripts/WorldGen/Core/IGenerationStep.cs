using UnityEngine;

namespace WorldGen.Core
{
    public interface IGenerationStep
    {
        string Name { get; }
        void Generate(WorldGenSettings settings, WorldContext ctx);
    }
}