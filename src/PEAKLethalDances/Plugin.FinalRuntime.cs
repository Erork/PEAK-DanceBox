using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using BepInEx;
using BepInEx.Configuration;
using PEAKEmoteLib;
using UnityEngine;

namespace NadiyaJafi.PEAKLethalDances;

public sealed partial class Plugin
{
    private ConfigEntry<bool> enableSettingsGui = null!;
    private ConfigEntry<KeyCode> settingsHotkey = null!;
    private ConfigEntry<float> settingsGuiScale = null!;
    private ConfigEntry<bool> lazyLoadModels = null!;
    private ConfigEntry<bool> localDiscoveryEnabled = null!;
    private ConfigEntry<string> localDiscoveryRoots = null!;
    private ConfigEntry<bool> scanLocalOnStartup = null!;
    private ConfigEntry<bool> scanLocalSubdirectories = null!;
    private ConfigEntry<bool> extractLocalEmbeddedBundles = null!;
    private ConfigEntry<int> maximumLocalDiscoveryFileMegabytes = null!;
    private ConfigEntry<bool> externalImportEnabled = null!;
    private ConfigEntry<string> externalImportPath = null!;
    private ConfigEntry<string> externalImportPackageFilter = null!;
    private ConfigEntry<bool> scanExternalImportsOnStartup = null!;
    private ConfigEntry<bool> extractEmbeddedBundles = null!;
    private ConfigEntry<bool> importExternalAudio = null!;
    private ConfigEntry<int> maximumImportFileMegabytes = null!;

    private const string BepInExPluginsToken = "{BepInExPlugins}";
    private const string ModDirectoryToken = "{ModDirectory}";
    private const string LocalAssetIndexFileName = "local-asset-index.tsv";

