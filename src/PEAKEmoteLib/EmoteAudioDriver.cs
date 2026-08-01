using System;
using System.Linq;
using UnityEngine;
using UnityEngine.Audio;

namespace PEAKEmoteLib;

/// <summary>
/// Starts bundle audio after sample data is ready. Local-character music is
/// listener-relative while remote music remains positional. Dance audio can be
/// routed through PEAK's Music mixer so the game's Music slider controls it.
/// </summary>
internal sealed class EmoteAudioDriver : MonoBehaviour
{
    private const float ForcedStreamingStartDelay = 2.5f;
    private const float RetryInterval = 0.25f;
    private const float GiveUpDelay = 8f;
    private const float VolumeRouteRefreshInterval = 2f;

    private AudioSource source = null!;
    private Character character = null!;
    private Transform originalParent = null!;
    private AudioClip? pendingClip;
    private string pendingDisplayName = string.Empty;
    private bool pendingLoop;
    private float requestedAt;
    private float nextStartAttemptAt;
    private int startAttempts;
    private bool listenerRelative;

    private float volumeMultiplier = 1f;
    private AudioMixerGroup? gameMusicMixerGroup;
    private AudioSource? gameMusicReferenceSource;
    private float nextVolumeRouteRefreshAt;
    private string volumeRouteDescription = "standalone";
    private string lastLoggedVolumeRoute = string.Empty;
    private bool missingRouteWarningLogged;

    public void Initialize(AudioSource audioSource, Character owner)
    {
        source = audioSource;
        character = owner;
        originalParent = audioSource.transform.parent;
        enabled = false;
    }

    public void Play(Emote emote)
    {
        StopPlayback();

        AudioClip? clip = emote.AudioClip;
        if (clip == null)
        {
            DanceLog.Info($"Custom emote '{emote.DisplayName}' has no paired music.");
            return;
        }
        if (!emote.AudioEnabled)
        {
            DanceLog.Info(
                $"Custom emote '{emote.DisplayName}' has paired music '{clip.name}', but music is disabled by configuration.");
            return;
        }

        volumeMultiplier = Mathf.Clamp01(emote.AudioVolume);
        PrepareOutput(emote);
        source.clip = clip;
        source.loop = emote.LoopAudio;
        source.minDistance = Mathf.Max(0.1f, emote.AudioMinDistance);
        source.maxDistance = Mathf.Max(source.minDistance + 0.1f, emote.AudioMaxDistance);
        RefreshGameMusicVolumeRoute(true);
        ApplyEffectiveVolume();

        pendingClip = clip;
        pendingDisplayName = emote.DisplayName;
        pendingLoop = emote.LoopAudio;
        requestedAt = Time.time;
        nextStartAttemptAt = requestedAt;
        startAttempts = 0;

        if (clip.loadState == AudioDataLoadState.Unloaded && !clip.LoadAudioData())
        {
            DanceLog.Warning($"Music '{clip.name}' could not explicitly start loading for '{emote.DisplayName}'; streaming playback will still be attempted.");
        }

        if (clip.loadState == AudioDataLoadState.Failed)
        {
            FailPending($"Music '{clip.name}' failed to load for '{emote.DisplayName}'.");
            return;
        }

        if (clip.loadState == AudioDataLoadState.Loaded)
        {
            StartPendingAtSynchronizedTime();
            return;
        }

        enabled = true;
        DanceLog.Debug($"Waiting for music '{clip.name}' to finish loading for '{emote.DisplayName}'.");
    }

    private void OnDestroy()
    {
        StopPlayback();
        if (source != null)
        {
            Destroy(source.gameObject);
        }
    }

    public void StopPlayback()
    {
        enabled = false;
        pendingClip = null;
        pendingDisplayName = string.Empty;
        pendingLoop = false;
        requestedAt = 0f;
        nextStartAttemptAt = 0f;
        startAttempts = 0;
        listenerRelative = false;
        nextVolumeRouteRefreshAt = 0f;
        if (source == null)
        {
            return;
        }

        source.Stop();
        source.clip = null;
        if (originalParent != null && source.transform.parent != originalParent)
        {
            source.transform.SetParent(originalParent, false);
            source.transform.localPosition = Vector3.zero;
            source.transform.localRotation = Quaternion.identity;
        }
    }

    private void PrepareOutput(Emote emote)
    {
        source.gameObject.SetActive(true);
        source.enabled = true;
        source.playOnAwake = false;
        source.mute = false;
        source.priority = 128;
        source.pitch = 1f;
        source.panStereo = 0f;
        source.dopplerLevel = 0f;
        source.reverbZoneMix = 0f;
        source.spatialize = false;
        // Do not bypass the game's audio chain. Earlier builds explicitly set
        // outputAudioMixerGroup=null and bypassEffects=true, which guaranteed
        // that the Music slider could never affect dance audio.
        source.bypassEffects = false;
        source.bypassListenerEffects = false;
        source.bypassReverbZones = true;
        source.ignoreListenerPause = false;

        listenerRelative = Character.localCharacter != null && character == Character.localCharacter;
        if (listenerRelative)
        {
            AudioListener? listener = FindActiveListener();
            if (listener != null)
            {
                source.transform.SetParent(listener.transform, false);
                source.transform.localPosition = Vector3.zero;
                source.transform.localRotation = Quaternion.identity;
            }
            source.spatialBlend = 0f;
            source.rolloffMode = AudioRolloffMode.Linear;
        }
        else
        {
            if (originalParent != null && source.transform.parent != originalParent)
            {
                source.transform.SetParent(originalParent, false);
            }
            source.transform.localPosition = Vector3.zero;
            source.transform.localRotation = Quaternion.identity;
            source.spatialBlend = Mathf.Clamp01(emote.AudioSpatialBlend);
            source.rolloffMode = AudioRolloffMode.Linear;
        }
    }

