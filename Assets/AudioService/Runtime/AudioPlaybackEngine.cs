using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace Controller.Audio
{
    internal sealed class AudioPlaybackEngine
    {
        private enum TweenCompletion { None, StartPending, Stop }
        private struct Tween
        {
            public float Start, Target, Duration, Elapsed;
            public TweenCompletion Completion;
            public Playback Playback;
            public bool Active => Duration > 0;
            public float Advance(float delta) { Elapsed += delta; return Mathf.Lerp(Start, Target, Duration <= 0 ? 1 : Mathf.Clamp01(Elapsed / Duration)); }
            public bool Done => Elapsed >= Duration;
        }
        private sealed class Emitter
        {
            public AudioSource Source;
            public bool Reserved, InPool;
            public int Bank = -1, UpdateIndex = -1;
            public float AppliedGain = float.NaN;
            public float Envelope = 1f;
            public Tween Transition;
            public Playback Current, Pending;
        }
        private sealed class Playback
        {
            public AudioHandle Handle;
            public PlaybackOptions Options;
            public AudioClip Clip;
            public AudioClipLease Lease;
            public AudioClipStore.Usage ResidentUsage;
            public Action CancelLoad;
            public Emitter Emitter;
            public int Bank;
            public int StartedFrame;
        }
        private sealed class Preparation
        {
            public string Group;
            public Action<bool> Completed;
            public AudioClipLease Lease;
            public Action CancelLoad;
            public bool Finished;
        }
        private readonly List<Preparation> preparations = new();
        private readonly AudioCallbackQueue callbacks = new();
        internal void NotifyCompleted(Action callback) => callbacks.Enqueue(callback);
        internal AudioCallbackQueue.Scope BeginMutation() => callbacks.Begin();
        private readonly AudioController host;
        private readonly List<Playback> active = new();
        private readonly Stack<List<Playback>> snapshots = new();
        private Dictionary<(AudioCategory, AudioId), int> audioCounts;
        private bool audioCountsActive;
        private readonly List<Emitter> emitters = new();
        private readonly List<Emitter> updating = new();
        private readonly Stack<Emitter> pool = new();
        private readonly Emitter[] reserved = new Emitter[5];
        private readonly float[] bankGains = { 1, 1, 1, 1, 1 };
        private readonly Tween[] bankTweens = new Tween[5];
        private readonly float[] busGains = { 1, 1, 1, 1 };
        private readonly Tween[] busTweens = new Tween[4];
        private readonly bool[] muted = new bool[4];
        private long nextId;
        private bool disposed, gamePaused, backgroundPaused, listenerPaused;
        private AudioClipStore store;
        private IAudioClipProvider provider;
        private sealed class ExternalId { public AudioId Id; }
        private ConditionalWeakTable<AudioClip, ExternalId> externalIds;
        private static readonly ConditionalWeakTable<AudioClip, ExternalId>.CreateValueCallback CreateExternalId
            = clip => new ExternalId { Id = new AudioId("external:" + clip.GetEntityId().ToString()) };
        private AudioId Identify(AudioClip clip)
        {
            // Cache by managed identity without extending the caller's clip lifetime.
            externalIds ??= new ConditionalWeakTable<AudioClip, ExternalId>();
            return externalIds.GetValue(clip, CreateExternalId).Id;
        }
        public string LastFailure { get; private set; }
        public int MaxVoices { get; set; }
        public AudioConcurrencyPolicy ConcurrencyPolicy { get; set; }

        public AudioPlaybackEngine(AudioController host, AudioSource[] sources, IAudioClipProvider provider)
        {
            this.host = host;
            listenerPaused = AudioListener.pause;
            for (int i = 0; i < sources.Length; i++)
            { reserved[i] = new Emitter { Source = sources[i], Bank = i, Reserved = true }; emitters.Add(reserved[i]); }
            SetProvider(provider);
        }
        public void SetProvider(IAudioClipProvider next, bool force = false, bool refreshLookup = false)
        {
            using var mutation = callbacks.Begin();
            if (!force && ReferenceEquals(provider, next) && store != null) return;
            if (disposed) return;
            // Publish the new generation before cancellation callbacks can request work.
            var previousStore = store;
            var previousRequests = active.ToArray();
            provider = next; store = new AudioClipStore(host, provider, callbacks);
            foreach (var playback in previousRequests) if (playback.Handle.State == AudioPlaybackState.Loading) Finish(playback, AudioCompletion.Cancelled);
            foreach (var preparation in preparations.ToArray()) FinishPreparation(preparation, false);
            previousStore?.Dispose();
            if (refreshLookup && AudioValues.Alive(next) && next is IAudioClipProviderRefresh refreshable)
                AudioCallbacks.Invoke(refreshable.RefreshClipLookup);
        }
        private Playback Find(AudioHandle handle) => ReferenceEquals(handle.Owner, this) ? handle.Playback as Playback : null;
        private List<Playback> RentSnapshot()
            => snapshots.Count > 0 ? snapshots.Pop() : new List<Playback>(active.Count);
        private void ReturnSnapshot(List<Playback> snapshot) { snapshot.Clear(); snapshots.Push(snapshot); }
        private int CountAudio(AudioHandle handle)
        {
            // Build only when a per-audio limit is used. Keep ordinary unlimited
            // sessions free of index maintenance, including after a busy scene ends.
            if (!audioCountsActive)
            {
                audioCounts ??= new Dictionary<(AudioCategory, AudioId), int>();
                audioCounts.Clear();
                foreach (var p in active) IncrementAudio(p.Handle);
                audioCountsActive = true;
            }
            audioCounts.TryGetValue((handle.Category, handle.AudioId), out int count);
            return count;
        }
        private void IncrementAudio(AudioHandle handle)
        {
            var key = (handle.Category, handle.AudioId);
            audioCounts.TryGetValue(key, out int count); audioCounts[key] = count + 1;
        }
        private void AddActive(Playback playback)
        {
            active.Add(playback);
            playback.Handle.Playback = playback;
            if (audioCountsActive) IncrementAudio(playback.Handle);
        }
        private void RemoveActive(Playback playback)
        {
            playback.Handle.Playback = null;
            if (!active.Remove(playback) || !audioCountsActive) return;
            var key = (playback.Handle.Category, playback.Handle.AudioId);
            int remaining = audioCounts[key] - 1;
            if (remaining == 0) audioCounts.Remove(key); else audioCounts[key] = remaining;
            if (active.Count == 0) audioCountsActive = false;
        }
        private Emitter CreateEmitter()
        {
            var go = new GameObject("AudioEmitter"); go.transform.SetParent(host.transform, false);
            var source = go.AddComponent<AudioSource>(); source.playOnAwake = false; source.spatialBlend = 0; source.Stop();
            var emitter = new Emitter { Source = source }; emitters.Add(emitter);
            return emitter;
        }
        public int PrewarmSources(int targetCount)
        {
            if (disposed || !host.Ready || targetCount <= 0) return 0;
            int missing = targetCount - (emitters.Count - reserved.Length);
            if (missing <= 0) return 0;
            for (int i = 0; i < missing; i++)
            {
                var emitter = CreateEmitter(); emitter.InPool = true; pool.Push(emitter);
            }
            // Reserve the bookkeeping arrays as well as native sources. Idle
            // emitters stay out of updating and acquire current gains on Start.
            if (active.Capacity < emitters.Count) active.Capacity = emitters.Count;
            if (updating.Capacity < emitters.Count) updating.Capacity = emitters.Count;
            return missing;
        }
        public int TrimIdleSources(int minimumCapacity, int maxToRemove)
        {
            if (disposed || !host.Ready || maxToRemove <= 0) return 0;
            minimumCapacity = Mathf.Max(0, minimumCapacity);
            int removed = 0;
            while (removed < maxToRemove && pool.Count > 0 && emitters.Count - reserved.Length > minimumCapacity)
            {
                var emitter = pool.Pop(); emitter.InPool = false;
                emitters.Remove(emitter); removed++;
                UnityEngine.Object.Destroy(emitter.Source.gameObject);
            }
            return removed;
        }
        private Emitter Rent(int bank)
        {
            if (bank >= 0 && (bank != 1 || (reserved[bank].Current == null && reserved[bank].Pending == null)))
            { Activate(reserved[bank]); return reserved[bank]; }
            Emitter emitter;
            if (pool.Count > 0) { emitter = pool.Pop(); emitter.InPool = false; }
            else emitter = CreateEmitter();
            emitter.Bank = bank; Activate(emitter);
            return emitter;
        }
        private void Activate(Emitter emitter)
        {
            if (emitter.UpdateIndex >= 0) return;
            emitter.UpdateIndex = updating.Count; updating.Add(emitter);
        }
        private void Deactivate(Emitter emitter)
        {
            int index = emitter.UpdateIndex; if (index < 0) return;
            int last = updating.Count - 1;
            updating[index] = updating[last]; updating[index].UpdateIndex = index;
            updating.RemoveAt(last); emitter.UpdateIndex = -1;
        }
        private void Recycle(Emitter emitter)
        {
            if (emitter == null || emitter.Current != null || emitter.Pending != null) return;
            Deactivate(emitter);
            emitter.Transition = default; emitter.Envelope = 1;
            var source = emitter.Source;
            source.Stop(); source.clip = null; source.loop = false; source.pitch = 1; source.mute = false;
            source.ignoreListenerPause = false; source.outputAudioMixerGroup = null; source.spatialBlend = 0;
            source.transform.localPosition = Vector3.zero;
            if (!emitter.Reserved && !emitter.InPool) { emitter.Bank = -1; emitter.InPool = true; pool.Push(emitter); }
            ApplyGain(emitter);
        }
        public AudioHandle Play(AudioCategory category, AudioClipAddress address, AudioClip direct, PlayOptions supplied, int bank = -1, AudioId id = default)
        {
            using var mutation = callbacks.Begin();
            string key = address?.Id ?? id.Value;
            var handle = new AudioHandle { Id = ++nextId, AudioId = direct != null ? Identify(direct) : new AudioId(key), Category = category, Owner = this };
            if (disposed || !host.Ready || (int)category < 0 || (int)category > 2 || (direct == null && string.IsNullOrWhiteSpace(key)))
            { Reject(handle, AudioCompletion.Failed, "Service unavailable or empty audio id/clip"); return handle; }
            int maxInstances = Mathf.Max(0, supplied?.MaxInstances ?? 0);
            if (maxInstances == 0 && address != null) maxInstances = Mathf.Max(0, address.MaxInstances);
            if (!TryPlanAdmission(handle, bank, maxInstances, supplied?.ConcurrencyPolicy ?? AudioConcurrencyPolicy.RejectNew, out var victims)) return handle;
            StartAccepted(handle, key, address, direct, supplied, bank, victims);
            return handle;
        }
        private void StartAccepted(AudioHandle handle, string key, AudioClipAddress address, AudioClip direct, PlayOptions supplied, int bank, Playback[] victims)
        {
            // Snapshot before eviction can release a provider lease or invoke external code.
            var options = new PlaybackOptions(supplied, bank == 0);
            var playback = new Playback { Handle = handle, Options = options, Bank = bank };
            if (victims != null)
                foreach (var victim in victims)
                    Finish(victim, victim.Handle.State == AudioPlaybackState.Loading ? AudioCompletion.Cancelled : AudioCompletion.Stopped);
            if (bank >= 0 && bank != 1)
            {
                var slot = reserved[bank];
                if (slot.Pending != null) Finish(slot.Pending, AudioCompletion.Cancelled);
                slot.Transition = default; slot.Envelope = 1; bankTweens[bank] = default;
            }
            AddActive(playback);
            if (bank != 1)
            {
                var emitter = Rent(bank); playback.Emitter = emitter; emitter.Pending = playback;
            }
            // All one-shots now have their own source, including the legacy convenience API.
            if (direct != null) Loaded(playback, direct);
            else if (store.TryAcquireResident(handle.Category, key, out var usage)) Loaded(playback, usage.Clip, usage: usage);
            else RequestClip(playback, address ?? new AudioClipAddress(key, handle.Category));
        }
        private void RequestClip(Playback playback, AudioClipAddress address)
        {
            // Isolate the load closure from direct playback and rejected requests.
            var cancel = store.Request(address, playback.Options.AllowAsyncLoad, lease => Loaded(playback, lease?.Clip, lease, address));
            if (!playback.Handle.IsFinished && playback.Clip == null) playback.CancelLoad = cancel;
        }
        private bool TryPlanAdmission(AudioHandle incoming, int bank, int maxInstances, AudioConcurrencyPolicy policy, out Playback[] victims)
        {
            victims = null;
            if (MaxVoices <= 0 && maxInstances <= 0) return true;
            bool replacesBank = bank >= 0 && bank != 1;
            int count = active.Count, same = 0;
            // Replacement banks exclude their current/pending requests; retain
            // their scan. Ordinary requests can count Loading/Paused via the index.
            if (replacesBank)
            {
                count = 0;
                foreach (var p in active)
                {
                    if (replacesBank && p.Bank == bank) continue;
                    count++;
                    if (maxInstances > 0 && SameAudio(p, incoming)) same++;
                }
            }
            else if (maxInstances > 0) same = CountAudio(incoming);
            // Decide every limit before stopping anything. Per-audio evictions also
            // free global capacity; lowered limits may require more than one victim.
            int soundVictims = maxInstances > 0 ? Mathf.Max(0, same - maxInstances + 1) : 0;
            if (soundVictims > 0 && policy == AudioConcurrencyPolicy.RejectNew)
            { Reject(incoming, AudioCompletion.Rejected, "Per-audio concurrency limit"); return false; }
            int globalVictims = MaxVoices > 0 ? Mathf.Max(0, count - soundVictims - MaxVoices + 1) : 0;
            if (globalVictims > 0 && ConcurrencyPolicy == AudioConcurrencyPolicy.RejectNew)
            { Reject(incoming, AudioCompletion.Rejected, "Global concurrency limit"); return false; }
            int victimCount = soundVictims + globalVictims;
            if (victimCount == 0) return true;

            // Preserve per-audio-first, then global-oldest order. Only accepted
            // requests that actually evict allocate a candidate snapshot.
            victims = new Playback[victimCount];
            int soundIndex = 0, globalIndex = soundVictims;
            foreach (var p in active)
            {
                if (replacesBank && p.Bank == bank) continue;
                if (soundIndex < soundVictims && SameAudio(p, incoming)) victims[soundIndex++] = p;
                else if (globalIndex < victimCount) victims[globalIndex++] = p;
                if (soundIndex == soundVictims && globalIndex == victimCount) break;
            }
            return true;
        }
        private static bool SameAudio(Playback playback, AudioHandle handle) => playback.Handle.Category == handle.Category && playback.Handle.AudioId.Equals(handle.AudioId);

        private void Reject(AudioHandle handle, AudioCompletion result, string reason)
        {
            LastFailure = reason; handle.Finish(result, reason); host.ReportFailure(reason);
            if (result == AudioCompletion.Failed) host.ReportPlaybackFailure(handle, reason);
        }
        private void Loaded(Playback playback, AudioClip clip, AudioClipLease lease = null, AudioClipAddress address = null, AudioClipStore.Usage usage = default)
        {
            using var mutation = callbacks.Begin();
            if (disposed || playback.Handle.IsFinished) { usage.Dispose(); AudioCallbacks.Dispose(lease); return; }
            SyncListenerPause();
            playback.CancelLoad = null;
            if (clip == null)
            {
                usage.Dispose();
                AudioCallbacks.Dispose(lease);
                string reason = "Clip load failed: " + playback.Handle.AudioId;
                Finish(playback, AudioCompletion.Failed, reason);
                host.ReportPlaybackFailure(playback.Handle, reason, address, playback.Options.AllowAsyncLoad);
                return;
            }
            playback.Lease = lease; playback.ResidentUsage = usage; playback.Clip = clip;
            if (playback.Emitter == null)
            {
                var primary = reserved[1];
                if (primary.Current != null && primary.Current.Handle.State == AudioPlaybackState.Playing && !Paused(primary.Current) && !primary.Source.isPlaying)
                    Finish(primary.Current, AudioCompletion.Completed);
                playback.Emitter = Rent(playback.Bank); playback.Emitter.Pending = playback;
            }
            var emitter = playback.Emitter;
            if (emitter.Current != null && playback.Bank == 0 && playback.Options.FadeOutSeconds > 0 && emitter.Source.isPlaying)
            {
                emitter.Transition = new Tween { Start = emitter.Envelope, Target = 0, Duration = playback.Options.FadeOutSeconds, Completion = TweenCompletion.StartPending, Playback = playback };
            }
            else Start(playback);
        }
        private void Start(Playback playback)
        {
            if (disposed || playback.Handle.IsFinished || playback.Emitter.Pending != playback) return;
            var emitter = playback.Emitter;
            if (emitter.Current != null) Finish(emitter.Current, AudioCompletion.Stopped);
            if (playback.Handle.IsFinished || disposed) return;
            emitter.Pending = null; emitter.Current = playback; emitter.Transition = default;
            emitter.Envelope = playback.Options.FadeInSeconds > 0 ? 0 : 1;
            var source = emitter.Source;
            host.ConfigureSource(source, playback.Handle.Category);
            source.clip = playback.Clip; source.loop = playback.Options.Loop;
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
            RemoveActive(playback);
            var cancel = playback.CancelLoad; playback.CancelLoad = null;
            var lease = playback.Lease; playback.Lease = null;
            var usage = playback.ResidentUsage; playback.ResidentUsage = default;
            playback.Clip = null;
            var emitter = playback.Emitter;
            if (emitter != null)
            {
                if (emitter.Pending == playback) { emitter.Pending = null; emitter.Transition = default; emitter.Envelope = 1; }
                if (emitter.Current == playback) { emitter.Source.Stop(); emitter.Source.clip = null; emitter.Current = null; emitter.Transition = default; emitter.Envelope = 1; }
            }
            Recycle(emitter);
            if (reason != null) { LastFailure = reason; host.ReportFailure(reason); }
            playback.Handle.Finish(result, reason);
            // Store leases update internal counts now. Actual provider releases
            // are queued by the store until this entire engine mutation commits.
            AudioCallbacks.Invoke(cancel); usage.Dispose(); AudioCallbacks.Dispose(lease);
        }
        public bool Stop(AudioHandle handle, float seconds) => StopAt(handle, seconds, 0f);
        private bool StopAt(AudioHandle handle, float seconds, float target)
        {
            using var mutation = callbacks.Begin();
            SyncListenerPause();
            var p = Find(handle); if (p == null) return false;
            StopPlayback(p, seconds, target); return true;
        }
        private void StopPlayback(Playback p, float seconds, float target)
        {
            if (p.Handle.IsFinished) return;
            if (p.Handle.State == AudioPlaybackState.Loading) Finish(p, AudioCompletion.Cancelled);
            else if (p.Handle.State == AudioPlaybackState.Paused) Finish(p, AudioCompletion.Stopped);
            else if (seconds <= 0) Finish(p, AudioCompletion.Stopped);
            else p.Emitter.Transition = new Tween { Start = p.Emitter.Envelope, Target = target, Duration = seconds, Completion = TweenCompletion.Stop, Playback = p };
        }
        public bool Pause(AudioHandle handle, bool pause)
        { SyncListenerPause(); var p = Find(handle); if (p == null) return false; handle.UserPaused = pause; ApplyPause(p); return true; }
        private bool Paused(Playback p) => p.Handle.UserPaused || backgroundPaused || ((gamePaused || listenerPaused) && !p.Options.IgnoreGamePause);
        private void SyncListenerPause()
        {
            bool value = AudioListener.pause;
            if (listenerPaused == value) return;
            listenerPaused = value;
            foreach (var p in active) ApplyPause(p);
        }
        private void ApplyPause(Playback p)
        {
            if (p.Handle.State == AudioPlaybackState.Loading || p.Handle.IsFinished) return;
            if (Paused(p) && p.Handle.State != AudioPlaybackState.Paused) { p.Emitter.Source.Pause(); p.Handle.State = AudioPlaybackState.Paused; }
            else if (!Paused(p) && p.Handle.State == AudioPlaybackState.Paused) { p.Emitter.Source.UnPause(); p.Handle.State = AudioPlaybackState.Playing; }
        }
        public void SetPaused(bool paused, bool background = false)
        {
            if ((background ? backgroundPaused : gamePaused) == paused) return;
            if (background) backgroundPaused = paused; else gamePaused = paused;
            // ApplyPause only updates native sources and handle state; no external callbacks.
            foreach (var p in active) ApplyPause(p);
        }
        public void SetMuted(AudioChannel channel, bool value)
        {
            if (muted[(int)channel] == value) return;
            muted[(int)channel] = value;
            RefreshGains(ChannelMask(channel));
        }
        public AudioChannelDiagnostics GetChannelDiagnostics(AudioChannel channel, float volume)
            => new AudioChannelDiagnostics(volume, busGains[(int)channel], muted[(int)channel]);
        public void StopCategory(AudioCategory? category, bool loopOnly, float seconds, float target = 0f)
        {
            using var mutation = callbacks.Begin();
            SyncListenerPause();
            var selected = RentSnapshot();
            try
            {
                foreach (var p in active)
                    if ((!category.HasValue || p.Handle.Category == category) && (!loopOnly || p.Options.Loop)) selected.Add(p);
                foreach (var p in selected) if (p.Handle.State == AudioPlaybackState.Loading) StopPlayback(p, 0, target);
                foreach (var p in selected) StopPlayback(p, seconds, target);
            }
            finally { ReturnSnapshot(selected); }
        }
        public void FadeBus(AudioChannel channel, float target, float seconds, bool stopAfter)
        {
            using var mutation = callbacks.Begin();
            var index = (int)channel; busTweens[index] = default;
            if (stopAfter)
            {
                StopCategory(channel == AudioChannel.Master ? (AudioCategory?)null : (AudioCategory)(index - 1), false, seconds, target);
            }
            else if (seconds <= 0) busGains[index] = target;
            else busTweens[index] = new Tween { Start = busGains[index], Target = target, Duration = seconds };
            RefreshGains(ChannelMask(channel));
        }
        public void FadeLegacy(int bank, float target, float seconds, bool stopAfter)
        {
            using var mutation = callbacks.Begin();
            bankTweens[bank] = default;
            SyncListenerPause();
            var selected = RentSnapshot();
            try
            {
                foreach (var p in active) if (p.Bank == bank) selected.Add(p);
                foreach (var p in selected) if (p.Handle.State == AudioPlaybackState.Loading) Finish(p, AudioCompletion.Cancelled);
                foreach (var p in selected)
                {
                    if (p.Handle.IsFinished) continue;
                    if (stopAfter) StopPlayback(p, seconds, target);
                    else p.Emitter.Transition = default;
                }
            }
            finally { ReturnSnapshot(selected); }
            if (!stopAfter)
            {
                bankGains[bank] *= reserved[bank].Envelope;
                foreach (var e in updating) if (e.Bank == bank) e.Envelope = 1;
                reserved[bank].Envelope = 1;
                if (seconds <= 0) bankGains[bank] = target;
                else bankTweens[bank] = new Tween { Start = bankGains[bank], Target = target, Duration = seconds };
            }
            RefreshGains(0, 1 << bank);
        }
        private static int ChannelMask(AudioChannel channel) => channel == AudioChannel.Master ? 7 : 1 << ((int)channel - 1);
        private static AudioCategory CategoryOf(Emitter emitter) => emitter.Current?.Handle.Category
            ?? (emitter.Bank == 0 ? AudioCategory.Bgm : emitter.Bank == 1 || emitter.Bank == 2 ? AudioCategory.Sfx : AudioCategory.Voice);
        private static bool NeedsGain(Emitter emitter, int categories, int banks)
            => categories == 7 || (categories != 0 && (categories & (1 << (int)CategoryOf(emitter))) != 0)
                || (banks != 0 && emitter.Bank >= 0 && (banks & (1 << emitter.Bank)) != 0);
        private void ApplyGain(Emitter emitter)
        {
            if (emitter.Source == null) return;
            var p = emitter.Current;
            var category = CategoryOf(emitter);
            int bus = (int)category + 1;
            float gain = busGains[0] * busGains[bus] * (muted[0] || muted[bus] ? 0 : 1);
            if (emitter.Bank >= 0) gain *= bankGains[emitter.Bank];
            gain *= (p?.Options.Volume ?? 1) * emitter.Envelope * host.SourceSettingsGain(category);
            gain = AudioValues.Unit(gain);
            if (emitter.AppliedGain == gain) return;
            emitter.Source.volume = gain; emitter.AppliedGain = gain;
        }
        public void RefreshRouting()
        {
            foreach (var e in emitters)
            {
                var category = e.Current?.Handle.Category ?? (e.Bank == 0 ? AudioCategory.Bgm : e.Bank == 1 || e.Bank == 2 ? AudioCategory.Sfx : AudioCategory.Voice);
                host.ConfigureSource(e.Source, category);
            }
        }
        public void RefreshGains(int categories = 7, int banks = 0)
        {
            foreach (var emitter in updating)
                if (NeedsGain(emitter, categories, banks)) ApplyGain(emitter);
            RefreshIdleReservedGains(categories, banks);
        }
        private void RefreshIdleReservedGains(int categories, int banks)
        {
            // The five compatibility sources expose their gain even while idle.
            // Dynamic idle sources take current state in Start, just before Play.
            foreach (var emitter in reserved)
                if (emitter.UpdateIndex < 0 && NeedsGain(emitter, categories, banks)) ApplyGain(emitter);
        }
        public void Tick(float delta)
        {
            using var mutation = callbacks.Begin();
            if (disposed) return;
            SyncListenerPause();
            for (int i = preparations.Count - 1; i >= 0; i--) CheckPreparation(preparations[i]);
            int changedCategories = 0, changedBanks = 0;
            for (int i = 0; i < 4; i++) if (busTweens[i].Active)
            { changedCategories |= ChannelMask((AudioChannel)i); ref var t = ref busTweens[i]; busGains[i] = t.Advance(delta); if (t.Done) t = default; }
            for (int i = 0; i < 5; i++) if (bankTweens[i].Active)
            { changedBanks |= 1 << i; ref var t = ref bankTweens[i]; bankGains[i] = t.Advance(delta); if (t.Done) t = default; }
            bool gainsChanged = changedCategories != 0 || changedBanks != 0;
            int iSource = 0;
            while (iSource < updating.Count)
            {
                var emitter = updating[iSource]; var p = emitter.Current;
                bool envelopeChanged = false;
                if (emitter.Transition.Active && (p == null || !Paused(p)))
                {
                    envelopeChanged = true;
                    ref var tween = ref emitter.Transition; emitter.Envelope = tween.Advance(delta);
                    if (tween.Done)
                    {
                        var completion = tween.Completion; var target = tween.Playback; tween = default;
                        if (completion == TweenCompletion.StartPending) Start(target);
                        else if (completion == TweenCompletion.Stop && ReferenceEquals(emitter.Current, target)) Finish(target, AudioCompletion.Stopped);
                    }
                }
                if (envelopeChanged || (gainsChanged && NeedsGain(emitter, changedCategories, changedBanks))) ApplyGain(emitter);
                p = emitter.Current;
                if (p != null && p.Handle.State == AudioPlaybackState.Playing && !p.Options.Loop && Time.frameCount > p.StartedFrame + 1 && !emitter.Source.isPlaying)
                    Finish(p, AudioCompletion.Completed);
                // Recycle swaps the last active emitter into this position.
                if (iSource < updating.Count && ReferenceEquals(updating[iSource], emitter)) iSource++;
            }
            if (gainsChanged) RefreshIdleReservedGains(changedCategories, changedBanks);
        }
        public void Preload(AudioClipAddress address, string group, Action<bool> completed)
        {
            using var mutation = callbacks.Begin();
            store.Request(address, true, lease =>
            {
                bool success = lease?.Clip != null; AudioCallbacks.Dispose(lease);
                if (completed != null) callbacks.Enqueue(() => AudioCallbacks.Invoke(completed, success));
            }, group);
        }
        public void PrepareClip(AudioClipAddress address, string group, Action<bool> completed)
        {
            using var mutation = callbacks.Begin();
            if (disposed) { callbacks.Enqueue(() => AudioCallbacks.Invoke(completed, false)); return; }
            var preparation = new Preparation { Group = group, Completed = completed };
            preparations.Add(preparation);
            var cancel = store.Request(address, true, lease =>
            {
                using var delivery = callbacks.Begin();
                if (preparation.Finished) { AudioCallbacks.Dispose(lease); return; }
                preparation.CancelLoad = null; preparation.Lease = lease;
                if (lease?.Clip == null) { FinishPreparation(preparation, false); return; }
                try
                {
                    if (lease.Clip.loadState == AudioDataLoadState.Unloaded && !lease.Clip.LoadAudioData())
                    { FinishPreparation(preparation, false); return; }
                    CheckPreparation(preparation);
                }
                catch (Exception) { FinishPreparation(preparation, false); }
            }, group);
            if (!preparation.Finished && preparation.Lease == null) preparation.CancelLoad = cancel;
        }
        private void CheckPreparation(Preparation preparation)
        {
            if (preparation.Finished || preparation.Lease == null) return;
            var clip = preparation.Lease.Clip;
            if (clip == null || clip.loadState == AudioDataLoadState.Failed) FinishPreparation(preparation, false);
            else if (clip.loadState == AudioDataLoadState.Loaded) FinishPreparation(preparation, true);
        }
        private void FinishPreparation(Preparation preparation, bool success)
        {
            if (preparation.Finished) return;
            preparation.Finished = true; preparations.Remove(preparation);
            var cancel = preparation.CancelLoad; preparation.CancelLoad = null;
            var lease = preparation.Lease; preparation.Lease = null;
            var completed = preparation.Completed; preparation.Completed = null;
            if (completed != null) callbacks.Enqueue(() => AudioCallbacks.Invoke(completed, success));
            AudioCallbacks.Invoke(cancel); AudioCallbacks.Dispose(lease);
        }
        public bool TryGetCached(AudioClipAddress address, out AudioClip clip) => store.TryGetCached(address, out clip);
        public void ReleaseGroup(string group)
        {
            using var mutation = callbacks.Begin();
            foreach (var preparation in preparations.ToArray())
                if (preparation.Group == group) FinishPreparation(preparation, false);
            store.ReleaseGroup(group);
        }
        public void ReleaseUnused() => store.ReleaseUnused();
        public void AudioConfigurationChanged()
        {
            using var mutation = callbacks.Begin();
            if (disposed) return;
            const string reason = "Audio system configuration changed";
            foreach (var p in active.ToArray())
                Finish(p, p.Handle.State == AudioPlaybackState.Loading ? AudioCompletion.Cancelled : AudioCompletion.Failed, reason);
            // Reset invalidates old preparations and resident data assumptions.
            SetProvider(provider, true);
            Array.Clear(bankTweens, 0, bankTweens.Length); Array.Clear(busTweens, 0, busTweens.Length);
            foreach (var emitter in emitters) emitter.AppliedGain = float.NaN;
            RefreshRouting(); RefreshGains();
        }
        public AudioDiagnostics Diagnostics
        {
            get
            {
                int playing = 0, paused = 0, loading = 0;
                foreach (var p in active) { if (p.Handle.State == AudioPlaybackState.Loading) loading++; else if (p.Handle.State == AudioPlaybackState.Paused) paused++; else playing++; }
                return new AudioDiagnostics(playing, paused, loading, pool.Count, emitters.Count, store.CachedCount, store.UserCount, store.LoadingCount, LastFailure, preparations.Count, gamePaused, backgroundPaused);
            }
        }
        public void Shutdown()
        {
            using var mutation = callbacks.Begin();
            if (disposed) return; disposed = true;
            foreach (var p in active.ToArray()) Finish(p, p.Handle.State == AudioPlaybackState.Loading ? AudioCompletion.Cancelled : AudioCompletion.Stopped);
            foreach (var preparation in preparations.ToArray()) FinishPreparation(preparation, false);
            store.Dispose();
            externalIds = null;
            foreach (var e in emitters) if (!e.Reserved && e.Source != null) UnityEngine.Object.Destroy(e.Source.gameObject);
            pool.Clear();
        }
    }
}
