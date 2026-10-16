using UnityEditor;
using UnityEngine;

namespace Esad.Audio.Editor
{
    [CustomEditor(typeof(AudioSettingsPlayerPrefs)), CanEditMultipleObjects]
    public sealed class AudioSettingsPlayerPrefsEditor : UnityEditor.Editor
    {
        private bool advanced;
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.PropertyField(serializedObject.FindProperty("m_Script"));
            var autoSave = serializedObject.FindProperty("autoSave");
            EditorGUILayout.PropertyField(autoSave);
            if (autoSave.boolValue || autoSave.hasMultipleDifferentValues)
                EditorGUILayout.PropertyField(serializedObject.FindProperty("saveDelaySeconds"),
                    new GUIContent("Save Delay (seconds)", "Wait this long after the last volume change before saving. Zero saves on the next update."));
            EditorGUILayout.HelpBox("Volume changes take effect immediately. Disabling this component or pausing the app saves pending changes even with Auto Save off. Code can also call Flush().", MessageType.Info);
            using (new EditorGUI.DisabledScope(Application.isPlaying))
            {
                EditorGUILayout.LabelField("Default volumes", EditorStyles.boldLabel);
                Field("defaultMaster");
                Field("defaultBgm");
                Field("defaultSfx");
                Field("defaultVoice");
                advanced = EditorGUILayout.Foldout(advanced, "Storage keys", true);
                if (advanced)
                {
                    Field("masterKey");
                    Field("bgmKey");
                    Field("sfxKey");
                    Field("voiceKey");
                }
            }
            EditorGUILayout.HelpBox("Defaults apply when no saved value exists. Change defaults and storage keys outside Play Mode; changing a key does not migrate old saved values.", MessageType.Info);
            serializedObject.ApplyModifiedProperties();
        }
        private void Field(string name) => EditorGUILayout.PropertyField(serializedObject.FindProperty(name));
    }
}
