using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Controller.Audio.Editor
{
    [CustomEditor(typeof(AudioController))]
    public sealed class AudioControllerEditor : UnityEditor.Editor
    {
        private bool advanced;
        private bool channelControls;
        private double nextCheck;
        private IReadOnlyList<AudioSetupIssue> issues;
        private void OnEnable()
        {
            RequestChecks();
            EditorApplication.update += RefreshChecks;
        }
        private void OnDisable() => EditorApplication.update -= RefreshChecks;
        private void RequestChecks()
        {
            issues = null;
            nextCheck = 0;
        }
        private void RefreshChecks()
        {
            if (target == null || EditorApplication.timeSinceStartup < nextCheck) return;
            issues = AudioSetupDiagnostics.GetIssues((AudioController)target);
            nextCheck = EditorApplication.timeSinceStartup + .5;
            Repaint();
        }
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.PropertyField(serializedObject.FindProperty("m_Script"));
            EditorGUILayout.HelpBox("Use AudioService.PlayBgm / PlaySfx / PlayVoice. Every play returns a handle; keep it only when you need to control that sound. No preload or prewarm is required for basic playback.", MessageType.Info);
            DrawSettings();
            if (serializedObject.ApplyModifiedProperties())
                RequestChecks();
            var controller = (AudioController)target;
            DrawChecks(controller);
            if (Application.isPlaying)
                DrawPlayModeState(controller);
        }
        // Setup fields lock in Play Mode; the logging switches stay editable.
        private void DrawSettings()
        {
            using (new EditorGUI.DisabledScope(Application.isPlaying))
            {
                Field("clipProviderSource");
                Field("settingsSource");
                Field("mixer");
                Field("catalog");
            }
            Field("logPlaybackFailures");
            advanced = EditorGUILayout.Foldout(advanced, "Advanced settings", true);
            if (!advanced)
                return;
            using (new EditorGUI.DisabledScope(Application.isPlaying))
            {
                Field("loadDefaultMixer");
                Field("maxConcurrentSounds");
                Field("concurrencyPolicy");
                Field("pauseOnBackground");
            }
            Field("verboseLogging");
            EditorGUILayout.HelpBox("Max Concurrent Sounds includes BGM, SFX and Voice. 0 means no service limit.", MessageType.Info);
        }
        private void DrawPlayModeState(AudioController controller)
        {
            EditorGUILayout.HelpBox("Stop Play Mode to change setup here. Runtime configuration changes should use AudioController properties/methods so caches and routing refresh together.", MessageType.Info);
            var state = controller.Diagnostics;
            EditorGUILayout.LabelField("Service", controller.Ready ? "Ready" : "Not ready");
            EditorGUILayout.LabelField("Playback", $"{state.Playing} playing / {state.Paused} paused / {state.Loading} loading");
            if (!string.IsNullOrEmpty(state.LastFailure))
                EditorGUILayout.HelpBox("Last recorded failure (may be historical): " + state.LastFailure, MessageType.Warning);
            channelControls = EditorGUILayout.Foldout(channelControls, "Channel controls (read only)", true);
            if (!channelControls || !controller.Ready)
                return;
            foreach (var channel in AudioMixerLayout.Channels)
            {
                var output = controller.GetChannelDiagnostics(channel);
                EditorGUILayout.LabelField(channel.ToString(), $"Volume {output.Volume:P0} / Fade {output.FadeGain:P0} / {(output.Muted ? "Muted" : "Unmuted")}");
            }
        }
        private void DrawChecks(AudioController controller)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Audio checks", EditorStyles.boldLabel);
            if (GUILayout.Button("Refresh audio checks"))
                RequestChecks();
            if (issues == null) RefreshChecks();
            if (issues != null)
            {
                foreach (var issue in issues)
                {
                    EditorGUILayout.HelpBox(issue.Message, issue.Severity);
                    if (issue.Context != null && issue.Context != controller && GUILayout.Button("Select " + issue.Context.name))
                    {
                        Selection.activeObject = issue.Context;
                        EditorGUIUtility.PingObject(issue.Context);
                    }
                }
                if (issues.Count == 0)
                    EditorGUILayout.HelpBox("No issues found in the checked scene setup and service controls.", MessageType.Info);
            }
            EditorGUILayout.HelpBox("Checks refresh twice a second while this Inspector is open. They do not test clip content, mixer effects, speaker output or operating-system volume. Ready means the service is initialized.", MessageType.None);
        }
        private void Field(string name) => EditorGUILayout.PropertyField(serializedObject.FindProperty(name));
    }
}
