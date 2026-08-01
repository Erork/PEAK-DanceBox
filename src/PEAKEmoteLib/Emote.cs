using UnityEngine;

namespace PEAKEmoteLib;

/// <summary>
/// A custom PEAK emote with optional spatial audio.
/// </summary>
public class Emote
{
    public const string CustomEmotePrefix = "PEAKEmoteLib_";

    public enum EmoteType
    {
        /// <summary>Use PEAK's normal emote timeout.</summary>
        Vanilla,
        /// <summary>Play once for the full clip duration.</summary>
        OneShot,
        /// <summary>Keep looping until movement, jump, another emote, or cancellation.</summary>
        Loop
    }

    public AnimationClip AnimationClip { get; private set; }
    public EmoteType Type { get; private set; }
    public bool DisableIK { get; private set; }
    public string Name { get; private set; }
    public string DisplayName { get; private set; }
    public Sprite Icon { get; private set; }

    public AudioClip? AudioClip { get; private set; }
    public bool AudioEnabled { get; private set; }
    public bool LoopAudio { get; private set; }
    public float AudioVolume { get; private set; }
    public float AudioSpatialBlend { get; private set; }
    public float AudioMinDistance { get; private set; }
    public float AudioMaxDistance { get; private set; }

    public static Sprite PlaceholderSprite { get; }

    static Emote()
    {
        Texture2D transparentTexture = new(1, 1);
        transparentTexture.SetPixel(0, 0, new Color(0, 0, 0, 0));
        transparentTexture.Apply();
        PlaceholderSprite = Sprite.Create(
            transparentTexture,
            new Rect(0, 0, 1, 1),
            new Vector2(0.5f, 0.5f));
    }

    public Emote(
        string name,
        AnimationClip animationClip,
        Sprite? icon = null,
        EmoteType type = EmoteType.Vanilla,
        bool disableIK = false,
        AudioClip? audioClip = null,
        bool loopAudio = false,
        float audioVolume = 0.50f,
        float audioSpatialBlend = 1f,
        float audioMinDistance = 2f,
        float audioMaxDistance = 24f)
    {
        Name = CustomEmotePrefix + name;
        DisplayName = name;
        AnimationClip = animationClip;
        Type = type;
        DisableIK = disableIK;
        Icon = icon == null ? PlaceholderSprite : icon;

        AudioClip = audioClip;
        AudioEnabled = audioClip != null;
        LoopAudio = loopAudio;
        AudioVolume = Mathf.Clamp01(audioVolume);
        AudioSpatialBlend = Mathf.Clamp01(audioSpatialBlend);
        AudioMinDistance = Mathf.Max(0.1f, audioMinDistance);
        AudioMaxDistance = Mathf.Max(AudioMinDistance + 0.1f, audioMaxDistance);
    }

    public Emote(
        string name,
        AnimationClip animationClip,
        Texture2D iconTexture,
        EmoteType type = EmoteType.Vanilla,
        bool disableIK = false,
        AudioClip? audioClip = null,
        bool loopAudio = false,
        float audioVolume = 0.50f,
        float audioSpatialBlend = 1f,
        float audioMinDistance = 2f,
        float audioMaxDistance = 24f)
        : this(
            name,
            animationClip,
            Sprite.Create(iconTexture, new Rect(0, 0, iconTexture.width, iconTexture.height), new Vector2(0.5f, 0.5f)),
            type,
            disableIK,
            audioClip,
            loopAudio,
            audioVolume,
            audioSpatialBlend,
            audioMinDistance,
            audioMaxDistance)
    {
    }

    /// <summary>
    /// Retained for source compatibility with upstream PEAKEmoteLib consumers.
    /// This self-contained fork resolves the display text through a direct
    /// LocalizedText patch instead of PEAKLib.UI.
    /// </summary>
    public void AddLocalization(string text, LocalizedText.Language language)
    {
        if (!string.IsNullOrWhiteSpace(text))
        {
            DisplayName = text;
        }
    }

    public void ConfigureAudio(
        AudioClip? audioClip,
        bool enabled,
        bool loop,
        float volume,
        float spatialBlend,
        float minDistance,
        float maxDistance)
    {
        AudioClip = audioClip;
        AudioEnabled = enabled && audioClip != null;
        LoopAudio = loop;
        AudioVolume = Mathf.Clamp01(volume);
        AudioSpatialBlend = Mathf.Clamp01(spatialBlend);
        AudioMinDistance = Mathf.Max(0.1f, minDistance);
        AudioMaxDistance = Mathf.Max(AudioMinDistance + 0.1f, maxDistance);
    }
}
