using HarmonyLib;
using UnityEngine;

namespace PEAKEmoteLib;

/// <summary>
/// Extends PEAK's hard-coded two-second emote lifetime using an independent
/// clip clock. Movement cancellation is configurable; custom source-model
/// playback no longer has to disappear as soon as the player walks.
/// </summary>
[HarmonyPatch(typeof(CharacterAnimations), "Update")]
public static class CharacterAnimationsUpdatePatch
{
    [HarmonyPrefix]
    public static void Prefix(CharacterAnimations __instance)
    {
        Emote? currentEmote = __instance.GetCurrentEmote();
        if (currentEmote == null)
        {
            __instance.SetEmoting(__instance.emoting);
            return;
        }

        float elapsed = __instance.GetCustomEmoteElapsed();
        bool movementCancelled = RuntimeOptions.CancelEmoteOnMovement &&
                                 __instance.character.input.movementInput.magnitude > 0.1f;
        bool jumpCancelled = RuntimeOptions.CancelEmoteOnJump &&
                             __instance.character.input.jumpWasPressed;
        bool airborneCancelled = RuntimeOptions.CancelEmoteWhenAirborne &&
                                 __instance.character.data.sinceGrounded > 0.2f;
        bool playerCancelled = elapsed > 0.7f &&
                               (movementCancelled || jumpCancelled || airborneCancelled);
        bool oneShotFinished = currentEmote.Type == Emote.EmoteType.OneShot &&
                               elapsed >= Mathf.Max(0.05f, currentEmote.AnimationClip.length);

        if (playerCancelled || oneShotFinished)
        {
            string reason = oneShotFinished
                ? $"One-shot emote '{currentEmote.DisplayName}' reached the end of its clip."
                : movementCancelled
                    ? $"Custom emote '{currentEmote.DisplayName}' cancelled by player movement."
                    : jumpCancelled
                        ? $"Custom emote '{currentEmote.DisplayName}' cancelled by jump input."
                        : $"Custom emote '{currentEmote.DisplayName}' cancelled because the player became airborne.";
            DanceLog.Debug(reason);
            CharacterAnimationsRPCA_PlayRemovePatch.StopCustomEmote(__instance, true);
            return;
        }

        if (currentEmote.Type == Emote.EmoteType.Loop || currentEmote.Type == Emote.EmoteType.OneShot)
        {
            KeepPlaying(__instance, currentEmote);

            // PEAK clears emotes after a short native timer and also uses the
            // same timer for movement cancellation. When movement cancellation
            // is disabled, hold it below PEAK's 0.7-second movement threshold;
            // jump/airborne cancellation is handled explicitly above.
            float nativeTimerCeiling = RuntimeOptions.CancelEmoteOnMovement ? 1f : 0.5f;
            __instance.sinceEmoteStart = Mathf.Min(__instance.sinceEmoteStart, nativeTimerCeiling);

            // Not every third-party bundle has loopTime authored correctly.
            // Explicitly restart Animator-override loops at their real clip
            // duration. Source-model playback owns its own Playable clock.
            if (__instance.ShouldReplayLoop())
            {
                __instance.character.refs.animator.Play(__instance.GetPlaybackStateName(), 0, 0f);
                __instance.MarkLoopReplayed();
            }
        }
    }

    [HarmonyPostfix]
    public static void Postfix(CharacterAnimations __instance)
    {
        Emote? currentEmote = __instance.GetCurrentEmote();
        if (currentEmote == null)
        {
            return;
        }

        if (currentEmote.Type == Emote.EmoteType.Vanilla)
        {
            if (!__instance.emoting)
            {
                CharacterAnimationsRPCA_PlayRemovePatch.StopCustomEmote(__instance, true);
            }
            return;
        }

        KeepPlaying(__instance, currentEmote);
    }

    private static void KeepPlaying(CharacterAnimations characterAnimations, Emote emote)
    {
        // A visible/hidden Humanoid source rig evaluates the custom clip itself.
        // Driving PEAK's native Dance2 state at the same time moves the invisible
        // Scout head/camera hierarchy and causes the reported camera shake.
        bool sourceDriven = characterAnimations.IsSourceModelPlayback();
        characterAnimations.character.refs.animator.SetBool(characterAnimations.AN_EMOTE, !sourceDriven);
        characterAnimations.emoting = true;
        characterAnimations.SetEmoting(true);
        if (emote.DisableIK)
        {
            characterAnimations.character.data.overrideIKForSeconds = Mathf.Max(
                characterAnimations.character.data.overrideIKForSeconds,
                0.5f);
        }
    }
}
