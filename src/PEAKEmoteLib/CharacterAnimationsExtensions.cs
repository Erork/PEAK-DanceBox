using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace PEAKEmoteLib;

/// <summary>
/// Per-character state and the runtime bridge between custom clips and PEAK's
/// real emote state. The override clip key and the Animator state name are
/// deliberately tracked separately: RPCA_PlayRemove calls Animator.Play with a
/// state name, while AnimatorOverrideController replaces a clip asset.
/// </summary>
internal static class CharacterAnimationsExtensions
{
    private static readonly ConditionalWeakTable<CharacterAnimations, Holder> Data = new();

    public static AnimatorOverrideController? EnsureAnimatorOverrideController(this CharacterAnimations characterAnimations)
    {
        Holder holder = Data.GetOrCreateValue(characterAnimations);
        Animator animator = characterAnimations.character.refs.animator;
        RuntimeAnimatorController current = animator.runtimeAnimatorController;

        if (current == null)
        {
            return null;
        }

        if (holder.AnimatorOverrideController != null && ReferenceEquals(current, holder.AnimatorOverrideController))
        {
            return holder.AnimatorOverrideController;
        }

        // Layer on top of the controller currently installed by PEAK (or by
        // another mod) instead of returning to a stale controller captured at
        // character creation time.
        AnimatorOverrideController controller = new(current);
        holder.AnimatorOverrideController = controller;
        CaptureOverrideSlot(holder, controller);

        if (holder.CurrentEmote != null && !holder.UsingSourceModel)
        {
            ApplyOverride(holder, controller, holder.CurrentEmote.AnimationClip);
        }

        // PEAK caches Animator parameter hashes. Assigning runtimeAnimatorController
        // directly leaves that cache stale and produces partially driven poses.
        characterAnimations.SetAnimatorController(controller);
        holder.PlaybackStateName = FindPlaybackStateName(animator, holder.OverrideKeyName);
        return controller;
    }

