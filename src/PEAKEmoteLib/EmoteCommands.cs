using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace PEAKEmoteLib;

/// <summary>
/// Synchronized utility commands used by the fixed gameplay hotkeys. Relative
/// model changes are resolved to an absolute catalog name before the RPC is
/// sent so every client receives the same selection.
/// </summary>
internal static class EmoteCommands
{
    public const string PreviousModel = Emote.CustomEmotePrefix + "Command_PreviousModel";
    public const string NextModel = Emote.CustomEmotePrefix + "Command_NextModel";
    public const string RandomMusicDance = Emote.CustomEmotePrefix + "Command_RandomMusicDance";
    public const string StopDance = Emote.CustomEmotePrefix + "Command_StopDance";
    public const string SelectModelPrefix = Emote.CustomEmotePrefix + "Command_SelectModel_";

    private static readonly Dictionary<string, string> DisplayNames = new(StringComparer.Ordinal)
    {
        [PreviousModel] = "LC Previous Model",
        [NextModel] = "LC Next Model",
        [RandomMusicDance] = "LC Random Music Dance",
        [StopDance] = "LC Stop Dance"
    };

    private static readonly string[] StaticPoseMarkers =
    {
        "pose", "tpose", "bindpose", "bind", "idle", "preview", "static",
        "placeholder", "testanimation", "restpose", "referencepose"
    };

    private static string lastRandomEmoteName = string.Empty;

    public static bool IsCommand(string value)
    {
        return DisplayNames.ContainsKey(value) || value.StartsWith(SelectModelPrefix, StringComparison.Ordinal);
    }

    public static bool TryGetDisplayName(string value, out string displayName)
    {
        if (DisplayNames.TryGetValue(value, out string? found))
        {
            displayName = found;
            return true;
        }

        displayName = string.Empty;
        return false;
    }

    public static string CreateSelectModelCommand(string modelName)
    {
        return SelectModelPrefix + modelName;
    }

    public static bool TryParseSelectModelCommand(string value, out string modelName)
    {
        if (!value.StartsWith(SelectModelPrefix, StringComparison.Ordinal))
        {
            modelName = string.Empty;
            return false;
        }

        modelName = value.Substring(SelectModelPrefix.Length);
        return !string.IsNullOrWhiteSpace(modelName);
    }

    public static bool TryCreateRelativeModelCommand(
        CharacterAnimations? animations,
        int direction,
        out string command)
    {
        command = string.Empty;
        if (animations == null || !RuntimeOptions.EnableModelCycling)
        {
            return false;
        }

        IReadOnlyList<string> models = RuntimeOptions.GetAvailableModelNames();
        if (models.Count == 0)
        {
            return false;
        }

        string current = animations.GetSelectedModelName();
        int currentIndex = FindModelIndex(models, current);
        int step = direction < 0 ? -1 : 1;
        int nextIndex = currentIndex < 0
            ? (step > 0 ? 0 : models.Count - 1)
            : (currentIndex + step + models.Count) % models.Count;

        command = CreateSelectModelCommand(models[nextIndex]);
        return true;
    }

    public static bool TryChooseRandomMusicEmote(Emote? currentEmote, out Emote emote)
    {
        Emote[] allMusic = EmoteRegistry.GetEmotes().Values
            .Where(candidate => candidate.AudioEnabled && candidate.AudioClip != null)
            .OrderBy(candidate => candidate.Name, StringComparer.Ordinal)
            .ToArray();
        if (allMusic.Length == 0)
        {
            emote = null!;
            return false;
        }

        Emote[] danceMusic = allMusic.Where(IsLikelyMusicDance).ToArray();
        Emote[] pool = danceMusic.Length > 0 ? danceMusic : allMusic;

        Emote[] nonRepeating = pool
            .Where(candidate => !string.Equals(candidate.Name, lastRandomEmoteName, StringComparison.Ordinal) &&
                                !string.Equals(candidate.Name, currentEmote?.Name, StringComparison.Ordinal))
            .ToArray();
        if (nonRepeating.Length > 0)
        {
            pool = nonRepeating;
        }

        int totalWeight = pool.Sum(GetRandomSelectionWeight);
        int roll = UnityEngine.Random.Range(0, Math.Max(1, totalWeight));
        Emote selected = pool[0];
        foreach (Emote candidate in pool)
        {
            int weight = GetRandomSelectionWeight(candidate);
            if (roll < weight)
            {
                selected = candidate;
                break;
            }
            roll -= weight;
        }

        emote = selected;
        lastRandomEmoteName = selected.Name;
        return true;
    }

    private static bool IsLikelyMusicDance(Emote candidate)
    {
        AudioClip? audio = candidate.AudioClip;
        AnimationClip animation = candidate.AnimationClip;
        if (audio == null || audio.length < 1.5f || animation.length < 0.45f)
        {
            return false;
        }

        string combined = Normalize(candidate.Name + " " + candidate.DisplayName + " " + animation.name);
        return !StaticPoseMarkers.Any(marker => combined.Contains(marker));
    }

    private static int GetRandomSelectionWeight(Emote candidate)
    {
        int weight = 1;
        if (candidate.Type == Emote.EmoteType.Loop) weight += 4;
        if (candidate.AnimationClip.isLooping) weight += 2;
        if (candidate.AnimationClip.length >= 2f) weight += 2;
        if (candidate.AudioClip != null && candidate.AudioClip.length >= 8f) weight += 2;
        return weight;
    }

    private static int FindModelIndex(IReadOnlyList<string> models, string current)
    {
        for (int i = 0; i < models.Count; i++)
        {
            if (string.Equals(models[i], current, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }
        return -1;
    }

    private static string Normalize(string value)
    {
        return new string(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
    }
}
