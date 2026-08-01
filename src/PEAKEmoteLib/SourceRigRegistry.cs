using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace PEAKEmoteLib;

/// <summary>
/// Keeps references to Humanoid source prefabs. Normal dance-bundle rigs can
/// act as invisible pose solvers; model-pack rigs can additionally be rendered
/// as a complete replacement model while a dance is active.
/// </summary>
internal static class SourceRigRegistry
{
    private static readonly List<SourceRigAsset> Assets = new();

    private static readonly HashSet<int> SeenAnimatorIds = new();

    public static void RegisterModelPrefabs(IEnumerable<SourceRigRegistration> registrations)
    {
        Assets.Clear();
        SeenAnimatorIds.Clear();
        AddRegistrations(registrations);
        WriteRegistrationSummary();
    }

    public static void RegisterAdditionalModelPrefabs(IEnumerable<SourceRigRegistration> registrations)
    {
        AddRegistrations(registrations);
        Assets.Sort((left, right) => ScoreSolver(right).CompareTo(ScoreSolver(left)));
    }

    public static bool ContainsVisibleSelectionName(string name)
    {
        return Assets.Any(candidate => IsUsable(candidate) && candidate.IsVisualModelPack &&
            string.Equals(candidate.SelectionName, name, StringComparison.OrdinalIgnoreCase));
    }

    private static void AddRegistrations(IEnumerable<SourceRigRegistration> registrations)
    {
        foreach (SourceRigRegistration registration in registrations.Where(item => item.Prefab != null))
        {
            GameObject prefab = registration.Prefab;
            Animator[] animators;
            try
            {
                animators = prefab.GetComponentsInChildren<Animator>(true);
            }
            catch (Exception exception)
            {
                DanceLog.Debug($"Could not inspect source model '{prefab.name}': {exception.Message}");
                continue;
            }

            foreach (Animator animator in animators)
            {
                Avatar? avatar = animator.avatar;
                if (avatar == null || !avatar.isValid || !avatar.isHuman || !SeenAnimatorIds.Add(animator.GetInstanceID()))
                {
                    continue;
                }

                int rendererCount = 0;
                try { rendererCount = prefab.GetComponentsInChildren<Renderer>(true).Length; } catch { }
                Assets.Add(new SourceRigAsset(
                    prefab, animator, avatar, rendererCount,
                    GetRelativePath(prefab.transform, animator.transform),
                    registration.SourceName, registration.IsVisualModelPack));
            }
        }
        Assets.Sort((left, right) => ScoreSolver(right).CompareTo(ScoreSolver(left)));
    }

    private static void WriteRegistrationSummary()
    {
        if (Assets.Count == 0)
        {
            DanceLog.Warning(
                "No Humanoid source rig was found in the loaded bundles. Humanoid dances cannot use the source-model playback path on PEAK's avatar-less Scout Animator.");
            return;
        }

        if (DanceLog.IsEnabled(DanceLogLevel.Info))
        {
            string summary = string.Join(", ", Assets.Take(8).Select(asset =>
                $"{asset.Prefab.name}/{asset.Avatar.name}({asset.RendererCount} renderer(s))"));
            DanceLog.Info($"Registered {Assets.Count} Humanoid source rig(s): {summary}.");
            SourceRigAsset[] visualModels = Assets.Where(asset => asset.IsVisualModelPack && asset.RendererCount > 0).ToArray();
            if (visualModels.Length > 0)
            {
                string visualSummary = string.Join(", ", visualModels.Take(24).Select(asset => asset.SelectionName));
                DanceLog.Info($"Discovered {visualModels.Length} visible dance model(s): {visualSummary}.");
            }
        }
    }

    public static bool TryGetBest(out SourceRigAsset asset)
    {
        return TryGetBestSolver(out asset, null);
    }

    public static bool TryGetBestSolver(out SourceRigAsset asset, SourceRigAsset? excluded)
    {
        foreach (SourceRigAsset candidate in Assets)
        {
            if (ReferenceEquals(candidate, excluded) || candidate.IsVisualModelPack || !IsUsable(candidate))
            {
                continue;
            }

            asset = candidate;
            return true;
        }

        // Some custom packs may contain the only usable Humanoid Avatar. Keep a
        // final permissive fallback, while still avoiding the model that just failed.
        foreach (SourceRigAsset candidate in Assets)
        {
            if (!ReferenceEquals(candidate, excluded) && IsUsable(candidate))
            {
                asset = candidate;
                return true;
            }
        }

        asset = null!;
        return false;
    }