    private static AudioListener? FindActiveListener()
    {
        foreach (AudioListener listener in UnityEngine.Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None))
        {
            if (listener != null && listener.isActiveAndEnabled)
            {
                return listener;
            }
        }
        return null;
    }

    private void Update()
    {
        RefreshGameMusicVolumeRoute(false);
        ApplyEffectiveVolume();

        AudioClip? clip = pendingClip;
        if (clip == null)
        {
            enabled = source != null && source.isPlaying && RuntimeOptions.FollowGameMusicVolume;
            return;
        }

        if (clip.loadState == AudioDataLoadState.Failed)
        {
            FailPending($"Music '{clip.name}' failed while loading for '{pendingDisplayName}'.");
            return;
        }

        if (Time.time < nextStartAttemptAt)
        {
            return;
        }

        if (clip.loadState == AudioDataLoadState.Loaded)
        {
            StartPendingAtSynchronizedTime();
            return;
        }

        if (Time.time - requestedAt >= ForcedStreamingStartDelay)
        {
            StartPendingAtSynchronizedTime(true);
        }
    }

    private void RefreshGameMusicVolumeRoute(bool force)
    {
        if (source == null)
        {
            return;
        }

        if (!RuntimeOptions.FollowGameMusicVolume)
        {
            gameMusicMixerGroup = null;
            gameMusicReferenceSource = null;
            source.outputAudioMixerGroup = null;
            volumeRouteDescription = "standalone-config-volume";
            return;
        }

        if (!force && Time.unscaledTime < nextVolumeRouteRefreshAt)
        {
            return;
        }
        nextVolumeRouteRefreshAt = Time.unscaledTime + VolumeRouteRefreshInterval;

        AudioMixerGroup? bestGroup = null;
        AudioSource? bestReference = null;
        int bestGroupScore = int.MinValue;
        int bestReferenceScore = int.MinValue;

        foreach (AudioSource candidate in Resources.FindObjectsOfTypeAll<AudioSource>())
        {
            if (candidate == null || candidate == source || candidate.gameObject == null ||
                !candidate.gameObject.scene.IsValid() ||
                candidate.gameObject.name.IndexOf("PEAKEmoteLib", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                continue;
            }

            AudioMixerGroup? group = candidate.outputAudioMixerGroup;
            int score = ScoreMusicText(candidate.name) + ScoreMusicText(candidate.gameObject.name);
            if (candidate.clip != null)
            {
                score += ScoreMusicText(candidate.clip.name) / 2;
            }
            if (group != null)
            {
                score += ScoreMusicText(group.name) * 2;
                if (group.audioMixer != null)
                {
                    score += ScoreMusicText(group.audioMixer.name);
                }
            }

            if (group != null && score > bestGroupScore)
            {
                bestGroupScore = score;
                bestGroup = group;
            }

            if (candidate.isActiveAndEnabled && candidate.gameObject.activeInHierarchy && score > bestReferenceScore)
            {
                // Only an active scene AudioSource is safe as a volume fallback.
                // Resources.FindObjectsOfTypeAll also returns disabled objects and
                // prefab-like instances whose serialized volume is commonly zero.
                bestReferenceScore = score;
                bestReference = candidate;
            }
        }

        // Some scenes have the mixer loaded before their music AudioSource.
        if (bestGroup == null || bestGroupScore < 100)
        {
            foreach (AudioMixerGroup group in Resources.FindObjectsOfTypeAll<AudioMixerGroup>())
            {
                if (group == null)
                {
                    continue;
                }
                int score = ScoreMusicText(group.name) * 2;
                if (group.audioMixer != null)
                {
                    score += ScoreMusicText(group.audioMixer.name);
                }
                if (score > bestGroupScore)
                {
                    bestGroupScore = score;
                    bestGroup = group;
                }
            }
        }

        if (bestGroup != null && bestGroupScore >= 100)
        {
            gameMusicMixerGroup = bestGroup;
            gameMusicReferenceSource = null;
            source.outputAudioMixerGroup = bestGroup;
            string mixerName = bestGroup.audioMixer == null ? "unknown-mixer" : bestGroup.audioMixer.name;
            volumeRouteDescription = $"music-mixer:{mixerName}/{bestGroup.name}";
            missingRouteWarningLogged = false;
        }
        else if (bestReference != null && bestReferenceScore >= 80)
        {
            gameMusicMixerGroup = null;
            gameMusicReferenceSource = bestReference;
            source.outputAudioMixerGroup = null;
            volumeRouteDescription = $"music-source-volume:{bestReference.gameObject.name}";
            missingRouteWarningLogged = false;
        }
        else
        {
            gameMusicMixerGroup = null;
            gameMusicReferenceSource = null;
            source.outputAudioMixerGroup = null;
            volumeRouteDescription = "standalone-no-reliable-music-route";
            if (!missingRouteWarningLogged)
            {
                missingRouteWarningLogged = true;
                DanceLog.Warning(
                    "Could not find a reliable active PEAK Music mixer/source in the current scene. Dance audio is using its configured multiplier temporarily; " +
                    "the route will be searched again while playback continues.");
            }
        }

        if (!string.Equals(lastLoggedVolumeRoute, volumeRouteDescription, StringComparison.Ordinal))
        {
            lastLoggedVolumeRoute = volumeRouteDescription;
            DanceLog.Info(
                $"Dance-music volume route selected: {volumeRouteDescription}; configuredMultiplier={volumeMultiplier:0.00}.");
        }
    }

    private static int ScoreMusicText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return 0;
        }

        string normalized = new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
        int score = 0;
        if (normalized.Contains("mainmusic")) score += 180;
        if (normalized.Contains("music")) score += 90;
        if (normalized.Contains("bgm")) score += 80;
        if (normalized.Contains("soundtrack")) score += 70;
        if (normalized.Contains("sfx") || normalized.Contains("voice") || normalized.Contains("dialog")) score -= 120;
        return score;
    }

    private void ApplyEffectiveVolume()
    {
        if (source == null)
        {
            return;
        }

        float gameMusicFactor = 1f;
        if (RuntimeOptions.FollowGameMusicVolume && gameMusicMixerGroup == null && gameMusicReferenceSource != null)
        {
            gameMusicFactor = gameMusicReferenceSource.mute
                ? 0f
                : Mathf.Clamp01(gameMusicReferenceSource.volume);
        }
        source.volume = Mathf.Clamp01(volumeMultiplier * gameMusicFactor);
    }

    private void StartPendingAtSynchronizedTime(bool allowUnloadedStreaming = false)
    {
        AudioClip? clip = pendingClip;
        if (clip == null || source == null)
        {
            enabled = false;
            return;
        }

        if (!allowUnloadedStreaming && clip.loadState != AudioDataLoadState.Loaded)
        {
            return;
        }

        float elapsed = Mathf.Max(0f, Time.time - requestedAt);
        if (!pendingLoop && clip.length > 0.05f && elapsed >= clip.length)
        {
            DanceLog.Warning(
                $"Music '{clip.name}' became playable after one-shot emote '{pendingDisplayName}' had already ended; skipping stale playback.");
            StopPlayback();
            return;
        }

        float playbackTime = 0f;
        if (clip.length > 0.05f)
        {
            playbackTime = pendingLoop ? elapsed % clip.length : Mathf.Min(elapsed, clip.length - 0.01f);
        }

        try
        {
            source.time = playbackTime;
        }
        catch (Exception exception)
        {
            DanceLog.Debug($"Could not seek music '{clip.name}' to {playbackTime:0.00}s: {exception.Message}");
        }

        startAttempts++;
        ApplyEffectiveVolume();
        source.Play();
        if (!source.isPlaying)
        {
            if (elapsed >= GiveUpDelay)
            {
                FailPending(
                    $"AudioSource did not enter playback for music '{clip.name}' after {startAttempts} attempt(s) over {elapsed:0.00}s.");
                return;
            }

            nextStartAttemptAt = Time.time + RetryInterval;
            enabled = true;
            if (startAttempts == 1 || startAttempts % 8 == 0)
            {
                DanceLog.Warning(
                    $"AudioSource has not started music '{clip.name}' for '{pendingDisplayName}' yet; retrying (attempt {startAttempts}, state={clip.loadState}).");
            }
            return;
        }

        float actualOffset = playbackTime;
        try
        {
            actualOffset = source.time;
        }
        catch
        {
        }

        DanceLog.Info(
            $"Started music '{clip.name}' for '{pendingDisplayName}' " +
            $"(state={clip.loadState}, attempts={startAttempts}, offset={actualOffset:0.00}s, " +
            $"volumeMultiplier={volumeMultiplier:0.00}, effectiveSourceVolume={source.volume:0.00}, " +
            $"volumeRoute={volumeRouteDescription}, local2D={listenerRelative}, spatialBlend={source.spatialBlend:0.00}, " +
            $"listenerVolume={AudioListener.volume:0.00}, listenerPaused={AudioListener.pause}, host='{source.gameObject.name}').");

        pendingClip = null;
        pendingDisplayName = string.Empty;
        pendingLoop = false;
        enabled = RuntimeOptions.FollowGameMusicVolume && source.isPlaying;
    }

    private void FailPending(string message)
    {
        DanceLog.Warning(message);
        StopPlayback();
    }
}