    public static bool InstallCustomEmote(this CharacterAnimations characterAnimations, Emote emote)
    {
        Holder holder = Data.GetOrCreateValue(characterAnimations);
        Animator animator = characterAnimations.character.refs.animator;
        AnimatorOverrideController? controller = characterAnimations.EnsureAnimatorOverrideController();
        if (controller == null)
        {
            DanceLog.Error($"Cannot play custom emote '{emote.DisplayName}': the PEAK Animator has no runtime controller.");
            return false;
        }

        characterAnimations.StopCustomVisualPlayback();
        bool targetHasHumanoidAvatar = animator.avatar != null &&
                                      animator.avatar.isHuman &&
                                      animator.avatar.isValid;

        if (emote.AnimationClip.isHumanMotion && !targetHasHumanoidAvatar)
        {
            // Current PEAK builds use an avatar-less Scout Animator. Humanoid
            // muscle clips therefore cannot be evaluated by an override on the
            // Scout itself. Evaluate them on an invisible source Avatar, then
            // transfer the solved pose onto PEAK's own visible skeleton.
            SourceRigAsset sourceRig = null!;
            bool useVisibleModel = false;
            if (RuntimeOptions.ReplaceModelWhileDancing)
            {
                string selectedModel = characterAnimations.GetSelectedModelName();
                string? resolvedModel = RuntimeOptions.EnsureModel(selectedModel);
                if (!string.IsNullOrWhiteSpace(resolvedModel))
                {
                    selectedModel = resolvedModel;
                    characterAnimations.SetSelectedModelName(resolvedModel);
                }
                useVisibleModel = SourceRigRegistry.TryGetVisible(selectedModel, out sourceRig);
            }

            if (!useVisibleModel && !SourceRigRegistry.TryGetBest(out sourceRig))
            {
                // Lazy mode intentionally opens no model bundle at startup. If
                // visible replacement is disabled (or the selected visual rig is
                // unsuitable), load the selected catalog entry now and use its
                // Humanoid avatar as the hidden pose solver.
                string selectedSolver = characterAnimations.GetSelectedModelName();
                string? resolvedSolver = RuntimeOptions.EnsureModel(selectedSolver);
                if (!string.IsNullOrWhiteSpace(resolvedSolver))
                {
                    characterAnimations.SetSelectedModelName(resolvedSolver);
                }

                if (!SourceRigRegistry.TryGetBest(out sourceRig))
                {
                    DanceLog.Error(
                        $"Cannot play Humanoid emote '{emote.DisplayName}': PEAK has no Humanoid Avatar and no Humanoid solver rig could be loaded on demand.");
                    return false;
                }
            }

            // Keep PEAK's own Dance2 slot vanilla. The source rig owns the
            // custom clip and our patched lifecycle owns timing; PEAK's native
            // Dance2 state is deliberately suppressed to keep the camera stable.
            characterAnimations.RestoreVanillaEmoteClip();
            SourceModelEmoteDriver driver = characterAnimations.GetOrCreateSourceModelEmoteDriver();
            bool sourcePlaybackStarted = driver.Play(emote, sourceRig, useVisibleModel);
            if (!sourcePlaybackStarted && useVisibleModel &&
                SourceRigRegistry.TryGetBestSolver(out SourceRigAsset fallbackRig, sourceRig))
            {
                DanceLog.Warning(
                    $"Visible model '{sourceRig.SelectionName}' failed for '{emote.DisplayName}'. " +
                    $"Retrying on PEAK's original body with hidden solver '{fallbackRig.SelectionName}'.");
                sourcePlaybackStarted = driver.Play(emote, fallbackRig, false);
            }

            if (!sourcePlaybackStarted)
            {
                return false;
            }
            holder.UsingSourceModel = true;
        }
        else
        {
            if (holder.OverrideKeyClip == null)
            {
                DanceLog.Error($"Cannot play custom emote '{emote.DisplayName}': no replaceable vanilla dance clip was found.");
                return false;
            }

            if (!ApplyOverride(holder, controller, emote.AnimationClip))
            {
                DanceLog.Error(
                    $"Cannot play custom emote '{emote.DisplayName}': override key '{holder.OverrideKeyName}' is absent from the active controller.");
                return false;
            }

            // Always use PEAK's setter so _lastHashCachedController and every
            // Animator parameter hash stay in sync with the override controller.
            characterAnimations.SetAnimatorController(controller);
            holder.UsingSourceModel = false;
        }

        holder.PlaybackStateName = FindPlaybackStateName(animator, holder.OverrideKeyName);
        holder.CurrentEmote = emote;
        holder.StartedAt = Time.time;
        holder.NextLoopAt = holder.StartedAt + Mathf.Max(0.05f, emote.AnimationClip.length);
        return true;
    }

    public static void RestoreVanillaEmoteClip(this CharacterAnimations characterAnimations)
    {
        Holder holder = Data.GetOrCreateValue(characterAnimations);
        AnimatorOverrideController? controller = holder.AnimatorOverrideController;
        if (controller == null || holder.OverrideKeyClip == null || !holder.OriginalOverrideCaptured)
        {
            return;
        }

        AnimationClip? original = holder.OriginalOverrideClip;
        if (original == null)
        {
            return;
        }

        controller[holder.OverrideKeyClip] = original;
        if (ReferenceEquals(characterAnimations.character.refs.animator.runtimeAnimatorController, controller))
        {
            characterAnimations.SetAnimatorController(controller);
        }
    }

    public static string GetPlaybackStateName(this CharacterAnimations characterAnimations)
    {
        Holder holder = Data.GetOrCreateValue(characterAnimations);
        return string.IsNullOrWhiteSpace(holder.PlaybackStateName)
            ? CharacterAnimationsRPCA_PlayRemovePatch.PreferredPlaybackState
            : holder.PlaybackStateName;
    }

    public static string GetOverrideKeyName(this CharacterAnimations characterAnimations)
    {
        Holder holder = Data.GetOrCreateValue(characterAnimations);
        return string.IsNullOrWhiteSpace(holder.OverrideKeyName)
            ? CharacterAnimationsRPCA_PlayRemovePatch.PlaybackState
            : holder.OverrideKeyName;
    }

