using System;
using System.Linq;
using BepInEx.Configuration;
using PEAKEmoteLib;
using UnityEngine;

namespace NadiyaJafi.PEAKLethalDances;

public sealed partial class Plugin
{
    private bool settingsGuiOpen;
    private int settingsTab;
    private Rect settingsWindow = new(120f, 70f, 920f, 650f);
    private Vector2 modelScroll;
    private Vector2 musicScroll;
    private Vector2 optionsScroll;
    private Vector2 importScroll;
    private string modelSearch = string.Empty;
    private string musicSearch = string.Empty;
    private string selectedMusicEmote = string.Empty;
    private string localDiscoveryRootsEdit = string.Empty;
    private string importPathEdit = string.Empty;
    private string importFilterEdit = string.Empty;
    private CursorLockMode previousCursorLock;
    private bool previousCursorVisible;
    private GUIStyle? titleStyle;
    private GUIStyle? tabStyle;
    private GUIStyle? selectedTabStyle;
    private GUIStyle? cardStyle;
    private GUIStyle? rowStyle;
    private GUIStyle? selectedRowStyle;
    private GUIStyle? mutedStyle;
    private Texture2D? panelTexture;
    private Texture2D? cardTexture;
    private Texture2D? selectedTexture;
    private Texture2D? overlayTexture;
    private float nextModelHotkeyAt;
    private float nextRandomDanceHotkeyAt;

    private const KeyCode PreviousModelHotkey = KeyCode.PageUp;
    private const KeyCode NextModelHotkey = KeyCode.PageDown;
    private const KeyCode RandomMusicHotkey = KeyCode.Y;

    private void Update()
    {
        PollExternalImport();
        PollLocalDiscovery();

        if (!settingsGuiOpen)
        {
            HandleGameplayHotkeys();
        }

        if (!enableSettingsGui.Value) return;
        if (Input.GetKeyDown(settingsHotkey.Value))
        {
            if (settingsGuiOpen) CloseSettingsGui(); else OpenSettingsGui();
        }
        if (settingsGuiOpen && Input.GetKeyDown(KeyCode.Escape)) CloseSettingsGui();
    }

    private void HandleGameplayHotkeys()
    {
        CharacterAnimations? animations = NetworkCommandSender.FindLocalAnimations();
        if (animations == null)
        {
            return;
        }

        if (Time.unscaledTime >= nextModelHotkeyAt)
        {
            int direction = Input.GetKeyDown(PreviousModelHotkey) ? -1 :
                Input.GetKeyDown(NextModelHotkey) ? 1 : 0;
            if (direction != 0 && EmoteCommands.TryCreateRelativeModelCommand(animations, direction, out string command))
            {
                nextModelHotkeyAt = Time.unscaledTime + 0.35f;
                NetworkCommandSender.Send(command);
            }
        }

        if (Time.unscaledTime >= nextRandomDanceHotkeyAt && Input.GetKeyDown(RandomMusicHotkey) &&
            EmoteCommands.TryChooseRandomMusicEmote(animations.GetCurrentEmote(), out Emote randomEmote))
        {
            nextRandomDanceHotkeyAt = Time.unscaledTime + 0.25f;
            NetworkCommandSender.Send(randomEmote.Name);
        }
    }

    private void OpenSettingsGui()
    {
        if (settingsGuiOpen) return;
        settingsGuiOpen = true;
        previousCursorLock = Cursor.lockState;
        previousCursorVisible = Cursor.visible;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        localDiscoveryRootsEdit = (localDiscoveryRoots.Value ?? string.Empty).Replace(";", Environment.NewLine);
        importPathEdit = externalImportPath.Value ?? string.Empty;
        importFilterEdit = externalImportPackageFilter.Value ?? string.Empty;
    }

    private void CloseSettingsGui()
    {
        if (!settingsGuiOpen) return;
        settingsGuiOpen = false;
        Cursor.lockState = previousCursorLock;
        Cursor.visible = previousCursorVisible;
    }

