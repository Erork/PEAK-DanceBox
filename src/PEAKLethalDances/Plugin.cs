using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using PEAKEmoteLib;
using UnityEngine;
using UnityEngine.Networking;

namespace NadiyaJafi.PEAKLethalDances;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed partial class Plugin : BaseUnityPlugin
{
    public const string PluginGuid = "com.nadiyajafi.peaklethaldances";
    public const string PluginName = "PEAK Lethal Dances Complete";
    public const string PluginVersion = "2.0.7";

    private static readonly string[] KnownBundleFileNames =
    {
        "peak_lethal_dances",
        "lethal_fineilldoitmyself",
        "lethal_moisture_animationreplacements",
        "lethal_customemotespackage",
        "lethal_customemotespackage2",
        "fineilldoitmyself",
        "moisture_animationreplacements",
        "customemotespackage",
        "customemotespackage2",
        "huluoboanimex",
        "huluobobgmex",
        "huluobopopex"
    };

    private static readonly DanceDefinition[] KnownDances =
    {
        new(
            "NadiyaJafi_LethalDefaultDance",
            "LC Default Dance",
            Emote.EmoteType.Loop,
            new[] { "Default Dance", "DefaultDance", "Dance Moves", "Fortnite Default Dance" },
            new[] { "Default Dance", "DefaultDance", "Dance Moves", "Fortnite Default Dance", "music", "song" }),
        new(
            "NadiyaJafi_LethalFloss",
            "LC Floss",
            Emote.EmoteType.Loop,
            new[] { "Floss" },
            new[] { "Floss" }),
        new(
            "NadiyaJafi_LethalDab",
            "LC Dab",
            Emote.EmoteType.OneShot,
            new[] { "Dab", "Deep Dab" },
            new[] { "Dab", "Deep Dab" })
    };

    private readonly List<AssetBundle> loadedBundles = new();
    // AudioClips imported as Streaming or loaded in the background may continue
    // reading from their AssetBundle after registration. Keep only those bundle
    // containers alive; pure animation/icon/model bundles can still be released.
    private readonly HashSet<AssetBundle> audioBackedBundles = new();
    private readonly List<ExternalMusicBinding> externalMusicBindings = new();
    private ManualLogSource log = null!;

    private ConfigEntry<bool> enableKnownDances = null!;
    private ConfigEntry<bool> autoImportAllAnimations = null!;
    private ConfigEntry<bool> allowNonHumanoidAnimations = null!;
    private ConfigEntry<bool> disableIk = null!;
    private ConfigEntry<bool> useBundleIcons = null!;
    private ConfigEntry<bool> enableMusic = null!;
    private ConfigEntry<float> musicVolume = null!;
    private ConfigEntry<bool> followGameMusicVolume = null!;
    private ConfigEntry<float> musicSpatialBlend = null!;
    private ConfigEntry<float> musicMinDistance = null!;
    private ConfigEntry<float> musicMaxDistance = null!;
    private ConfigEntry<bool> dumpAssetInventory = null!;

    private ConfigEntry<bool> replaceModelWhileDancing = null!;
    private ConfigEntry<string> preferredModel = null!;
    private ConfigEntry<bool> enableModelCycling = null!;
    private ConfigEntry<bool> autoScaleVisibleModel = null!;
    private ConfigEntry<float> visibleModelScale = null!;
    private ConfigEntry<float> visibleModelTargetHeightRatio = null!;
    private ConfigEntry<float> visibleModelHeightOffset = null!;
    private ConfigEntry<float> visibleModelForwardOffset = null!;
    private ConfigEntry<float> visibleModelYaw = null!;
    private ConfigEntry<bool> hidePeakRenderers = null!;
    private ConfigEntry<bool> groundVisibleModelFeet = null!;
    private ConfigEntry<float> visibleModelGroundOffset = null!;
    private ConfigEntry<bool> stabilizeCameraWhileDancing = null!;
    private ConfigEntry<bool> cancelEmoteOnMovement = null!;
    private ConfigEntry<bool> cancelEmoteOnJump = null!;
    private ConfigEntry<bool> cancelEmoteWhenAirborne = null!;
    private ConfigEntry<bool> transferPelvisPosition = null!;
    private ConfigEntry<float> pelvisPositionWeight = null!;
    private ConfigEntry<float> maxPelvisOffset = null!;
    private ConfigEntry<int> defaultsRevision = null!;

