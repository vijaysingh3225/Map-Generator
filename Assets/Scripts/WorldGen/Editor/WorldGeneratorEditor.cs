#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using WorldGen.Core;

namespace WorldGen.Editor
{
    [CustomEditor(typeof(WorldGenerator))]
    public sealed class WorldGeneratorEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            EditorGUILayout.Space(8);

            var generator = (WorldGenerator)target;
            using (new EditorGUI.DisabledScope(generator == null))
            {
                if (GUILayout.Button("Generate World"))
                {
                    Generate(generator);
                }
            }
        }

        private static void Generate(WorldGenerator generator)
        {
            if (generator == null) return;

            Undo.IncrementCurrentGroup();
            var group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("WorldGen: Generate World");

            // We can't predict what objects will be created/destroyed by Generate(),
            // but we can at least make the scene dirty so the user can save.
            generator.Generate();

            EditorSceneManager.MarkSceneDirty(generator.gameObject.scene);
            Undo.CollapseUndoOperations(group);
        }
    }
}
#endif