    public static bool GetEmoting(this CharacterAnimations characterAnimations)
    {
        return Data.GetOrCreateValue(characterAnimations).Emoting;
    }

    public static void SetEmoting(this CharacterAnimations characterAnimations, bool emoting)
    {
        Data.GetOrCreateValue(characterAnimations).Emoting = emoting;
    }

    public static Emote? GetCurrentEmote(this CharacterAnimations characterAnimations)
    {
        return Data.GetOrCreateValue(characterAnimations).CurrentEmote;
    }

    public static void ClearCurrentEmote(this CharacterAnimations characterAnimations)
    {
        Holder holder = Data.GetOrCreateValue(characterAnimations);
        holder.CurrentEmote = null;
        holder.StartedAt = 0f;
        holder.NextLoopAt = 0f;
    }

    public static float GetCustomEmoteElapsed(this CharacterAnimations characterAnimations)
    {
        Holder holder = Data.GetOrCreateValue(characterAnimations);
        return holder.CurrentEmote == null ? 0f : Mathf.Max(0f, Time.time - holder.StartedAt);
    }

    public static bool ShouldReplayLoop(this CharacterAnimations characterAnimations)
    {
        Holder holder = Data.GetOrCreateValue(characterAnimations);
        return holder.CurrentEmote != null &&
               !holder.UsingSourceModel &&
               holder.CurrentEmote.Type == Emote.EmoteType.Loop &&
               !holder.CurrentEmote.AnimationClip.isLooping &&
               Time.time >= holder.NextLoopAt;
    }

    public static void MarkLoopReplayed(this CharacterAnimations characterAnimations)
    {
        Holder holder = Data.GetOrCreateValue(characterAnimations);
        Emote? emote = holder.CurrentEmote;
        if (emote == null)
        {
            return;
        }

        float duration = Mathf.Max(0.05f, emote.AnimationClip.length);
        do
        {
            holder.NextLoopAt += duration;
        }
        while (holder.NextLoopAt <= Time.time);
    }

    public static bool IsSourceModelPlayback(this CharacterAnimations characterAnimations)
    {
        return Data.GetOrCreateValue(characterAnimations).UsingSourceModel;
    }

    public static bool IsVisibleSourceModelPlayback(this CharacterAnimations characterAnimations)
    {
        Holder holder = Data.GetOrCreateValue(characterAnimations);
        return holder.UsingSourceModel && holder.SourceModelDriver != null && holder.SourceModelDriver.IsVisibleModel;
    }

    public static string GetSelectedModelName(this CharacterAnimations characterAnimations)
    {
        Holder holder = Data.GetOrCreateValue(characterAnimations);
        if (string.IsNullOrWhiteSpace(holder.SelectedModelName))
        {
            holder.SelectedModelName = RuntimeOptions.PreferredModel ?? string.Empty;
        }
        return holder.SelectedModelName;
    }

    public static bool CycleSelectedModel(this CharacterAnimations characterAnimations, int direction, out string selectedName)
    {
        Holder holder = Data.GetOrCreateValue(characterAnimations);
        IReadOnlyList<string> names = RuntimeOptions.GetAvailableModelNames();
        if (names.Count == 0)
        {
            selectedName = string.Empty;
            return false;
        }

        string current = characterAnimations.GetSelectedModelName();
        int index = -1;
        for (int i = 0; i < names.Count; i++)
        {
            if (string.Equals(names[i], current, StringComparison.OrdinalIgnoreCase))
            {
                index = i;
                break;
            }
        }

        int step = direction < 0 ? -1 : 1;
        index = index < 0 ? (step > 0 ? 0 : names.Count - 1) : (index + step + names.Count) % names.Count;
        holder.SelectedModelName = names[index];
        selectedName = holder.SelectedModelName;
        return true;
    }

