using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CS2SteamLauncher
{
    public sealed class UpdateManifest
    {
        public string Version { get; set; } = string.Empty;
        public string PackageUrl { get; set; } = string.Empty;
        public string? Sha256 { get; set; }
        public string? ReleaseNotes { get; set; }
        public bool Mandatory { get; set; }
        public string? Channel { get; set; }
    }

    public sealed class UpdateCheckResult
    {
        public bool IsUpdateAvailable { get; set; }
        public string CurrentVersionText { get; set; } = "0.0.0";
        public string LatestVersionText { get; set; } = "0.0.0";
        public UpdateManifest? Manifest { get; set; }
        public string? ErrorMessage { get; set; }
    }

    public sealed class UpdateDownloadResult
    {
        public bool Success { get; set; }
        public string? PackageFilePath { get; set; }
        public string? ErrorMessage { get; set; }
    }

    public sealed class UpdateService
    {
        private static readonly HttpClient HttpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        public async Task<UpdateCheckResult> CheckForUpdateAsync(string manifestUrl, string updateChannel, CancellationToken cancellationToken = default)
        {
            var result = new UpdateCheckResult
            {
                CurrentVersionText = GetCurrentVersion().ToString(3)
            };

            if (string.IsNullOrWhiteSpace(manifestUrl))
            {
                result.ErrorMessage = "Manifest url is empty.";
                return result;
            }

            try
            {
                using var response = await HttpClient.GetAsync(manifestUrl, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    result.ErrorMessage = $"Request failed: {(int)response.StatusCode} {response.ReasonPhrase}";
                    return result;
                }

                string json = await response.Content.ReadAsStringAsync(cancellationToken);
                if (!TryParseManifest(json, updateChannel, out UpdateManifest? manifest, out string parseError) || manifest == null)
                {
                    result.ErrorMessage = parseError;
                    return result;
                }

                if (!TryParseVersion(manifest.Version, out Version? latestVersion) || latestVersion == null)
                {
                    result.ErrorMessage = $"Invalid version in manifest: {manifest.Version}";
                    return result;
                }

                Version currentVersion = GetCurrentVersion();
                result.LatestVersionText = latestVersion.ToString(3);
                result.Manifest = manifest;
                result.IsUpdateAvailable = latestVersion > currentVersion;
                return result;
            }
            catch (OperationCanceledException)
            {
                result.ErrorMessage = "Update check canceled.";
                return result;
            }
            catch (Exception ex)
            {
                result.ErrorMessage = $"Update check failed: {ex.Message}";
                return result;
            }
        }

        public async Task<UpdateDownloadResult> DownloadPackageAsync(UpdateManifest manifest, string destinationPath, CancellationToken cancellationToken = default)
        {
            var result = new UpdateDownloadResult();

            if (manifest == null)
            {
                result.ErrorMessage = "Manifest is null.";
                return result;
            }

            if (string.IsNullOrWhiteSpace(manifest.PackageUrl))
            {
                result.ErrorMessage = "PackageUrl is empty.";
                return result;
            }

            try
            {
                string? directory = Path.GetDirectoryName(destinationPath);
                if (string.IsNullOrWhiteSpace(directory))
                {
                    result.ErrorMessage = "Invalid destination path.";
                    return result;
                }

                Directory.CreateDirectory(directory);

                using var response = await HttpClient.GetAsync(manifest.PackageUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    result.ErrorMessage = $"Download failed: {(int)response.StatusCode} {response.ReasonPhrase}";
                    return result;
                }

                await using var networkStream = await response.Content.ReadAsStreamAsync(cancellationToken);
                await using var fileStream = File.Create(destinationPath);
                using var hasher = SHA256.Create();

                byte[] buffer = new byte[81920];
                int bytesRead;
                while ((bytesRead = await networkStream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                    hasher.TransformBlock(buffer, 0, bytesRead, null, 0);
                }

                hasher.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                string actualSha256 = Convert.ToHexString(hasher.Hash ?? Array.Empty<byte>()).ToLowerInvariant();

                if (!string.IsNullOrWhiteSpace(manifest.Sha256))
                {
                    string expectedSha256 = manifest.Sha256.Trim().ToLowerInvariant();
                    if (!string.Equals(expectedSha256, actualSha256, StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            File.Delete(destinationPath);
                        }
                        catch
                        {
                            // Best-effort cleanup.
                        }

                        result.ErrorMessage = "SHA256 mismatch.";
                        return result;
                    }
                }

                result.Success = true;
                result.PackageFilePath = destinationPath;
                return result;
            }
            catch (OperationCanceledException)
            {
                result.ErrorMessage = "Download canceled.";
                return result;
            }
            catch (Exception ex)
            {
                result.ErrorMessage = $"Download failed: {ex.Message}";
                return result;
            }
        }

        public string BuildDefaultPackagePath(UpdateManifest manifest)
        {
            string versionFolder = SanitizePathSegment(string.IsNullOrWhiteSpace(manifest.Version) ? "unknown" : manifest.Version);
            string extension = ".zip";

            if (Uri.TryCreate(manifest.PackageUrl, UriKind.Absolute, out Uri? packageUri))
            {
                string fileName = Path.GetFileName(packageUri.LocalPath);
                string ext = Path.GetExtension(fileName);
                if (!string.IsNullOrWhiteSpace(ext))
                {
                    extension = ext;
                }
            }

            string root = Path.Combine(Path.GetTempPath(), "CS2Launcher", "updates", versionFolder);
            return Path.Combine(root, $"package{extension}");
        }

        public bool TryStartUpdater(string updaterExePath, string packagePath, out string error)
        {
            error = string.Empty;

            try
            {
                if (!File.Exists(updaterExePath))
                {
                    error = $"Updater not found: {updaterExePath}";
                    return false;
                }

                if (!File.Exists(packagePath))
                {
                    error = $"Package not found: {packagePath}";
                    return false;
                }

                using Process current = Process.GetCurrentProcess();
                string currentExePath = current.MainModule?.FileName ?? string.Empty;
                string currentExeName = Path.GetFileName(currentExePath);
                string targetDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

                if (string.IsNullOrWhiteSpace(currentExeName) || string.IsNullOrWhiteSpace(targetDir))
                {
                    error = "Failed to resolve current executable metadata.";
                    return false;
                }

                string arguments =
                    $"--package \"{packagePath}\" " +
                    $"--targetDir \"{targetDir}\" " +
                    $"--processId {current.Id} " +
                    $"--exeName \"{currentExeName}\"";

                string workingDir = Path.GetDirectoryName(updaterExePath) ?? AppContext.BaseDirectory;

                // First try ShellExecute for best compatibility with standard app launch behavior.
                var shellStartInfo = new ProcessStartInfo
                {
                    FileName = updaterExePath,
                    Arguments = arguments,
                    UseShellExecute = true,
                    WorkingDirectory = workingDir
                };

                try
                {
                    Process.Start(shellStartInfo);
                    return true;
                }
                catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
                {
                    // ERROR_CANCELLED: user canceled UAC/SmartScreen/Windows security prompt.
                    // Retry with direct process creation to reduce shell-side prompt impact.
                    var directStartInfo = new ProcessStartInfo
                    {
                        FileName = updaterExePath,
                        Arguments = arguments,
                        UseShellExecute = false,
                        WorkingDirectory = workingDir,
                        CreateNoWindow = true
                    };

                    Process.Start(directStartInfo);
                    return true;
                }
            }
            catch (Exception ex)
            {
                error = $"Failed to start updater: {ex.Message}";
                return false;
            }
        }

        public static string GetDefaultUpdaterPath()
        {
            string baseDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return Path.Combine(baseDir, "Updater.exe");
        }

        private static bool TryParseManifest(string json, string updateChannel, out UpdateManifest? manifest, out string error)
        {
            manifest = null;
            error = string.Empty;

            try
            {
                using JsonDocument doc = JsonDocument.Parse(json);
                JsonElement root = doc.RootElement;

                JsonElement manifestElement;
                if (root.ValueKind == JsonValueKind.Object &&
                    root.TryGetProperty("channels", out JsonElement channelsElement) &&
                    channelsElement.ValueKind == JsonValueKind.Object)
                {
                    string channel = string.IsNullOrWhiteSpace(updateChannel) ? "stable" : updateChannel.Trim();
                    if (!channelsElement.TryGetProperty(channel, out manifestElement))
                    {
                        error = $"Channel not found: {channel}";
                        return false;
                    }
                }
                else
                {
                    manifestElement = root;
                }

                manifest = JsonSerializer.Deserialize<UpdateManifest>(manifestElement.GetRawText(), new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (manifest == null)
                {
                    error = "Manifest is null after deserialize.";
                    return false;
                }

                if (string.IsNullOrWhiteSpace(manifest.Version) || string.IsNullOrWhiteSpace(manifest.PackageUrl))
                {
                    error = "Manifest missing required fields: version/packageUrl.";
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                error = $"Manifest parse failed: {ex.Message}";
                return false;
            }
        }

        private static Version GetCurrentVersion()
        {
            Version? fromAssembly = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version;
            if (fromAssembly != null)
            {
                return fromAssembly;
            }

            return new Version(0, 0, 0, 0);
        }

        private static bool TryParseVersion(string text, out Version? version)
        {
            version = null;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            string normalized = text.Trim();
            if (normalized.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized.Substring(1);
            }

            return Version.TryParse(normalized, out version);
        }

        private static string SanitizePathSegment(string text)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                text = text.Replace(c, '_');
            }

            return text;
        }
    }
}