    private void Awake()
    {
        log = Logger;
        BindConfig();
        DanceLog.Initialize(Logger, "Off");
        PEAKEmoteLib.Plugin.Initialize(Logger);
        ApplyConfigMigrations();

        string pluginDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? Paths.PluginPath;
        InitializeFinalRuntime(pluginDirectory);
        ApplyRuntimeOptions();
        DanceLog.Info(
            $"Runtime config: MusicEnabled={enableMusic.Value}, MusicVolumeMultiplier={musicVolume.Value:0.00}, " +
            $"FollowGameMusicVolume={followGameMusicVolume.Value}, " +
            $"ReplaceModelWhileDancing={replaceModelWhileDancing.Value}, PreferredModel='{preferredModel.Value}', " +
            $"ModelCycling={enableModelCycling.Value}, AutoScale={autoScaleVisibleModel.Value}, " +
            $"TargetHeightRatio={visibleModelTargetHeightRatio.Value:0.00}, ScaleMultiplier={visibleModelScale.Value:0.00}, " +
            $"GroundFeet={groundVisibleModelFeet.Value}, GroundOffset={visibleModelGroundOffset.Value:0.000}, " +
            $"StabilizeCamera={stabilizeCameraWhileDancing.Value}, CancelOnMovement={cancelEmoteOnMovement.Value}.");

        IReadOnlyList<BundleAssetSet> bundles = LoadBundles(pluginDirectory);
        SourceRigRegistry.RegisterModelPrefabs(bundles.SelectMany(bundle =>
            bundle.ModelPrefabs.Select(model => new SourceRigRegistration(
                model,
                Path.GetFileName(bundle.Path),
                IsModelPackBundle(bundle.Path)))));
        if (dumpAssetInventory.Value)
        {
            WriteAssetInventory(pluginDirectory, bundles);
        }
        IReadOnlyList<AnimationAsset> animations = SelectUniqueAnimations(bundles);

        if (animations.Count == 0)
        {
            DanceLog.Error(
                "No AnimationClip was loaded. The plugin itself is running, but Unity rejected or could not find every dance bundle. " +
                "Keep the shipped 'bundles' directory beside the DLL and check this log for the rejected filename.");
            return;
        }

        var registeredClips = new HashSet<int>();
        int registered = 0;
        int withMusic = 0;

        if (enableKnownDances.Value)
        {
            foreach (DanceDefinition definition in KnownDances)
            {
                AnimationAsset? animation = FindBestAnimation(animations, definition.AnimationAliases);
                if (animation == null)
                {
                    DanceLog.Warning($"Known dance '{definition.DisplayName}' was not found in the loaded bundles.");
                    continue;
                }

                if (RegisterDance(animation, definition.SystemName, definition.DisplayName, definition.Type, definition.AudioAliases))
                {
                    registeredClips.Add(animation.Clip.GetInstanceID());
                    registered++;
                    if (FindBestAudio(animation, definition.AudioAliases, bundles) != null)
                    {
                        withMusic++;
                    }
                }
            }
        }

        if (autoImportAllAnimations.Value)
        {
            foreach (AnimationAsset animation in animations.OrderBy(asset => asset.Clip.name, StringComparer.OrdinalIgnoreCase))
            {
                if (registeredClips.Contains(animation.Clip.GetInstanceID()) || IsExcludedHelperClip(animation.Clip.name))
                {
                    continue;
                }

                if (!animation.Clip.isHumanMotion && !allowNonHumanoidAnimations.Value)
                {
                    DanceLog.Warning($"Skipping non-Humanoid clip '{animation.Clip.name}'. Set AllowNonHumanoidAnimations=true to force-import it.");
                    continue;
                }

                Emote.EmoteType type = animation.Clip.isLooping ? Emote.EmoteType.Loop : Emote.EmoteType.OneShot;
                string safeName = SanitizeIdentifier(animation.Clip.name);
                string systemName = $"NadiyaJafi_Auto_{safeName}_{StableHash(animation.Clip.name):X8}";
                string displayName = "LC " + HumanizeName(animation.Clip.name);

                if (RegisterDance(animation, systemName, displayName, type, new[] { animation.Clip.name }))
                {
                    registeredClips.Add(animation.Clip.GetInstanceID());
                    registered++;
                    if (FindBestAudio(animation, new[] { animation.Clip.name }, bundles) != null)
                    {
                        withMusic++;
                    }
                }
            }
        }

        DanceLog.Info(
            $"{PluginName} loaded successfully: {registered} emote(s) registered, {withMusic} paired with bundle music, " +
            $"{bundles.Count} AssetBundle(s) accepted by Unity.");

        if (enableMusic.Value)
        {
            StartCoroutine(LoadExternalMusic(pluginDirectory));
        }

        ReleaseNonAudioBundleFiles();
    }

    private void OnDestroy()
    {
        foreach (AssetBundle bundle in loadedBundles)
        {
            if (bundle != null)
            {
                bundle.Unload(false);
            }
        }
        loadedBundles.Clear();
        audioBackedBundles.Clear();
        SourceRigRegistry.Clear();
        ShutdownFinalRuntime();
        PEAKEmoteLib.Plugin.Shutdown();
    }

    private void BindConfig()
    {
        enableKnownDances = Config.Bind("Import", "EnableKnownDances", true, "Register Default Dance, Floss and Dab using explicit matching rules.");
        autoImportAllAnimations = Config.Bind("Import", "AutoImportAllAnimations", true, "Register every other usable AnimationClip found in the shipped or custom bundles.");
        allowNonHumanoidAnimations = Config.Bind("Import", "AllowNonHumanoidAnimations", false, "Force-register clips not marked as Humanoid. They may deform the PEAK character.");
        disableIk = Config.Bind("Animation", "DisableIKWhileDancing", true, "Continuously suppress PEAK IK while a custom dance is active.");
        useBundleIcons = Config.Bind("Animation", "UseBundleIcons", true, "Use matching Sprite/Texture assets from icon bundles (including huluobopopex) before generating fallback icons.");

        enableMusic = Config.Bind("Music", "EnableMusic", true, "Play matching AudioClip assets for custom emotes. Local playback is listener-relative; remote playback can remain positional.");
        musicVolume = Config.Bind("Music", "Volume", 0.50f, new ConfigDescription("Dance-music multiplier. With FollowGameMusicVolume enabled, 0.50 means half of the game's current Music volume.", new AcceptableValueRange<float>(0f, 1f)));
        followGameMusicVolume = Config.Bind("Music", "FollowGameMusicVolume", true, "Route dance audio through PEAK's Music mixer group when available, so the in-game Music slider and mute state control it. Falls back to tracking the game's main-music AudioSource volume.");
        musicSpatialBlend = Config.Bind("Music", "SpatialBlend", 1f, new ConfigDescription("Remote-player music only: 0 is global 2D audio; 1 is positional 3D audio. Local-player music is listener-relative to avoid camera distance issues.", new AcceptableValueRange<float>(0f, 1f)));
        musicMinDistance = Config.Bind("Music", "MinDistance", 2f, new ConfigDescription("Distance at which music begins attenuating.", new AcceptableValueRange<float>(0.1f, 50f)));
        musicMaxDistance = Config.Bind("Music", "MaxDistance", 24f, new ConfigDescription("Maximum audible distance.", new AcceptableValueRange<float>(1f, 200f)));

        dumpAssetInventory = Config.Bind("Debug", "DumpAssetInventory", false, "Write a TSV inventory of every loaded animation/audio/icon/Avatar/model asset beside the plugin DLL.");

        replaceModelWhileDancing = Config.Bind("Model", "ReplaceModelWhileDancing", true, "Show a complete Lethal Company model-pack character while custom Humanoid dances play. Disable to animate PEAK's original model instead.");
        preferredModel = Config.Bind("Model", "PreferredModel", "example70", "Preferred model prefab/avatar/source name. Partial catalog names such as example70 or example90 are accepted and can also be selected from the GUI.");
        enableModelCycling = Config.Bind("Model", "EnableModelCycling", true, "Allow PageUp/PageDown to select the previous or next catalog model. Selection remains lazy and does not load a bundle until a dance uses it.");
        autoScaleVisibleModel = Config.Bind("Model", "AutoScale", true, "Automatically match the replacement model's rendered height to PEAK's third-person body, with a conservative full-body-bone fallback.");
        visibleModelScale = Config.Bind("Model", "ScaleMultiplier", 1f, new ConfigDescription("Additional replacement-model scale multiplier after visual-height auto scaling. Use values below 1 only for personal fine tuning.", new AcceptableValueRange<float>(0.1f, 5f)));
        visibleModelTargetHeightRatio = Config.Bind("Model", "TargetHeightRatio", 0.95f, new ConfigDescription("Replacement-model visual height relative to PEAK's third-person body. 0.95 keeps large imported models slightly smaller and prevents camera clipping.", new AcceptableValueRange<float>(0.5f, 1.5f)));
        visibleModelHeightOffset = Config.Bind("Model", "HeightOffset", 0f, new ConfigDescription("Replacement-model vertical offset in metres.", new AcceptableValueRange<float>(-3f, 3f)));
        visibleModelForwardOffset = Config.Bind("Model", "ForwardOffset", 2.5f, new ConfigDescription("Preferred distance from the player when spawning a visible dance model. Collision checks may choose a side position or a shorter safe distance when the forward area is blocked.", new AcceptableValueRange<float>(0.8f, 5f)));
        visibleModelYaw = Config.Bind("Model", "YawDegrees", 0f, new ConfigDescription("Rotate the replacement model around the vertical axis.", new AcceptableValueRange<float>(-180f, 180f)));
        hidePeakRenderers = Config.Bind("Model", "HidePeakRenderers", false, "Optionally hide PEAK's original body after the spawned dance model is confirmed visible. Disabled by default because the dance model now appears beside/in front of the player instead of replacing the body at the same position.");
        groundVisibleModelFeet = Config.Bind("Model", "GroundFeet", true, "Ground the replacement model's lowest animated foot/toe after its final collision-aware spawn position is chosen.");
        visibleModelGroundOffset = Config.Bind("Model", "GroundOffset", 0.02f, new ConfigDescription("Small clearance above PEAK's foot plane after automatic grounding. Increase slightly if shoe soles still clip into terrain.", new AcceptableValueRange<float>(-0.25f, 0.5f)));

        stabilizeCameraWhileDancing = Config.Bind("Playback", "StabilizeCamera", true, "Do not run PEAK's native Dance2/head-bob animation while a custom Humanoid source model is driving the dance. This prevents the local camera from inheriting invisible body motion.");
        cancelEmoteOnMovement = Config.Bind("Playback", "CancelOnMovement", false, "Stop a custom dance when normal movement input is pressed. Disabled by default so the replacement model keeps dancing while following the player.");
        cancelEmoteOnJump = Config.Bind("Playback", "CancelOnJump", true, "Stop a custom dance when jump is pressed.");
        cancelEmoteWhenAirborne = Config.Bind("Playback", "CancelWhenAirborne", true, "Stop a custom dance after the player has been airborne for more than 0.2 seconds.");

        transferPelvisPosition = Config.Bind("Retarget", "TransferPelvisPosition", true, "When using PEAK's original model, transfer only the verified pelvis/waist translation with strict clamping. The player root is never moved.");
        pelvisPositionWeight = Config.Bind("Retarget", "PelvisPositionWeight", 0.85f, new ConfigDescription("Blend weight for safe pelvis translation.", new AcceptableValueRange<float>(0f, 1f)));
        maxPelvisOffset = Config.Bind("Retarget", "MaxPelvisOffset", 0.35f, new ConfigDescription("Maximum pelvis offset from the bind pose in metres.", new AcceptableValueRange<float>(0f, 1.5f)));

        defaultsRevision = Config.Bind("Migration", "DefaultsRevision", 0, "Internal one-time default migration marker. Do not edit unless you intentionally want migrations to run again.");
        BindFinalConfig();
    }

