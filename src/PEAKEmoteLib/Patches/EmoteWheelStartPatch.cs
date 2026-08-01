using System;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace PEAKEmoteLib;

/// <summary>
/// Adds registered custom dances to new emote-wheel pages. Model switching and
/// random music playback use fixed gameplay hotkeys instead of extra wheel
/// entries, keeping the wheel smaller and cheaper to rebuild.
/// </summary>
[HarmonyPatch(typeof(EmoteWheel), "Start")]
public static class EmoteWheelStartPatch
{
    public const int SlicesPerPage = 8;

    [HarmonyPostfix]
    public static void Postfix(EmoteWheel __instance)
    {
        int vanillaPages = __instance.pages;
        DiscoverVanillaDanceState(__instance, vanillaPages);

        Emote[] customEmotes = EmoteRegistry.GetEmotes().Values.ToArray();
        int customPages = (customEmotes.Length + SlicesPerPage - 1) / SlicesPerPage;
        __instance.pages = vanillaPages + customPages;
        Array.Resize(ref __instance.data, __instance.pages * SlicesPerPage);

        for (int i = vanillaPages * SlicesPerPage; i < __instance.data.Length; i++)
        {
            int customIndex = i - vanillaPages * SlicesPerPage;
            if (customIndex >= customEmotes.Length)
            {
                __instance.data[i] = null;
                continue;
            }

            Emote emote = customEmotes[customIndex];
            EmoteWheelData data = ScriptableObject.CreateInstance<EmoteWheelData>();
            data.emoteName = emote.Name;
            data.anim = emote.Name;
            data.emoteSprite = emote.Icon;
            __instance.data[i] = data;
        }
    }

    private static void DiscoverVanillaDanceState(EmoteWheel emoteWheel, int vanillaPages)
    {
        EmoteWheelData[] data = emoteWheel.data;
        if (data == null || data.Length == 0)
        {
            return;
        }

        int count = Math.Min(data.Length, Math.Max(0, vanillaPages * SlicesPerPage));
        EmoteWheelData? best = null;
        int bestScore = 0;
        for (int index = 0; index < count; index++)
        {
            EmoteWheelData? candidate = data[index];
            if (candidate == null || string.IsNullOrWhiteSpace(candidate.anim) ||
                candidate.anim.StartsWith(Emote.CustomEmotePrefix))
            {
                continue;
            }

            int score = ScoreDance2Candidate(candidate);
            if (score > bestScore)
            {
                best = candidate;
                bestScore = score;
            }
        }

        if (best != null)
        {
            CharacterAnimationsRPCA_PlayRemovePatch.DiscoverVanillaPlaybackState(best.anim);
        }
    }

    private static int ScoreDance2Candidate(EmoteWheelData data)
    {
        if (string.Equals(data.anim, CharacterAnimationsRPCA_PlayRemovePatch.PlaybackState, StringComparison.Ordinal))
        {
            return 1000;
        }
        if (data.anim.IndexOf("Dance2", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return 900;
        }
        if (!string.IsNullOrWhiteSpace(data.emoteName) &&
            data.emoteName.IndexOf("Dance2", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return 800;
        }
        return 0;
    }
}
