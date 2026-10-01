using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Controller.Audio.Editor
{
    [CustomEditor(typeof(AudioCatalog))]
    public sealed class AudioCatalogEditor : UnityEditor.Editor
    {
        private IReadOnlyList<string> issues;
        private void OnEnable() { ValidateData(); Undo.undoRedoPerformed += OnUndoRedo; }
        private void OnDisable() => Undo.undoRedoPerformed -= OnUndoRedo;
        private void OnUndoRedo() { ValidateData(); Repaint(); }
        private void ValidateData() => issues = ((AudioCatalog)target).GetValidationIssues();

        public override void OnInspectorGUI()
        {
            if (DrawDefaultInspector() || issues == null) ValidateData();
            if (GUILayout.Button("Validate Catalog")) ValidateData();
            foreach (var issue in issues) EditorGUILayout.HelpBox(issue, MessageType.Warning);
            if (issues.Count == 0) EditorGUILayout.HelpBox("Catalog keys are valid. Asset availability must be verified with the configured providers.", MessageType.Info);
            if (Application.isPlaying)
                EditorGUILayout.HelpBox("Catalog changes refresh pending requests and cached lookups automatically. Existing playback keeps its current clip.", MessageType.Info);
        }
    }
}
