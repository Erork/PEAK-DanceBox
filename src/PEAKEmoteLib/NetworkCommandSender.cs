using System;
using System.Linq;
using System.Reflection;

namespace PEAKEmoteLib;

/// <summary>
/// Sends the same RPCA_PlayRemove command used by the vanilla emote wheel.
/// Reflection avoids a hard compile-time dependency on a particular Photon
/// package version. A local fallback is retained for offline/testing sessions.
/// </summary>
internal static class NetworkCommandSender
{
    public static bool Send(string command)
    {
        CharacterAnimations? animations = FindLocalAnimations();
        if (animations == null || string.IsNullOrWhiteSpace(command))
        {
            return false;
        }

        try
        {
            object character = animations.character;
            PropertyInfo? photonViewProperty = character.GetType().GetProperty(
                "photonView",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            object? photonView = photonViewProperty?.GetValue(character);
            if (photonView != null)
            {
                MethodInfo? rpc = photonView.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public)
                    .FirstOrDefault(method =>
                    {
                        if (!string.Equals(method.Name, "RPC", StringComparison.Ordinal)) return false;
                        ParameterInfo[] parameters = method.GetParameters();
                        return parameters.Length == 3 && parameters[0].ParameterType == typeof(string) && parameters[2].ParameterType == typeof(object[]);
                    });
                if (rpc != null)
                {
                    Type targetType = rpc.GetParameters()[1].ParameterType;
                    object target = Enum.Parse(targetType, "All");
                    rpc.Invoke(photonView, new object[] { "RPCA_PlayRemove", target, new object[] { command, true } });
                    return true;
                }
            }
        }
        catch (Exception exception)
        {
            DanceLog.Debug("Photon RPC reflection fallback: " + exception.Message);
        }

        try
        {
            animations.RPCA_PlayRemove(command, true);
            return true;
        }
        catch (Exception exception)
        {
            DanceLog.Error("Could not send emote command: " + exception.Message);
            return false;
        }
    }

    public static bool StopLocal()
    {
        CharacterAnimations? animations = FindLocalAnimations();
        if (animations == null) return false;
        CharacterAnimationsRPCA_PlayRemovePatch.StopCustomEmote(animations, true);
        return true;
    }

    public static CharacterAnimations? FindLocalAnimations()
    {
        if (Character.localCharacter == null) return null;
        CharacterAnimations? animations = Character.localCharacter.GetComponent<CharacterAnimations>();
        if (animations == null) animations = Character.localCharacter.GetComponentInChildren<CharacterAnimations>(true);
        return animations;
    }
}
