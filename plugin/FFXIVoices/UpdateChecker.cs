using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;

namespace FFXIVoices;

/// <summary>Checks for plugin updates from the CommsLink server and stages downloads.</summary>
public sealed class UpdateChecker : IDisposable
{
    private readonly Configuration config;
    private readonly IDalamudPluginInterface pluginInterface;
    private readonly IPluginLog log;
    private readonly IChatGui chatGui;

    private string PendingDir => Path.Combine(
        pluginInterface.GetPluginConfigDirectory(), "pending-update");

    private string InstallDir => Path.GetDirectoryName(
        pluginInterface.AssemblyLocation.FullName)!;

    /// <summary>True if an update has been downloaded and is waiting for reload.</summary>
    public bool UpdatePending { get; private set; }

    /// <summary>The version string of the available/staged update.</summary>
    public string? NewVersion { get; private set; }

    /// <summary>Changelog for the available update.</summary>
    public string? Changelog { get; private set; }

    public UpdateChecker(
        Configuration config,
        IDalamudPluginInterface pluginInterface,
        IPluginLog log,
        IChatGui chatGui)
    {
        this.config = config;
        this.pluginInterface = pluginInterface;
        this.log = log;
        this.chatGui = chatGui;
    }

    /// <summary>
    /// Apply a previously staged update by copying files from pending-update/ into the install directory.
    /// Call this early in Plugin constructor before other initialization.
    /// </summary>
    public void ApplyStagedUpdate()
    {
        try
        {
            if (!Directory.Exists(PendingDir))
                return;

            var files = Directory.GetFiles(PendingDir);
            if (files.Length == 0)
            {
                Directory.Delete(PendingDir, true);
                return;
            }

            log.Information("[UpdateChecker] Applying staged update from {0}", PendingDir);
            var applied = 0;

            foreach (var srcFile in files)
            {
                var fileName = Path.GetFileName(srcFile);
                var destFile = Path.Combine(InstallDir, fileName);

                try
                {
                    // Try to rename the old file out of the way first
                    var oldFile = destFile + ".old";
                    if (File.Exists(destFile))
                    {
                        try { File.Delete(oldFile); } catch { }
                        try { File.Move(destFile, oldFile); } catch { }
                    }

                    File.Copy(srcFile, destFile, true);
                    applied++;
                }
                catch (Exception ex)
                {
                    log.Warning("[UpdateChecker] Could not update {0}: {1}", fileName, ex.Message);
                }
            }

            // Clean up pending dir and old files
            try { Directory.Delete(PendingDir, true); } catch { }
            foreach (var old in Directory.GetFiles(InstallDir, "*.old"))
            {
                try { File.Delete(old); } catch { }
            }

            if (applied > 0)
            {
                log.Information("[UpdateChecker] Applied {0} file(s). Reload plugin to use new version.", applied);
                chatGui.Print("[CommsLink Voices] Update applied! Reload the plugin for the new version.");
            }
        }
        catch (Exception ex)
        {
            log.Error("[UpdateChecker] Error applying staged update: {0}", ex.Message);
        }
    }

    /// <summary>Check the server for a newer version, download and stage if available.</summary>
    public async Task CheckForUpdateAsync()
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

            // Fetch version metadata
            var response = await client.GetAsync($"{config.ServerUrl}/update/version");
            if (!response.IsSuccessStatusCode)
                return;

            var body = await response.Content.ReadAsStringAsync();
            var meta = JsonDocument.Parse(body).RootElement;

            if (!meta.TryGetProperty("version", out var versionEl))
                return;

            var remoteVersionStr = versionEl.GetString();
            if (string.IsNullOrEmpty(remoteVersionStr))
                return;

            // Compare versions
            var currentVersion = Assembly.GetExecutingAssembly().GetName().Version;
            if (!System.Version.TryParse(remoteVersionStr, out var remoteVersion))
                return;

            // Pad to 4-part for comparison (server may send "0.2.0" → "0.2.0.0")
            if (remoteVersion.Revision < 0)
                remoteVersion = new System.Version(remoteVersion.Major, remoteVersion.Minor, remoteVersion.Build, 0);

            if (remoteVersion <= currentVersion)
            {
                log.Information("[UpdateChecker] Up to date (current={0}, remote={1})", currentVersion, remoteVersion);
                return;
            }

            log.Information("[UpdateChecker] Update available: {0} → {1}", currentVersion, remoteVersion);

            // Get expected hash
            var expectedHash = meta.TryGetProperty("sha256", out var hashEl) ? hashEl.GetString() : null;
            var changelog = meta.TryGetProperty("changelog", out var clEl) ? clEl.GetString() : null;

            // Download the zip
            var zipResponse = await client.GetAsync($"{config.ServerUrl}/update/download");
            if (!zipResponse.IsSuccessStatusCode)
            {
                log.Warning("[UpdateChecker] Failed to download update: {0}", zipResponse.StatusCode);
                return;
            }

            var zipBytes = await zipResponse.Content.ReadAsByteArrayAsync();

            // Verify hash
            if (!string.IsNullOrEmpty(expectedHash))
            {
                var actualHash = Convert.ToHexString(SHA256.HashData(zipBytes)).ToLowerInvariant();
                if (actualHash != expectedHash)
                {
                    log.Error("[UpdateChecker] Hash mismatch! Expected={0} Got={1}", expectedHash, actualHash);
                    return;
                }
            }

            // Extract to pending directory
            if (Directory.Exists(PendingDir))
                Directory.Delete(PendingDir, true);
            Directory.CreateDirectory(PendingDir);

            using var zipStream = new MemoryStream(zipBytes);
            using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);
            foreach (var entry in archive.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name))
                    continue; // skip directories
                var destPath = Path.Combine(PendingDir, entry.Name);
                entry.ExtractToFile(destPath, true);
            }

            NewVersion = remoteVersionStr;
            Changelog = changelog;
            UpdatePending = true;

            log.Information("[UpdateChecker] Update v{0} downloaded and staged ({1} bytes)", remoteVersionStr, zipBytes.Length);
            chatGui.Print($"[CommsLink Voices] Update v{remoteVersionStr} downloaded! Reload the plugin to apply.");
        }
        catch (Exception ex)
        {
            log.Warning("[UpdateChecker] Update check failed: {0}", ex.Message);
        }
    }

    public void Dispose() { }
}
