using System.Collections.Generic;
using UnityEngine;
using WorldGen.Steps;

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
                Seed = settings.seed,
                Rng = new System.Random(settings.seed),
                WorldRoot = new GameObject(settings.worldRootName)
            };

            // If Step_BuildWorldGridData is present anywhere in the list, enforce that it runs immediately after
            // Step_ApplyHeightFbm (so the canonical grid matches the final displaced mesh).
            Step_BuildWorldGridData gridStep = null;
            for (int i = 0; i < stepBehaviours.Count; i++)
            {
                if (stepBehaviours[i] is Step_BuildWorldGridData s)
                {
                    gridStep = s;
                    break;
                }
            }
            var ranGridStep = false;

            foreach (var mb in stepBehaviours)
            {
                if (mb == null) continue;

                if (mb is IGenerationStep step)
                {
                    // Skip the grid step here; we run it in the enforced spot.
                    if (!ranGridStep && step is Step_BuildWorldGridData)
                        continue;

                    step.Generate(settings, ctx);

                    if (!ranGridStep && gridStep != null && step is Step_ApplyHeightFbm)
                    {
                        gridStep.Generate(settings, ctx);
                        ranGridStep = true;
                    }
                }
                else
                {
                    Debug.LogWarning($"{mb.name} does not implement IGenerationStep.");
                }
            }

            // If no ApplyHeightFbm exists but the grid step does, run it at the end (still better than not running).
            if (!ranGridStep && gridStep != null)
            {
                gridStep.Generate(settings, ctx);
            }
        }
    }
}
