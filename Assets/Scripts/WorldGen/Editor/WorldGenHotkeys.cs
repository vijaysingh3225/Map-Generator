#if UNITY_EDITOR
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using WorldGen.Core;

namespace WorldGen.Editor
{
    public static class WorldGenHotkeys
    {
        // % = Ctrl (Windows) / Cmd (Mac), & = Alt, so this becomes:
        // Windows: Ctrl+Alt+G
        // Mac: Cmd+Alt+G
        [MenuItem("Tools/WorldGen/Generate (Auto) %&g")]
        public static void GenerateAuto()
        {
            var generator = FindFirstWorldGeneratorInLoadedScenes();
            if (generator == null)
            {
                Debug.LogError("WorldGen: No WorldGenerator found in any loaded scene. Add a WorldGenerator component to a scene GameObject.");
                return;
            }

            generator.Generate();
            EditorSceneManager.MarkSceneDirty(generator.gameObject.scene);
            Selection.activeObject = generator.gameObject;
            EditorGUIUtility.PingObject(generator.gameObject);
        }

        private static WorldGenerator FindFirstWorldGeneratorInLoadedScenes()
        {
            // In the Editor, FindObjectsOfTypeAll can see objects across loaded scenes (including inactive).
            // Filter out assets/prefabs by requiring a valid scene and no HideFlags.
            var all = Resources.FindObjectsOfTypeAll<WorldGenerator>();
            return all.FirstOrDefault(g =>
                g != null &&
                g.gameObject != null &&
                g.gameObject.hideFlags == HideFlags.None &&
                g.gameObject.scene.IsValid() &&
                g.gameObject.scene.isLoaded);
        }
    }
}
#endif


