using HarmonyLib;

namespace PEAKEmoteLib;

[HarmonyPatch(typeof(CharacterAnimations), "UpdateHeadBob")]
public static class CharacterAnimationsUpdateHeadBobPatch
{
    [HarmonyPrefix]
    public static bool Prefix(CharacterAnimations __instance)
    {
        // Renderer visibility has no bearing on camera transforms. The shake
        // came from PEAK updating native head bob while a separate Humanoid rig
        // was already driving the custom dance. Freeze only the local custom
        // source-model head-bob pass; ordinary look controls and remote players
        // are unaffected.
        return !RuntimeOptions.StabilizeCameraWhileDancing ||
               __instance.GetCurrentEmote() == null ||
               !__instance.IsSourceModelPlayback() ||
               Character.localCharacter == null ||
               __instance.character != Character.localCharacter;
    }

    [HarmonyPostfix]
    public static void Postfix(CharacterAnimations __instance)
    {
        __instance.EnsureAnimatorOverrideController();
    }
}
