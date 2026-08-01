using HarmonyLib;
using UnityEngine;

namespace PEAKEmoteLib;

/// <summary>
/// Enables mouse-wheel paging while the emote wheel is open.
/// </summary>
[HarmonyPatch(typeof(GUIManager), "UpdateEmoteWheel")]
public static class GUIManagerUpdateEmoteWheelPatch
{
    [HarmonyPostfix]
    public static void Postfix(GUIManager __instance)
    {
        if (!__instance.emoteWheel.activeSelf)
        {
            return;
        }

        EmoteWheel emoteWheel = __instance.emoteWheel.GetComponent<EmoteWheel>();
        if (Input.mouseScrollDelta[1] < 0)
        {
            emoteWheel.TabNext();
        }
        else if (Input.mouseScrollDelta[1] > 0)
        {
            emoteWheel.TabPrev();
        }
    }
}
