using System;
using System.Collections.Generic;
using UnityEngine;

namespace Controller.Audio
{
    internal sealed class AudioPlaybackEngine
    {
        private sealed class Tween
        {
            public float Start, Target, Duration, Elapsed;
            public Action Completed;
            public float Advance(float delta) { Elapsed += delta; return Mathf.Lerp(Start, Target, Duration <= 0 ? 1 : Mathf.Clamp01(Elapsed / Duration)); }
            public bool Done => Elapsed >= Duration;
        }
        private sealed class Emitter
        {
            public AudioSource Source;
            public bool Reserved, InPool;
            public int Bank = -1;
            public float Envelope = 1f;
            public Tween Transition;
            public Playback Current, Pending;
        }
        private sealed class Playback
        {
            public AudioHandle Handle;
            public PlayOptions Options;
            public AudioClipLease Lease;
            public Action CancelLoad;
            public Emitter Emitter;
            public int Bank;
            public int StartedFrame;
        }
        private readonly AudioCallbackQueue callbacks = new();
        internal void NotifyCompleted(Action callback) => callbacks.Enqueue(callback);
        private readonly AudioController host;
        private readonly List<Playback> active = new();
        private readonly List<Emitter> emitters = new();
        private readonly Stack<Emitter> pool = new();
        private readonly Emitter[] reserved = new Emitter[5];
        private readonly float[] bankGains = { 1, 1, 1, 1, 1 };
        private readonly Tween[] bankTweens = new Tween[5];
        private readonly float[] busGains = { 1, 1, 1, 1 };
        private readonly Tween[] busTweens = new Tween[4];
        private readonly bool[] muted = new bool[4];
        private long nextId;
        private bool disposed, gamePaused, backgroundPaused;
        private AudioClipStore store;
        private IAudioClipProvider provider;
        public string LastFailure { get; private set; }
        public int MaxVoices { get; set; }
        public AudioConcurrencyPolicy ConcurrencyPolicy { get; set; }

