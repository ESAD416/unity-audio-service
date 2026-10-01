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
        private void OnEnable() { issues = null; nextCheck = 0; EditorApplication.update += RefreshChecks; }
        private void OnDisable() => EditorApplication.update -= RefreshChecks;
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
            using (new EditorGUI.DisabledScope(Application.isPlaying))
            { Field("clipProviderSource"); Field("settingsSource"); Field("mixer"); Field("catalog"); }
            Field("logPlaybackFailures");
            advanced = EditorGUILayout.Foldout(advanced, "Advanced settings", true);
            if (advanced)
            {
                using (new EditorGUI.DisabledScope(Application.isPlaying))
                {
                    Field("loadDefaultMixer");
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("maxVoices"), new GUIContent("Max Concurrent Sounds",
                        "Total BGM, SFX and Voice requests, including loading and paused sounds. 0 means no service limit."));
                    Field("concurrencyPolicy"); Field("pauseOnBackground");
                }
                Field("verboseLogging");
                EditorGUILayout.HelpBox("Max Concurrent Sounds includes BGM, SFX and Voice. 0 means no service limit.", MessageType.Info);
            }
            if (serializedObject.ApplyModifiedProperties()) { issues = null; nextCheck = 0; }
            var controller = (AudioController)target;
            DrawChecks(controller);
            if (!Application.isPlaying) return;
            EditorGUILayout.HelpBox("Stop Play Mode to change setup here. Runtime configuration changes should use AudioController properties/methods so caches and routing refresh together.", MessageType.Info);
            var state = controller.Diagnostics;
            EditorGUILayout.LabelField("Service", controller.Ready ? "Ready" : "Not ready");
            EditorGUILayout.LabelField("Playback", $"{state.Playing} playing / {state.Paused} paused / {state.Loading} loading");
            if (!string.IsNullOrEmpty(state.LastFailure))
                EditorGUILayout.HelpBox("Last recorded failure (may be historical): " + state.LastFailure, MessageType.Warning);
            channelControls = EditorGUILayout.Foldout(channelControls, "Channel controls (read only)", true);
            if (channelControls && controller.Ready)
                for (int i = 0; i < 4; i++)
                {
                    var channel = (AudioChannel)i;
                    var output = controller.GetChannelDiagnostics(channel);
                    EditorGUILayout.LabelField(channel.ToString(), $"Volume {output.Volume:P0} / Fade {output.FadeGain:P0} / {(output.Muted ? "Muted" : "Unmuted")}");
                }
        }
        private void DrawChecks(AudioController controller)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Audio checks", EditorStyles.boldLabel);
            if (GUILayout.Button("Refresh audio checks")) { nextCheck = 0; issues = null; }
            if (issues == null) RefreshChecks();
            if (issues != null)
            {
                foreach (var issue in issues)
                {
                    EditorGUILayout.HelpBox(issue.Message, issue.Severity);
                    if (issue.Context != null && issue.Context != controller && GUILayout.Button("Select " + issue.Context.name))
                    { Selection.activeObject = issue.Context; EditorGUIUtility.PingObject(issue.Context); }
                }
                if (issues.Count == 0)
                    EditorGUILayout.HelpBox("No issues found in the checked scene setup and service controls.", MessageType.Info);
            }
            EditorGUILayout.HelpBox("Checks refresh twice a second while this Inspector is open. They do not test clip content, mixer effects, speaker output or operating-system volume. Ready means the service is initialized.", MessageType.None);
        }
        private void Field(string name) => EditorGUILayout.PropertyField(serializedObject.FindProperty(name));
    }
}
