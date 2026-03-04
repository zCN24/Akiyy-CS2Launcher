using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace CS2SteamLauncher
{
    public class CS2Launcher
    {
        private const int CS2_APP_ID = 730; // CS2 Steam App ID
        private const int SteamStartupWaitMs = 20000;

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

        public async Task<bool> LaunchAndConnectAsync(string serverIp, int serverPort, string? password)
        {
            if (string.IsNullOrWhiteSpace(serverIp))
            {
                throw new ArgumentException("服务器IP不能为空", nameof(serverIp));
            }

            if (serverPort <= 0 || serverPort > 65535)
            {
                throw new ArgumentException("服务器端口无效", nameof(serverPort));
            }

            string connectUrl = BuildConnectUrl(serverIp, serverPort, password);

            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = connectUrl,
                    UseShellExecute = true
                };

                Process.Start(startInfo);
                return true;
            }
            catch
            {
                // 如果直接使用 steam:// 失败，尝试先启动Steam再重试
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
                            FileName = connectUrl,
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

        public string BuildConnectUrl(string serverIp, int serverPort, string? password)
        {
            string connectUrl = $"steam://run/{CS2_APP_ID}//+connect {serverIp}:{serverPort}";
            if (!string.IsNullOrWhiteSpace(password))
            {
                connectUrl += $"?password={password}";
            }

            return connectUrl;
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