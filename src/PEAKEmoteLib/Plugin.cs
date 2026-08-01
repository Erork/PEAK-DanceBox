using BepInEx.Logging;
using HarmonyLib;

namespace PEAKEmoteLib;

/// <summary>
/// Integrated runtime bootstrap. In this project PEAKEmoteLib is compiled into
/// the main dance plugin, so it is not a second BepInEx plugin or dependency.
/// </summary>
internal static class Plugin
{
    internal static ManualLogSource Log { get; private set; } = null!;

    private static Harmony? harmony;

    internal static void Initialize(ManualLogSource logger)
    {
        if (harmony != null)
        {
            return;
        }

        Log = logger;
        harmony = new Harmony("com.dline.dancebox.integrated-emote-runtime");
        harmony.PatchAll(typeof(Plugin).Assembly);
        DanceLog.Info("Integrated PEAKEmoteLib runtime loaded with loop-emote and spatial-audio support.");
    }

    internal static void Shutdown()
    {
        if (harmony == null)
        {
            return;
        }

        harmony.UnpatchSelf();
        harmony = null;
        DanceLog.Info("Integrated PEAKEmoteLib runtime unloaded.");
    }
}

/// <summary>
/// Compatibility helpers retained from upstream PEAKEmoteLib. Because the
/// runtime is compiled into the main plugin, these methods register directly
/// into the integrated registry without requiring another DLL.
/// </summary>
public static class BaseUnityPluginExtensions
{
    public static Emote RegisterEmote(
        this BepInEx.BaseUnityPlugin plugin,
        string name,
        UnityEngine.AnimationClip clip,
        UnityEngine.Sprite? icon = null,
        Emote.EmoteType type = Emote.EmoteType.Vanilla,
        bool disableIK = false)
    {
        return EmoteRegistry.RegisterEmote(name, clip, icon, type, disableIK);
    }

    public static Emote RegisterEmote(
        this BepInEx.BaseUnityPlugin plugin,
        string name,
        UnityEngine.AnimationClip clip,
        UnityEngine.Texture2D iconTexture,
        Emote.EmoteType type = Emote.EmoteType.Vanilla,
        bool disableIK = false)
    {
        return EmoteRegistry.RegisterEmote(name, clip, iconTexture, type, disableIK);
    }

    public static Emote RegisterEmote(this BepInEx.BaseUnityPlugin plugin, Emote emote)
    {
        return EmoteRegistry.RegisterEmote(emote);
    }

    public static System.Collections.Generic.IEnumerable<Emote> RegisterEmotes(
        this BepInEx.BaseUnityPlugin plugin,
        System.Collections.Generic.IEnumerable<Emote> emotes)
    {
        return EmoteRegistry.RegisterEmotes(emotes);
    }
}