    public static IReadOnlyList<string> GetVisibleSelectionNames()
    {
        return Assets
            .Where(candidate => IsUsable(candidate) && candidate.IsVisualModelPack && candidate.RendererCount > 0)
            .Select(candidate => candidate.SelectionName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static bool TryGetVisible(string preferredName, out SourceRigAsset asset)
    {
        SourceRigAsset[] candidates = Assets
            .Where(candidate => IsUsable(candidate) && candidate.IsVisualModelPack && candidate.RendererCount > 0)
            .ToArray();
        if (candidates.Length == 0)
        {
            asset = null!;
            return false;
        }

        string preferred = Normalize(preferredName ?? string.Empty);
        if (preferred.Length > 0)
        {
            SourceRigAsset? matched = candidates
                .Select(candidate => new { Candidate = candidate, Score = ScoreVisualMatch(candidate, preferred) })
                .Where(item => item.Score > 0)
                .OrderByDescending(item => item.Score)
                .ThenByDescending(item => item.Candidate.RendererCount)
                .Select(item => item.Candidate)
                .FirstOrDefault();
            if (matched != null)
            {
                asset = matched;
                return true;
            }

            DanceLog.Warning(
                $"Preferred dance model '{preferredName}' was not found; using the first valid model-pack prefab instead.");
        }

        asset = candidates
            .OrderByDescending(candidate => candidate.RendererCount)
            .ThenBy(candidate => candidate.SelectionName, StringComparer.OrdinalIgnoreCase)
            .First();
        return true;
    }

    public static void Clear()
    {
        Assets.Clear();
        SeenAnimatorIds.Clear();
    }

    private static bool IsUsable(SourceRigAsset candidate)
    {
        return candidate.Prefab != null && candidate.Avatar != null &&
               candidate.Avatar.isValid && candidate.Avatar.isHuman;
    }

    private static int ScoreVisualMatch(SourceRigAsset asset, string preferred)
    {
        string prefab = Normalize(asset.Prefab.name);
        string animator = Normalize(asset.Animator.name);
        string avatar = Normalize(asset.Avatar.name);
        string source = Normalize(asset.SourceName);
        string combined = prefab + animator + avatar + source;

        if (prefab == preferred || animator == preferred || avatar == preferred)
        {
            return 100000;
        }
        if (source.Contains(preferred))
        {
            return 80000;
        }
        if (combined.Contains(preferred))
        {
            return 60000;
        }
        if (preferred.Contains(prefab) || preferred.Contains(animator) || preferred.Contains(avatar))
        {
            return 40000;
        }
        return 0;
    }

    private static int ScoreSolver(SourceRigAsset asset)
    {
        string name = Normalize(asset.Prefab.name + " " + asset.Animator.name + " " + asset.Avatar.name);
        int score = asset.RendererCount > 0 ? 1000 : 0;

        if (name.Contains("playablescavenger"))
        {
            score += 100000;
        }
        else if (name == "scout" || name.Contains("scoutavatar"))
        {
            score += 40000;
        }
        else if (name.Contains("commando"))
        {
            score += 30000;
        }
        else if (name.Contains("scout"))
        {
            score += 20000;
        }

        score -= Math.Max(0, asset.RendererCount - 12) * 10;
        return score;
    }

    private static string Normalize(string value)
    {
        return new string(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
    }

    private static string GetRelativePath(Transform root, Transform target)
    {
        if (target == root)
        {
            return string.Empty;
        }

        var segments = new Stack<string>();
        Transform? current = target;
        while (current != null && current != root)
        {
            segments.Push(current.name);
            current = current.parent;
        }

        return current == root ? string.Join("/", segments) : string.Empty;
    }
}

internal sealed class SourceRigRegistration
{
    public SourceRigRegistration(GameObject prefab, string sourceName, bool isVisualModelPack)
    {
        Prefab = prefab;
        SourceName = sourceName;
        IsVisualModelPack = isVisualModelPack;
    }

    public GameObject Prefab { get; }
    public string SourceName { get; }
    public bool IsVisualModelPack { get; }
}

internal sealed class SourceRigAsset
{
    public SourceRigAsset(
        GameObject prefab,
        Animator animator,
        Avatar avatar,
        int rendererCount,
        string animatorPath,
        string sourceName,
        bool isVisualModelPack)
    {
        Prefab = prefab;
        Animator = animator;
        Avatar = avatar;
        RendererCount = rendererCount;
        AnimatorPath = animatorPath;
        SourceName = sourceName;
        IsVisualModelPack = isVisualModelPack;
    }

    public GameObject Prefab { get; }
    public Animator Animator { get; }
    public Avatar Avatar { get; }
    public int RendererCount { get; }
    public string AnimatorPath { get; }
    public string SourceName { get; }
    public bool IsVisualModelPack { get; }
    public string SelectionName
    {
        get
        {
            string sourceId = ExtractXiehenId(SourceName);
            if (!string.IsNullOrEmpty(sourceId))
            {
                return sourceId;
            }

            string prefabName = Prefab == null ? string.Empty : Prefab.name;
            if (!string.IsNullOrWhiteSpace(prefabName) && !int.TryParse(prefabName, out _))
            {
                return prefabName;
            }
            return Avatar == null || string.IsNullOrWhiteSpace(Avatar.name) ? prefabName : Avatar.name;
        }
    }

    private static string ExtractXiehenId(string sourceName)
    {
        if (string.IsNullOrWhiteSpace(sourceName))
        {
            return string.Empty;
        }

        int searchFrom = 0;
        while (searchFrom < sourceName.Length)
        {
            int index = sourceName.IndexOf("example", searchFrom, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                break;
            }

            int digitStart = index + "example".Length;
            int digitEnd = digitStart;
            while (digitEnd < sourceName.Length && char.IsDigit(sourceName[digitEnd]))
            {
                digitEnd++;
            }
            if (digitEnd > digitStart)
            {
                return sourceName.Substring(index, digitEnd - index).ToLowerInvariant();
            }
            searchFrom = digitStart;
        }
        return string.Empty;
    }
}