    private void OnGUI()
    {
        if (!settingsGuiOpen) return;
        EnsureGuiStyles();
        Matrix4x4 previousMatrix = GUI.matrix;
        float scale = Mathf.Clamp(settingsGuiScale.Value, 0.75f, 1.5f);
        GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1f));
        Rect overlay = new(0f, 0f, Screen.width / scale, Screen.height / scale);
        GUI.DrawTexture(overlay, overlayTexture!, ScaleMode.StretchToFill);
        settingsWindow.width = Mathf.Min(980f, overlay.width - 40f);
        settingsWindow.height = Mathf.Min(700f, overlay.height - 40f);
        settingsWindow.x = Mathf.Clamp(settingsWindow.x, 10f, Mathf.Max(10f, overlay.width - settingsWindow.width - 10f));
        settingsWindow.y = Mathf.Clamp(settingsWindow.y, 10f, Mathf.Max(10f, overlay.height - settingsWindow.height - 10f));
        settingsWindow = GUILayout.Window(904201, settingsWindow, DrawSettingsWindow, string.Empty, cardStyle!);
        GUI.matrix = previousMatrix;
    }

    private void DrawSettingsWindow(int id)
    {
        GUILayout.BeginVertical();
        GUILayout.BeginHorizontal();
        GUILayout.Label("PEAK Lethal Dances", titleStyle!, GUILayout.Height(42f));
        GUILayout.FlexibleSpace();
        GUILayout.Label("2.0.7", mutedStyle!, GUILayout.Width(90f));
        if (GUILayout.Button("×", GUILayout.Width(38f), GUILayout.Height(32f))) CloseSettingsGui();
        GUILayout.EndHorizontal();
        GUILayout.Space(6f);

        GUILayout.BeginHorizontal();
        DrawTab(0, "Models");
        DrawTab(1, "Music & Dances");
        DrawTab(2, "Playback");
        DrawTab(3, "Import");
        DrawTab(4, "System");
        GUILayout.EndHorizontal();
        GUILayout.Space(10f);

        switch (settingsTab)
        {
            case 0: DrawModelsTab(); break;
            case 1: DrawMusicTab(); break;
            case 2: DrawPlaybackTab(); break;
            case 3: DrawImportTab(); break;
            default: DrawSystemTab(); break;
        }
        GUILayout.FlexibleSpace();
        GUILayout.BeginHorizontal();
        GUILayout.Label($"GUI: {settingsHotkey.Value}  |  Models: PageUp/PageDown  |  Random music: Y  |  Dances: {EmoteRegistry.GetEmotes().Count}  |  Models: {GetAvailableModelNames().Count}", mutedStyle!);
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("Save settings", GUILayout.Width(130f), GUILayout.Height(30f))) ApplySettingsLive();
        GUILayout.EndHorizontal();
        GUILayout.EndVertical();
        GUI.DragWindow(new Rect(0f, 0f, settingsWindow.width - 60f, 48f));
    }

    private void DrawTab(int index, string label)
    {
        GUIStyle style = settingsTab == index ? selectedTabStyle! : tabStyle!;
        if (GUILayout.Button(label, style, GUILayout.Height(34f))) settingsTab = index;
    }

    private void DrawModelsTab()
    {
        GUILayout.BeginHorizontal();
        GUILayout.BeginVertical(cardStyle!, GUILayout.Width(310f));
        GUILayout.Label("Model library", titleStyle!);
        modelSearch = GUILayout.TextField(modelSearch, GUILayout.Height(28f));
        modelScroll = GUILayout.BeginScrollView(modelScroll);
        string current = NetworkCommandSender.FindLocalAnimations()?.GetSelectedModelName() ?? preferredModel.Value;
        foreach (string name in GetAvailableModelNames().Where(name => string.IsNullOrWhiteSpace(modelSearch) || name.IndexOf(modelSearch, StringComparison.OrdinalIgnoreCase) >= 0))
        {
            GUIStyle style = string.Equals(current, name, StringComparison.OrdinalIgnoreCase) ? selectedRowStyle! : rowStyle!;
            if (GUILayout.Button(name, style, GUILayout.Height(30f)))
            {
                preferredModel.Value = name;
                RuntimeOptions.PreferredModel = name;
                NetworkCommandSender.Send(EmoteCommands.CreateSelectModelCommand(name));
            }
        }
        GUILayout.EndScrollView();
        GUILayout.EndVertical();

        GUILayout.Space(10f);
        GUILayout.BeginVertical(cardStyle!);
        GUILayout.Label("Model settings", titleStyle!);
        Toggle("Show selected model while dancing", replaceModelWhileDancing);
        Toggle("Lazy-load model bundles", lazyLoadModels);
        Toggle("Automatically scale model", autoScaleVisibleModel);
        visibleModelTargetHeightRatio.Value = LabeledSlider("Target height", visibleModelTargetHeightRatio.Value, 0.5f, 1.5f, "0.00×");
        visibleModelScale.Value = LabeledSlider("Fine scale", visibleModelScale.Value, 0.1f, 2f, "0.00×");
        Toggle("Ground feet", groundVisibleModelFeet);
        visibleModelGroundOffset.Value = LabeledSlider("Sole clearance", visibleModelGroundOffset.Value, -0.05f, 0.20f, "0.000 m");
        visibleModelHeightOffset.Value = LabeledSlider("Height offset", visibleModelHeightOffset.Value, -1f, 1f, "0.00 m");
        visibleModelForwardOffset.Value = LabeledSlider("Spawn distance", visibleModelForwardOffset.Value, 0.8f, 5f, "0.00 m");
        visibleModelYaw.Value = LabeledSlider("Yaw", visibleModelYaw.Value, -180f, 180f, "0°");
        Toggle("Hide original body after visibility check", hidePeakRenderers);
        GUILayout.EndVertical();
        GUILayout.EndHorizontal();
    }

    private void DrawMusicTab()
    {
        var dances = EmoteRegistry.GetEmotes().Values
            .Where(emote => emote.AudioClip != null)
            .OrderBy(emote => emote.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        GUILayout.BeginHorizontal();
        GUILayout.BeginVertical(cardStyle!, GUILayout.Width(500f));
        GUILayout.Label("Music dance list", titleStyle!);
        musicSearch = GUILayout.TextField(musicSearch, GUILayout.Height(28f));
        musicScroll = GUILayout.BeginScrollView(musicScroll);
        foreach (Emote emote in dances.Where(emote => string.IsNullOrWhiteSpace(musicSearch) || emote.DisplayName.IndexOf(musicSearch, StringComparison.OrdinalIgnoreCase) >= 0 || emote.AudioClip!.name.IndexOf(musicSearch, StringComparison.OrdinalIgnoreCase) >= 0))
        {
            GUIStyle style = selectedMusicEmote == emote.Name ? selectedRowStyle! : rowStyle!;
            if (GUILayout.Button($"{emote.DisplayName}   ·   {emote.AudioClip!.name}   [{emote.AudioClip.length:0}s]", style, GUILayout.Height(31f))) selectedMusicEmote = emote.Name;
        }
        GUILayout.EndScrollView();
        GUILayout.EndVertical();
        GUILayout.Space(10f);
        GUILayout.BeginVertical(cardStyle!);
        GUILayout.Label("Music control", titleStyle!);
        Toggle("Enable dance music", enableMusic);
        Toggle("Follow game Music volume", followGameMusicVolume);
        musicVolume.Value = LabeledSlider("Relative volume", musicVolume.Value, 0f, 1f, "0%");
        GUILayout.Space(12f);
        GUI.enabled = !string.IsNullOrWhiteSpace(selectedMusicEmote);
        if (GUILayout.Button("Play selected dance", GUILayout.Height(38f))) NetworkCommandSender.Send(selectedMusicEmote);
        GUI.enabled = true;
        if (GUILayout.Button("Random music dance", GUILayout.Height(38f)) && EmoteCommands.TryChooseRandomMusicEmote(NetworkCommandSender.FindLocalAnimations()?.GetCurrentEmote(), out Emote random)) NetworkCommandSender.Send(random.Name);
        if (GUILayout.Button("Stop dance", GUILayout.Height(32f))) NetworkCommandSender.Send(EmoteCommands.StopDance);
        GUILayout.EndVertical();
        GUILayout.EndHorizontal();
    }

    private void DrawPlaybackTab()
    {
        optionsScroll = GUILayout.BeginScrollView(optionsScroll);
        GUILayout.BeginVertical(cardStyle!);
        GUILayout.Label("Playback & camera", titleStyle!);
        Toggle("Stabilize camera while dancing", stabilizeCameraWhileDancing);
        Toggle("Cancel on movement", cancelEmoteOnMovement);
        Toggle("Cancel on jump", cancelEmoteOnJump);
        Toggle("Cancel while airborne", cancelEmoteWhenAirborne);
        Toggle("Disable IK while dancing", disableIk);
        Toggle("Enable model cycling", enableModelCycling);
        GUILayout.Space(10f);
        GUILayout.Label("PEAK original-model retarget", titleStyle!);
        Toggle("Transfer pelvis position", transferPelvisPosition);
        pelvisPositionWeight.Value = LabeledSlider("Pelvis weight", pelvisPositionWeight.Value, 0f, 1f, "0.00");
        maxPelvisOffset.Value = LabeledSlider("Maximum pelvis offset", maxPelvisOffset.Value, 0f, 1f, "0.00 m");
        GUILayout.EndVertical();
        GUILayout.EndScrollView();
    }

    private void DrawImportTab()
    {
        importScroll = GUILayout.BeginScrollView(importScroll);
        GUILayout.BeginVertical(cardStyle!);
        GUILayout.Label("Local asset discovery", titleStyle!);
        GUILayout.Label("Choose exactly which directories are indexed. Enter one directory per line or separate directories with semicolons. The default is only {BepInExPlugins}; it already contains this mod's DLL directory.", mutedStyle!);
        Toggle("Enable local asset discovery", localDiscoveryEnabled);
        Toggle("Refresh index when PEAK starts", scanLocalOnStartup);
        Toggle("Scan subdirectories", scanLocalSubdirectories);
        Toggle("Extract UnityFS payloads from local DLLs", extractLocalEmbeddedBundles);
        maximumLocalDiscoveryFileMegabytes.Value = Mathf.RoundToInt(LabeledSlider("Maximum local file size", maximumLocalDiscoveryFileMegabytes.Value, 16f, 2048f, "0 MB"));
        GUILayout.Space(8f);
        GUILayout.Label("Scan roots");
        localDiscoveryRootsEdit = GUILayout.TextArea(localDiscoveryRootsEdit, GUILayout.MinHeight(76f));
        GUILayout.Label("Tokens: {BepInExPlugins} = PEAK\\BepInEx\\plugins, {ModDirectory} = this DLL's directory.", mutedStyle!);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Use PEAK plugins", GUILayout.Height(30f))) localDiscoveryRootsEdit = BepInExPluginsToken;
        if (GUILayout.Button("Add mod directory", GUILayout.Height(30f))) localDiscoveryRootsEdit = AppendDiscoveryRoot(localDiscoveryRootsEdit, ModDirectoryToken);
        GUI.enabled = localDiscoveryTask == null && importTask == null;
        if (GUILayout.Button(localDiscoveryTask == null ? "Apply and rescan" : "Scanning in background…", GUILayout.Height(30f)))
        {
            localDiscoveryRoots.Value = NormalizeDiscoveryRootSetting(localDiscoveryRootsEdit);
            localDiscoveryRootsEdit = (localDiscoveryRoots.Value ?? string.Empty).Replace(";", Environment.NewLine);
            Config.Save();
            StartLocalDiscoveryScan();
        }
        GUI.enabled = true;
        GUILayout.EndHorizontal();
        string[] resolvedRoots = GetLocalDiscoveryRoots().ToArray();
        GUILayout.Label("Active roots: " + (resolvedRoots.Length == 0 ? "none" : string.Join(" | ", resolvedRoots)), mutedStyle!);
        if (lastLocalDiscoveryResult != null) GUILayout.Label(lastLocalDiscoveryResult.Summary, lastLocalDiscoveryResult.Success ? rowStyle! : selectedRowStyle!);
        GUILayout.Label("Performance: directories are enumerated to detect new files, but unchanged candidates reuse local-asset-index.tsv and are not reopened for Bundle-header detection. Nested and duplicate roots are collapsed automatically. The internal imports/bundles, imports/model-bundles and imports/music caches are always included.", mutedStyle!);
        GUILayout.EndVertical();
        GUILayout.Space(10f);

        GUILayout.BeginVertical(cardStyle!);
        GUILayout.Label("Safe external asset importer", titleStyle!);
        GUILayout.Label("Only UnityFS AssetBundles and OGG/WAV/MP3 files are copied. Foreign plugin DLL code is never loaded or executed. The default source is PEAK's plugins directory; paste Lethal Company's plugins path here when importing from that game.", mutedStyle!);
        GUILayout.Space(8f);
        GUILayout.Label("Source folder");
        importPathEdit = GUILayout.TextField(importPathEdit, GUILayout.Height(28f));
        GUILayout.Label("Package filter (semicolon separated)");
        importFilterEdit = GUILayout.TextField(importFilterEdit, GUILayout.Height(28f));
        Toggle("Extract UnityFS payloads embedded in DLLs", extractEmbeddedBundles);
        Toggle("Copy external audio files", importExternalAudio);
        maximumImportFileMegabytes.Value = Mathf.RoundToInt(LabeledSlider("Maximum file size", maximumImportFileMegabytes.Value, 16f, 2048f, "0 MB"));
        GUILayout.Space(10f);
        GUI.enabled = importTask == null && localDiscoveryTask == null;
        if (GUILayout.Button(importTask == null ? "Scan and import assets" : "Importing in background…", GUILayout.Height(40f)))
        {
            externalImportPath.Value = importPathEdit.Trim();
            externalImportPackageFilter.Value = importFilterEdit.Trim();
            Config.Save();
            StartExternalImport();
        }
        GUI.enabled = true;
        if (lastImportReport != null)
        {
            GUILayout.Space(10f);
            GUILayout.Label(lastImportReport.Summary, lastImportReport.Success ? rowStyle! : selectedRowStyle!);
            foreach (string message in lastImportReport.Messages) GUILayout.Label("• " + message, mutedStyle!);
        }
        GUILayout.Space(8f);
        GUILayout.Label($"Import cache: {importsRoot}", mutedStyle!);
        GUILayout.Label("Newly imported dances/models are loaded on the next game restart.", mutedStyle!);
        GUILayout.EndVertical();
        GUILayout.EndScrollView();
    }

    private void DrawSystemTab()
    {
        GUILayout.BeginVertical(cardStyle!);
        GUILayout.Label("System", titleStyle!);
        Toggle("Enable settings GUI", enableSettingsGui);
        settingsGuiScale.Value = LabeledSlider("GUI scale", settingsGuiScale.Value, 0.75f, 1.5f, "0.00×");
        Toggle("Write asset inventory TSV", dumpAssetInventory);
        GUILayout.Space(12f);
        GUILayout.Label("Performance", titleStyle!);
        GUILayout.Label("• Selecting a model is instant; its bundle loads only when a dance needs it.\n• Visible model animation is capped at 30 updates per second.\n• Off-screen skinned meshes no longer update continuously.\n• Visibility checks are throttled and model bundles remain lazy-loaded.\n• PageUp/PageDown switch models; Y starts a music-first random dance.", mutedStyle!);
        GUILayout.EndVertical();
    }

    private static string AppendDiscoveryRoot(string current, string root)
    {
        string normalized = NormalizeDiscoveryRootSetting(current);
        string[] roots = normalized.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
        if (roots.Any(value => string.Equals(value, root, StringComparison.OrdinalIgnoreCase))) return normalized.Replace(";", Environment.NewLine);
        return (normalized.Length == 0 ? root : normalized + ";" + root).Replace(";", Environment.NewLine);
    }

    private static string NormalizeDiscoveryRootSetting(string value)
    {
        return string.Join(";", (value ?? string.Empty)
            .Split(new[] { ';', '\r', '\n', '|' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(path => path.Trim())
            .Where(path => path.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private void Toggle(string label, ConfigEntry<bool> entry)
    {
        entry.Value = GUILayout.Toggle(entry.Value, label, GUILayout.Height(25f));
    }

    private float LabeledSlider(string label, float value, float minimum, float maximum, string format)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label, GUILayout.Width(180f));
        value = GUILayout.HorizontalSlider(value, minimum, maximum);
        string text = format.Contains("%") ? (value * 100f).ToString("0") + "%" : value.ToString(format.Replace("×", string.Empty).Replace(" m", string.Empty).Replace("°", string.Empty)) + (format.Contains("×") ? "×" : format.Contains(" m") ? " m" : format.Contains("°") ? "°" : format.Contains("MB") ? " MB" : string.Empty);
        GUILayout.Label(text, GUILayout.Width(72f));
        GUILayout.EndHorizontal();
        return value;
    }

    private void EnsureGuiStyles()
    {
        if (titleStyle != null) return;
        panelTexture = MakeTexture(new Color(0.055f, 0.065f, 0.085f, 0.98f));
        cardTexture = MakeTexture(new Color(0.09f, 0.105f, 0.135f, 0.98f));
        selectedTexture = MakeTexture(new Color(0.18f, 0.42f, 0.65f, 0.95f));
        overlayTexture = MakeTexture(new Color(0f, 0f, 0f, 0.62f));
        cardStyle = new GUIStyle(GUI.skin.box) { normal = { background = panelTexture }, padding = new RectOffset(16, 16, 14, 14) };
        titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
        mutedStyle = new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = true, normal = { textColor = new Color(0.70f, 0.74f, 0.80f) } };
        tabStyle = new GUIStyle(GUI.skin.button) { fontSize = 13, margin = new RectOffset(2, 2, 2, 2) };
        selectedTabStyle = new GUIStyle(tabStyle) { normal = { background = selectedTexture }, fontStyle = FontStyle.Bold };
        rowStyle = new GUIStyle(GUI.skin.button) { alignment = TextAnchor.MiddleLeft, normal = { background = cardTexture }, padding = new RectOffset(10, 10, 5, 5) };
        selectedRowStyle = new GUIStyle(rowStyle) { normal = { background = selectedTexture }, fontStyle = FontStyle.Bold };
    }

    private static Texture2D MakeTexture(Color color)
    {
        var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
        texture.SetPixel(0, 0, color);
        texture.Apply(false, true);
        return texture;
    }
}