    public static bool SetSelectedModelName(this CharacterAnimations characterAnimations, string modelName)
    {
        // Selecting a catalog entry must stay cheap. The corresponding bundle is
        // loaded only when a Humanoid dance actually needs to instantiate it.
        IReadOnlyList<string> names = RuntimeOptions.GetAvailableModelNames();
        string? matched = names.FirstOrDefault(name =>
            string.Equals(name, modelName, StringComparison.OrdinalIgnoreCase));
        if (matched == null && SourceRigRegistry.ContainsVisibleSelectionName(modelName))
        {
            matched = modelName;
        }
        if (matched == null) return false;
        Data.GetOrCreateValue(characterAnimations).SelectedModelName = matched;
        return true;
    }

    public static SourceModelEmoteDriver GetOrCreateSourceModelEmoteDriver(this CharacterAnimations characterAnimations)
    {
        Holder holder = Data.GetOrCreateValue(characterAnimations);
        if (holder.SourceModelDriver != null)
        {
            return holder.SourceModelDriver;
        }

        SourceModelEmoteDriver driver = characterAnimations.character.gameObject.AddComponent<SourceModelEmoteDriver>();
        driver.Initialize(characterAnimations);
        holder.SourceModelDriver = driver;
        return driver;
    }

    public static void StopCustomVisualPlayback(this CharacterAnimations characterAnimations)
    {
        Holder holder = Data.GetOrCreateValue(characterAnimations);
        if (holder.SourceModelDriver != null)
        {
            holder.SourceModelDriver.StopPlayback();
        }
        holder.UsingSourceModel = false;
    }

    public static EmoteAudioDriver GetOrCreateEmoteAudioDriver(this CharacterAnimations characterAnimations)
    {
        Holder holder = Data.GetOrCreateValue(characterAnimations);
        if (holder.AudioDriver != null)
        {
            return holder.AudioDriver;
        }

        GameObject characterHost = characterAnimations.character.gameObject;
        GameObject audioHost = new("PEAKEmoteLib_AudioHost");
        audioHost.transform.SetParent(characterHost.transform, false);
        audioHost.transform.localPosition = Vector3.zero;
        audioHost.transform.localRotation = Quaternion.identity;

        AudioSource source = audioHost.AddComponent<AudioSource>();
        source.name = "PEAKEmoteLib_AudioSource";
        source.playOnAwake = false;
        source.enabled = true;
        source.spatialize = false;
        source.spatialBlend = 1f;
        source.rolloffMode = AudioRolloffMode.Linear;
        source.dopplerLevel = 0f;
        source.pitch = 1f;
        source.minDistance = 2f;
        source.maxDistance = 24f;

        EmoteAudioDriver driver = characterHost.AddComponent<EmoteAudioDriver>();
        driver.Initialize(source, characterAnimations.character);
        holder.AudioDriver = driver;
        return driver;
    }

    public static void PlayEmoteAudio(this CharacterAnimations characterAnimations, Emote emote)
    {
        characterAnimations.GetOrCreateEmoteAudioDriver().Play(emote);
    }

    public static void StopEmoteAudio(this CharacterAnimations characterAnimations)
    {
        Holder holder = Data.GetOrCreateValue(characterAnimations);
        if (holder.AudioDriver != null)
        {
            holder.AudioDriver.StopPlayback();
        }
    }

    private static void CaptureOverrideSlot(Holder holder, AnimatorOverrideController controller)
    {
        var overrides = new List<KeyValuePair<AnimationClip, AnimationClip>>();
        controller.GetOverrides(overrides);

        KeyValuePair<AnimationClip, AnimationClip>? slot = FindOverrideSlot(overrides);
        if (slot == null)
        {
            holder.OverrideKeyClip = null;
            holder.OverrideKeyName = CharacterAnimationsRPCA_PlayRemovePatch.PlaybackState;
            holder.OriginalOverrideClip = null;
            holder.OriginalOverrideCaptured = false;
            DanceLog.Error("The active PEAK Animator controller has no discoverable dance override slot.");
            return;
        }

        holder.OverrideKeyClip = slot.Value.Key;
        holder.OverrideKeyName = slot.Value.Key.name;
        holder.OriginalOverrideClip = slot.Value.Value != null ? slot.Value.Value : slot.Value.Key;
        holder.OriginalOverrideCaptured = true;
    }