        public AudioPlaybackEngine(AudioController host, AudioSource[] sources, IAudioClipProvider provider)
        {
            this.host = host;
            for (int i = 0; i < sources.Length; i++)
            { reserved[i] = new Emitter { Source = sources[i], Bank = i, Reserved = true }; emitters.Add(reserved[i]); }
            SetProvider(provider);
        }
        public void SetProvider(IAudioClipProvider next, bool force = false)
        {
            using var mutation = callbacks.Begin();
            if (!force && ReferenceEquals(provider, next) && store != null) return;
            if (disposed) return;
            // Publish the new generation before cancellation callbacks can request work.
            var previousStore = store;
            var previousRequests = active.ToArray();
            provider = next; store = new AudioClipStore(host, provider);
            foreach (var playback in previousRequests) if (playback.Handle.State == AudioPlaybackState.Loading) Finish(playback, AudioCompletion.Cancelled);
            previousStore?.Dispose();
        }
        private Playback Find(AudioHandle handle) => active.Find(p => ReferenceEquals(p.Handle, handle));
        private Emitter Rent(int bank)
        {
            if (bank >= 0 && (bank != 1 || (reserved[bank].Current == null && reserved[bank].Pending == null))) return reserved[bank];
            Emitter emitter;
            if (pool.Count > 0) { emitter = pool.Pop(); emitter.InPool = false; }
            else
            {
                var go = new GameObject("AudioEmitter"); go.transform.SetParent(host.transform, false);
                emitter = new Emitter { Source = go.AddComponent<AudioSource>() }; emitters.Add(emitter);
            }
            emitter.Bank = bank;
            return emitter;
        }
        private void Recycle(Emitter emitter)
        {
            if (emitter == null || emitter.Current != null || emitter.Pending != null) return;
            emitter.Transition = null; emitter.Envelope = 1;
            var source = emitter.Source;
            source.Stop(); source.clip = null; source.loop = false; source.pitch = 1; source.mute = false;
            source.ignoreListenerPause = false; source.outputAudioMixerGroup = null; source.spatialBlend = 0;
            source.transform.localPosition = Vector3.zero;
            if (!emitter.Reserved && !emitter.InPool) { emitter.Bank = -1; emitter.InPool = true; pool.Push(emitter); }
            ApplyGain(emitter);
        }
        public AudioHandle Play(AudioCategory category, AudioClipAddress address, AudioClip direct, PlayOptions supplied, int bank = -1)
        {
            using var mutation = callbacks.Begin();
            var options = (supplied ?? new PlayOptions()).Snapshot();
            var handle = new AudioHandle { Id = ++nextId, AudioId = new AudioId(address?.Id ?? (direct != null ? "external:" + direct.GetEntityId() : null)), Category = category, Owner = this };
            var playback = new Playback { Handle = handle, Options = options, Bank = bank };
            if (disposed || !host.Ready || (int)category < 0 || (int)category > 2 || (direct == null && string.IsNullOrWhiteSpace(address?.Id)))
            { Reject(handle, AudioCompletion.Failed, "Service unavailable or empty audio id/clip"); return handle; }
            if (options.MaxInstances == 0 && address != null) options.MaxInstances = Mathf.Max(0, address.MaxInstances);
            if (bank >= 0 && bank != 1)
            {
                var slot = reserved[bank];
                if (slot.Pending != null) Finish(slot.Pending, AudioCompletion.Cancelled);
                slot.Transition = null; slot.Envelope = 1; bankTweens[bank] = null;
            }
            if (!MakeRoom(playback)) return handle;
            active.Add(playback);
            if (bank != 1)
            {
                var emitter = Rent(bank); playback.Emitter = emitter; emitter.Pending = playback;
            }
            // All one-shots now have their own source, including the legacy convenience API.
            if (direct != null) Loaded(playback, new AudioClipLease(direct));
            else
            {
                var cancel = store.Request(address, options.AllowAsyncLoad, lease => Loaded(playback, lease));
                if (!playback.Handle.IsFinished && playback.Lease == null) playback.CancelLoad = cancel;
            }
            return handle;
        }
        private bool MakeRoom(Playback incoming)
        {
            while (true)
            {
                int count = 0, same = 0;
                Playback oldest = null, oldestSame = null;
                foreach (var p in active)
                {
                    if (incoming.Bank >= 0 && incoming.Bank != 1 && p.Bank == incoming.Bank) continue;
                    count++; if (oldest == null) oldest = p;
                    if (p.Handle.Category == incoming.Handle.Category && p.Handle.AudioId.Equals(incoming.Handle.AudioId))
                    { same++; if (oldestSame == null) oldestSame = p; }
                }
                bool soundFull = incoming.Options.MaxInstances > 0 && same >= incoming.Options.MaxInstances;
                bool globalFull = MaxVoices > 0 && count >= MaxVoices;
                if (!soundFull && !globalFull) return true;
                var policy = soundFull ? incoming.Options.ConcurrencyPolicy : ConcurrencyPolicy;
                if (policy == AudioConcurrencyPolicy.RejectNew)
                { Reject(incoming.Handle, AudioCompletion.Rejected, soundFull ? "Per-audio concurrency limit" : "Global concurrency limit"); return false; }
                var victim = soundFull ? oldestSame : oldest;
                if (victim == null) return false;
                Finish(victim, victim.Handle.State == AudioPlaybackState.Loading ? AudioCompletion.Cancelled : AudioCompletion.Stopped);
            }
        }

