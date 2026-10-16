using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;

namespace CS2_Admin.Utils;

public static class AutoUpdater
{
    private const string RepoOwner = "agasking1337";
    private const string RepoName = "CS2_Admin";

    // Kullanıcı tarafından düzenlenmiş dosyalar — asla üzerine yazılmaz.
    // Bu dosyalar zaten configs/plugins/CS2_Admin/ altında ayrı tutuluyor,
    // ama release zip'inde yanlışlıkla bulunurlarsa diye defensif filtre uygulanır.
    private static readonly HashSet<string> ProtectedFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "config.json",
        "permissions.json",
        "commands.json",
        "maps.json",
        "discord.json",
        "afk.json",
        "chat_tags.json",
        "tags.json",
        "commandblocker.json"
    };

    private static readonly HashSet<string> SkippedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        "runtimes"
    };

    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromMinutes(1)
    };

    static AutoUpdater()
    {
        HttpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("CS2_Admin_Updater", "1.0"));
        HttpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public static async Task CheckForUpdatesAsync(ISwiftlyCore core, string currentVersion)
    {
        try
        {
            core.Logger.LogInformationIfEnabled("Checking for updates on GitHub...");

            var url = $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest";
            using var response = await HttpClient.GetAsync(url);

            if (!response.IsSuccessStatusCode)
            {
                core.Logger.LogWarningIfEnabled("Failed to check for updates. GitHub API responded with: {Status}", response.StatusCode);
                return;
            }

            var json = await response.Content.ReadAsStringAsync();
            var release = JsonSerializer.Deserialize<GithubRelease>(json);

            if (release == null || string.IsNullOrEmpty(release.TagName))
            {
                core.Logger.LogWarningIfEnabled("Failed to parse GitHub release data (tag_name missing).");
                return;
            }

            var latestVer = release.TagName.TrimStart('v', 'V');
            var currVer = (currentVersion ?? string.Empty).TrimStart('v', 'V');

            var cmp = CompareVersions(latestVer, currVer);
            if (cmp <= 0)
            {
                core.Logger.LogInformationIfEnabled("You are running the latest version: {Version}", currVer);
                return;
            }

            core.Logger.LogInformationIfEnabled("New version available! {OldVersion} -> {NewVersion}. Downloading...", currVer, latestVer);

            var asset = release.Assets?.FirstOrDefault(a => a.BrowserDownloadUrl.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
            if (asset == null || string.IsNullOrEmpty(asset.BrowserDownloadUrl))
            {
                core.Logger.LogWarningIfEnabled("Could not find a .zip asset in the latest release.");
                return;
            }

            await ApplyUpdateAsync(core, asset.BrowserDownloadUrl);
        }
        catch (Exception ex)
        {
            core.Logger.LogErrorIfEnabled(ex, "Auto-Updater encountered an error");
        }
    }

    private static async Task ApplyUpdateAsync(ISwiftlyCore core, string downloadUrl)
    {
        var pluginPath = core.PluginPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var parentDir = Directory.GetParent(pluginPath)?.FullName ?? pluginPath;
        var tempZipPath = Path.Combine(parentDir, "cs2admin_update_temp.zip");
        var tempExtractPath = Path.Combine(parentDir, "cs2admin_update_extracted");

        try
        {
            // 1) Stream-based download (büyük dosyaları belleğe yüklemeden diske yaz)
            using (var zipResponse = await HttpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead))
            {
                if (!zipResponse.IsSuccessStatusCode)
                {
                    core.Logger.LogWarningIfEnabled("Failed to download update. HTTP status: {Status}", zipResponse.StatusCode);
                    return;
                }

                await using var src = await zipResponse.Content.ReadAsStreamAsync();
                await using var dst = File.Create(tempZipPath);
                await src.CopyToAsync(dst);
            }

            // 2) Temiz zip çıkar
            if (Directory.Exists(tempExtractPath))
            {
                Directory.Delete(tempExtractPath, true);
            }
            ZipFile.ExtractToDirectory(tempZipPath, tempExtractPath, overwriteFiles: true);

            // 3) Zip içindeki CS2_Admin.dll dosyasını bul
            var dllPath = Directory.GetFiles(tempExtractPath, "CS2_Admin.dll", SearchOption.AllDirectories).FirstOrDefault();
            if (dllPath == null)
            {
                core.Logger.LogWarningIfEnabled("Downloaded update is invalid (CS2_Admin.dll not found in archive).");
                return;
            }

            var sourcePluginDir = Path.GetDirectoryName(dllPath);
            if (string.IsNullOrEmpty(sourcePluginDir)) return;

            // 4) Dosyaları plugin dizinine kopyala (kullanıcı dosyaları korunur)
            CopyDirectorySafe(sourcePluginDir, pluginPath);

            core.Logger.LogInformationIfEnabled("Update applied successfully! Swiftly will reload the plugin automatically.");
        }
        catch (Exception ex)
        {
            core.Logger.LogErrorIfEnabled("Failed to apply update: {Message}", ex.Message);
        }
        finally
        {
            try
            {
                if (File.Exists(tempZipPath)) File.Delete(tempZipPath);
                if (Directory.Exists(tempExtractPath)) Directory.Delete(tempExtractPath, true);
            }
            catch
            {
                // Cleanup hatası kritik değil
            }
        }
    }

    private static void CopyDirectorySafe(string sourceDir, string targetDir)
    {
        Directory.CreateDirectory(targetDir);

        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var fileName = Path.GetFileName(file);
            if (ProtectedFiles.Contains(fileName) ||
                ((fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ||
                  fileName.EndsWith(".jsonc", StringComparison.OrdinalIgnoreCase)) &&
                 File.Exists(Path.Combine(targetDir, fileName))))
            {
                // Kullanıcı dosyası — atla. EnsureConfig zaten migrate eder.
                continue;
            }
            var destFile = Path.Combine(targetDir, fileName);
            File.Copy(file, destFile, overwrite: true);
        }

        foreach (var dir in Directory.GetDirectories(sourceDir))
        {
            var dirName = Path.GetFileName(dir);
            if (SkippedDirectories.Contains(dirName))
                continue;
            CopyDirectorySafe(dir, Path.Combine(targetDir, dirName));
        }
    }

    // Semver uyumlu versiyon karşılaştırması.
    // "1.0.0-beta" vs "1.0.0-alpha" gibi durumları doğru handle eder.
    // Pozitif: a > b, Negatif: a < b, Sıfır: eşit.
    private static int CompareVersions(string a, string b)
    {
        if (string.IsNullOrWhiteSpace(a) && string.IsNullOrWhiteSpace(b)) return 0;
        if (string.IsNullOrWhiteSpace(a)) return -1;
        if (string.IsNullOrWhiteSpace(b)) return 1;

        var aParts = a.Split('-', 2);
        var bParts = b.Split('-', 2);

        if (!Version.TryParse(aParts[0], out var va)) va = new Version(0, 0, 0);
        if (!Version.TryParse(bParts[0], out var vb)) vb = new Version(0, 0, 0);

        var coreCmp = va.CompareTo(vb);
        if (coreCmp != 0) return coreCmp;

        // Core sürümler eşit — prerelease etiketlerine bak
        // Kural: prerelease < release
        var aHasPre = aParts.Length == 2;
        var bHasPre = bParts.Length == 2;

        if (!aHasPre && !bHasPre) return 0;
        if (!aHasPre) return 1;  // a release, b prerelease → a > b
        if (!bHasPre) return -1; // a prerelease, b release → a < b

        return StringComparer.OrdinalIgnoreCase.Compare(aParts[1], bParts[1]);
    }

    private class GithubRelease
    {
        [JsonPropertyName("tag_name")]
        public string TagName { get; set; } = string.Empty;

        [JsonPropertyName("assets")]
        public GithubAsset[] Assets { get; set; } = Array.Empty<GithubAsset>();
    }

    private class GithubAsset
    {
        [JsonPropertyName("browser_download_url")]
        public string BrowserDownloadUrl { get; set; } = string.Empty;
    }
}
