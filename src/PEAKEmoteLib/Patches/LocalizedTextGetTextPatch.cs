using System;
using HarmonyLib;

namespace PEAKEmoteLib;

/// <summary>
/// Resolves custom emote names without requiring PEAKLib.UI. The emote wheel
/// calls this exact PEAK overload while displaying the hovered slice.
/// </summary>
[HarmonyPatch(typeof(LocalizedText), nameof(LocalizedText.GetText), new Type[] { typeof(string), typeof(bool) })]
public static class LocalizedTextGetTextPatch
{
    [HarmonyPrefix]
    public static bool Prefix(string __0, ref string __result)
    {
        if (!EmoteRegistry.TryGetDisplayName(__0, out string displayName) &&
            !EmoteCommands.TryGetDisplayName(__0, out displayName))
        {
            return true;
        }

        __result = displayName;
        return false;
    }
}