    private void ApplyConfigMigrations()
    {
        const int currentRevision = 207;
        if (defaultsRevision.Value >= currentRevision)
        {
            return;
        }

        bool changed = RemoveObsoleteConfigurationEntries();

        // 1.3.2 and earlier shipped 0.65 as the untouched default. Only migrate
        // that exact legacy value so deliberately customized volumes are preserved.
        if (Mathf.Abs(musicVolume.Value - 0.65f) <= 0.0001f)
        {
            musicVolume.Value = 0.50f;
            changed = true;
        }

        const string legacyExternalPath = @"C:\Program Files (x86)\Steam\steamapps\common\Lethal Company\BepInEx\plugins";
        const string legacyExternalFilter = "Customize;HuluoboEmotesEX;CustomSounds;LethalEmotes;Emote;Dance;ModelReplacement;Suits";
        if (defaultsRevision.Value < 203 &&
            string.Equals((externalImportPath.Value ?? string.Empty).TrimEnd('\\', '/'), legacyExternalPath.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase) &&
            string.Equals(externalImportPackageFilter.Value ?? string.Empty, legacyExternalFilter, StringComparison.Ordinal))
        {
            externalImportPath.Value = Paths.PluginPath;
            externalImportPackageFilter.Value = string.Empty;
            changed = true;
        }

        if (defaultsRevision.Value < 206 && visibleModelForwardOffset.Value <= 0.8001f)
        {
            // Older releases spawned the replacement at the player's origin.
            // Move untouched/near-zero setups into a visible, collision-checked
            // preview position and keep the original PEAK body visible by default.
            visibleModelForwardOffset.Value = 1.4f;
            if (hidePeakRenderers.Value)
            {
                hidePeakRenderers.Value = false;
            }
            changed = true;
        }

        if (defaultsRevision.Value < 207 && Mathf.Abs(visibleModelForwardOffset.Value - 1.4f) <= 0.0001f)
        {
            // 2.0.6 introduced 1.4 m as its untouched default. Move only that
            // exact value to the new 2.5 m viewing distance so custom positions
            // remain untouched.
            visibleModelForwardOffset.Value = 2.5f;
            changed = true;
        }

        defaultsRevision.Value = currentRevision;
        Config.Save();
    }

    private bool RemoveObsoleteConfigurationEntries()
    {
        bool removed = false;
        removed |= RemoveObsoleteConfigurationEntry("Performance", "ReleaseBundleFilesAfterLoad", true);
        removed |= RemoveObsoleteConfigurationEntry("Logging", "Level", "Off");
        removed |= RemoveObsoleteConfigurationEntry("Playback", "EnableUtilityWheelEntries", true);
        removed |= RemoveObsoleteConfigurationEntry("Model", "LoadAllModelPacks", false);
        removed |= RemoveObsoleteConfigurationEntry("Debug", "VerboseAssetLog", false);
        return removed;
    }

    private bool RemoveObsoleteConfigurationEntry<T>(string section, string key, T defaultValue)
    {
        Config.Bind(section, key, defaultValue, string.Empty);
        return Config.Remove(new ConfigDefinition(section, key));
    }

