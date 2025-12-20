#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using WorldGen.Core;

namespace WorldGen.Editor
{
    public sealed class WorldGenWindow : EditorWindow
    {
        [MenuItem("Tools/WorldGen/Generator")]
        public static void ShowWindow() => GetWindow<WorldGenWindow>("WorldGen");

        private WorldGenerator generator;

        private void OnGUI()
        {
            generator = (WorldGenerator)EditorGUILayout.ObjectField("Generator", generator, typeof(WorldGenerator), true);

            using (new EditorGUI.DisabledScope(generator == null))
            {
                if (GUILayout.Button("Generate World"))
                {
                    generator.Generate();
                }
            }
        }
    }
}
#endif
