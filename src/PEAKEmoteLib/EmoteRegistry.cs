using System.Collections.Generic;
using UnityEngine;

namespace PEAKEmoteLib;

/// <summary>
/// A public registry for custom emotes.
/// </summary>
/// 
/// <remarks>
/// Emotes registered via this class are automatically discovered and added to 
/// new pages on the in-game emote wheel. See <see cref="EmoteWheelStartPatch"/> 
/// for implementation details.  
/// </remarks>
internal static class EmoteRegistry
{

    private static Dictionary<string, Emote> _emotes = new Dictionary<string, Emote>();

    /// <summary>
    /// A convenience method that constructs a new <see cref="Emote"/> instance 
    /// then registers it. 
    /// </summary>
    /// <param name="name">See <see cref="Emote.Name"/> </param>
    /// <param name="clip">A Unity AnimationClip object containing the animation data for this emote.</param>
    /// <param name="icon">
    /// An optional Unity Sprite object used to represent this emote on the in-game emote wheel. 
    /// If <see cref="null"/> is passed, the Emote's <see cref="Emote.Icon"/> is set to 
    /// <see cref="Emote.PlaceholderSprite"/>. 
    /// </param>
    /// <param name="type">See <see cref="Emote.Type"/> </param>
    /// <param name="disableIK">
    /// Whether or not PEAKEmoteLib should attempt to override the character's IK 
    /// (Inverse Kinematics) rig while this emote is playing.
    /// </param>
    /// 
    /// <returns>
    /// The newly constructed and registered <see cref="Emote"/> instance (for 
    /// example, to be used with <see cref="Emote.AddLocalization"/> ). 
    /// </returns>
    public static Emote RegisterEmote(string name, AnimationClip clip, Sprite? icon = null, Emote.EmoteType type = Emote.EmoteType.Vanilla, bool disableIK = false)
    {
        return RegisterEmote(new Emote(name, clip, icon, type, disableIK));
    }

    /// <summary>
    /// A convenience method that constructs a new <see cref="Emote"/> instance 
    /// then registers it. 
    /// </summary>
    /// <param name="name">See <see cref="Emote.Name"/> </param>
    /// <param name="clip">A Unity AnimationClip object containing the animation data for this emote.</param>
    /// <param name="iconTexture">A Unity Texture2D object used to represent this emote on the in-game emote wheel.</param>
    /// <param name="type">See <see cref="Emote.Type"/></param>
    /// <param name="disableIK">
    /// Whether or not PEAKEmoteLib should attempt to override the character's IK 
    /// (Inverse Kinematics) rig while this emote is playing.
    /// </param>
    /// 
    /// <returns>
    /// The newly constructed and registered <see cref="Emote"/> instance (for 
    /// example, to be used with <see cref="Emote.AddLocalization"/> ). 
    /// </returns>
    public static Emote RegisterEmote(string name, AnimationClip clip, Texture2D iconTexture, Emote.EmoteType type = Emote.EmoteType.Vanilla, bool disableIK = false)
    {
        return RegisterEmote(new Emote(name, clip, iconTexture, type, disableIK));
    }

    /// <summary>
    /// Registers an <see cref="Emote"/> instance.  
    /// </summary>
    /// 
    /// <remarks>
    /// If another <see cref="Emote"/> has already been registered with an 
    /// identical <see cref="Emote.Name"/>, this one will be ignored.   
    /// </remarks>
    /// 
    /// <param name="emote">The <see cref="Emote"/> instance to register.</param>
    /// 
    /// <returns>The same <see cref="Emote"/> instance passed in.</returns>
    public static Emote RegisterEmote(Emote emote)
    {
        if (_emotes.ContainsKey(emote.Name))
        {
            DanceLog.Warning($"Ignoring duplicate emote '{emote.Name}'. Custom emote names must be globally unique.");
            return emote;
        }

        _emotes.Add(emote.Name, emote);

        DanceLog.Info($"Registered new emote: '{emote.Name}'");
        return emote;
    }

    /// <summary>
    /// Registers multiple <see cref="Emote"/> instances. 
    /// </summary>
    /// 
    /// <remarks>
    /// If another <see cref="Emote"/> has already been registered with an 
    /// identical <see cref="Emote.Name"/>, any duplicates will be ignored.   
    /// </remarks>
    /// 
    /// <param name="emotes">
    /// An IEnumerable collection of <see cref="Emote"/> instances to register. 
    /// </param>
    /// 
    /// <returns>
    /// The same IEnumerable collection of <see cref="Emote"/> instances passed in. 
    /// </returns>
    public static IEnumerable<Emote> RegisterEmotes(IEnumerable<Emote> emotes)
    {
        foreach (Emote emote in emotes)
        {
            RegisterEmote(emote);
        }
        return emotes;
    }

    /// <summary>
    /// Gets a read-only copy of all registered emotes.
    /// </summary>
    /// 
    /// <returns>
    /// A read-only dictionary of all registered emotes, keyed by 
    /// <see cref="Emote.Name"/>. 
    /// </returns>
    public static IReadOnlyDictionary<string, Emote> GetEmotes()
    {
        return _emotes;
    }

    public static bool TryGetDisplayName(string key, out string displayName)
    {
        if (_emotes.TryGetValue(key, out Emote? emote))
        {
            displayName = emote.DisplayName;
            return true;
        }

        displayName = string.Empty;
        return false;
    }
}