    private void ApplyRuntimeOptions()
    {
        RuntimeOptions.FollowGameMusicVolume = followGameMusicVolume.Value;
        RuntimeOptions.ReplaceModelWhileDancing = replaceModelWhileDancing.Value;
        RuntimeOptions.PreferredModel = preferredModel.Value ?? string.Empty;
        RuntimeOptions.EnableModelCycling = enableModelCycling.Value;
        RuntimeOptions.AutoScaleVisibleModel = autoScaleVisibleModel.Value;
        RuntimeOptions.VisibleModelScale = visibleModelScale.Value;
        RuntimeOptions.VisibleModelTargetHeightRatio = visibleModelTargetHeightRatio.Value;
        RuntimeOptions.VisibleModelHeightOffset = visibleModelHeightOffset.Value;
        RuntimeOptions.VisibleModelForwardOffset = visibleModelForwardOffset.Value;
        RuntimeOptions.VisibleModelYaw = visibleModelYaw.Value;
        RuntimeOptions.HidePeakRenderers = hidePeakRenderers.Value;
        RuntimeOptions.GroundVisibleModelFeet = groundVisibleModelFeet.Value;
        RuntimeOptions.VisibleModelGroundOffset = visibleModelGroundOffset.Value;
        RuntimeOptions.StabilizeCameraWhileDancing = stabilizeCameraWhileDancing.Value;
        RuntimeOptions.CancelEmoteOnMovement = cancelEmoteOnMovement.Value;
        RuntimeOptions.CancelEmoteOnJump = cancelEmoteOnJump.Value;
        RuntimeOptions.CancelEmoteWhenAirborne = cancelEmoteWhenAirborne.Value;
        RuntimeOptions.TransferPelvisPosition = transferPelvisPosition.Value;
        RuntimeOptions.PelvisPositionWeight = pelvisPositionWeight.Value;
        RuntimeOptions.MaxPelvisOffset = maxPelvisOffset.Value;
        ApplyFinalRuntimeOptions();
    }

    private bool RegisterDance(
        AnimationAsset animation,
        string systemName,
        string displayName,
        Emote.EmoteType type,
        IEnumerable<string> audioAliases)
    {
        AnimationClip clip = animation.Clip;
        if (clip.length <= 0.05f)
        {
            DanceLog.Warning($"Skipping empty clip '{clip.name}'.");
            return false;
        }
        if (clip.legacy)
        {
            DanceLog.Warning($"Skipping Legacy clip '{clip.name}'. Mecanim Animator states cannot retarget Legacy Animation clips.");
            return false;
        }
        if (!clip.isHumanMotion && !allowNonHumanoidAnimations.Value)
        {
            DanceLog.Warning(
                $"Skipping non-Humanoid clip '{clip.name}'. Generic clips retain source-model transform paths and caused the distorted partial poses in the old implementation. " +
                "Reimport it as Humanoid in PEAK's Unity version, or explicitly enable AllowNonHumanoidAnimations for diagnostics.");
            return false;
        }

        AudioClip? audio = FindBestAudio(animation, audioAliases, animation.AllBundles);
        bool loopAudio = type == Emote.EmoteType.Loop;
        Sprite icon = useBundleIcons.Value
            ? FindBestIcon(animation, audioAliases.Append(clip.name), animation.AllBundles) ?? ProceduralIconFactory.CreateSprite(clip.name, type)
            : ProceduralIconFactory.CreateSprite(clip.name, type);

        Emote emote = new(
            systemName,
            clip,
            icon,
            type,
            disableIk.Value,
            enableMusic.Value ? audio : null,
            loopAudio,
            musicVolume.Value,
            musicSpatialBlend.Value,
            musicMinDistance.Value,
            musicMaxDistance.Value);
        emote.AddLocalization(displayName, LocalizedText.Language.English);
        this.RegisterEmote(emote);
        externalMusicBindings.Add(new ExternalMusicBinding(
            emote,
            animation.Clip.name,
            audioAliases.Append(animation.Clip.name).ToArray(),
            loopAudio));

        string audioDescription = audio == null
            ? "no matching music"
            : $"music='{audio.name}' ({audio.length:0.00}s, loop={loopAudio})";
        DanceLog.Info(
            $"Registered '{displayName}' from '{clip.name}' ({clip.length:0.00}s, human={clip.isHumanMotion}, " +
            $"loop={type == Emote.EmoteType.Loop}), {audioDescription}.");
        return true;
    }

    private IEnumerator LoadExternalMusic(string pluginDirectory)
    {
        string[] musicDirectories =
        {
            Path.Combine(pluginDirectory, "music"),
            Path.Combine(pluginDirectory, "Music"),
            Path.Combine(pluginDirectory, "PEAKLethalDances", "music"),
            Path.Combine(pluginDirectory, "imports", "music")
        };

        string[] musicFiles = GetIndexedAudioFiles()
            .Concat(EnumerateLocalFilesSafely(musicDirectories.Where(Directory.Exists), true).Where(IsSupportedAudioFile))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (musicFiles.Length == 0)
        {
            DanceLog.Info("No external music files found. Optional music can be added as OGG/WAV/MP3 files in the plugin's 'music' folder.");
            yield break;
        }

        int loaded = 0;
        var audioCache = new Dictionary<string, AudioClip>(StringComparer.OrdinalIgnoreCase);
        foreach (ExternalMusicBinding binding in externalMusicBindings)
        {
            if (binding.Emote.AudioClip != null)
            {
                continue;
            }

            string? file = FindBestExternalMusicFile(musicFiles, binding.Aliases);
            if (file == null)
            {
                continue;
            }

            if (!audioCache.TryGetValue(file, out AudioClip? clip))
            {
                AudioType audioType = GetAudioType(file);
                string uri = new Uri(file).AbsoluteUri;
                using (UnityWebRequest request = UnityWebRequestMultimedia.GetAudioClip(uri, audioType))
                {
                    yield return request.SendWebRequest();

                    if (request.result != UnityWebRequest.Result.Success)
                    {
                        DanceLog.Warning($"Failed to load external music '{file}': {request.error}");
                        continue;
                    }

                    clip = DownloadHandlerAudioClip.GetContent(request);
                    if (clip == null)
                    {
                        DanceLog.Warning($"Unity returned no AudioClip for external music '{file}'.");
                        continue;
                    }

                    clip.name = Path.GetFileNameWithoutExtension(file);
                    audioCache[file] = clip;
                }
            }

            binding.Emote.ConfigureAudio(
                clip,
                true,
                binding.Loop,
                musicVolume.Value,
                musicSpatialBlend.Value,
                musicMinDistance.Value,
                musicMaxDistance.Value);
            loaded++;
            DanceLog.Info($"Paired external music '{Path.GetFileName(file)}' with animation '{binding.AnimationName}'.");
        }

        DanceLog.Info($"External music import finished: {loaded} emote(s) paired from {musicFiles.Length} file(s).");
    }

    private static bool IsSupportedAudioFile(string path)
    {
        string extension = Path.GetExtension(path);
        return extension.Equals(".ogg", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".wav", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".mp3", StringComparison.OrdinalIgnoreCase);
    }

    private static AudioType GetAudioType(string path)
    {
        string extension = Path.GetExtension(path);
        if (extension.Equals(".ogg", StringComparison.OrdinalIgnoreCase))
        {
            return AudioType.OGGVORBIS;
        }
        if (extension.Equals(".wav", StringComparison.OrdinalIgnoreCase))
        {
            return AudioType.WAV;
        }
        return AudioType.MPEG;
    }

