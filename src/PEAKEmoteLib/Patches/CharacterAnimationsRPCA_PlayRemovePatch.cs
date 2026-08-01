using HarmonyLib;
using UnityEngine;

namespace PEAKEmoteLib;

[HarmonyPatch(typeof(CharacterAnimations), nameof(CharacterAnimations.RPCA_PlayRemove), new[] { typeof(string), typeof(bool) })]
public static class CharacterAnimationsRPCA_PlayRemovePatch
{
    // The supplied Assembly-CSharp.dll confirms that RPCA_PlayRemove passes its
    // string directly to Animator.Play(stateName, 0, 0). The wheel scan below
    // discovers the actual serialized Dance2 state; this name remains the
    // compatibility fallback for controller variants.
    public const string PlaybackState = "A_Scout_Emote_Dance2";

    private static string discoveredPlaybackState = PlaybackState;

    internal static string PreferredPlaybackState => string.IsNullOrWhiteSpace(discoveredPlaybackState)
        ? PlaybackState
        : discoveredPlaybackState;

    internal static void DiscoverVanillaPlaybackState(string stateName)
    {
        if (string.IsNullOrWhiteSpace(stateName) || stateName.StartsWith(Emote.CustomEmotePrefix))
        {
            return;
        }

        if (string.Equals(discoveredPlaybackState, stateName, System.StringComparison.Ordinal))
        {
            return;
        }

        discoveredPlaybackState = stateName;
        DanceLog.Info($"Discovered vanilla Dance2 Animator state from EmoteWheelData: '{stateName}'.");
    }

    [HarmonyPrefix]
    public static bool Prefix(CharacterAnimations __instance, ref string emoteName, bool succeeded)
    {
        if (EmoteCommands.IsCommand(emoteName))
        {
            if (emoteName == EmoteCommands.StopDance)
            {
                StopCustomEmote(__instance, true);
                return false;
            }

            if (EmoteCommands.TryParseSelectModelCommand(emoteName, out string absoluteModelName))
            {
                HandleAbsoluteModelCommand(__instance, absoluteModelName);
                return false;
            }

            if (emoteName == EmoteCommands.PreviousModel || emoteName == EmoteCommands.NextModel)
            {
                // Compatibility fallback when a UI entry could not be resolved
                // to an absolute target before the RPC was sent.
                HandleModelCommand(__instance, emoteName == EmoteCommands.PreviousModel ? -1 : 1);
                return false;
            }

            if (emoteName == EmoteCommands.RandomMusicDance)
            {
                // Compatibility fallback for older clients or external callers.
                // The fixed Y hotkey normally sends the concrete emote name.
                if (!EmoteCommands.TryChooseRandomMusicEmote(__instance.GetCurrentEmote(), out Emote randomEmote))
                {
                    DanceLog.Warning("Random music dance was requested, but no registered emote has a paired AudioClip.");
                    return false;
                }

                DanceLog.Info($"Random music dance selected '{randomEmote.DisplayName}'.");
                emoteName = randomEmote.Name;
            }
        }

        if (emoteName.StartsWith(Emote.CustomEmotePrefix))
        {
            if (!EmoteRegistry.GetEmotes().TryGetValue(emoteName, out Emote? emote))
            {
                DanceLog.Warning($"Received unknown custom emote RPC '{emoteName}'; ignoring it instead of asking Animator.Play to play an invalid state.");
                StopCustomEmote(__instance, true);
                return false;
            }

            return StartCustomEmote(__instance, emote, ref emoteName);
        }

        if (__instance.GetCurrentEmote() != null)
        {
            StopCustomEmote(__instance, true);
        }
        else
        {
            __instance.RestoreVanillaEmoteClip();
        }

        return true;
    }

    private static void HandleAbsoluteModelCommand(CharacterAnimations characterAnimations, string modelName)
    {
        if (!characterAnimations.SetSelectedModelName(modelName))
        {
            DanceLog.Warning($"Synchronized model selection '{modelName}' is not available on this client.");
            return;
        }

        DanceLog.Info($"Selected dance model '{characterAnimations.GetSelectedModelName()}' for player '{characterAnimations.character.name}'.");
        RestartCurrentHumanoidEmote(characterAnimations);
    }

