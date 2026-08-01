using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Dline.DanceBox;

internal sealed class ExternalImportOptions
{
    public string SourceRoot { get; set; } = string.Empty;
    public IReadOnlyList<string> SourceRoots { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> ExcludedRoots { get; set; } = Array.Empty<string>();
    public string DestinationRoot { get; set; } = string.Empty;
    public string PackageFilter { get; set; } = string.Empty;
    public bool ExtractEmbeddedBundles { get; set; } = true;
    public bool Recursive { get; set; } = true;
    public bool CopyBundles { get; set; } = true;
    public bool CopyAudio { get; set; } = true;
    public int MaximumFileMegabytes { get; set; } = 512;
}

internal sealed class ExternalImportReport
{
    public int FilesScanned { get; set; }
    public int BundlesCopied { get; set; }
    public int EmbeddedBundlesExtracted { get; set; }
    public int AudioFilesCopied { get; set; }
    public int FilesSkippedFromCache { get; set; }
    public int RejectedFiles { get; set; }
    public string Error { get; set; } = string.Empty;
    public List<string> Messages { get; } = new();

    public bool Success => string.IsNullOrEmpty(Error);

    public string Summary => Success
        ? $"Scanned {FilesScanned} file(s), imported {BundlesCopied + EmbeddedBundlesExtracted} bundle(s) and {AudioFilesCopied} audio file(s); {FilesSkippedFromCache} unchanged file(s) skipped. Restart PEAK to load newly imported assets."
        : Error;
}

/// <summary>
/// Imports assets without loading or executing foreign Lethal Company DLLs.
/// Raw Unity bundles are copied; UnityFS payloads embedded in assemblies are
/// extracted by parsing the UnityFS header's declared file size.
/// </summary>
internal static class ExternalResourceImporter
{
    private const string ManifestFileName = "import-cache.tsv";
    private static readonly byte[] UnityFsSignature = Encoding.ASCII.GetBytes("UnityFS\0");
    private static readonly string[] AudioExtensions = { ".ogg", ".wav", ".mp3" };
    private static readonly HashSet<string> IgnoredExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdb", ".xml", ".json", ".cfg", ".ini", ".md", ".txt", ".log",
        ".png", ".jpg", ".jpeg", ".webp", ".gif", ".bmp", ".manifest",
        ".cs", ".csproj", ".sln", ".ps1", ".cmd", ".bat", ".sha256"
    };

