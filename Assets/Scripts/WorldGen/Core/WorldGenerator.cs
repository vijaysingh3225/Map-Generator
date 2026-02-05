using System.Collections.Generic;
using UnityEngine;

namespace WorldGen.Core
{
    public sealed class WorldGenerator : MonoBehaviour
    {
        [SerializeField] private WorldGenSettings settings;
        [SerializeField] private List<MonoBehaviour> stepBehaviours = new();
        // Each entry must implement IGenerationStep

        public void Generate()
        {
            if (settings == null)
            {
                Debug.LogError("WorldGenSettings not assigned.");
                return;
            }

            if (settings.clearPrevious)
            {
                var existing = GameObject.Find(settings.worldRootName);
                if (existing != null) DestroyImmediate(existing);
            }

            var ctx = new WorldContext
            {
                WorldRoot = new GameObject(settings.worldRootName)
            };

            foreach (var mb in stepBehaviours)
            {
                if (mb == null) continue;

                if (mb is IGenerationStep step)
                {
                    step.Generate(settings, ctx);
                }
                else
                {
                    Debug.LogWarning($"{mb.name} does not implement IGenerationStep.");
                }
            }
        }
    }
}