    private static readonly HashSet<string> LocalIndexIgnoredExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".dll", ".exe", ".pdb", ".xml", ".json", ".cfg", ".ini", ".config",
        ".md", ".txt", ".log", ".csv", ".tsv", ".yml", ".yaml", ".manifest",
        ".png", ".jpg", ".jpeg", ".webp", ".gif", ".bmp", ".tga", ".psd",
        ".cs", ".csproj", ".sln", ".ps1", ".cmd", ".bat", ".sh", ".sha256",
        ".zip", ".7z", ".rar", ".tar", ".gz", ".pdf", ".docx", ".xlsx", ".pptx",
        ".db", ".sqlite", ".cache", ".tmp", ".bak"
    };

    private readonly Dictionary<string, ModelCatalogEntry> modelCatalog = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> loadedBundlePaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> indexedBundlePaths = new(StringComparer.OrdinalIgnoreCase);
    private string[] cachedModelNames = Array.Empty<string>();
    private string[] localDiscoveredFiles = Array.Empty<string>();
    private bool modelCatalogDirty = true;
    private string pluginDirectory = string.Empty;
    private string importsRoot = string.Empty;
    private Task<ExternalImportReport>? importTask;
    private ExternalImportReport? lastImportReport;
    private Task<LocalDiscoveryResult>? localDiscoveryTask;
    private LocalDiscoveryResult? lastLocalDiscoveryResult;

    private void BindFinalConfig()
    {
        enableSettingsGui = Config.Bind("GUI", "Enabled", true, "Enable the in-game settings window.");
        settingsHotkey = Config.Bind("GUI", "Hotkey", KeyCode.Comma, "Open or close the settings GUI. Default: comma (,).");
        settingsGuiScale = Config.Bind("GUI", "Scale", 1f, new ConfigDescription("Settings GUI scale.", new AcceptableValueRange<float>(0.75f, 1.5f)));
        lazyLoadModels = Config.Bind("Performance", "LazyLoadModels", true, "Load only the selected model bundle and load other models on demand. This substantially reduces startup time and memory use.");

        localDiscoveryEnabled = Config.Bind("Local Discovery", "Enabled", true, "Discover model, animation and music assets under the configured scan roots.");
        localDiscoveryRoots = Config.Bind("Local Discovery", "ScanRoots", BepInExPluginsToken,
            "Directories to scan, separated by semicolons or new lines. Tokens: {BepInExPlugins} and {ModDirectory}. The default scans only PEAK's BepInEx/plugins tree, which already contains this mod DLL directory.");
        scanLocalOnStartup = Config.Bind("Local Discovery", "ScanOnStartup", true, "Refresh the cached local asset index during startup. Disable this for a completely manual rescan workflow.");
        scanLocalSubdirectories = Config.Bind("Local Discovery", "ScanSubdirectories", true, "Recursively scan subdirectories under each configured root.");
        extractLocalEmbeddedBundles = Config.Bind("Local Discovery", "ExtractBundlesFromDll", true, "Safely extract embedded UnityFS payloads from DLLs under the configured roots without loading or executing those DLLs.");
        maximumLocalDiscoveryFileMegabytes = Config.Bind("Local Discovery", "MaximumFileMB", 512, new ConfigDescription("Skip larger files during local discovery.", new AcceptableValueRange<int>(16, 2048)));

        externalImportEnabled = Config.Bind("External Import", "Enabled", true, "Allow safe asset import from another BepInEx plugin folder. Foreign DLL code is never executed.");
        externalImportPath = Config.Bind("External Import", "SourcePath", Paths.PluginPath, "External import root. Defaults to PEAK's BepInEx/plugins directory; set this to Lethal Company's plugins directory when importing from that game.");
        externalImportPackageFilter = Config.Bind("External Import", "PackageFilter", string.Empty, "When SourcePath points to a plugins root, optionally scan only top-level folders containing one of these semicolon-separated terms. Blank scans all subdirectories.");
        scanExternalImportsOnStartup = Config.Bind("External Import", "ScanOnStartup", false, "Scan the external folder during startup. Disabled by default for performance; use the GUI Import tab instead.");
        extractEmbeddedBundles = Config.Bind("External Import", "ExtractBundlesFromDll", true, "Extract UnityFS payloads embedded in model replacement DLLs without loading or executing the DLL.");
        importExternalAudio = Config.Bind("External Import", "CopyAudioFiles", true, "Copy OGG/WAV/MP3 files into the local import cache.");
        maximumImportFileMegabytes = Config.Bind("External Import", "MaximumFileMB", 512, new ConfigDescription("Skip larger files while scanning.", new AcceptableValueRange<int>(16, 2048)));
    }

    private void InitializeFinalRuntime(string directory)
    {
        pluginDirectory = directory;
        importsRoot = Path.Combine(pluginDirectory, "imports");
        Directory.CreateDirectory(importsRoot);
        Directory.CreateDirectory(Path.Combine(importsRoot, "bundles"));
        Directory.CreateDirectory(Path.Combine(importsRoot, "model-bundles"));
        Directory.CreateDirectory(Path.Combine(importsRoot, "music"));

        if (localDiscoveryEnabled.Value)
        {
            LocalDiscoveryResult discovery = scanLocalOnStartup.Value
                ? RunLocalDiscoveryPass(GetLocalDiscoveryRoots().ToArray(), scanLocalSubdirectories.Value, extractLocalEmbeddedBundles.Value, maximumLocalDiscoveryFileMegabytes.Value)
                : LoadCachedLocalDiscoveryResult();
            ApplyLocalDiscoveryResult(discovery);
        }
        else
        {
            ApplyLocalDiscoveryResult(new LocalDiscoveryResult());
        }

        if (externalImportEnabled.Value && scanExternalImportsOnStartup.Value)
        {
            lastImportReport = ExternalResourceImporter.Import(CreateImportOptions());
            if (!lastImportReport.Success) DanceLog.Warning(lastImportReport.Error);
        }

        BuildModelCatalog();
    }

    private void ShutdownFinalRuntime()
    {
        CloseSettingsGui();
        RuntimeOptions.AvailableModelNamesProvider = null;
        RuntimeOptions.EnsureModelAvailable = null;
    }

    private void ApplyFinalRuntimeOptions()
    {
        RuntimeOptions.AvailableModelNamesProvider = GetAvailableModelNames;
        RuntimeOptions.EnsureModelAvailable = EnsureModelAvailable;
    }

    private ExternalImportOptions CreateImportOptions()
    {
        string sourceRoot = externalImportPath.Value ?? string.Empty;
        bool sourceIsLocalPluginTree = IsPathInside(sourceRoot, Paths.PluginPath);
        return new ExternalImportOptions
        {
            SourceRoot = sourceRoot,
            ExcludedRoots = sourceIsLocalPluginTree ? new[] { importsRoot } : Array.Empty<string>(),
            DestinationRoot = importsRoot,
            PackageFilter = externalImportPackageFilter.Value ?? string.Empty,
            ExtractEmbeddedBundles = extractEmbeddedBundles.Value,
            Recursive = true,
            // Local PEAK assets are discovered in place; copying them into the
            // imports cache would create duplicates. External game folders are
            // copied so the source game can be updated or removed independently.
            CopyBundles = !sourceIsLocalPluginTree,
            CopyAudio = importExternalAudio.Value && !sourceIsLocalPluginTree,
            MaximumFileMegabytes = maximumImportFileMegabytes.Value
        };
    }

    private static bool IsPathInside(string candidate, string root)
    {
        try
        {
            string candidatePath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(candidate.Trim().Trim('"')))
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string rootPath = Path.GetFullPath(root)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return candidatePath.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    private void StartExternalImport()
    {
        if (!externalImportEnabled.Value || importTask is { IsCompleted: false } || localDiscoveryTask is { IsCompleted: false }) return;
        ExternalImportOptions options = CreateImportOptions();
        importTask = Task.Run(() => ExternalResourceImporter.Import(options));
    }

    private void PollExternalImport()
    {
        if (importTask == null || !importTask.IsCompleted) return;
        try { lastImportReport = importTask.Result; }
        catch (Exception exception) { lastImportReport = new ExternalImportReport { Error = exception.Message }; }
        importTask = null;
        if (localDiscoveryEnabled.Value) StartLocalDiscoveryScan();
        else BuildModelCatalog();
    }


    private void StartLocalDiscoveryScan()
    {
        if (!localDiscoveryEnabled.Value || localDiscoveryTask is { IsCompleted: false } || importTask is { IsCompleted: false }) return;
        string[] roots = GetLocalDiscoveryRoots().ToArray();
        bool recursive = scanLocalSubdirectories.Value;
        bool extractDllBundles = extractLocalEmbeddedBundles.Value;
        int maximumFileMegabytes = maximumLocalDiscoveryFileMegabytes.Value;
        localDiscoveryTask = Task.Run(() => RunLocalDiscoveryPass(roots, recursive, extractDllBundles, maximumFileMegabytes));
    }

    private void PollLocalDiscovery()
    {
        if (localDiscoveryTask == null || !localDiscoveryTask.IsCompleted) return;
        try
        {
            ApplyLocalDiscoveryResult(localDiscoveryTask.Result);
            BuildModelCatalog();
        }
        catch (Exception exception)
        {
            lastLocalDiscoveryResult = new LocalDiscoveryResult { Error = exception.Message };
        }
        localDiscoveryTask = null;
    }

    private LocalDiscoveryResult RunLocalDiscoveryPass(string[] roots, bool recursive, bool extractDllBundles, int maximumFileMegabytes)
    {
        var result = new LocalDiscoveryResult { Roots = roots };
        if (roots.Length == 0)
        {
            result.Error = "No valid local discovery directory is configured.";
            return result;
        }

        if (extractDllBundles)
        {
            var options = new ExternalImportOptions
            {
                SourceRoots = roots,
                ExcludedRoots = new[] { importsRoot },
                DestinationRoot = importsRoot,
                PackageFilter = string.Empty,
                ExtractEmbeddedBundles = true,
                Recursive = recursive,
                CopyBundles = false,
                CopyAudio = false,
                MaximumFileMegabytes = maximumFileMegabytes
            };
            ExternalImportReport embeddedReport = ExternalResourceImporter.Import(options);
            result.EmbeddedBundleReport = embeddedReport;
            if (!embeddedReport.Success)
            {
                result.Error = embeddedReport.Error;
                return result;
            }
        }

        BuildLocalAssetIndex(roots, recursive, maximumFileMegabytes, result);
        return result;
    }

    private IEnumerable<string> GetLocalDiscoveryRoots()
    {
        var configuredRoots = SplitDiscoveryRoots(localDiscoveryRoots.Value);
        var normalized = new List<string>();
        IEnumerable<string> internalImportRoots = new[]
        {
            Path.Combine(importsRoot, "bundles"),
            Path.Combine(importsRoot, "model-bundles"),
            Path.Combine(importsRoot, "music")
        };
        foreach (string configured in configuredRoots.Concat(internalImportRoots))
        {
            string expanded = configured
                .Replace(BepInExPluginsToken, Paths.PluginPath)
                .Replace(ModDirectoryToken, pluginDirectory);
            expanded = Environment.ExpandEnvironmentVariables(expanded.Trim().Trim('"'));
            try
            {
                string fullPath = Path.GetFullPath(expanded).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (Directory.Exists(fullPath) && !normalized.Contains(fullPath, StringComparer.OrdinalIgnoreCase))
                {
                    normalized.Add(fullPath);
                }
            }
            catch { }
        }

        normalized.Sort((left, right) => left.Length != right.Length
            ? left.Length.CompareTo(right.Length)
            : StringComparer.OrdinalIgnoreCase.Compare(left, right));

        var collapsed = new List<string>();
        foreach (string root in normalized)
        {
            if (scanLocalSubdirectories.Value && collapsed.Any(parent => IsPathInside(root, parent))) continue;
            collapsed.Add(root);
        }
        return collapsed;
    }

    private static IEnumerable<string> SplitDiscoveryRoots(string value)
    {
        return (value ?? string.Empty)
            .Split(new[] { ';', '\r', '\n', '|' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(path => path.Trim())
            .Where(path => path.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private LocalDiscoveryResult LoadCachedLocalDiscoveryResult()
    {
        var result = new LocalDiscoveryResult { Roots = GetLocalDiscoveryRoots().ToArray(), UsedCachedIndexOnly = true };
        Dictionary<string, LocalIndexEntry> cache = LoadLocalAssetIndex();
        foreach (KeyValuePair<string, LocalIndexEntry> pair in cache)
        {
            if (!File.Exists(pair.Key) || !IsFileWithinDiscoveryRoots(pair.Key, result.Roots, scanLocalSubdirectories.Value)) continue;
            if (pair.Value.Kind == LocalAssetKind.Audio) result.AudioFiles.Add(pair.Key);
            else if (pair.Value.Kind == LocalAssetKind.Bundle) result.BundleFiles.Add(pair.Key);
        }
        return result;
    }

    private static bool IsFileWithinDiscoveryRoots(string file, IEnumerable<string> roots, bool recursive)
    {
        string? directory;
        try { directory = Path.GetDirectoryName(Path.GetFullPath(file)); }
        catch { return false; }
        if (string.IsNullOrWhiteSpace(directory)) return false;
        foreach (string root in roots)
        {
            if (recursive)
            {
                if (IsPathInside(file, root)) return true;
            }
            else
            {
                try
                {
                    if (string.Equals(Path.GetFullPath(directory), Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase)) return true;
                }
                catch { }
            }
        }
        return false;
    }

    private void BuildLocalAssetIndex(IEnumerable<string> roots, bool recursive, int maximumFileMegabytes, LocalDiscoveryResult result)
    {
        Dictionary<string, LocalIndexEntry> previous = LoadLocalAssetIndex();
        var updated = new Dictionary<string, LocalIndexEntry>(StringComparer.OrdinalIgnoreCase);
        long maximumBytes = Math.Max(1, maximumFileMegabytes) * 1024L * 1024L;

        foreach (string file in EnumerateLocalFilesSafely(roots, recursive))
        {
            result.FilesEnumerated++;
            string extension = Path.GetExtension(file);
            if (LocalIndexIgnoredExtensions.Contains(extension)) continue;

            FileInfo info;
            try { info = new FileInfo(file); }
            catch { continue; }
            if (info.Length <= 0 || info.Length > maximumBytes) continue;

            string fullPath;
            try { fullPath = Path.GetFullPath(file); }
            catch { continue; }
            string stamp = info.Length.ToString(CultureInfo.InvariantCulture) + ":" +
                info.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture);

            LocalIndexEntry entry;
            if (previous.TryGetValue(fullPath, out LocalIndexEntry cached) && cached.Stamp == stamp)
            {
                entry = cached;
                result.FilesReusedFromCache++;
            }
            else
            {
                LocalAssetKind kind = IsSupportedAudioFile(fullPath)
                    ? LocalAssetKind.Audio
                    : LooksLikeUnityBundle(fullPath) ? LocalAssetKind.Bundle : LocalAssetKind.Unsupported;
                entry = new LocalIndexEntry(stamp, kind);
                result.FilesProbed++;
            }

            updated[fullPath] = entry;
            if (entry.Kind == LocalAssetKind.Audio) result.AudioFiles.Add(fullPath);
            else if (entry.Kind == LocalAssetKind.Bundle) result.BundleFiles.Add(fullPath);
        }

        SaveLocalAssetIndex(updated);
    }

    private void ApplyLocalDiscoveryResult(LocalDiscoveryResult result)
    {
        lastLocalDiscoveryResult = result;
        localDiscoveredFiles = result.AudioFiles
            .Concat(result.BundleFiles)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        indexedBundlePaths.Clear();
        foreach (string path in result.BundleFiles) indexedBundlePaths.Add(path);
        DanceLog.Debug(result.Summary);
    }

    private Dictionary<string, LocalIndexEntry> LoadLocalAssetIndex()
    {
        var entries = new Dictionary<string, LocalIndexEntry>(StringComparer.OrdinalIgnoreCase);
        string path = Path.Combine(importsRoot, LocalAssetIndexFileName);
        if (!File.Exists(path)) return entries;
        try
        {
            foreach (string line in File.ReadAllLines(path))
            {
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#", StringComparison.Ordinal)) continue;
                string[] parts = line.Split('\t');
                if (parts.Length < 3 || !Enum.TryParse(parts[2], out LocalAssetKind kind)) continue;
                string source = Encoding.UTF8.GetString(Convert.FromBase64String(parts[0]));
                entries[source] = new LocalIndexEntry(parts[1], kind);
            }
        }
        catch { }
        return entries;
    }

    private void SaveLocalAssetIndex(Dictionary<string, LocalIndexEntry> entries)
    {
        string path = Path.Combine(importsRoot, LocalAssetIndexFileName);
        string temporary = path + ".tmp";
        using (var writer = new StreamWriter(temporary, false, new UTF8Encoding(false)))
        {
            writer.WriteLine("# base64-path\tstamp\tkind");
            foreach (KeyValuePair<string, LocalIndexEntry> pair in entries.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
            {
                string encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(pair.Key));
                writer.WriteLine(encoded + "\t" + pair.Value.Stamp + "\t" + pair.Value.Kind);
            }
        }
        if (File.Exists(path)) File.Delete(path);
        File.Move(temporary, path);
    }

    private static IEnumerable<string> EnumerateLocalFilesSafely(IEnumerable<string> roots, bool recursive)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string root in roots)
        {
            var pending = new Stack<string>();
            pending.Push(root);
            while (pending.Count > 0)
            {
                string directory = pending.Pop();
                string normalized;
                try { normalized = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar); }
                catch { continue; }
                if (!visited.Add(normalized)) continue;

                try
                {
                    var directoryInfo = new DirectoryInfo(directory);
                    if ((directoryInfo.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                }
                catch { continue; }

                string[] files = Array.Empty<string>();
                try { files = Directory.GetFiles(directory); } catch { }
                foreach (string file in files) yield return file;
                if (!recursive) continue;

                string[] children = Array.Empty<string>();
                try { children = Directory.GetDirectories(directory); } catch { }
                foreach (string child in children) pending.Push(child);
            }
        }
    }

    private IEnumerable<string> GetIndexedAudioFiles()
    {
        return localDiscoveredFiles.Where(IsSupportedAudioFile);
    }

    private void BuildModelCatalog()
    {
        modelCatalog.Clear();
        modelCatalogDirty = true;
        foreach (string candidate in EnumerateBundleCandidates(pluginDirectory))
        {
            string fullPath;
            try { fullPath = Path.GetFullPath(candidate); } catch { continue; }
            if (!File.Exists(fullPath) || !IsModelPackBundle(fullPath) || !IsIndexedOrUnityBundle(fullPath)) continue;
            string name = InferModelCatalogName(fullPath);
            if (!modelCatalog.ContainsKey(name)) modelCatalog[name] = new ModelCatalogEntry(name, fullPath);
        }
    }

    private bool IsIndexedOrUnityBundle(string path)
    {
        string fullPath;
        try { fullPath = Path.GetFullPath(path); }
        catch { return false; }
        return indexedBundlePaths.Contains(fullPath) || LooksLikeUnityBundle(fullPath);
    }

    private IReadOnlyList<string> GetAvailableModelNames()
    {
        if (!modelCatalogDirty) return cachedModelNames;
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (ModelCatalogEntry entry in modelCatalog.Values) names.Add(entry.ResolvedName ?? entry.DisplayName);
        foreach (string name in SourceRigRegistry.GetVisibleSelectionNames()) names.Add(name);
        cachedModelNames = names.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray();
        modelCatalogDirty = false;
        return cachedModelNames;
    }

    private string? EnsureModelAvailable(string requestedName)
    {
        if (string.IsNullOrWhiteSpace(requestedName)) return null;
        if (SourceRigRegistry.ContainsVisibleSelectionName(requestedName)) return requestedName;

        string normalized = NormalizeName(requestedName);
        ModelCatalogEntry? entry = modelCatalog.Values.FirstOrDefault(item =>
            NormalizeName(item.ResolvedName ?? item.DisplayName) == normalized ||
            NormalizeName(Path.GetFileNameWithoutExtension(item.Path)).Contains(normalized) ||
            normalized.Contains(NormalizeName(item.DisplayName)));
        if (entry == null) return null;
        if (entry.ResolvedName != null && SourceRigRegistry.ContainsVisibleSelectionName(entry.ResolvedName)) return entry.ResolvedName;

        try
        {
            string fullPath = Path.GetFullPath(entry.Path);
            if (!loadedBundlePaths.Contains(fullPath))
            {
                AssetBundle bundle = AssetBundle.LoadFromFile(fullPath);
                if (bundle == null) return null;
                loadedBundlePaths.Add(fullPath);
                var before = new HashSet<string>(SourceRigRegistry.GetVisibleSelectionNames(), StringComparer.OrdinalIgnoreCase);
                GameObject[] prefabs = LoadAssetsSafely<GameObject>(bundle, fullPath, "GameObject/model");
                SourceRigRegistry.RegisterAdditionalModelPrefabs(prefabs.Select(model =>
                    new SourceRigRegistration(model, Path.GetFileName(fullPath), true)));
                bundle.Unload(false);
                entry.NewlyRegisteredName = SourceRigRegistry.GetVisibleSelectionNames().FirstOrDefault(name => !before.Contains(name));
            }

            string? resolved = SourceRigRegistry.GetVisibleSelectionNames()
                .FirstOrDefault(name => NormalizeName(name) == normalized)
                ?? entry.NewlyRegisteredName;
            if (!string.IsNullOrWhiteSpace(resolved))
            {
                entry.ResolvedName = resolved;
                modelCatalogDirty = true;
                return resolved;
            }
        }
        catch (Exception exception)
        {
            DanceLog.Warning($"Could not lazy-load model '{requestedName}': {exception.Message}");
        }
        return null;
    }

    private static string InferModelCatalogName(string path)
    {
        string stem = Path.GetFileNameWithoutExtension(path);
        int xiehen = stem.IndexOf("xiehen", StringComparison.OrdinalIgnoreCase);
        if (xiehen >= 0)
        {
            int end = xiehen + 6;
            while (end < stem.Length && char.IsDigit(stem[end])) end++;
            if (end > xiehen + 6) return stem.Substring(xiehen, end - xiehen).ToLowerInvariant();
        }
        return stem.Replace("_embedded_", " #").Replace('_', ' ').Trim();
    }

    private void ReleaseNonAudioBundleFiles()
    {
        var retained = new List<AssetBundle>();
        foreach (AssetBundle bundle in loadedBundles.ToArray())
        {
            if (bundle != null && audioBackedBundles.Contains(bundle))
            {
                retained.Add(bundle);
                continue;
            }

            try { if (bundle != null) bundle.Unload(false); } catch { }
        }

        loadedBundles.Clear();
        loadedBundles.AddRange(retained);
    }

    private void ApplySettingsLive()
    {
        ApplyRuntimeOptions();
        foreach (Emote emote in EmoteRegistry.GetEmotes().Values)
        {
            emote.ConfigureAudio(emote.AudioClip, enableMusic.Value && emote.AudioClip != null, emote.LoopAudio,
                musicVolume.Value, musicSpatialBlend.Value, musicMinDistance.Value, musicMaxDistance.Value);
        }
        Config.Save();
    }

    private enum LocalAssetKind
    {
        Unsupported,
        Bundle,
        Audio
    }

    private readonly struct LocalIndexEntry
    {
        public LocalIndexEntry(string stamp, LocalAssetKind kind) { Stamp = stamp; Kind = kind; }
        public string Stamp { get; }
        public LocalAssetKind Kind { get; }
    }

    private sealed class LocalDiscoveryResult
    {
        public string[] Roots { get; set; } = Array.Empty<string>();
        public List<string> BundleFiles { get; } = new();
        public List<string> AudioFiles { get; } = new();
        public ExternalImportReport? EmbeddedBundleReport { get; set; }
        public int FilesEnumerated { get; set; }
        public int FilesProbed { get; set; }
        public int FilesReusedFromCache { get; set; }
        public bool UsedCachedIndexOnly { get; set; }
        public string Error { get; set; } = string.Empty;
        public bool Success => string.IsNullOrEmpty(Error);
        public string Summary => Success
            ? $"Local discovery: {BundleFiles.Count} bundle(s), {AudioFiles.Count} audio file(s), " +
              $"{FilesProbed} changed candidate(s) probed, {FilesReusedFromCache} unchanged candidate(s) reused" +
              (UsedCachedIndexOnly ? " (cached index only)." : ".")
            : "Local discovery failed: " + Error;
    }

    private sealed class ModelCatalogEntry
    {
        public ModelCatalogEntry(string displayName, string path) { DisplayName = displayName; Path = path; }
        public string DisplayName { get; }
        public string Path { get; }
        public string? ResolvedName { get; set; }
        public string? NewlyRegisteredName { get; set; }
    }
}