    public static ExternalImportReport Import(ExternalImportOptions options)
    {
        var report = new ExternalImportReport();
        try
        {
            string destinationRoot = Path.GetFullPath(options.DestinationRoot);
            string[] sourceRoots = ResolveSourceRoots(options);
            if (sourceRoots.Length == 0)
            {
                report.Error = "No valid import source directory was found.";
                return report;
            }

            string bundleDirectory = Path.Combine(destinationRoot, "bundles");
            string modelDirectory = Path.Combine(destinationRoot, "model-bundles");
            string musicDirectory = Path.Combine(destinationRoot, "music");
            Directory.CreateDirectory(bundleDirectory);
            Directory.CreateDirectory(modelDirectory);
            Directory.CreateDirectory(musicDirectory);

            string manifestPath = Path.Combine(destinationRoot, ManifestFileName);
            Dictionary<string, CacheEntry> cache = LoadManifest(manifestPath);
            var updatedCache = new Dictionary<string, CacheEntry>(cache, StringComparer.OrdinalIgnoreCase);
            string[] filterTokens = SplitFilter(options.PackageFilter);
            string[] roots = sourceRoots.SelectMany(root => ResolveScanRoots(root, filterTokens))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            string[] excludedRoots = options.ExcludedRoots.Append(destinationRoot)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(NormalizeDirectoryPath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            long maximumBytes = Math.Max(1, options.MaximumFileMegabytes) * 1024L * 1024L;

            foreach (string file in EnumerateFilesSafely(roots, excludedRoots, options.Recursive))
            {
                if (!ShouldInspectFile(file, options)) continue;
                report.FilesScanned++;
                FileInfo info;
                try { info = new FileInfo(file); }
                catch { report.RejectedFiles++; continue; }

                if (info.Length <= 0 || info.Length > maximumBytes)
                {
                    report.RejectedFiles++;
                    continue;
                }

                string cacheKey = NormalizePath(file);
                string stamp = info.Length.ToString(CultureInfo.InvariantCulture) + ":" + info.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture);
                if (cache.TryGetValue(cacheKey, out CacheEntry previous) && previous.Stamp == stamp && previous.Outputs.All(File.Exists))
                {
                    updatedCache[cacheKey] = previous;
                    report.FilesSkippedFromCache++;
                    continue;
                }

                var outputs = new List<string>();
                string extension = info.Extension.ToLowerInvariant();
                if (options.CopyAudio && AudioExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
                {
                    string output = CopyUnique(file, musicDirectory, BuildStableStem(file));
                    outputs.Add(output);
                    report.AudioFilesCopied++;
                }
                else if (options.CopyBundles && LooksLikeUnityBundle(file))
                {
                    string targetDirectory = IsLikelyModelSource(file) ? modelDirectory : bundleDirectory;
                    string output = CopyUnique(file, targetDirectory, BuildStableStem(file));
                    outputs.Add(output);
                    report.BundlesCopied++;
                }
                else if (options.ExtractEmbeddedBundles && extension == ".dll")
                {
                    foreach (ExtractedBundle extracted in ExtractEmbeddedUnityFs(file, maximumBytes))
                    {
                        string targetDirectory = IsLikelyModelSource(file) ? modelDirectory : bundleDirectory;
                        string stem = BuildStableStem(file) + "_embedded_" + extracted.Index.ToString("00", CultureInfo.InvariantCulture);
                        string output = WriteUnique(extracted.Data, targetDirectory, stem + ".bundle");
                        outputs.Add(output);
                        report.EmbeddedBundlesExtracted++;
                    }
                }

                // Cache inspected files even when they contain no supported assets.
                // This is important when scanning an entire BepInEx/plugins tree:
                // ordinary plugin DLLs must not be read again on every startup.
                updatedCache[cacheKey] = new CacheEntry(stamp, outputs);
            }

            SaveManifest(manifestPath, updatedCache);
            report.Messages.Add("Foreign assemblies were never loaded or executed.");
            report.Messages.Add("Imported assets are isolated under the plugin's imports directory.");
            report.Messages.Add(options.Recursive
                ? "Directory traversal included subdirectories; unchanged files were skipped by the import cache."
                : "Only the selected directory level was scanned; unchanged files were skipped by the import cache.");
        }
        catch (Exception exception)
        {
            report.Error = "Import failed: " + exception.Message;
        }
        return report;
    }

    private static string[] ResolveSourceRoots(ExternalImportOptions options)
    {
        IEnumerable<string> configured = options.SourceRoots.Count > 0
            ? options.SourceRoots
            : new[] { options.SourceRoot };
        return configured
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => Environment.ExpandEnvironmentVariables(value.Trim().Trim('"')))
            .Select(TryGetFullPath)
            .Where(value => value != null && Directory.Exists(value))
            .Select(value => value!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string? TryGetFullPath(string path)
    {
        try { return Path.GetFullPath(path); }
        catch { return null; }
    }

    private static IEnumerable<string> ResolveScanRoots(string sourceRoot, string[] filterTokens)
    {
        if (filterTokens.Length == 0 || !string.Equals(Path.GetFileName(sourceRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)), "plugins", StringComparison.OrdinalIgnoreCase))
        {
            return new[] { sourceRoot };
        }

        var roots = new List<string>();
        try
        {
            foreach (string directory in Directory.EnumerateDirectories(sourceRoot, "*", SearchOption.TopDirectoryOnly))
            {
                string name = Path.GetFileName(directory);
                if (filterTokens.Any(token => name.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    roots.Add(directory);
                }
            }
        }
        catch { }
        return roots;
    }

    private static IEnumerable<string> EnumerateFilesSafely(IEnumerable<string> roots, IReadOnlyList<string> excludedRoots, bool recursive)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var pending = new Stack<string>();
            pending.Push(root);
            while (pending.Count > 0)
            {
                string directory = pending.Pop();
                string normalizedDirectory = NormalizeDirectoryPath(directory);
                if (!visited.Add(normalizedDirectory) || IsInsideExcludedRoot(normalizedDirectory, excludedRoots)) continue;

                try
                {
                    var directoryInfo = new DirectoryInfo(directory);
                    if ((directoryInfo.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                }
                catch { continue; }

                string[] files = Array.Empty<string>();
                string[] children = Array.Empty<string>();
                try { files = Directory.GetFiles(directory); } catch { }
                try { children = Directory.GetDirectories(directory); } catch { }
                foreach (string file in files) yield return file;
                if (recursive)
                {
                    foreach (string child in children) pending.Push(child);
                }
            }
        }
    }

    private static bool ShouldInspectFile(string path, ExternalImportOptions options)
    {
        string extension = Path.GetExtension(path).ToLowerInvariant();
        if (options.ExtractEmbeddedBundles && extension == ".dll") return true;
        if (options.CopyAudio && AudioExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase)) return true;
        if (!options.CopyBundles) return false;

        // AssetBundles often have no extension, so unknown extensions still need
        // an eight-byte header probe. Skip common non-bundle files to keep a full
        // plugins-directory scan cheap.
        return !IgnoredExtensions.Contains(extension);
    }

    private static string NormalizeDirectoryPath(string path)
    {
        string normalized = NormalizePath(path).TrimEnd('/');
        return normalized + "/";
    }

    private static bool IsInsideExcludedRoot(string normalizedDirectory, IReadOnlyList<string> excludedRoots)
    {
        foreach (string excluded in excludedRoots)
        {
            if (normalizedDirectory.StartsWith(excluded, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static IEnumerable<ExtractedBundle> ExtractEmbeddedUnityFs(string path, long maximumBytes)
    {
        byte[] data;
        try { data = File.ReadAllBytes(path); }
        catch { yield break; }

        int search = 0;
        int index = 0;
        while (search <= data.Length - UnityFsSignature.Length)
        {
            int offset = IndexOf(data, UnityFsSignature, search);
            if (offset < 0) yield break;
            search = offset + UnityFsSignature.Length;
            if (!TryReadUnityFsLength(data, offset, out long length) || length <= 0 || length > maximumBytes || offset + length > data.LongLength)
            {
                continue;
            }

            var payload = new byte[checked((int)length)];
            Buffer.BlockCopy(data, offset, payload, 0, checked((int)length));
            yield return new ExtractedBundle(index++, payload);
            search = offset + checked((int)length);
        }
    }

    private static bool TryReadUnityFsLength(byte[] data, int offset, out long length)
    {
        length = 0;
        int position = offset + UnityFsSignature.Length;
        if (position + 4 >= data.Length) return false;
        position += 4; // format version, big endian
        if (!SkipNullTerminated(data, ref position) || !SkipNullTerminated(data, ref position) || position + 8 > data.Length) return false;
        length = ReadInt64BigEndian(data, position);
        return length >= 64;
    }

    private static bool SkipNullTerminated(byte[] data, ref int position)
    {
        int limit = Math.Min(data.Length, position + 256);
        while (position < limit)
        {
            if (data[position++] == 0) return true;
        }
        return false;
    }

    private static long ReadInt64BigEndian(byte[] data, int offset)
    {
        ulong value = 0;
        for (int i = 0; i < 8; i++) value = (value << 8) | data[offset + i];
        return value > long.MaxValue ? 0 : (long)value;
    }

    private static int IndexOf(byte[] haystack, byte[] needle, int start)
    {
        for (int i = Math.Max(0, start); i <= haystack.Length - needle.Length; i++)
        {
            int j = 0;
            while (j < needle.Length && haystack[i + j] == needle[j]) j++;
            if (j == needle.Length) return i;
        }
        return -1;
    }

    private static bool LooksLikeUnityBundle(string path)
    {
        try
        {
            using FileStream stream = File.OpenRead(path);
            byte[] header = new byte[8];
            int read = stream.Read(header, 0, header.Length);
            string magic = Encoding.ASCII.GetString(header, 0, read);
            return magic.StartsWith("UnityFS", StringComparison.Ordinal) || magic.StartsWith("UnityRaw", StringComparison.Ordinal) || magic.StartsWith("UnityWeb", StringComparison.Ordinal);
        }
        catch { return false; }
    }

    private static bool IsLikelyModelSource(string path)
    {
        string value = path.Replace('\\', '/').ToLowerInvariant();
        string[] tokens = { "customize", "modelreplacement", "model-replacement", "moresuits", "more_suits", "/suits", "skin", "avatar", "character" };
        return tokens.Any(value.Contains);
    }

    private static string CopyUnique(string source, string directory, string stem)
    {
        string extension = Path.GetExtension(source);
        string target = Path.Combine(directory, SanitizeFileName(stem) + extension.ToLowerInvariant());
        File.Copy(source, target, true);
        return target;
    }

    private static string WriteUnique(byte[] bytes, string directory, string fileName)
    {
        string target = Path.Combine(directory, SanitizeFileName(fileName));
        File.WriteAllBytes(target, bytes);
        return target;
    }

    private static string GetUniquePath(string directory, string fileName)
    {
        string path = Path.Combine(directory, fileName);
        if (!File.Exists(path)) return path;
        string stem = Path.GetFileNameWithoutExtension(fileName);
        string extension = Path.GetExtension(fileName);
        for (int index = 2; index < 10000; index++)
        {
            path = Path.Combine(directory, stem + "_" + index.ToString(CultureInfo.InvariantCulture) + extension);
            if (!File.Exists(path)) return path;
        }
        return Path.Combine(directory, stem + "_" + Guid.NewGuid().ToString("N") + extension);
    }

    private static string BuildStableStem(string path)
    {
        string package = Path.GetFileName(Path.GetDirectoryName(path) ?? string.Empty);
        string file = DecodeUnicodeEscapes(Path.GetFileNameWithoutExtension(path));
        string hash;
        using (SHA1 sha = SHA1.Create())
        {
            byte[] digest = sha.ComputeHash(Encoding.UTF8.GetBytes(NormalizePath(path)));
            hash = BitConverter.ToString(digest, 0, 4).Replace("-", string.Empty).ToLowerInvariant();
        }
        return SanitizeFileName(package + "_" + file + "_" + hash);
    }

    private static string DecodeUnicodeEscapes(string value)
    {
        var builder = new StringBuilder(value.Length);
        for (int i = 0; i < value.Length; i++)
        {
            if (i + 5 < value.Length && value[i] == '#' && (value[i + 1] == 'U' || value[i + 1] == 'u') &&
                ushort.TryParse(value.Substring(i + 2, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ushort code))
            {
                builder.Append((char)code);
                i += 5;
            }
            else builder.Append(value[i]);
        }
        return builder.ToString();
    }

    private static string SanitizeFileName(string value)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(value.Length);
        foreach (char character in value)
        {
            builder.Append(invalid.Contains(character) || char.IsControl(character) ? '_' : character);
        }
        string result = builder.ToString().Trim().Trim('.');
        if (string.IsNullOrWhiteSpace(result)) result = "imported_asset";
        if (result.Length > 120)
        {
            string tail = result.Substring(result.Length - 16);
            result = result.Substring(0, 100) + "_" + tail;
        }
        return result;
    }

    private static string[] SplitFilter(string value) => (value ?? string.Empty)
        .Split(new[] { ';', ',', '|' }, StringSplitOptions.RemoveEmptyEntries)
        .Select(token => token.Trim())
        .Where(token => token.Length > 0)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    private static string NormalizePath(string path)
    {
        try { return Path.GetFullPath(path).Replace('\\', '/').ToLowerInvariant(); }
        catch { return path.Replace('\\', '/').ToLowerInvariant(); }
    }

    private static Dictionary<string, CacheEntry> LoadManifest(string path)
    {
        var entries = new Dictionary<string, CacheEntry>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(path)) return entries;
        foreach (string line in File.ReadAllLines(path))
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#", StringComparison.Ordinal)) continue;
            string[] parts = line.Split('\t');
            if (parts.Length < 3) continue;
            string[] outputs = parts[2].Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries);
            entries[parts[0]] = new CacheEntry(parts[1], outputs);
        }
        return entries;
    }

    private static void SaveManifest(string path, Dictionary<string, CacheEntry> entries)
    {
        string temporary = path + ".tmp";
        using (var writer = new StreamWriter(temporary, false, new UTF8Encoding(false)))
        {
            writer.WriteLine("# source\tstamp\toutputs");
            foreach (KeyValuePair<string, CacheEntry> pair in entries.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
            {
                writer.WriteLine(pair.Key + "\t" + pair.Value.Stamp + "\t" + string.Join("|", pair.Value.Outputs));
            }
        }
        if (File.Exists(path)) File.Delete(path);
        File.Move(temporary, path);
    }

    private sealed class CacheEntry
    {
        public CacheEntry(string stamp, IEnumerable<string> outputs)
        {
            Stamp = stamp;
            Outputs = outputs.ToArray();
        }
        public string Stamp { get; }
        public string[] Outputs { get; }
    }

    private readonly struct ExtractedBundle
    {
        public ExtractedBundle(int index, byte[] data) { Index = index; Data = data; }
        public int Index { get; }
        public byte[] Data { get; }
    }
}