    private static KeyValuePair<AnimationClip, AnimationClip>? FindOverrideSlot(
        List<KeyValuePair<AnimationClip, AnimationClip>> overrides)
    {
        foreach (KeyValuePair<AnimationClip, AnimationClip> pair in overrides)
        {
            if (pair.Key != null && pair.Key.name == CharacterAnimationsRPCA_PlayRemovePatch.PlaybackState)
            {
                return pair;
            }
        }

        foreach (KeyValuePair<AnimationClip, AnimationClip> pair in overrides)
        {
            if (pair.Key != null && pair.Key.name.IndexOf("Dance2", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                DanceLog.Warning($"Vanilla dance clip key changed; using '{pair.Key.name}'.");
                return pair;
            }
        }

        foreach (KeyValuePair<AnimationClip, AnimationClip> pair in overrides)
        {
            if (pair.Key != null &&
                pair.Key.name.IndexOf("Emote", StringComparison.OrdinalIgnoreCase) >= 0 &&
                pair.Key.name.IndexOf("Dance", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                DanceLog.Warning($"Dance2 clip key was not found; using '{pair.Key.name}'.");
                return pair;
            }
        }

        return null;
    }

    private static bool ApplyOverride(Holder holder, AnimatorOverrideController controller, AnimationClip replacement)
    {
        if (holder.OverrideKeyClip == null)
        {
            return false;
        }

        controller[holder.OverrideKeyClip] = replacement;
        return true;
    }

    private static string FindPlaybackStateName(Animator animator, string overrideKeyName)
    {
        string[] shortCandidates =
        {
            CharacterAnimationsRPCA_PlayRemovePatch.PreferredPlaybackState,
            CharacterAnimationsRPCA_PlayRemovePatch.PlaybackState,
            overrideKeyName,
            "Emote_Dance2",
            "Dance2"
        };
        var candidates = new List<string>();
        foreach (string candidate in shortCandidates)
        {
            candidates.Add(candidate);
            candidates.Add("Base Layer." + candidate);
        }

        foreach (string candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            int hash = Animator.StringToHash(candidate);
            if (animator.HasState(0, hash))
            {
                if (candidate != CharacterAnimationsRPCA_PlayRemovePatch.PreferredPlaybackState)
                {
                    DanceLog.Warning($"PEAK dance state name changed; using Animator state '{candidate}'.");
                }
                return candidate;
            }
        }

        // The supplied Assembly-CSharp confirms that the RPC argument is passed
        // to Animator.Play, while the serialized controller owns the actual state
        // names. Keep the established Dance2 name as a final compatibility fallback.
        DanceLog.Warning(
            $"Animator.HasState could not confirm a dance state; falling back to '{CharacterAnimationsRPCA_PlayRemovePatch.PreferredPlaybackState}'.");
        return CharacterAnimationsRPCA_PlayRemovePatch.PreferredPlaybackState;
    }

    private sealed class Holder
    {
        public AnimatorOverrideController? AnimatorOverrideController;
        public AnimationClip? OverrideKeyClip;
        public string OverrideKeyName = CharacterAnimationsRPCA_PlayRemovePatch.PlaybackState;
        public AnimationClip? OriginalOverrideClip;
        public bool OriginalOverrideCaptured;
        public string PlaybackStateName = CharacterAnimationsRPCA_PlayRemovePatch.PlaybackState;
        public bool Emoting;
        public Emote? CurrentEmote;
        public float StartedAt;
        public float NextLoopAt;
        public bool UsingSourceModel;
        public SourceModelEmoteDriver? SourceModelDriver;
        public EmoteAudioDriver? AudioDriver;
        public string SelectedModelName = string.Empty;
    }
}
