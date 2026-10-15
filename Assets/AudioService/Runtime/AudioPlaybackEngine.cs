using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace Controller.Audio
{
    internal enum PlaybackSlot
    {
        None,
        Bgm,
        Dialogue
    }

    internal sealed class AudioPlaybackEngine
    {
        internal enum TweenCompletion
        {
            None,
            StartPending,
            Stop
        }

        internal struct Tween
        {
            public float Start, Target, Duration, Elapsed;
            public TweenCompletion Completion;
            public Playback Playback;
            public bool Active => Duration > 0;

            /// <summary>Move between two values; nothing runs at the end.</summary>
            public static Tween Fade(float from, float to, float seconds)
                => new Tween { Start = from, Target = to, Duration = seconds };

            /// <summary>Fade to silence, then run the completion for playback.</summary>
            public static Tween FadeOut(float from, float seconds, TweenCompletion then, Playback playback)
                => new Tween { Start = from, Target = 0, Duration = seconds, Completion = then, Playback = playback };

            /// <summary>True when this tween will run the given completion for playback.</summary>
            public bool Is(TweenCompletion completion, Playback playback)
                => Completion == completion && ReferenceEquals(Playback, playback);

            public float Advance(float delta)
            {
                Elapsed += delta;
                return Mathf.Lerp(Start, Target, Duration <= 0 ? 1 : Mathf.Clamp01(Elapsed / Duration));
            }

            public bool Done => Elapsed >= Duration;
        }

        internal sealed class Emitter
        {
            public AudioSource Source;
            public bool InPool;
            public int UpdateIndex = -1;
            public float AppliedGain = float.NaN;
            public float Envelope = 1f;
            public Tween Transition;
            public Playback Current, Pending;
            public bool InUse => Current != null || Pending != null;
        }

        // What keeps a playback's clip valid: a lease from an asynchronous store delivery,
        // a resident store usage (no allocation), or nothing for an externally owned clip.
        internal struct ClipHold
        {
            private AudioClipLease lease;
            private AudioClipStore.Usage usage;
            public AudioClip Clip { get; private set; }

            public static ClipHold External(AudioClip clip) => new ClipHold { Clip = clip };
            public static ClipHold Of(AudioClipLease lease) => new ClipHold { lease = lease, Clip = lease?.Clip };
            public static ClipHold Of(AudioClipStore.Usage usage) => new ClipHold { usage = usage, Clip = usage.Clip };

            // A playback keeps its hold in exactly one field; clear that field before releasing.
            public void Release()
            {
                var heldUsage = usage;
                var heldLease = lease;
                this = default;
                heldUsage.Dispose();
                AudioCallbacks.Dispose(heldLease);
            }
        }

        internal sealed class Playback
        {
            public AudioHandle Handle;
            public PlaybackOptions Options;
            public ClipHold Hold;
            public Action CancelLoad;
            public Emitter Emitter;
            public PlaybackSlot Slot;
            public int StartedFrame;
        }

        // A replacement slot (BGM or dialogue): the sound playing in it and the newer request
        // waiting to replace it. The engine changes either role only through these methods.
        private sealed class SlotState
        {
            public Playback Current { get; private set; }
            public Playback Pending { get; private set; }

            public void Reserve(Playback playback) => Pending = playback;

            public void Promote(Playback playback)
            {
                Pending = null;
                Current = playback;
            }

            public void Leave(Playback playback)
            {
                if (Pending == playback)
                    Pending = null;
                if (Current == playback)
                    Current = null;
            }
        }

        // Temporary per-channel controls; saved player volume lives in AudioMixerState.
        private struct Bus
        {
            public float Gain;
            public Tween Fade;
            public bool Muted;
        }

        private readonly AudioPreparationQueue preparations;
        private readonly AudioCallbackQueue callbacks = new();
        internal void NotifyCompleted(Action callback) => callbacks.Enqueue(callback);
        internal AudioCallbackQueue.Scope BeginMutation() => callbacks.Begin();
        private readonly AudioController host;
        // Accepted requests in admission order, including Loading and Paused.
        private readonly List<Playback> active = new();
        private readonly Stack<List<Playback>> snapshots = new();
        private readonly AudioAdmission admission;
        private readonly AudioSourcePool sources;
        private readonly SlotState bgm = new();
        private readonly SlotState dialogue = new();
        private SlotState GetSlot(PlaybackSlot slot) => slot switch
        {
            PlaybackSlot.Bgm => bgm,
            PlaybackSlot.Dialogue => dialogue,
            _ => null
        };
        private readonly Bus[] buses = new Bus[AudioMixerLayout.ChannelCount];
        private long nextId;
        private bool disposed, gamePaused, backgroundPaused, listenerPaused;
        // New work needs an engine that is not shut down and a host that is initialized.
        private bool Usable => !disposed && host.Ready;
        private AudioClipStore store;
        private IAudioClipProvider provider;
        private ConditionalWeakTable<AudioClip, StrongBox<AudioId>> externalIds;
        private static readonly ConditionalWeakTable<AudioClip, StrongBox<AudioId>>.CreateValueCallback CreateExternalId
            = clip => new StrongBox<AudioId>(new AudioId("external:" + clip.GetEntityId().ToString()));
        private AudioId Identify(AudioClip clip)
        {
            // Cache by managed identity without extending the caller's clip lifetime.
            externalIds ??= new ConditionalWeakTable<AudioClip, StrongBox<AudioId>>();
            return externalIds.GetValue(clip, CreateExternalId).Value;
        }

        public string LastFailure { get; private set; }
        public int MaxConcurrentSounds
        {
            get => admission.MaxConcurrentSounds;
            set => admission.MaxConcurrentSounds = value;
        }

        public AudioConcurrencyPolicy ConcurrencyPolicy
        {
            get => admission.ConcurrencyPolicy;
            set => admission.ConcurrencyPolicy = value;
        }

        public AudioPlaybackEngine(AudioController host, IAudioClipProvider provider)
        {
            this.host = host;
            admission = new AudioAdmission(active);
            sources = new AudioSourcePool(host.transform);
            Array.Fill(buses, new Bus { Gain = 1 });
            listenerPaused = AudioListener.pause;
            preparations = new AudioPreparationQueue(callbacks);
            SetProvider(provider);
        }

        /// <summary>Use another provider. The same provider keeps the current store.</summary>
        public void SetProvider(IAudioClipProvider next)
        {
            if (!ReferenceEquals(provider, next) || store == null)
                ResetStore(next);
        }

        /// <summary>Start a new store generation, cancelling pending loads and preparations.
        /// Playing sounds keep their leases from the previous store.</summary>
        public void ResetStore(IAudioClipProvider next)
        {
            using var mutation = callbacks.Begin();
            if (disposed)
                return;
            // Publish the new generation before cancellation callbacks can request work.
            var previousStore = store;
            var previousRequests = active.ToArray();
            provider = next;
            store = new AudioClipStore(provider, callbacks);
            foreach (var playback in previousRequests)
                if (playback.Handle.State == AudioPlaybackState.Loading)
                    Finish(playback, AudioCompletion.Cancelled);
            preparations.CancelAll();
            previousStore?.Dispose();
        }

        /// <summary>Reset the store, then let the provider publish fresh lookups within the same mutation.</summary>
        public void RefreshLookup(IAudioClipProvider next)
        {
            using var mutation = callbacks.Begin();
            ResetStore(next);
            if (!disposed && AudioValues.Alive(next) && next is IAudioClipProviderRefresh refreshable)
                AudioCallbacks.Invoke(refreshable.RefreshClipLookup);
        }

        private Playback Find(AudioHandle handle) => ReferenceEquals(handle.Owner, this) ? handle.Playback : null;
        private List<Playback> RentSnapshot() => snapshots.Count > 0 ? snapshots.Pop() : new List<Playback>(active.Count);
        private void ReturnSnapshot(List<Playback> snapshot)
        {
            snapshot.Clear();
            snapshots.Push(snapshot);
        }

        private void AddActive(Playback playback)
        {
            active.Add(playback);
            playback.Handle.Playback = playback;
            admission.Added(playback);
        }

        private void RemoveActive(Playback playback)
        {
            playback.Handle.Playback = null;
            if (active.Remove(playback))
                admission.Removed(playback);
        }

        public int PrewarmSources(int targetCount)
        {
            if (!Usable || targetCount <= 0)
                return 0;
            int created = sources.Prewarm(targetCount);
            // Reserve playback bookkeeping as well as native sources.
            if (created > 0 && active.Capacity < sources.Count)
                active.Capacity = sources.Count;
            return created;
        }

        public int TrimIdleSources(int minimumCapacity, int maxToRemove)
        {
            if (!Usable || maxToRemove <= 0)
                return 0;
            return sources.Trim(Mathf.Max(0, minimumCapacity), maxToRemove);
        }

        /// <summary>Play an externally owned clip. The service never loads or unloads it.</summary>
        public AudioHandle Play(AudioCategory category, AudioClip clip, PlayOptions options, PlaybackSlot slot)
        {
            using var mutation = callbacks.Begin();
            var handle = NewHandle(category, Identify(clip));
            var playback = Admit(handle, options, slot, 0);
            if (playback != null)
                Loaded(playback, ClipHold.External(clip));
            return handle;
        }

        /// <summary>Play the clip at a resolved address. A resident clip starts at once; any other loads first.</summary>
        public AudioHandle Play(ResolvedAudioClip address, PlayOptions options, PlaybackSlot slot)
        {
            using var mutation = callbacks.Begin();
            var handle = NewHandle(address.Category, new AudioId(address.Id));
            var playback = Admit(handle, options, slot, address.MaxInstances);
            if (playback == null)
                return handle;
            // Loading reserves admission, not a native AudioSource.
            if (store.TryAcquireResident(address.Category, address.Id, out var usage))
                Loaded(playback, ClipHold.Of(usage));
            else
                RequestClip(playback, address);
            return handle;
        }

        private AudioHandle NewHandle(AudioCategory category, AudioId id) => new AudioHandle
        {
            Id = ++nextId,
            AudioId = id,
            Category = category,
            Owner = this
        };

        // Validate and admit a request. Returns the accepted playback, already active and
        // holding its slot, or null after finishing the handle as failed or rejected.
        private Playback Admit(AudioHandle handle, PlayOptions supplied, PlaybackSlot slot, int catalogLimit)
        {
            string invalid = InvalidRequest(handle);
            if (invalid != null)
            {
                Reject(handle, AudioCompletion.Failed, invalid);
                return null;
            }

            // PlayOptions.MaxInstances wins; otherwise the catalog entry's limit applies.
            int maxInstances = Mathf.Max(0, supplied?.MaxInstances ?? 0);
            if (maxInstances == 0)
                maxInstances = Mathf.Max(0, catalogLimit);
            string rejected = admission.Plan(handle, slot, maxInstances, supplied?.ConcurrencyPolicy ?? AudioConcurrencyPolicy.RejectNew, out var victims);
            if (rejected != null)
            {
                Reject(handle, AudioCompletion.Rejected, rejected);
                return null;
            }

            return StartAccepted(handle, supplied, slot, victims);
        }

        private string InvalidRequest(AudioHandle handle)
        {
            if (!Usable)
                return AudioHandle.NotReadyReason;
            if (!AudioValues.IsValidCategory(handle.Category))
                return "Invalid AudioCategory. Use Bgm, Sfx or Voice";
            if (string.IsNullOrWhiteSpace(handle.AudioId.Value))
                return "Empty audio ID and no AudioClip. Pass a non-empty ID or an existing clip";
            return null;
        }

        private Playback StartAccepted(AudioHandle handle, PlayOptions supplied, PlaybackSlot slot, Playback[] victims)
        {
            // Snapshot before eviction can release a provider lease or invoke external code.
            var options = new PlaybackOptions(supplied, slot == PlaybackSlot.Bgm);
            var playback = new Playback
            {
                Handle = handle,
                Options = options,
                Slot = slot
            };
            if (victims != null)
                foreach (var victim in victims)
                    Finish(victim, StopResult(victim));
            var state = GetSlot(slot);
            if (state != null)
            {
                if (state.Pending != null)
                    Finish(state.Pending, AudioCompletion.Cancelled);
                state.Reserve(playback);
            }

            AddActive(playback);
            return playback;
        }

        private void RequestClip(Playback playback, ResolvedAudioClip address)
        {
            // Isolate the load closure from direct playback and rejected requests.
            var cancel = store.Request(address, playback.Options.AllowAsyncLoad, lease => Loaded(playback, ClipHold.Of(lease), address));
            if (!playback.Handle.IsFinished && playback.Hold.Clip == null)
                playback.CancelLoad = cancel;
        }

        private void Reject(AudioHandle handle, AudioCompletion result, string reason)
        {
            RecordFailure(reason);
            handle.Finish(result, reason);
            if (result == AudioCompletion.Failed)
                host.ReportPlaybackFailure(handle.Category, handle.AudioId, reason);
        }

        private void RecordFailure(string reason)
        {
            LastFailure = reason;
            host.LogVerbose(reason);
        }

        private void Loaded(Playback playback, ClipHold hold, ResolvedAudioClip? address = null)
        {
            using var mutation = callbacks.Begin();
            if (disposed || playback.Handle.IsFinished)
            {
                hold.Release();
                return;
            }

            SyncListenerPause();
            playback.CancelLoad = null;
            if (hold.Clip == null)
            {
                hold.Release();
                string reason = "Clip load failed: " + playback.Handle.AudioId;
                Finish(playback, AudioCompletion.Failed, reason);
                host.ReportPlaybackFailure(playback.Handle.Category, playback.Handle.AudioId, reason, address, playback.Options.AllowAsyncLoad);
                return;
            }

            playback.Hold = hold;
            var state = GetSlot(playback.Slot);
            playback.Emitter = state?.Current?.Emitter ?? sources.Rent();
            playback.Emitter.Pending = playback;
            var emitter = playback.Emitter;
            if (FadesOutCurrentMusic(playback, emitter))
                emitter.Transition = Tween.FadeOut(emitter.Envelope, playback.Options.FadeOutSeconds, TweenCompletion.StartPending, playback);
            else
                Start(playback);
        }

        // BGM replacement fades the audible current music out before starting the new one.
        private static bool FadesOutCurrentMusic(Playback incoming, Emitter emitter)
            => incoming.Slot == PlaybackSlot.Bgm && incoming.Options.FadeOutSeconds > 0
                && emitter.Current != null && emitter.Source.isPlaying;

        // Only the request still waiting for its source starts; a finished or replaced one does not.
        private bool CanStart(Playback playback)
            => !disposed && !playback.Handle.IsFinished && playback.Emitter.Pending == playback;

        private void Start(Playback playback)
        {
            if (!CanStart(playback))
                return;
            var emitter = playback.Emitter;
            // This playback is the one waiting for the source, so retire the current sound
            // without starting its waiter again.
            if (emitter.Current != null)
                Retire(emitter.Current, AudioCompletion.Stopped);
            if (!CanStart(playback))
                return;
            GetSlot(playback.Slot)?.Promote(playback);
            emitter.Pending = null;
            emitter.Current = playback;
            emitter.Transition = default;
            emitter.Envelope = playback.Options.FadeInSeconds > 0 ? 0 : 1;
            var source = emitter.Source;
            host.ConfigureSource(source, playback.Handle.Category);
            source.clip = playback.Hold.Clip;
            source.loop = playback.Options.Loop;
            source.pitch = playback.Options.Pitch;
            source.ignoreListenerPause = playback.Options.IgnoreGamePause;
            ApplyGain(emitter);
            source.Play();
            playback.StartedFrame = Time.frameCount;
            playback.Handle.State = AudioPlaybackState.Playing;
            ApplyPause(playback);
            if (playback.Options.FadeInSeconds > 0)
                emitter.Transition = Tween.Fade(0, 1, playback.Options.FadeInSeconds);
        }

        private void Finish(Playback playback, AudioCompletion result, string reason = null)
        {
            var waiting = Retire(playback, result, reason);
            if (waiting != null && !disposed)
                Start(waiting);
        }

        // End the playback and release what it holds. Returns the request that was waiting
        // for its source; Finish starts that request, Start retires a sound to take its source.
        private Playback Retire(Playback playback, AudioCompletion result, string reason = null)
        {
            if (playback.Handle.IsFinished)
                return null;
            // Commit every state change before releases and notifications can reenter.
            RemoveActive(playback);
            var cancel = playback.CancelLoad;
            playback.CancelLoad = null;
            var hold = playback.Hold;
            playback.Hold = default;
            GetSlot(playback.Slot)?.Leave(playback);
            var waiting = LeaveEmitter(playback);
            if (reason != null)
                RecordFailure(reason);
            playback.Handle.Finish(result, reason);
            // Store leases update internal counts now. Actual provider releases
            // are queued by the store until this entire engine mutation commits.
            AudioCallbacks.Invoke(cancel);
            hold.Release();
            return waiting;
        }

        // Detach from the native source and recycle it once unused. Returns the request
        // waiting on that source when the finished playback was its current sound.
        private Playback LeaveEmitter(Playback playback)
        {
            var emitter = playback.Emitter;
            playback.Emitter = null;
            if (emitter == null)
                return null;
            Playback waiting = null;
            if (emitter.Pending == playback)
            {
                emitter.Pending = null;
                RestoreCancelledFade(emitter, playback);
            }

            if (emitter.Current == playback)
            {
                emitter.Source.Stop();
                emitter.Source.clip = null;
                emitter.Current = null;
                emitter.Transition = default;
                emitter.Envelope = 1;
                waiting = emitter.Pending;
            }

            sources.Recycle(emitter);
            return waiting;
        }

        // Return the current music from its partial fade at the cancelled fade's speed
        // instead of jumping to full volume. A later stop or replacement starts from this
        // envelope; another owner's stop transition stays.
        private static void RestoreCancelledFade(Emitter emitter, Playback cancelled)
        {
            ref var transition = ref emitter.Transition;
            if (!transition.Is(TweenCompletion.StartPending, cancelled))
                return;
            transition = Tween.Fade(emitter.Envelope, 1, (1 - emitter.Envelope) * transition.Duration);
        }

        public bool Stop(AudioHandle handle, float seconds)
        {
            using var mutation = callbacks.Begin();
            SyncListenerPause();
            var p = Find(handle);
            if (p == null)
                return false;
            StopPlayback(p, seconds);
            return true;
        }

        private void StopPlayback(Playback p, float seconds)
        {
            if (p.Handle.IsFinished)
                return;
            // Only a playing sound fades; loading, paused or zero-time stops finish now.
            if (p.Handle.State == AudioPlaybackState.Playing && seconds > 0)
                p.Emitter.Transition = Tween.FadeOut(p.Emitter.Envelope, seconds, TweenCompletion.Stop, p);
            else
                Finish(p, StopResult(p));
        }

        // Stopping a request that has not started is a cancellation.
        private static AudioCompletion StopResult(Playback playback)
            => playback.Handle.State == AudioPlaybackState.Loading ? AudioCompletion.Cancelled : AudioCompletion.Stopped;

        public bool Pause(AudioHandle handle, bool pause)
        {
            SyncListenerPause();
            var p = Find(handle);
            if (p == null)
                return false;
            handle.UserPaused = pause;
            ApplyPause(p);
            return true;
        }

        private bool Paused(Playback p) => p.Handle.UserPaused || backgroundPaused || ((gamePaused || listenerPaused) && !p.Options.IgnoreGamePause);
        private void SyncListenerPause()
        {
            bool value = AudioListener.pause;
            if (listenerPaused == value)
                return;
            listenerPaused = value;
            foreach (var p in active)
                ApplyPause(p);
        }

        private void ApplyPause(Playback p)
        {
            if (p.Handle.State == AudioPlaybackState.Loading || p.Handle.IsFinished)
                return;
            bool shouldPause = Paused(p);
            if (shouldPause == (p.Handle.State == AudioPlaybackState.Paused))
                return;
            if (shouldPause)
                p.Emitter.Source.Pause();
            else
                p.Emitter.Source.UnPause();
            p.Handle.State = shouldPause ? AudioPlaybackState.Paused : AudioPlaybackState.Playing;
        }

        public void SetGamePaused(bool paused) => SetPauseFlag(ref gamePaused, paused);
        public void SetBackgroundPaused(bool paused) => SetPauseFlag(ref backgroundPaused, paused);

        private void SetPauseFlag(ref bool flag, bool paused)
        {
            if (flag == paused)
                return;
            flag = paused;
            // ApplyPause only updates native sources and handle state; no external callbacks.
            foreach (var p in active)
                ApplyPause(p);
        }

        public void SetMuted(AudioChannel channel, bool value)
        {
            ref var bus = ref buses[(int)channel];
            if (bus.Muted == value)
                return;
            bus.Muted = value;
            RefreshGains(AudioMixerLayout.MaskOf(channel));
        }

        public AudioChannelDiagnostics GetChannelDiagnostics(AudioChannel channel, float volume)
        {
            ref readonly var bus = ref buses[(int)channel];
            return new AudioChannelDiagnostics(volume, bus.Gain, bus.Muted);
        }

        /// <summary>Stop one category, or every category when null.</summary>
        public void StopCategory(AudioCategory? category, float seconds)
        {
            using var mutation = callbacks.Begin();
            SyncListenerPause();
            var selected = RentSnapshot();
            try
            {
                foreach (var p in active)
                    if (!category.HasValue || p.Handle.Category == category)
                        selected.Add(p);
                foreach (var p in selected)
                    if (p.Handle.State == AudioPlaybackState.Loading)
                        StopPlayback(p, 0);
                foreach (var p in selected)
                    StopPlayback(p, seconds);
            }
            finally
            {
                ReturnSnapshot(selected);
            }
        }

        public void FadeBus(AudioChannel channel, float target, float seconds)
        {
            ref var bus = ref buses[(int)channel];
            if (seconds <= 0)
            {
                bus.Fade = default;
                bus.Gain = target;
            }
            else
                bus.Fade = Tween.Fade(bus.Gain, target, seconds);
            RefreshGains(AudioMixerLayout.MaskOf(channel));
        }

        private static bool NeedsGain(Emitter emitter, CategoryMask categories)
            => emitter.Current != null && (categories & AudioMixerLayout.MaskOf(emitter.Current.Handle.Category)) != 0;
        private void ApplyGain(Emitter emitter)
        {
            if (emitter.Source == null || emitter.Current == null)
                return;
            var p = emitter.Current;
            var category = p.Handle.Category;
            ref readonly var master = ref buses[(int)AudioChannel.Master];
            ref readonly var bus = ref buses[(int)AudioMixerLayout.ChannelOf(category)];
            float gain = master.Muted || bus.Muted ? 0 : master.Gain * bus.Gain;
            gain *= p.Options.Volume * emitter.Envelope * host.SourceSettingsGain(category);
            gain = AudioValues.Unit(gain);
            if (emitter.AppliedGain == gain)
                return;
            emitter.Source.volume = gain;
            emitter.AppliedGain = gain;
        }

        public void RefreshRouting()
        {
            foreach (var e in sources.Active)
            {
                if (e.Current != null)
                    host.ConfigureSource(e.Source, e.Current.Handle.Category);
            }
        }

        public void RefreshGains(CategoryMask categories = CategoryMask.All)
        {
            foreach (var emitter in sources.Active)
                if (NeedsGain(emitter, categories))
                    ApplyGain(emitter);
        }

        private CategoryMask AdvanceBusFades(float delta)
        {
            var changedCategories = CategoryMask.None;
            for (int i = 0; i < buses.Length; i++)
            {
                ref var bus = ref buses[i];
                if (!bus.Fade.Active)
                    continue;
                changedCategories |= AudioMixerLayout.MaskOf(AudioMixerLayout.Channels[i]);
                bus.Gain = bus.Fade.Advance(delta);
                if (bus.Fade.Done)
                    bus.Fade = default;
            }

            return changedCategories;
        }

        private bool AdvanceTransition(Emitter emitter, float delta)
        {
            var current = emitter.Current;
            if (!emitter.Transition.Active || (current != null && Paused(current)))
                return false;

            ref var tween = ref emitter.Transition;
            emitter.Envelope = tween.Advance(delta);
            if (!tween.Done)
                return true;

            var completion = tween.Completion;
            var target = tween.Playback;
            tween = default;
            switch (completion)
            {
                case TweenCompletion.StartPending:
                    Start(target);
                    break;
                case TweenCompletion.Stop when ReferenceEquals(emitter.Current, target):
                    Finish(target, AudioCompletion.Stopped);
                    break;
            }

            return true;
        }

        public void Tick(float delta)
        {
            using var mutation = callbacks.Begin();
            if (disposed)
                return;
            SyncListenerPause();
            preparations.Tick();
            var changedCategories = AdvanceBusFades(delta);
            bool gainsChanged = changedCategories != CategoryMask.None;
            var updating = sources.Active;
            int iSource = 0;
            while (iSource < updating.Count)
            {
                var emitter = updating[iSource];
                bool envelopeChanged = AdvanceTransition(emitter, delta);

                if (envelopeChanged || (gainsChanged && NeedsGain(emitter, changedCategories)))
                    ApplyGain(emitter);
                if (EndedNaturally(emitter))
                    Finish(emitter.Current, AudioCompletion.Completed);
                // Recycle swaps the last active emitter into this position.
                if (iSource < updating.Count && ReferenceEquals(updating[iSource], emitter))
                    iSource++;
            }
        }

        // A playing one-shot whose native source stopped by itself. The start frame
        // and the next one are a grace period before isPlaying is trusted.
        private static bool EndedNaturally(Emitter emitter)
        {
            var p = emitter.Current;
            return p != null && p.Handle.State == AudioPlaybackState.Playing && !p.Options.Loop
                && Time.frameCount > p.StartedFrame + 1 && !emitter.Source.isPlaying;
        }

        public void PrepareClip(ResolvedAudioClip address, string group, Action<bool> completed)
        {
            using var mutation = callbacks.Begin();
            if (disposed)
            {
                callbacks.Enqueue(() => AudioCallbacks.Invoke(completed, false));
                return;
            }

            preparations.Prepare(store, address, group, completed);
        }

        public bool TryGetCached(ResolvedAudioClip address, out AudioClip clip) => store.TryGetCached(address, out clip);
        public void ReleaseGroup(string group)
        {
            using var mutation = callbacks.Begin();
            preparations.CancelGroup(group);
            store.ReleaseGroup(group);
        }

        public void ReleaseUnused() => store.ReleaseUnused();
        public void AudioConfigurationChanged()
        {
            using var mutation = callbacks.Begin();
            if (disposed)
                return;
            const string reason = "Audio system configuration changed";
            // Loops (including PlayBgm music) are continuous state and keep their handles.
            // One-shots and loops already being stopped end as before.
            foreach (var p in active.ToArray())
            {
                if (p.Handle.IsFinished)
                    continue;
                if (p.Handle.State == AudioPlaybackState.Loading)
                    Finish(p, AudioCompletion.Cancelled, reason);
                else if (!KeepsPlayingAfterReset(p))
                    Finish(p, AudioCompletion.Failed, reason);
            }

            // Reset invalidates old preparations and resident data assumptions.
            // Kept loops still own their leases from the previous store.
            ResetStore(provider);
            for (int i = 0; i < buses.Length; i++)
                buses[i].Fade = default;
            sources.ForgetAppliedGains();
            RefreshRouting();
            foreach (var p in active)
                RestartAfterReset(p);
            RefreshGains();
        }

        private static bool KeepsPlayingAfterReset(Playback p)
            => p.Options.Loop && !p.Emitter.Transition.Is(TweenCompletion.Stop, p);

        // A reset stops native sources. Replay a stopped loop from its start, keep
        // a source that survived, and restore the handle's paused state.
        private static void RestartAfterReset(Playback p)
        {
            var emitter = p.Emitter;
            emitter.Transition = default;
            emitter.Envelope = 1;
            var source = emitter.Source;
            bool paused = p.Handle.State == AudioPlaybackState.Paused;
            if (!paused && source.isPlaying)
                return;
            source.Stop();
            source.Play();
            if (paused)
                source.Pause();
        }

        public AudioDiagnostics Diagnostics
        {
            get
            {
                int playing = 0, paused = 0, loading = 0;
                foreach (var p in active)
                {
                    if (p.Handle.State == AudioPlaybackState.Loading)
                        loading++;
                    else if (p.Handle.State == AudioPlaybackState.Paused)
                        paused++;
                    else
                        playing++;
                }

                var (cached, users, pending) = store.Counts();
                return new AudioDiagnostics(playing, paused, loading, sources.IdleCount, sources.Count, cached, users, pending,
                    preparations.Count, LastFailure, gamePaused, backgroundPaused);
            }
        }

        public void Shutdown()
        {
            using var mutation = callbacks.Begin();
            if (disposed)
                return;
            disposed = true;
            foreach (var p in active.ToArray())
                Finish(p, StopResult(p));
            preparations.CancelAll();
            store.Dispose();
            externalIds = null;
            sources.DestroyAll();
        }
    }
}
