using UnityEditor;
using UnityEngine;

namespace Controller.Audio.Editor
{
    [CustomEditor(typeof(AudioBootstrap))]
    public sealed class AudioBootstrapEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var provider = serializedObject.FindProperty("clipProviderSource").objectReferenceValue;
            var settings = serializedObject.FindProperty("settingsHandlerSource").objectReferenceValue;
            if (provider == null)
                EditorGUILayout.HelpBox("No clip provider is connected here. Direct AudioClip playback still works; ID playback needs a provider registered here or by your code. The default AudioCtrl prefab is already connected.", MessageType.Info);
            else if (!(provider is IAudioClipProvider))
                EditorGUILayout.HelpBox("Clip Provider Source must implement IAudioClipProvider. Assign ResourcesAudioClipProvider, FallbackAudioClipProvider, or a compatible custom provider.", MessageType.Error);
            if (settings != null && !(settings is IAudioSettingsHandler))
                EditorGUILayout.HelpBox("Settings Handler Source must implement IAudioSettingsHandler. Assign AudioSettingsPlayerPrefs to save player volume.", MessageType.Error);
        }
    }
}