    private static string? FindBestExternalMusicFile(IEnumerable<string> files, IEnumerable<string> aliases)
    {
        string[] normalizedAliases = aliases
            .Select(NormalizeMediaName)
            .Where(alias => alias.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return files
            .Select(file => new
            {
                File = file,
                Score = normalizedAliases.Length == 0
                    ? 0
                    : normalizedAliases.Max(alias => SimilarityScore(
                        NormalizeMediaName(Path.GetFileNameWithoutExtension(file)),
                        alias))
            })
            .Where(item => item.Score >= 350)
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.File, StringComparer.OrdinalIgnoreCase)
            .Select(item => item.File)
            .FirstOrDefault();
    }

    private IReadOnlyList<BundleAssetSet> LoadBundles(string pluginDirectory)
    {
        var results = new List<BundleAssetSet>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string candidate in EnumerateBundleCandidates(pluginDirectory))
        {
            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(candidate);
            }
            catch
            {
                continue;
            }

            if (!File.Exists(fullPath) || !visited.Add(fullPath) || !IsIndexedOrUnityBundle(fullPath))
            {
                continue;
            }

            if (IsModelPackBundle(fullPath) && !ShouldLoadModelPack(fullPath))
            {
                continue;
            }

            try
            {
                AssetBundle bundle = AssetBundle.LoadFromFile(fullPath);
                if (bundle == null)
                {
                    DanceLog.Warning($"Unity rejected AssetBundle '{fullPath}'. It may need rebuilding with PEAK's Unity version.");
                    continue;
                }

                loadedBundles.Add(bundle);
                loadedBundlePaths.Add(fullPath);
                AnimationClip[] clips = LoadAssetsSafely<AnimationClip>(bundle, fullPath, "AnimationClip");
                AudioClip[] audio = LoadAssetsSafely<AudioClip>(bundle, fullPath, "AudioClip");
                if (audio.Length > 0)
                {
                    audioBackedBundles.Add(bundle);
                }
                Sprite[] sprites = LoadAssetsSafely<Sprite>(bundle, fullPath, "Sprite");
                Texture2D[] textures = LoadAssetsSafely<Texture2D>(bundle, fullPath, "Texture2D");
                Avatar[] avatars = LoadAssetsSafely<Avatar>(bundle, fullPath, "Avatar");
                GameObject[] modelPrefabs = LoadAssetsSafely<GameObject>(bundle, fullPath, "GameObject/model");
                var set = new BundleAssetSet(fullPath, bundle, clips, audio, sprites, textures, avatars, modelPrefabs);
                results.Add(set);

                DanceLog.Info(
                    $"Loaded bundle '{Path.GetFileName(fullPath)}': {clips.Length} animation(s), {audio.Length} audio clip(s), " +
                    $"{sprites.Length} sprite(s), {textures.Length} texture(s), {avatars.Length} avatar(s), {modelPrefabs.Length} model/prefab asset(s).");
            }
            catch (Exception exception)
            {
                DanceLog.Warning($"Failed to load bundle '{fullPath}': {exception}");
            }
        }

        foreach (BundleAssetSet set in results)
        {
            set.AllBundles = results;
        }

        return results;
    }

    private T[] LoadAssetsSafely<T>(AssetBundle bundle, string bundlePath, string assetType)
        where T : UnityEngine.Object
    {
        try
        {
            return bundle.LoadAllAssets<T>().Where(asset => asset != null).ToArray();
        }
        catch (Exception exception)
        {
            DanceLog.Warning(
                $"Bundle '{Path.GetFileName(bundlePath)}' could not enumerate {assetType} assets: {exception.Message}. " +
                "Other supported asset types from this bundle will still be used.");
            return Array.Empty<T>();
        }
    }

    private IEnumerable<string> EnumerateBundleCandidates(string pluginDirectory)
    {
        var yielded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string[] compatibilityRoots =
        {
            pluginDirectory,
            Path.Combine(pluginDirectory, "bundles"),
            Path.Combine(pluginDirectory, "assets"),
            Path.Combine(pluginDirectory, "model-bundles"),
            Path.Combine(pluginDirectory, "models"),
            Path.Combine(pluginDirectory, "original-bundles"),
            Path.Combine(pluginDirectory, "PEAKLethalDances"),
            Path.Combine(pluginDirectory, "PEAKLethalDances", "bundles"),
            Path.Combine(pluginDirectory, "imports", "bundles"),
            Path.Combine(pluginDirectory, "imports", "model-bundles")
        };

        foreach (string root in compatibilityRoots)
        {
            foreach (string name in KnownBundleFileNames)
            {
                string candidate = Path.Combine(root, name);
                if (yielded.Add(candidate)) yield return candidate;
            }
        }

        // localDiscoveredFiles is built once during startup from the DLL folder
        // and PEAK's BepInEx/plugins root. It already includes nested folders, so
        // model, animation and music packs can be arranged however the user likes.
        foreach (string file in localDiscoveredFiles)
        {
            string extension = Path.GetExtension(file);
            if (extension.Equals(".manifest", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".dll", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".json", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".cfg", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".md", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".png", StringComparison.OrdinalIgnoreCase) ||
                IsSupportedAudioFile(file))
            {
                continue;
            }
            if (yielded.Add(file)) yield return file;
        }
    }

    private static bool IsModelPackBundle(string path)
    {
        string normalized = path.Replace('\\', '/').ToLowerInvariant();
        string fileName = Path.GetFileName(path).ToLowerInvariant();
        return normalized.Contains("/model-bundles/") || normalized.Contains("/models/") ||
               normalized.Contains("/customize") || normalized.Contains("modelreplacement") ||
               normalized.Contains("model-replacement") || normalized.Contains("/suits/") ||
               normalized.Contains("/avatars/") || normalized.Contains("/characters/") ||
               fileName.StartsWith("example_model_", StringComparison.OrdinalIgnoreCase);
    }

    private bool ShouldLoadModelPack(string path)
    {
        // The catalog is built from bundle filenames without opening them. With
        // lazy loading enabled, even the preferred model remains closed until a
        // Humanoid dance actually requests a visible model or hidden solver.
        if (lazyLoadModels.Value)
        {
            return false;
        }

        return true;
    }

    private static bool LooksLikeUnityBundle(string path)
    {
        try
        {
            using FileStream stream = File.OpenRead(path);
            if (stream.Length < 16)
            {
                return false;
            }

            byte[] header = new byte[8];
            int read = stream.Read(header, 0, header.Length);
            string magic = Encoding.ASCII.GetString(header, 0, read);
            return magic.StartsWith("UnityFS", StringComparison.Ordinal) ||
                   magic.StartsWith("UnityRaw", StringComparison.Ordinal) ||
                   magic.StartsWith("UnityWeb", StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    private static IReadOnlyList<AnimationAsset> SelectUniqueAnimations(IReadOnlyList<BundleAssetSet> bundles)
    {
        var all = new List<AnimationAsset>();
        foreach (BundleAssetSet bundle in bundles)
        {
            foreach (AnimationClip clip in bundle.Animations)
            {
                all.Add(new AnimationAsset(clip, bundle, bundles));
            }
        }

        return all
            .GroupBy(asset => NormalizeName(asset.Clip.name), StringComparer.OrdinalIgnoreCase)
            .Where(group => !string.IsNullOrEmpty(group.Key))
            .Select(group => group
                .OrderByDescending(asset => asset.Clip.isHumanMotion)
                .ThenByDescending(asset => asset.Clip.length)
                .First())
            .ToArray();
    }

    private static AnimationAsset? FindBestAnimation(IEnumerable<AnimationAsset> animations, IEnumerable<string> aliases)
    {
        AnimationAsset[] candidates = animations.ToArray();
        string[] normalizedAliases = aliases.Select(NormalizeName).Where(alias => alias.Length > 0).ToArray();

        foreach (string alias in normalizedAliases)
        {
            AnimationAsset? exact = candidates.FirstOrDefault(candidate => NormalizeName(candidate.Clip.name) == alias);
            if (exact != null)
            {
                return exact;
            }
        }

        return candidates
            .Select(candidate => new
            {
                Asset = candidate,
                Score = normalizedAliases.Length == 0
                    ? 0
                    : normalizedAliases.Max(alias => SimilarityScore(NormalizeName(candidate.Clip.name), alias))
            })
            .Where(candidate => candidate.Score > 0)
            .OrderByDescending(candidate => candidate.Score)
            .ThenByDescending(candidate => candidate.Asset.Clip.isHumanMotion)
            .ThenByDescending(candidate => candidate.Asset.Clip.length)
            .Select(candidate => candidate.Asset)
            .FirstOrDefault();
    }

    private static AudioClip? FindBestAudio(
        AnimationAsset animation,
        IEnumerable<string> aliases,
        IReadOnlyList<BundleAssetSet> bundles)
    {
        string[] normalizedAliases = aliases
            .Append(animation.Clip.name)
            .Select(NormalizeMediaName)
            .Where(alias => alias.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        AudioClip? matched = ScoreAudio(animation.SourceBundle.Audio, normalizedAliases);
        if (matched != null)
        {
            return matched;
        }

        if (animation.SourceBundle.Audio.Length == 1)
        {
            return animation.SourceBundle.Audio[0];
        }

        AudioClip[] allAudio = bundles.SelectMany(bundle => bundle.Audio).Distinct().ToArray();
        matched = ScoreAudio(allAudio, normalizedAliases);
        if (matched != null)
        {
            return matched;
        }

        // Some Lethal Company packs split animations and music into sibling
        // bundles while preserving the original asset order. HuluoboEmotesEX
        // uses this layout (huluoboanimex + huluobobgmex). Only use this
        // fallback when the family and counts strongly agree, avoiding random
        // music assignment for unrelated bundles.
        string family = NormalizeBundleFamily(animation.SourceBundle.Path);
        int animationIndex = Array.IndexOf(animation.SourceBundle.Animations, animation.Clip);
        if (animationIndex >= 0 && family.Length > 0)
        {
            foreach (BundleAssetSet audioBundle in bundles.Where(bundle =>
                         bundle.Audio.Length > 0 &&
                         string.Equals(NormalizeBundleFamily(bundle.Path), family, StringComparison.OrdinalIgnoreCase)))
            {
                if (audioBundle.Audio.Length == animation.SourceBundle.Animations.Length &&
                    animationIndex < audioBundle.Audio.Length)
                {
                    return audioBundle.Audio[animationIndex];
                }
            }
        }

        return null;
    }

    private static AudioClip? ScoreAudio(IEnumerable<AudioClip> clips, IReadOnlyList<string> aliases)
    {
        return clips
            .Where(clip => clip != null)
            .Select(clip => new
            {
                Clip = clip,
                Name = NormalizeMediaName(clip.name),
                Score = aliases.Count == 0 ? 0 : aliases.Max(alias => SimilarityScore(NormalizeMediaName(clip.name), alias))
            })
            .Where(item => item.Score >= 350)
            .OrderByDescending(item => item.Score)
            .ThenByDescending(item => item.Clip.length)
            .Select(item => item.Clip)
            .FirstOrDefault();
    }

    private static Sprite? FindBestIcon(
        AnimationAsset animation,
        IEnumerable<string> aliases,
        IReadOnlyList<BundleAssetSet> bundles)
    {
        string[] normalizedAliases = aliases
            .Append(animation.Clip.name)
            .Select(NormalizeMediaName)
            .Where(alias => alias.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Sprite? sprite = ScoreSprite(animation.SourceBundle.Sprites, normalizedAliases);
        if (sprite != null)
        {
            return sprite;
        }

        sprite = ScoreSprite(bundles.SelectMany(bundle => bundle.Sprites), normalizedAliases);
        if (sprite != null)
        {
            return sprite;
        }

        Texture2D? texture = ScoreTexture(animation.SourceBundle.Textures, normalizedAliases);
        if (texture == null)
        {
            texture = ScoreTexture(bundles.SelectMany(bundle => bundle.Textures), normalizedAliases);
        }
        if (texture != null)
        {
            return CreateSprite(texture);
        }

        // Split packs such as huluoboanimex/huluobopopex often preserve asset
        // order even when icon names are opaque. Only use positional pairing
        // when the bundle family and asset counts agree exactly.
        string family = NormalizeBundleFamily(animation.SourceBundle.Path);
        int animationIndex = Array.IndexOf(animation.SourceBundle.Animations, animation.Clip);
        if (animationIndex < 0 || family.Length == 0)
        {
            return null;
        }

        foreach (BundleAssetSet iconBundle in bundles.Where(bundle =>
                     string.Equals(NormalizeBundleFamily(bundle.Path), family, StringComparison.OrdinalIgnoreCase)))
        {
            if (iconBundle.Sprites.Length == animation.SourceBundle.Animations.Length &&
                animationIndex < iconBundle.Sprites.Length)
            {
                return iconBundle.Sprites[animationIndex];
            }
            if (iconBundle.Textures.Length == animation.SourceBundle.Animations.Length &&
                animationIndex < iconBundle.Textures.Length)
            {
                return CreateSprite(iconBundle.Textures[animationIndex]);
            }
        }

        return null;
    }

    private static Sprite? ScoreSprite(IEnumerable<Sprite> sprites, IReadOnlyList<string> aliases)
    {
        return sprites
            .Where(sprite => sprite != null)
            .Select(sprite => new
            {
                Sprite = sprite,
                Score = aliases.Count == 0
                    ? 0
                    : aliases.Max(alias => SimilarityScore(NormalizeMediaName(sprite.name), alias))
            })
            .Where(item => item.Score >= 350)
            .OrderByDescending(item => item.Score)
            .ThenByDescending(item => item.Sprite.rect.width * item.Sprite.rect.height)
            .Select(item => item.Sprite)
            .FirstOrDefault();
    }

    private static Texture2D? ScoreTexture(IEnumerable<Texture2D> textures, IReadOnlyList<string> aliases)
    {
        return textures
            .Where(texture => texture != null && texture.width >= 16 && texture.height >= 16)
            .Select(texture => new
            {
                Texture = texture,
                Score = aliases.Count == 0
                    ? 0
                    : aliases.Max(alias => SimilarityScore(NormalizeMediaName(texture.name), alias))
            })
            .Where(item => item.Score >= 350)
            .OrderByDescending(item => item.Score)
            .ThenByDescending(item => item.Texture.width * item.Texture.height)
            .Select(item => item.Texture)
            .FirstOrDefault();
    }

    private static Sprite CreateSprite(Texture2D texture)
    {
        return Sprite.Create(
            texture,
            new Rect(0f, 0f, texture.width, texture.height),
            new Vector2(0.5f, 0.5f));
    }

    private static string NormalizeBundleFamily(string path)
    {
        string name = NormalizeName(Path.GetFileName(path));
        string[] roleWords =
        {
            "animations", "animation", "anim", "emotes", "emote",
            "bgm", "audio", "music", "sounds", "sound", "pop", "icons", "icon"
        };
        foreach (string word in roleWords)
        {
            name = name.Replace(word, string.Empty);
        }
        return name;
    }

    private void WriteAssetInventory(string pluginDirectory, IReadOnlyList<BundleAssetSet> bundles)
    {
        try
        {
            string path = Path.Combine(pluginDirectory, "PEAKLethalDances_assets.tsv");
            using StreamWriter writer = new(path, false, new UTF8Encoding(false));
            writer.WriteLine("bundle\ttype\tindex\tname\tlength_seconds\thumanoid\tloop\tchannels\tfrequency\tdetails");
            foreach (BundleAssetSet bundle in bundles)
            {
                string bundleName = Path.GetFileName(bundle.Path).Replace("\t", " ");
                for (int index = 0; index < bundle.Animations.Length; index++)
                {
                    AnimationClip clip = bundle.Animations[index];
                    string assetName = clip.name.Replace("\t", " ");
                    writer.WriteLine($"{bundleName}\tAnimationClip\t{index}\t{assetName}\t{clip.length:0.000}\t{clip.isHumanMotion}\t{clip.isLooping}\t\t\tlegacy={clip.legacy}");
                }
                for (int index = 0; index < bundle.Audio.Length; index++)
                {
                    AudioClip clip = bundle.Audio[index];
                    string assetName = clip.name.Replace("\t", " ");
                    writer.WriteLine($"{bundleName}\tAudioClip\t{index}\t{assetName}\t{clip.length:0.000}\t\t\t{clip.channels}\t{clip.frequency}\tloadState={clip.loadState}");
                }
                for (int index = 0; index < bundle.Sprites.Length; index++)
                {
                    Sprite sprite = bundle.Sprites[index];
                    writer.WriteLine($"{bundleName}\tSprite\t{index}\t{sprite.name.Replace("\t", " ")}\t\t\t\t\t\t{sprite.rect.width:0}x{sprite.rect.height:0}");
                }
                for (int index = 0; index < bundle.Textures.Length; index++)
                {
                    Texture2D texture = bundle.Textures[index];
                    writer.WriteLine($"{bundleName}\tTexture2D\t{index}\t{texture.name.Replace("\t", " ")}\t\t\t\t\t\t{texture.width}x{texture.height}");
                }
                for (int index = 0; index < bundle.Avatars.Length; index++)
                {
                    Avatar avatar = bundle.Avatars[index];
                    writer.WriteLine($"{bundleName}\tAvatar\t{index}\t{avatar.name.Replace("\t", " ")}\t\t{avatar.isHuman}\t\t\t\tvalid={avatar.isValid}");
                }
                for (int index = 0; index < bundle.ModelPrefabs.Length; index++)
                {
                    GameObject model = bundle.ModelPrefabs[index];
                    Animator? sourceAnimator = model.GetComponentInChildren<Animator>(true);
                    string avatarName = sourceAnimator?.avatar == null ? "none" : sourceAnimator.avatar.name;
                    writer.WriteLine($"{bundleName}\tGameObject\t{index}\t{model.name.Replace("\t", " ")}\t\t\t\t\t\tanimator={(sourceAnimator == null ? "none" : sourceAnimator.name)};avatar={avatarName}");
                }
            }
            DanceLog.Info($"Wrote loaded asset inventory: {path}");
        }
        catch (Exception exception)
        {
            DanceLog.Warning($"Could not write asset inventory: {exception.Message}");
        }
    }

    private static int SimilarityScore(string value, string alias)
    {
        if (value.Length == 0 || alias.Length == 0)
        {
            return 0;
        }
        if (value == alias)
        {
            return 1000;
        }
        if (value.EndsWith(alias, StringComparison.Ordinal) || alias.EndsWith(value, StringComparison.Ordinal))
        {
            return 800 - Math.Abs(value.Length - alias.Length);
        }
        if (value.Contains(alias) || alias.Contains(value))
        {
            return 600 - Math.Abs(value.Length - alias.Length);
        }
        return 0;
    }

    private static bool IsExcludedHelperClip(string name)
    {
        string normalized = NormalizeName(name);
        string[] excluded = { "nobones", "tpose", "bindpose", "preview", "testanimation", "placeholder" };
        return excluded.Any(value => normalized.Contains(value));
    }

    private static string NormalizeMediaName(string value)
    {
        string normalized = NormalizeName(value);
        string[] noise = { "audioclip", "audio", "music", "song", "soundtrack", "sound", "sfx", "loop", "intro", "outro", "emote", "animation" };
        foreach (string word in noise)
        {
            normalized = normalized.Replace(word, string.Empty);
        }
        return normalized;
    }

    private static string NormalizeName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        foreach (char character in value)
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
            }
        }
        return builder.ToString();
    }

    private static string SanitizeIdentifier(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (char character in value)
        {
            builder.Append(char.IsLetterOrDigit(character) ? character : '_');
        }
        string result = builder.ToString().Trim('_');
        return result.Length == 0 ? "Dance" : result;
    }

    private static string HumanizeName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Dance";
        }

        var builder = new StringBuilder(value.Length + 8);
        char previous = '\0';
        foreach (char character in value.Replace('_', ' ').Replace('-', ' '))
        {
            if (char.IsUpper(character) && char.IsLower(previous))
            {
                builder.Append(' ');
            }
            builder.Append(character);
            previous = character;
        }
        return string.Join(" ", builder.ToString().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
    }

    private static uint StableHash(string value)
    {
        const uint offset = 2166136261;
        const uint prime = 16777619;
        uint hash = offset;
        foreach (char character in value)
        {
            hash ^= character;
            hash *= prime;
        }
        return hash;
    }

    private sealed class ExternalMusicBinding
    {
        public ExternalMusicBinding(Emote emote, string animationName, string[] aliases, bool loop)
        {
            Emote = emote;
            AnimationName = animationName;
            Aliases = aliases;
            Loop = loop;
        }

        public Emote Emote { get; }
        public string AnimationName { get; }
        public string[] Aliases { get; }
        public bool Loop { get; }
    }

    private sealed class DanceDefinition
    {
        public DanceDefinition(string systemName, string displayName, Emote.EmoteType type, string[] animationAliases, string[] audioAliases)
        {
            SystemName = systemName;
            DisplayName = displayName;
            Type = type;
            AnimationAliases = animationAliases;
            AudioAliases = audioAliases;
        }

        public string SystemName { get; }
        public string DisplayName { get; }
        public Emote.EmoteType Type { get; }
        public string[] AnimationAliases { get; }
        public string[] AudioAliases { get; }
    }

    private sealed class BundleAssetSet
    {
        public BundleAssetSet(
            string path,
            AssetBundle bundle,
            AnimationClip[] animations,
            AudioClip[] audio,
            Sprite[] sprites,
            Texture2D[] textures,
            Avatar[] avatars,
            GameObject[] modelPrefabs)
        {
            Path = path;
            Bundle = bundle;
            Animations = animations;
            Audio = audio;
            Sprites = sprites;
            Textures = textures;
            Avatars = avatars;
            ModelPrefabs = modelPrefabs;
            AllBundles = Array.Empty<BundleAssetSet>();
        }

        public string Path { get; }
        public AssetBundle Bundle { get; }
        public AnimationClip[] Animations { get; }
        public AudioClip[] Audio { get; }
        public Sprite[] Sprites { get; }
        public Texture2D[] Textures { get; }
        public Avatar[] Avatars { get; }
        public GameObject[] ModelPrefabs { get; }
        public IReadOnlyList<BundleAssetSet> AllBundles { get; set; }
    }

    private sealed class AnimationAsset
    {
        public AnimationAsset(AnimationClip clip, BundleAssetSet sourceBundle, IReadOnlyList<BundleAssetSet> allBundles)
        {
            Clip = clip;
            SourceBundle = sourceBundle;
            AllBundles = allBundles;
        }

        public AnimationClip Clip { get; }
        public BundleAssetSet SourceBundle { get; }
        public IReadOnlyList<BundleAssetSet> AllBundles { get; }
    }

    private static class ProceduralIconFactory
    {
        private const int Size = 128;

        public static Texture2D Create(string seed, Emote.EmoteType type)
        {
            var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false)
            {
                name = "PEAKLethalDances_" + SanitizeIdentifier(seed) + "_Icon",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            var pixels = new Color32[Size * Size];
            texture.SetPixels32(pixels);

            uint hash = StableHash(seed);
            Color32 background = new((byte)(30 + hash % 90), (byte)(30 + (hash >> 8) % 90), (byte)(30 + (hash >> 16) % 90), 230);
            Color32 figure = new(245, 245, 245, 255);
            DrawDisc(texture, 64, 64, 57, background);
            DrawDisc(texture, 64, 93, 10, figure);
            DrawLine(texture, 64, 81, 64, 50, 7, figure);

            int pose = (int)(hash % 3);
            if (pose == 0)
            {
                DrawLine(texture, 64, 72, 34, 87, 7, figure);
                DrawLine(texture, 64, 72, 96, 58, 7, figure);
                DrawLine(texture, 64, 50, 43, 22, 8, figure);
                DrawLine(texture, 64, 50, 86, 28, 8, figure);
            }
            else if (pose == 1)
            {
                DrawLine(texture, 64, 72, 27, 56, 7, figure);
                DrawLine(texture, 64, 72, 102, 85, 7, figure);
                DrawLine(texture, 64, 50, 47, 20, 8, figure);
                DrawLine(texture, 64, 50, 83, 21, 8, figure);
            }
            else
            {
                DrawLine(texture, 64, 73, 31, 95, 7, figure);
                DrawLine(texture, 64, 73, 99, 95, 7, figure);
                DrawLine(texture, 64, 50, 48, 21, 8, figure);
                DrawLine(texture, 64, 50, 85, 30, 8, figure);
            }

            if (type == Emote.EmoteType.Loop)
            {
                DrawDisc(texture, 104, 104, 10, new Color32(255, 255, 255, 210));
                DrawDisc(texture, 104, 104, 5, background);
            }

            texture.Apply(false, false);
            return texture;
        }

        public static Sprite CreateSprite(string seed, Emote.EmoteType type)
        {
            Texture2D texture = Create(seed, type);
            return Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f));
        }

        private static void DrawLine(Texture2D texture, int x0, int y0, int x1, int y1, int thickness, Color32 color)
        {
            int dx = Math.Abs(x1 - x0);
            int sx = x0 < x1 ? 1 : -1;
            int dy = -Math.Abs(y1 - y0);
            int sy = y0 < y1 ? 1 : -1;
            int error = dx + dy;

            while (true)
            {
                DrawDisc(texture, x0, y0, Math.Max(1, thickness / 2), color);
                if (x0 == x1 && y0 == y1)
                {
                    break;
                }
                int doubledError = error * 2;
                if (doubledError >= dy)
                {
                    error += dy;
                    x0 += sx;
                }
                if (doubledError <= dx)
                {
                    error += dx;
                    y0 += sy;
                }
            }
        }

        private static void DrawDisc(Texture2D texture, int centerX, int centerY, int radius, Color32 color)
        {
            int radiusSquared = radius * radius;
            for (int y = -radius; y <= radius; y++)
            {
                for (int x = -radius; x <= radius; x++)
                {
                    if (x * x + y * y > radiusSquared)
                    {
                        continue;
                    }
                    int pixelX = centerX + x;
                    int pixelY = centerY + y;
                    if (pixelX >= 0 && pixelX < Size && pixelY >= 0 && pixelY < Size)
                    {
                        texture.SetPixel(pixelX, pixelY, color);
                    }
                }
            }
        }
    }
}
