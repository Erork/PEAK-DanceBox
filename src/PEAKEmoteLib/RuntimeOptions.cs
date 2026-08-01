using System;
using System.Collections.Generic;

namespace PEAKEmoteLib;

/// <summary>
/// Runtime options owned by the main plugin. Keeping them in the integrated
/// emote runtime avoids passing BepInEx ConfigEntry objects into every driver.
/// </summary>
internal static class RuntimeOptions
{
    public static Func<IReadOnlyList<string>>? AvailableModelNamesProvider { get; set; }
    public static Func<string, string?>? EnsureModelAvailable { get; set; }

    public static IReadOnlyList<string> GetAvailableModelNames()
    {
        return AvailableModelNamesProvider?.Invoke() ?? SourceRigRegistry.GetVisibleSelectionNames();
    }

    public static string? EnsureModel(string requestedName)
    {
        return EnsureModelAvailable?.Invoke(requestedName) ?? requestedName;
    }

    public static bool FollowGameMusicVolume { get; set; } = true;
    public static bool ReplaceModelWhileDancing { get; set; } = true;
    public static string PreferredModel { get; set; } = "example70";
    public static bool EnableModelCycling { get; set; } = true;
    public static bool AutoScaleVisibleModel { get; set; } = true;
    public static float VisibleModelScale { get; set; } = 1f;
    public static float VisibleModelTargetHeightRatio { get; set; } = 0.95f;
    public static float VisibleModelHeightOffset { get; set; }
    public static float VisibleModelForwardOffset { get; set; } = 2.5f;
    public static float VisibleModelYaw { get; set; }
    public static bool HidePeakRenderers { get; set; } = false;
    public static bool GroundVisibleModelFeet { get; set; } = true;
    public static float VisibleModelGroundOffset { get; set; } = 0.02f;

    public static bool StabilizeCameraWhileDancing { get; set; } = true;
    public static bool CancelEmoteOnMovement { get; set; } = false;
    public static bool CancelEmoteOnJump { get; set; } = true;
    public static bool CancelEmoteWhenAirborne { get; set; } = true;

    public static bool TransferPelvisPosition { get; set; } = true;
    public static float PelvisPositionWeight { get; set; } = 0.85f;
    public static float MaxPelvisOffset { get; set; } = 0.35f;
}