        private void Reject(AudioHandle handle, AudioCompletion result, string reason)
        { LastFailure = reason; handle.Finish(result, reason); host.ReportFailure(reason); }
        private void Loaded(Playback playback, AudioClipLease lease)
        {
            using var mutation = callbacks.Begin();
            if (disposed || playback.Handle.IsFinished || !active.Contains(playback)) { lease?.Dispose(); return; }
            playback.CancelLoad = null;
            if (lease?.Clip == null) { lease?.Dispose(); Finish(playback, AudioCompletion.Failed, "Clip load failed: " + playback.Handle.AudioId); return; }
            playback.Lease = lease;
            if (playback.Emitter == null)
            {
                var primary = reserved[1];
                if (primary.Current != null && primary.Current.Handle.State == AudioPlaybackState.Playing && !primary.Source.isPlaying)
                    Finish(primary.Current, AudioCompletion.Completed);
                playback.Emitter = Rent(playback.Bank); playback.Emitter.Pending = playback;
            }
            var emitter = playback.Emitter;
            if (emitter.Current != null && playback.Bank == 0 && playback.Options.FadeOutSeconds > 0 && emitter.Source.isPlaying)
            {
                emitter.Transition = new Tween { Start = emitter.Envelope, Target = 0, Duration = playback.Options.FadeOutSeconds, Completed = () => Start(playback) };
            }
            else Start(playback);
        }
        private void Start(Playback playback)
        {
            if (disposed || playback.Handle.IsFinished || playback.Emitter.Pending != playback) return;
            var emitter = playback.Emitter;
            if (emitter.Current != null) Finish(emitter.Current, AudioCompletion.Stopped);
            if (playback.Handle.IsFinished || disposed) return;
            emitter.Pending = null; emitter.Current = playback; emitter.Transition = null;
            emitter.Envelope = playback.Options.FadeInSeconds > 0 ? 0 : 1;
            var source = emitter.Source;
            host.ConfigureSource(source, playback.Handle.Category);
            source.clip = playback.Lease.Clip; source.loop = playback.Options.Loop;
            source.pitch = playback.Options.Pitch; source.ignoreListenerPause = playback.Options.IgnoreGamePause;
            ApplyGain(emitter); source.Play(); playback.StartedFrame = Time.frameCount;
            playback.Handle.State = AudioPlaybackState.Playing;
            ApplyPause(playback);
            if (playback.Options.FadeInSeconds > 0)
                emitter.Transition = new Tween { Start = 0, Target = 1, Duration = playback.Options.FadeInSeconds };
        }
        private void Finish(Playback playback, AudioCompletion result, string reason = null)
        {
            if (playback.Handle.IsFinished) return;
            active.Remove(playback);
            playback.CancelLoad?.Invoke(); playback.CancelLoad = null;
            var emitter = playback.Emitter;
            if (emitter != null)
            {
                if (emitter.Pending == playback) { emitter.Pending = null; emitter.Transition = null; emitter.Envelope = 1; }
                if (emitter.Current == playback) { emitter.Source.Stop(); emitter.Source.clip = null; emitter.Current = null; emitter.Transition = null; emitter.Envelope = 1; }
            }
            playback.Lease?.Dispose(); playback.Lease = null;
            Recycle(emitter);
            if (reason != null) { LastFailure = reason; host.ReportFailure(reason); }
            playback.Handle.Finish(result, reason);
        }
        public bool Stop(AudioHandle handle, float seconds) => StopAt(handle, seconds, 0f);
        private bool StopAt(AudioHandle handle, float seconds, float target)
        {
            using var mutation = callbacks.Begin();
            var p = Find(handle); if (p == null) return false;
            if (handle.State == AudioPlaybackState.Loading) Finish(p, AudioCompletion.Cancelled);
            else if (handle.State == AudioPlaybackState.Paused) Finish(p, AudioCompletion.Stopped);
            else if (seconds <= 0) Finish(p, AudioCompletion.Stopped);
            else p.Emitter.Transition = new Tween { Start = p.Emitter.Envelope, Target = target, Duration = seconds, Completed = () => Finish(p, AudioCompletion.Stopped) };
            return true;
        }
        public bool Pause(AudioHandle handle, bool pause)
        { var p = Find(handle); if (p == null) return false; handle.UserPaused = pause; ApplyPause(p); return true; }
        private bool Paused(Playback p) => p.Handle.UserPaused || backgroundPaused || (gamePaused && !p.Options.IgnoreGamePause);
        private void ApplyPause(Playback p)
        {
            if (p.Handle.State == AudioPlaybackState.Loading || p.Handle.IsFinished) return;
            if (Paused(p) && p.Handle.State != AudioPlaybackState.Paused) { p.Emitter.Source.Pause(); p.Handle.State = AudioPlaybackState.Paused; }
            else if (!Paused(p) && p.Handle.State == AudioPlaybackState.Paused) { p.Emitter.Source.UnPause(); p.Handle.State = AudioPlaybackState.Playing; }
        }
        public void SetPaused(bool paused, bool background = false)
        { if (background) backgroundPaused = paused; else gamePaused = paused; foreach (var p in active.ToArray()) ApplyPause(p); }
        public void SetMuted(AudioChannel channel, bool value) { muted[(int)channel] = value; RefreshGains(); }
        public void StopCategory(AudioCategory? category, bool loopOnly, float seconds, float target = 0f)
        {
            using var mutation = callbacks.Begin();
            var selected = active.FindAll(p => (!category.HasValue || p.Handle.Category == category) && (!loopOnly || p.Options.Loop));
            foreach (var p in selected) if (p.Handle.State == AudioPlaybackState.Loading) Stop(p.Handle, 0);
            foreach (var p in selected) if (!p.Handle.IsFinished) StopAt(p.Handle, seconds, target);
        }
        public void FadeBus(AudioChannel channel, float target, float seconds, bool stopAfter)
        {
            using var mutation = callbacks.Begin();
            var index = (int)channel; busTweens[index] = null;
            if (stopAfter)
            {
                StopCategory(channel == AudioChannel.Master ? (AudioCategory?)null : (AudioCategory)(index - 1), false, seconds, target);
            }
            else if (seconds <= 0) busGains[index] = target;
            else busTweens[index] = new Tween { Start = busGains[index], Target = target, Duration = seconds };
            RefreshGains();
        }
        public void FadeLegacy(int bank, float target, float seconds, bool stopAfter)
        {
            using var mutation = callbacks.Begin();
            bankTweens[bank] = null;
            var selected = active.FindAll(p => p.Bank == bank);
            foreach (var p in selected) if (p.Handle.State == AudioPlaybackState.Loading) Finish(p, AudioCompletion.Cancelled);
            foreach (var p in selected)
            {
                if (p.Handle.IsFinished) continue;
                if (stopAfter) StopAt(p.Handle, seconds, target);
                else p.Emitter.Transition = null;
            }
            if (!stopAfter)
            {
                bankGains[bank] *= reserved[bank].Envelope;
                foreach (var e in emitters) if (e.Bank == bank) e.Envelope = 1;
                if (seconds <= 0) bankGains[bank] = target;
                else bankTweens[bank] = new Tween { Start = bankGains[bank], Target = target, Duration = seconds };
            }
            RefreshGains();
        }
        private void ApplyGain(Emitter emitter)
        {
            if (emitter.Source == null) return;
            var p = emitter.Current;
            var category = p?.Handle.Category ?? (emitter.Bank == 0 ? AudioCategory.Bgm : emitter.Bank == 1 || emitter.Bank == 2 ? AudioCategory.Sfx : AudioCategory.Voice);
            int bus = (int)category + 1;
            float gain = busGains[0] * busGains[bus] * (muted[0] || muted[bus] ? 0 : 1);
            if (emitter.Bank >= 0) gain *= bankGains[emitter.Bank];
            gain *= (p?.Options.Volume ?? 1) * emitter.Envelope * host.SourceSettingsGain(category);
            emitter.Source.volume = AudioValues.Unit(gain);
        }
        public void RefreshRouting()
        {
            foreach (var e in emitters)
            {
                var category = e.Current?.Handle.Category ?? (e.Bank == 0 ? AudioCategory.Bgm : e.Bank == 1 || e.Bank == 2 ? AudioCategory.Sfx : AudioCategory.Voice);
                host.ConfigureSource(e.Source, category);
            }
        }
        public void RefreshGains() { foreach (var emitter in emitters) ApplyGain(emitter); }
        public void Tick(float delta)
        {
            using var mutation = callbacks.Begin();
            if (disposed) return;
            for (int i = 0; i < 4; i++) if (busTweens[i] != null)
            { var t = busTweens[i]; busGains[i] = t.Advance(delta); if (t.Done) busTweens[i] = null; }
            for (int i = 0; i < 5; i++) if (bankTweens[i] != null)
            { var t = bankTweens[i]; bankGains[i] = t.Advance(delta); if (t.Done) bankTweens[i] = null; }
            int count = emitters.Count;
            for (int i = 0; i < count; i++)
            {
                var emitter = emitters[i]; var p = emitter.Current;
                if (emitter.Transition != null && (p == null || !Paused(p)))
                {
                    var tween = emitter.Transition; emitter.Envelope = tween.Advance(delta);
                    if (tween.Done) { emitter.Transition = null; tween.Completed?.Invoke(); }
                }
                ApplyGain(emitter); p = emitter.Current;
                if (p != null && p.Handle.State == AudioPlaybackState.Playing && !p.Options.Loop && Time.frameCount > p.StartedFrame + 1 && !emitter.Source.isPlaying)
                    Finish(p, AudioCompletion.Completed);
            }
        }
        public void Preload(AudioClipAddress address, string group, Action<bool> completed)
        {
            store.Request(address, true, lease => { bool success = lease?.Clip != null; lease?.Dispose(); completed?.Invoke(success); }, group);
        }
        public bool TryGetCached(AudioClipAddress address, out AudioClip clip) => store.TryGetCached(address, out clip);
        public void ReleaseGroup(string group) => store.ReleaseGroup(group);
        public void ReleaseUnused() => store.ReleaseUnused();
        public AudioDiagnostics Diagnostics
        {
            get
            {
                int playing = 0, paused = 0, loading = 0;
                foreach (var p in active) { if (p.Handle.State == AudioPlaybackState.Loading) loading++; else if (p.Handle.State == AudioPlaybackState.Paused) paused++; else playing++; }
                return new AudioDiagnostics(playing, paused, loading, pool.Count, emitters.Count, store.CachedCount, store.UserCount, store.LoadingCount, LastFailure);
            }
        }
        public void Shutdown()
        {
            using var mutation = callbacks.Begin();
            if (disposed) return; disposed = true;
            foreach (var p in active.ToArray()) Finish(p, p.Handle.State == AudioPlaybackState.Loading ? AudioCompletion.Cancelled : AudioCompletion.Stopped);
            store.Dispose();
            foreach (var e in emitters) if (!e.Reserved && e.Source != null) UnityEngine.Object.Destroy(e.Source.gameObject);
            pool.Clear();
        }
    }
}
