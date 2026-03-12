using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace CS2SteamLauncher 
{
    public class CS2Launcher
    {
        private const int CS2_APP_ID = 730; // CS2 Steam App ID
        private const int SteamStartupWaitMs = 20000;
        private const string LauncherCfgFileName = "cs2launcher.cfg";

        public string GetActiveSteamUserInfo()
        {
            return GetSteamID64();
        }

        public bool IsSteamRunning()
        {
            try
            {
                var steamProcesses = Process.GetProcessesByName("steam");
                return steamProcesses.Length > 0;
            }
            catch
            {
                return false;
            }
        }

        public bool IsCs2Running()
        {
            try
            {
                var cs2Processes = Process.GetProcessesByName("cs2");
                return cs2Processes.Length > 0;
            }
            catch
            {
                return false;
            }
        }

        public bool TryStopCs2IfRunning(out int stoppedCount, out string error)
        {
            stoppedCount = 0;
            error = string.Empty;

            try
            {
                var cs2Processes = Process.GetProcessesByName("cs2");
                if (cs2Processes.Length == 0)
                {
                    return true;
                }

                foreach (var process in cs2Processes)
                {
                    try
                    {
                        process.Kill(entireProcessTree: true);
                        process.WaitForExit(5000);
                        stoppedCount++;
                    }
                    catch (Exception ex)
                    {
                        error = $"结束进程失败(PID={process.Id}): {ex.Message}";
                        return false;
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public async Task<bool> StartSteamAsync()
        {
            try
            {
                string steamPath = GetSteamPath();
                if (string.IsNullOrEmpty(steamPath))
                {
                    return false;
                }

                var startInfo = new ProcessStartInfo
                {
                    FileName = steamPath,
                    UseShellExecute = true
                };

                Process.Start(startInfo);
                await WaitForSteamClientAsync();
                return true;
            }
            catch
            {
                return false;
            }
        }

        public async Task<(bool overallSuccess, bool firstLaunchCommandSent)> LaunchAndConnectAsync(string serverIp, int serverPort, string? password, string? customLaunchOptions, Action<string>? onProgress = null)
        {
            if (string.IsNullOrWhiteSpace(serverIp))
            {
                throw new ArgumentException("服务器IP不能为空", nameof(serverIp));
            }

            if (serverPort <= 0 || serverPort > 65535)
            {
                throw new ArgumentException("服务器端口无效", nameof(serverPort));
            }

            bool requiresTwoStep = !string.IsNullOrWhiteSpace(password);
            string? passwordCfgFileName = null;

            if (requiresTwoStep)
            {
                onProgress?.Invoke("检测到服务器密码，准备创建临时 cfg 并使用 +exec 方式启动...");
                if (!TryPreparePasswordExecCfg(password!, out passwordCfgFileName, out string prepareError, onProgress))
                {
                    onProgress?.Invoke($"密码 cfg 准备失败: {prepareError}");
                    return (false, false);
                }
            }

            string launchUrl = BuildLaunchUrl(
                serverIp,
                serverPort,
                customLaunchOptions,
                includeConnect: !requiresTwoStep,
                execCfgFileName: passwordCfgFileName);

            bool launchOk = await LaunchViaSteamProtocolAsync(launchUrl);
            if (!launchOk)
            {
                return (false, false);
            }

            if (!requiresTwoStep)
            {
                return (true, true);
            }

            onProgress?.Invoke("已启动 CS2，等待进程就绪后再执行连接...");
            bool gameStarted = await WaitForGameStartAsync(onProgress);
            if (!gameStarted)
            {
                return (false, true);
            }

            onProgress?.Invoke("检测到 CS2 运行，等待 10 秒后发送连接指令...");
            await Task.Delay(10_000);

            string connectUrl = BuildConnectOnlyUrl(serverIp, serverPort);
            bool connectOk = await LaunchViaSteamProtocolAsync(connectUrl);
            return (connectOk, true);
        }

        /// <summary>
        /// 使用示例：获取 cfg 路径 -> 创建 pw.cfg -> 输出启动 URI。
        /// </summary>
        public string BuildPasswordExecUsageExample(Action<string>? onProgress = null)
        {
            if (!TryGetCs2CfgDirectory(out string cfgDir, out string error, onProgress))
            {
                onProgress?.Invoke($"示例失败：{error}");
                return string.Empty;
            }

            string cfgFileName = "pw.cfg";
            string cfgFilePath = Path.Combine(cfgDir, cfgFileName);

            try
            {
                File.WriteAllText(cfgFilePath, "password 123123" + Environment.NewLine, Encoding.UTF8);
                string uri = $"steam://rungameid/{CS2_APP_ID}//+exec {cfgFileName}";
                onProgress?.Invoke($"示例已创建 cfg: {cfgFilePath}");
                onProgress?.Invoke($"示例启动 URI: {uri}");
                return uri;
            }
            catch (Exception ex)
            {
                onProgress?.Invoke($"示例写入失败：{ex.Message}");
                return string.Empty;
            }
        }

        private async Task<bool> LaunchViaSteamProtocolAsync(string url)
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                };

                Process.Start(startInfo);
                return true;
            }
            catch
            {
                if (!IsSteamRunning())
                {
                    bool started = await StartSteamAsync();
                    if (!started)
                    {
                        return false;
                    }

                    try
                    {
                        var retryInfo = new ProcessStartInfo
                        {
                            FileName = url,
                            UseShellExecute = true
                        };

                        Process.Start(retryInfo);
                        return true;
                    }
                    catch
                    {
                        return false;
                    }
                }

                return false;
            }
        }

        public async Task MonitorGameLaunchAsync(Action<string>? onTick = null)
        {
            for (int i = 0; i < 30; i++) // 监控30秒
            {
                await Task.Delay(1000);

                try
                {
                    var cs2Processes = Process.GetProcessesByName("cs2");
                    if (cs2Processes.Length > 0)
                    {
                        onTick?.Invoke("CS2 进程已启动");
                        break;
                    }

                    if (i % 5 == 0)
                    {
                        onTick?.Invoke($"等待游戏启动... ({i + 1}/30秒)");
                    }
                }
                catch
                {
                    // 忽略监控过程中的错误
                }
            }
        }

        private async Task<bool> WaitForGameStartAsync(Action<string>? onTick = null)
        {
            for (int i = 0; i < 30; i++)
            {
                await Task.Delay(1000);

                try
                {
                    var cs2Processes = Process.GetProcessesByName("cs2");
                    if (cs2Processes.Length > 0)
                    {
                        onTick?.Invoke("检测到 CS2 进程启动");
                        return true;
                    }

                    if (i % 5 == 4)
                    {
                        onTick?.Invoke($"等待游戏启动... ({i + 1}/30秒)");
                    }
                }
                catch
                {
                    // 忽略监控过程中的错误
                }
            }

            onTick?.Invoke("在预期时间内未检测到 CS2 进程");
            return false;
        }

        public string BuildLaunchUrl(string serverIp, int serverPort, string? customLaunchOptions, bool includeConnect, string? execCfgFileName)
        {
            var args = new List<string>();

            // 如果传入 exec cfg，则第一步仅执行 cfg（通常用于预设 password）。
            if (!string.IsNullOrWhiteSpace(execCfgFileName))
            {
                args.Add($"+exec {execCfgFileName}");
            }

            if (includeConnect)
            {
                args.Add($"+connect {serverIp}:{serverPort}");
            }

            if (!string.IsNullOrWhiteSpace(customLaunchOptions))
            {
                args.Add(customLaunchOptions.Trim());
            }

            string argumentPart = string.Join(' ', args);
            return $"steam://rungameid/{CS2_APP_ID}//{argumentPart}";
        }

        public string BuildConnectOnlyUrl(string serverIp, int serverPort)
        {
            return $"steam://rungameid/{CS2_APP_ID}//+connect {serverIp}:{serverPort}";
        }

        private bool TryPreparePasswordExecCfg(string password, out string cfgFileName, out string error, Action<string>? onProgress)
        {
            cfgFileName = string.Empty;
            error = string.Empty;

            if (!TryGetCs2CfgDirectory(out string cfgDir, out error, onProgress))
            {
                return false;
            }

            try
            {
                Directory.CreateDirectory(cfgDir);

                // 固定使用一个 cfg 文件，避免重复创建多个临时文件。
                cfgFileName = LauncherCfgFileName;
                string cfgAbsolutePath = Path.Combine(cfgDir, cfgFileName);
                bool cfgAlreadyExists = File.Exists(cfgAbsolutePath);

                // 这里只写 password 指令，连接动作在 10 秒后单独下发 connect。
                string escapedPassword = EscapeCfgValue(password);
                string cfgContent = $"password \"{escapedPassword}\"" + Environment.NewLine;
                File.WriteAllText(cfgAbsolutePath, cfgContent, Encoding.UTF8);

                if (cfgAlreadyExists)
                {
                    onProgress?.Invoke($"检测到已有 cfg，已更新启动项: {cfgAbsolutePath}");
                }
                else
                {
                    onProgress?.Invoke($"未找到 cfg，已创建并写入启动项: {cfgAbsolutePath}");
                }

                return true;
            }
            catch (UnauthorizedAccessException ex)
            {
                error = $"写入 cfg 失败（权限不足）: {ex.Message}";
                return false;
            }
            catch (IOException ex)
            {
                error = $"写入 cfg 失败（IO错误）: {ex.Message}";
                return false;
            }
            catch (Exception ex)
            {
                error = $"写入 cfg 失败: {ex.Message}";
                return false;
            }
        }

        private bool TryGetCs2CfgDirectory(out string cfgDirectory, out string error, Action<string>? onProgress)
        {
            cfgDirectory = string.Empty;

            // 按需求优先执行方案2（Steamworks SDK），失败再回退方案1（VDF 解析）。
            if (TryGetCs2CfgDirectoryViaSteamworks(out cfgDirectory, out error))
            {
                onProgress?.Invoke($"方案2成功：通过 Steamworks 获取到 CS2 cfg 目录: {cfgDirectory}");
                return true;
            }

            onProgress?.Invoke($"方案2失败：{error}");
            onProgress?.Invoke("开始回退方案1：读取 libraryfolders.vdf 与 appmanifest_730.acf...");

            if (TryGetCs2CfgDirectoryViaVdf(out cfgDirectory, out error))
            {
                onProgress?.Invoke($"方案1成功：通过 VDF 获取到 CS2 cfg 目录: {cfgDirectory}");
                return true;
            }

            error = $"未找到 CS2 安装。最后一次错误: {error}";
            return false;
        }

        private bool TryGetCs2CfgDirectoryViaSteamworks(out string cfgDirectory, out string error)
        {
            cfgDirectory = string.Empty;
            error = string.Empty;

            try
            {
                // Steamworks 方案要求 Steam 客户端运行且可加载 steam_api64.dll。
                if (!SteamworksNative.SteamAPI_Init())
                {
                    error = "SteamAPI_Init() 失败，请确认 Steam 客户端已运行并可用。";
                    return false;
                }

                try
                {
                    IntPtr steamApps = SteamworksNative.SteamAPI_SteamApps_v008();
                    if (steamApps == IntPtr.Zero)
                    {
                        error = "SteamAPI_SteamApps_v008() 返回空指针。";
                        return false;
                    }

                    var folder = new StringBuilder(1024);
                    uint result = SteamworksNative.SteamAPI_ISteamApps_GetAppInstallDir(
                        steamApps,
                        CS2_APP_ID,
                        folder,
                        (uint)folder.Capacity);

                    if (result == 0)
                    {
                        error = "ISteamApps::GetAppInstallDir 返回 0，未获取到安装目录。";
                        return false;
                    }

                    string installDir = folder.ToString().Trim();
                    if (string.IsNullOrWhiteSpace(installDir))
                    {
                        error = "ISteamApps::GetAppInstallDir 返回了空路径。";
                        return false;
                    }

                    string candidate = Path.Combine(installDir, "game", "csgo", "cfg");
                    if (Directory.Exists(candidate))
                    {
                        cfgDirectory = candidate;
                        return true;
                    }

                    // 官方文档说明：即使未安装也可能返回默认库路径，因此必须二次校验。
                    error = $"GetAppInstallDir 返回路径存在，但未检测到 cfg 目录: {candidate}";
                    return false;
                }
                finally
                {
                    SteamworksNative.SteamAPI_Shutdown();
                }
            }
            catch (DllNotFoundException ex)
            {
                error = $"未找到 steam_api64.dll: {ex.Message}";
                return false;
            }
            catch (EntryPointNotFoundException ex)
            {
                error = $"Steamworks 导出函数缺失，请检查 SDK/动态库版本: {ex.Message}";
                return false;
            }
            catch (Exception ex)
            {
                error = $"Steamworks 方案异常: {ex.Message}";
                return false;
            }
        }

        private bool TryGetCs2CfgDirectoryViaVdf(out string cfgDirectory, out string error)
        {
            cfgDirectory = string.Empty;
            error = string.Empty;

            try
            {
                string steamRoot = GetSteamRootPath();
                if (string.IsNullOrWhiteSpace(steamRoot))
                {
                    error = "未找到 Steam 根目录（注册表与默认路径均失败）。";
                    return false;
                }

                string libraryVdfPath = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
                if (!File.Exists(libraryVdfPath))
                {
                    error = $"libraryfolders.vdf 不存在: {libraryVdfPath}";
                    return false;
                }

                string libraryVdfText = File.ReadAllText(libraryVdfPath);
                if (!TryParseVdf(libraryVdfText, out VdfNode? rootNode, out string parseError) || rootNode == null)
                {
                    error = $"libraryfolders.vdf 解析失败: {parseError}";
                    return false;
                }

                List<string> libraryPaths = ExtractLibraryPaths(rootNode, steamRoot);
                foreach (string libraryPath in libraryPaths)
                {
                    try
                    {
                        string manifestPath = Path.Combine(libraryPath, "steamapps", "appmanifest_730.acf");
                        if (!File.Exists(manifestPath))
                        {
                            continue;
                        }

                        string acfText = File.ReadAllText(manifestPath);
                        if (!TryParseVdf(acfText, out VdfNode? acfRoot, out string acfError) || acfRoot == null)
                        {
                            error = $"appmanifest_730.acf 解析失败 ({manifestPath}): {acfError}";
                            continue;
                        }

                        if (!TryGetVdfValueDeep(acfRoot, "installdir", out string? installDir) || string.IsNullOrWhiteSpace(installDir))
                        {
                            error = $"appmanifest_730.acf 缺少 installdir 字段 ({manifestPath})";
                            continue;
                        }

                        string gameRoot = Path.Combine(libraryPath, "steamapps", "common", installDir);
                        string candidate = Path.Combine(gameRoot, "game", "csgo", "cfg");
                        if (Directory.Exists(candidate))
                        {
                            cfgDirectory = candidate;
                            return true;
                        }

                        error = $"找到安装目录但 cfg 不存在: {candidate}";
                    }
                    catch (UnauthorizedAccessException ex)
                    {
                        error = $"访问库目录权限不足 ({libraryPath}): {ex.Message}";
                    }
                    catch (IOException ex)
                    {
                        error = $"读取库目录失败 ({libraryPath}): {ex.Message}";
                    }
                }

                if (string.IsNullOrWhiteSpace(error))
                {
                    error = "已扫描全部 Steam 库，未发现 appmanifest_730.acf。";
                }

                return false;
            }
            catch (UnauthorizedAccessException ex)
            {
                error = $"读取 Steam 配置权限不足: {ex.Message}";
                return false;
            }
            catch (IOException ex)
            {
                error = $"读取 Steam 配置失败: {ex.Message}";
                return false;
            }
            catch (Exception ex)
            {
                error = $"VDF 方案异常: {ex.Message}";
                return false;
            }
        }

        private string GetSteamRootPath()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
                if (key != null)
                {
                    // 需求要求优先读取 SteamPath（Steam 根目录）。
                    string? steamRoot = key.GetValue("SteamPath") as string;
                    if (!string.IsNullOrWhiteSpace(steamRoot) && Directory.Exists(steamRoot))
                    {
                        return steamRoot;
                    }

                    // 兼容有些环境只给了 SteamExe。
                    string? steamExe = key.GetValue("SteamExe") as string;
                    if (!string.IsNullOrWhiteSpace(steamExe) && File.Exists(steamExe))
                    {
                        string? dir = Path.GetDirectoryName(steamExe);
                        if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
                        {
                            return dir;
                        }
                    }
                }

                string[] defaultRoots =
                {
                    @"C:\Program Files (x86)\Steam",
                    @"C:\Program Files\Steam"
                };

                foreach (string path in defaultRoots)
                {
                    if (Directory.Exists(path))
                    {
                        return path;
                    }
                }
            }
            catch
            {
                // 忽略，统一返回空字符串。
            }

            return string.Empty;
        }

        private static string EscapeCfgValue(string value)
        {
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        private static List<string> ExtractLibraryPaths(VdfNode rootNode, string steamRoot)
        {
            var results = new List<string>();

            // 主库本身也要纳入扫描。
            if (!string.IsNullOrWhiteSpace(steamRoot))
            {
                results.Add(steamRoot);
            }

            VdfNode? libraryFoldersNode = null;
            if (rootNode.Children.TryGetValue("libraryfolders", out VdfNode? node))
            {
                libraryFoldersNode = node;
            }
            else
            {
                libraryFoldersNode = rootNode;
            }

            foreach (var kv in libraryFoldersNode.Children)
            {
                if (!int.TryParse(kv.Key, out _))
                {
                    continue;
                }

                string? path = null;
                if (!string.IsNullOrWhiteSpace(kv.Value.Value))
                {
                    // 旧格式："1" "D:\\SteamLibrary"
                    path = kv.Value.Value;
                }
                else if (kv.Value.Children.TryGetValue("path", out VdfNode? pathNode))
                {
                    // 新格式："1" { "path" "D:\\SteamLibrary" ... }
                    path = pathNode.Value;
                }

                if (string.IsNullOrWhiteSpace(path))
                {
                    continue;
                }

                string normalized = NormalizeSteamLibraryPath(path);
                if (!results.Contains(normalized, StringComparer.OrdinalIgnoreCase))
                {
                    results.Add(normalized);
                }
            }

            return results;
        }

        private static string NormalizeSteamLibraryPath(string path)
        {
            string normalized = path.Replace('/', Path.DirectorySeparatorChar);
            return normalized.Trim();
        }

        private static bool TryGetVdfValueDeep(VdfNode node, string key, out string? value)
        {
            value = null;

            if (node.Children.TryGetValue(key, out VdfNode? direct))
            {
                value = direct.Value;
                return !string.IsNullOrWhiteSpace(value);
            }

            foreach (var child in node.Children.Values)
            {
                if (TryGetVdfValueDeep(child, key, out value))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryParseVdf(string text, out VdfNode? root, out string error)
        {
            root = null;
            error = string.Empty;

            try
            {
                var parser = new VdfParser(text);
                root = parser.Parse();
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public class LauncherConfig
        {
            public string ServerIp { get; set; } = "127.0.0.1";
            public int ServerPort { get; set; } = 27015;
            public string? ServerPassword { get; set; }
            public string? CustomLaunchOptions { get; set; }
            public bool HasExecutedFirstLaunchCommand { get; set; }
            public bool AutoCheckUpdates { get; set; } = true;
            public string? UpdateManifestUrl { get; set; }
            public string? BackupUpdateManifestUrl { get; set; }
            public string UpdateChannel { get; set; } = "stable";
            public string? SkippedVersion { get; set; }
        }

        public static class LauncherConfigManager
        {
            private static readonly string ConfigDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            private static readonly string ConfigPath = Path.Combine(ConfigDir, "config.json");
            private static readonly string DefaultConfigPath = Path.Combine(ConfigDir, "config.default.json");
            private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true
            };

            public static async Task<LauncherConfig> LoadAsync()
            {
                try
                {
                    if (File.Exists(ConfigPath))
                    {
                        await using var stream = File.OpenRead(ConfigPath);
                        var cfg = await JsonSerializer.DeserializeAsync<LauncherConfig>(stream, JsonOptions);
                        if (cfg != null)
                        {
                            return cfg;
                        }
                    }

                    // Try template default file if present
                    if (File.Exists(DefaultConfigPath))
                    {
                        await using var defaultStream = File.OpenRead(DefaultConfigPath);
                        var templateCfg = await JsonSerializer.DeserializeAsync<LauncherConfig>(defaultStream, JsonOptions);
                        if (templateCfg != null)
                        {
                            await SaveAsync(templateCfg);
                            return templateCfg;
                        }
                    }

                    var defaultCfg = new LauncherConfig();
                    await SaveAsync(defaultCfg);
                    return defaultCfg;
                }
                catch
                {
                    // ignore and fallback to defaults
                }

                var fallbackCfg = new LauncherConfig();
                try
                {
                    await SaveAsync(fallbackCfg);
                }
                catch
                {
                    // swallow secondary failure
                }

                return fallbackCfg;
            }

            public static async Task SaveAsync(LauncherConfig config)
            {
                Directory.CreateDirectory(ConfigDir);
                await using var stream = File.Create(ConfigPath);
                await JsonSerializer.SerializeAsync(stream, config, JsonOptions);
            }
        }

        private async Task WaitForSteamClientAsync()
        {
            const int interval = 1000;
            int waited = 0;

            while (waited < SteamStartupWaitMs)
            {
                await Task.Delay(interval);
                waited += interval;

                if (IsSteamRunning())
                {
                    break;
                }
            }
        }

        private string GetSteamPath()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
                if (key != null)
                {
                    string? steamPath = key.GetValue("SteamExe") as string;
                    if (!string.IsNullOrEmpty(steamPath) && File.Exists(steamPath))
                    {
                        return steamPath;
                    }
                }

                string[] defaultPaths =
                {
                    @"C:\\Program Files (x86)\\Steam\\steam.exe",
                    @"C:\\Program Files\\Steam\\steam.exe"
                };

                foreach (string path in defaultPaths)
                {
                    if (File.Exists(path))
                    {
                        return path;
                    }
                }
            }
            catch
            {
                // 忽略路径解析异常
            }

            return string.Empty;
        }

        private sealed class VdfNode
        {
            public string? Value { get; set; }
            public Dictionary<string, VdfNode> Children { get; } = new Dictionary<string, VdfNode>(StringComparer.OrdinalIgnoreCase);
        }

        private sealed class VdfParser
        {
            private readonly string _text;
            private int _index;

            public VdfParser(string text)
            {
                _text = text ?? string.Empty;
            }

            public VdfNode Parse()
            {
                var root = new VdfNode();

                while (true)
                {
                    SkipWhitespaceAndComments();
                    if (IsEnd())
                    {
                        break;
                    }

                    string key = ReadQuotedString();
                    SkipWhitespaceAndComments();

                    if (Peek() == '{')
                    {
                        ReadChar();
                        root.Children[key] = ParseObject();
                    }
                    else
                    {
                        string value = ReadQuotedString();
                        root.Children[key] = new VdfNode { Value = value };
                    }
                }

                return root;
            }

            private VdfNode ParseObject()
            {
                var node = new VdfNode();

                while (true)
                {
                    SkipWhitespaceAndComments();
                    char current = Peek();

                    if (current == '\0')
                    {
                        throw new FormatException("VDF 结构不完整：缺少 '}'。");
                    }

                    if (current == '}')
                    {
                        ReadChar();
                        break;
                    }

                    string key = ReadQuotedString();
                    SkipWhitespaceAndComments();

                    if (Peek() == '{')
                    {
                        ReadChar();
                        node.Children[key] = ParseObject();
                    }
                    else
                    {
                        string value = ReadQuotedString();
                        node.Children[key] = new VdfNode { Value = value };
                    }
                }

                return node;
            }

            private string ReadQuotedString()
            {
                SkipWhitespaceAndComments();
                if (ReadChar() != '"')
                {
                    throw new FormatException($"VDF 解析失败：位置 {_index} 处不是引号字符串。");
                }

                var sb = new StringBuilder();
                while (true)
                {
                    if (IsEnd())
                    {
                        throw new FormatException("VDF 解析失败：字符串未闭合。");
                    }

                    char c = ReadChar();
                    if (c == '"')
                    {
                        break;
                    }

                    if (c == '\\')
                    {
                        if (IsEnd())
                        {
                            throw new FormatException("VDF 解析失败：转义字符不完整。");
                        }

                        char next = ReadChar();
                        sb.Append(next switch
                        {
                            '\\' => '\\',
                            '"' => '"',
                            'n' => '\n',
                            'r' => '\r',
                            't' => '\t',
                            _ => next
                        });
                    }
                    else
                    {
                        sb.Append(c);
                    }
                }

                return sb.ToString();
            }

            private void SkipWhitespaceAndComments()
            {
                while (!IsEnd())
                {
                    char c = Peek();
                    if (char.IsWhiteSpace(c))
                    {
                        _index++;
                        continue;
                    }

                    // 支持 VDF 常见注释：//
                    if (c == '/' && Peek(1) == '/')
                    {
                        _index += 2;
                        while (!IsEnd() && Peek() != '\n')
                        {
                            _index++;
                        }

                        continue;
                    }

                    break;
                }
            }

            private char ReadChar()
            {
                if (IsEnd())
                {
                    return '\0';
                }

                return _text[_index++];
            }

            private char Peek(int offset = 0)
            {
                int target = _index + offset;
                if (target < 0 || target >= _text.Length)
                {
                    return '\0';
                }

                return _text[target];
            }

            private bool IsEnd()
            {
                return _index >= _text.Length;
            }
        }

        private static class SteamworksNative
        {
            // 需要与 Steamworks SDK 提供的 steam_api64.dll 配套。
            private const string SteamApiDll = "steam_api64.dll";

            [DllImport(SteamApiDll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "SteamAPI_Init")]
            public static extern bool SteamAPI_Init();

            [DllImport(SteamApiDll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "SteamAPI_Shutdown")]
            public static extern void SteamAPI_Shutdown();

            [DllImport(SteamApiDll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "SteamAPI_SteamApps_v008")]
            public static extern IntPtr SteamAPI_SteamApps_v008();

            [DllImport(SteamApiDll, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, EntryPoint = "SteamAPI_ISteamApps_GetAppInstallDir")]
            public static extern uint SteamAPI_ISteamApps_GetAppInstallDir(IntPtr steamApps, uint appId, StringBuilder folder, uint folderBufferSize);
        }

        private string GetSteamID64()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam\ActiveProcess"))
                {
                    if (key != null)
                    {
                        object? activeUser = key.GetValue("ActiveUser");
                        if (activeUser != null)
                        {
                            uint accountId = Convert.ToUInt32(activeUser);
                            ulong steamId64 = SteamIDConverter.ConvertToSteamID64(accountId);
                            return steamId64.ToString();
                        }
                    }
                }

                string steamPath = GetSteamPath();
                if (!string.IsNullOrEmpty(steamPath))
                {
                    string steamConfigPath = Path.Combine(Path.GetDirectoryName(steamPath) ?? string.Empty, "config", "loginusers.vdf");
                    if (File.Exists(steamConfigPath))
                    {
                        string[] configLines = File.ReadAllLines(steamConfigPath);
                        foreach (string line in configLines)
                        {
                            if (line.Contains("\"accountid\""))
                            {
                                string[] parts = line.Split('"');
                                if (parts.Length >= 4)
                                {
                                    uint accountId = Convert.ToUInt32(parts[3]);
                                    ulong steamId64 = SteamIDConverter.ConvertToSteamID64(accountId);
                                    return steamId64.ToString();
                                }
                            }
                        }
                    }
                }

                return string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        // SteamID转换工具类
        public static class SteamIDConverter
        {
            // SteamID64基数
            private const ulong STEAM_ID64_BASE = 76561197960265728;

            public static ulong ConvertToSteamID64(uint accountId)
            {
                return STEAM_ID64_BASE + accountId;
            }

            public static uint ConvertToAccountID(ulong steamId64)
            {
                return (uint)(steamId64 - STEAM_ID64_BASE);
            }

            public static string ConvertToSteamID2(ulong steamId64)
            {
                uint accountId = ConvertToAccountID(steamId64);
                uint authServer = accountId & 1;
                uint accountIdPart = accountId >> 1;

                return $"STEAM_0:{authServer}:{accountIdPart}";
            }

            public static ulong ConvertFromSteamID2(string steamId2)
            {
                string[] parts = steamId2.Split(':');
                if (parts.Length != 3 || !parts[0].StartsWith("STEAM_"))
                {
                    throw new ArgumentException("Invalid SteamID2 format");
                }

                uint authServer = uint.Parse(parts[1]);
                uint accountIdPart = uint.Parse(parts[2]);

                uint accountId = (accountIdPart << 1) | authServer;

                return ConvertToSteamID64(accountId);
            }

            public static ulong ConvertFromSteamID3(string steamId3)
            {
                string content = steamId3.Trim('[', ']');
                string[] parts = content.Split(':');

                if (parts.Length != 3 || parts[0] != "U")
                {
                    throw new ArgumentException("Invalid SteamID3 format");
                }

                uint accountId = uint.Parse(parts[2]);
                return ConvertToSteamID64(accountId);
            }
        }
    }
}