    private static void HandleModelCommand(CharacterAnimations characterAnimations, int direction)
    {
        if (!RuntimeOptions.EnableModelCycling)
        {
            DanceLog.Info("Model cycling is disabled by configuration.");
            return;
        }

        Emote? currentEmote = characterAnimations.GetCurrentEmote();
        if (!characterAnimations.CycleSelectedModel(direction, out string selectedName))
        {
            DanceLog.Warning(
                "Model cycling was requested, but no visible model is available in the catalog.");
            return;
        }

        DanceLog.Info($"Selected dance model '{selectedName}' for player '{characterAnimations.character.name}'.");
        RestartCurrentHumanoidEmote(characterAnimations, currentEmote);
    }

    private static void RestartCurrentHumanoidEmote(CharacterAnimations characterAnimations, Emote? currentEmote = null)
    {
        Emote? emoteToRestart = currentEmote ?? characterAnimations.GetCurrentEmote();
        if (emoteToRestart == null || !emoteToRestart.AnimationClip.isHumanMotion)
        {
            return;
        }

        // Recreate the visible source rig immediately so the change is visible
        // during the current dance. Restart audio at the same time to keep the
        // two independent playback graphs synchronized from zero.
        string playbackState = emoteToRestart.Name;
        bool originalMethodNeeded = StartCustomEmote(characterAnimations, emoteToRestart, ref playbackState);
        if (originalMethodNeeded)
        {
            characterAnimations.character.refs.animator.Play(playbackState, 0, 0f);
        }
    }

    private static bool StartCustomEmote(CharacterAnimations characterAnimations, Emote emote, ref string emoteName)
    {
        bool visualInstalled = characterAnimations.InstallCustomEmote(emote);

        // Audio remains independent from model/retarget setup. A visual failure
        // must not prevent a correctly paired song from being attempted.
        characterAnimations.StopEmoteAudio();
        characterAnimations.PlayEmoteAudio(emote);

        if (!visualInstalled)
        {
            characterAnimations.character.refs.animator.SetBool(characterAnimations.AN_EMOTE, false);
            characterAnimations.emoting = false;
            characterAnimations.SetEmoting(false);
            characterAnimations.StopCustomVisualPlayback();
            characterAnimations.character.data.overrideIKForSeconds = 0f;
            characterAnimations.RestoreVanillaEmoteClip();
            characterAnimations.ClearCurrentEmote();
            DanceLog.Warning(
                $"Visual playback failed for '{emote.DisplayName}', but paired music playback was attempted independently.");
            return false;
        }

        bool sourceDriven = characterAnimations.IsSourceModelPlayback();
        characterAnimations.SetEmoting(true);
        characterAnimations.emoting = true;
        characterAnimations.sinceEmoteStart = 0f;
        characterAnimations.character.refs.animator.SetBool(characterAnimations.AN_EMOTE, !sourceDriven);
        if (emote.DisableIK)
        {
            characterAnimations.character.data.overrideIKForSeconds = Mathf.Max(0.5f, emote.AnimationClip.length);
        }

        string playbackState = characterAnimations.GetPlaybackStateName();
        DanceLog.Info(
            $"Playing custom emote '{emote.DisplayName}': state='{playbackState}', " +
            $"overrideKey='{characterAnimations.GetOverrideKeyName()}', clip='{emote.AnimationClip.name}', " +
            $"length={emote.AnimationClip.length:0.00}s, human={emote.AnimationClip.isHumanMotion}, " +
            $"model='{characterAnimations.GetSelectedModelName()}', " +
            $"mode={(characterAnimations.IsVisibleSourceModelPlayback() ? "visible-source-model-direct" : sourceDriven ? "peak-model-retarget" : "animator-override")}, " +
            $"nativeDanceStateSuppressed={sourceDriven}.");

        if (sourceDriven)
        {
            return false;
        }

        emoteName = playbackState;
        return true;
    }

    internal static void StopCustomEmote(CharacterAnimations characterAnimations, bool restoreVanillaClip)
    {
        characterAnimations.character.refs.animator.SetBool(characterAnimations.AN_EMOTE, false);
        characterAnimations.emoting = false;
        characterAnimations.SetEmoting(false);
        characterAnimations.StopEmoteAudio();
        characterAnimations.StopCustomVisualPlayback();
        characterAnimations.character.data.overrideIKForSeconds = 0f;
        if (restoreVanillaClip)
        {
            characterAnimations.RestoreVanillaEmoteClip();
        }
        characterAnimations.ClearCurrentEmote();
    }
}
