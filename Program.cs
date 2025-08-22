using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.IO;
using Microsoft.Win32;

namespace CS2SteamLauncher
{
    public class CS2Launcher
    {
        private const int CS2_APP_ID = 730; // CS2 Steam App ID

        public string GetActiveSteamUserInfo()
        {
            return GetSteamID64();
        }

        private string GetSteamPath()
        {
            try
            {
                // 从注册表获取Steam路径
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
                {
                    if (key != null)
                    {
                        string? steamPath = key.GetValue("SteamExe") as string;
                        if (!string.IsNullOrEmpty(steamPath) && File.Exists(steamPath))
                        {
                            return steamPath;
                        }
                    }
                }

                // 尝试默认路径
                string[] defaultPaths = {
                    @"C:\Program Files (x86)\Steam\steam.exe",
                    @"C:\Program Files\Steam\steam.exe"
                };

                foreach (string path in defaultPaths)
                {
                    if (File.Exists(path))
                    {
                        return path;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"获取Steam路径时出错: {ex.Message}");
            }

            return "";
        }

        // 获取用户的SteamID64
        private string GetSteamID64()
        {
            try
            {
                // 尝试从注册表获取当前登录用户的SteamID
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam\ActiveProcess"))
                {
                    if (key != null)
                    {
                        object? activeUser = key.GetValue("ActiveUser");
                        if (activeUser != null)
                        {
                            uint accountId = Convert.ToUInt32(activeUser);
                            // 将Account ID转换为SteamID64
                            ulong steamId64 = SteamIDConverter.ConvertToSteamID64(accountId);
                            return steamId64.ToString();
                        }
                    }
                }

                // 尝试从Steam配置文件中获取
                string steamPath = GetSteamPath();
                if (!string.IsNullOrEmpty(steamPath))
                {
                    string steamConfigPath = Path.Combine(Path.GetDirectoryName(steamPath) ?? "", "config", "loginusers.vdf");
                    if (File.Exists(steamConfigPath))
                    {
                        string[] configLines = File.ReadAllLines(steamConfigPath);
                        foreach (string line in configLines)
                        {
                            if (line.Contains("\"accountid\""))
                            {
                                // 提取accountid
                                string[] parts = line.Split('"');
                                if (parts.Length >= 4)
                                {
                                    uint accountId = Convert.ToUInt32(parts[3]);
                                    // 将Account ID转换为SteamID64
                                    ulong steamId64 = SteamIDConverter.ConvertToSteamID64(accountId);
                                    return steamId64.ToString();
                                }
                            }
                        }
                    }
                }

                Console.WriteLine("无法获取SteamID64");
                return "";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"获取SteamID64时出错: {ex.Message}");
                return "";
            }
        }

        // SteamID转换工具类
        public static class SteamIDConverter
        {
            // SteamID64的基数，这是Steam的"Universe"标识符
            // 76561197960265728 是Steam公共Universe的基数值
            // 这个值表示：1 << 56 | 1 << 52 | 1 << 44 | 0
            // 其中：
            // - 1 << 56 表示Universe（公共Universe为1）
            // - 1 << 52 表示Type（个体用户为1）
            // - 1 << 44 表示Instance（桌面用户为1）
            private const ulong STEAM_ID64_BASE = 76561197960265728;

            // 将Account ID转换为SteamID64
            // Account ID是Steam账户的唯一标识符，是一个32位无符号整数
            // SteamID64 = Universe + Type + Instance + Account ID
            public static ulong ConvertToSteamID64(uint accountId)
            {
                return STEAM_ID64_BASE + accountId;
            }

            // 将SteamID64转换为Account ID
            public static uint ConvertToAccountID(ulong steamId64)
            {
                return (uint)(steamId64 - STEAM_ID64_BASE);
            }

            // 将SteamID64转换为SteamID2格式（如STEAM_0:1:12345678）
            // SteamID2格式：STEAM_X:Y:Z
            // X: Universe (通常为0表示公共Universe)
            // Y: 认证服务器 (0或1)
            // Z: Account ID除以2的商
            public static string ConvertToSteamID2(ulong steamId64)
            {
                uint accountId = ConvertToAccountID(steamId64);
                uint authServer = accountId & 1; // 最低位决定认证服务器
                uint accountIdPart = accountId >> 1; // 右移一位得到账户ID部分

                return $"STEAM_0:{authServer}:{accountIdPart}";
            }

            // 将SteamID2格式转换为SteamID64
            public static ulong ConvertFromSteamID2(string steamId2)
            {
                // 格式: STEAM_0:X:Y
                string[] parts = steamId2.Split(':');
                if (parts.Length != 3 || !parts[0].StartsWith("STEAM_"))
                {
                    throw new ArgumentException("Invalid SteamID2 format");
                }

                uint authServer = uint.Parse(parts[1]);
                uint accountIdPart = uint.Parse(parts[2]);

                // 重建Account ID: (accountIdPart << 1) | authServer
                uint accountId = (accountIdPart << 1) | authServer;

                return ConvertToSteamID64(accountId);
            }

            // 将SteamID3格式转换为SteamID64（如[U:1:12345678]）
            // SteamID3格式：[U:X:Y]
            // U: 表示用户类型
            // X: Universe
            // Y: Account ID
            public static ulong ConvertFromSteamID3(string steamId3)
            {
                // 格式: [U:1:12345678]
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

        private async Task MonitorGameLaunch()
        {
            Console.WriteLine("监控游戏启动状态...");

            for (int i = 0; i < 30; i++) // 监控30秒
            {
                await Task.Delay(1000);

                try
                {
                    // 检查CS2进程是否启动
                    var cs2Processes = Process.GetProcessesByName("cs2");
                    if (cs2Processes.Length > 0)
                    {
                        Console.WriteLine("CS2进程已启动！");
                        break;
                    }

                    if (i % 5 == 0)
                    {
                        Console.WriteLine($"等待游戏启动... ({i + 1}/30秒)");
                    }
                }
                catch
                {
                    // 忽略监控过程中的错误
                }
            }
        }


        private bool IsSteamRunning()
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

        private async Task StartSteam()
        {
            try
            {
                string steamPath = GetSteamPath();
                if (!string.IsNullOrEmpty(steamPath))
                {
                    ProcessStartInfo startInfo = new ProcessStartInfo
                    {
                        FileName = steamPath,
                        UseShellExecute = true
                    };
                    Process.Start(startInfo);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"启动Steam失败: {ex.Message}");
            }
        }
    }

    // 主程序
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("CS2 Steam Launcher");
            Console.WriteLine("==================");

            var launcher = new CS2Launcher();

            // 示例使用
            string serverIp = "127.0.0.1";  // 替换为实际服务器IP
            int serverPort = 27015;         // 替换为实际端口
            string? password = null;        // 如果服务器有密码，在这里设置

            if (args.Length >= 2)
            {
                serverIp = args[0];
                if (int.TryParse(args[1], out int port))
                {
                    serverPort = port;
                }
                if (args.Length >= 3)
                {
                    password = args[2];
                }
            }
            else
            {
                Console.WriteLine("请输入服务器IP:");
                string? inputIp = Console.ReadLine();
                if (!string.IsNullOrEmpty(inputIp))
                {
                    serverIp = inputIp;
                }

                Console.WriteLine("请输入服务器端口 (默认27015):");
                string? inputPort = Console.ReadLine();
                if (!string.IsNullOrEmpty(inputPort) && int.TryParse(inputPort, out int port))
                {
                    serverPort = port;
                }

                Console.WriteLine("请输入服务器密码 (如无密码请直接回车):");
                string? inputPassword = Console.ReadLine();
                if (!string.IsNullOrEmpty(inputPassword))
                {
                    password = inputPassword;
                }
            }

            Console.WriteLine($"正在启动CS2并连接到: {serverIp}:{serverPort}");
            if (!string.IsNullOrEmpty(password))
            {
                Console.WriteLine("使用密码连接");
            }

            string steamid64 = launcher.GetActiveSteamUserInfo();
            Console.WriteLine($"steamid64: {steamid64}");

            // 通过steam://connect协议连接到服务器
            try
            {
                string connectUrl = $"steam://run/730//+connect {serverIp}:{serverPort}";
                if (!string.IsNullOrEmpty(password))
                {
                    connectUrl += $"?password={password}";
                }

                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = connectUrl,
                    UseShellExecute = true
                };

                Process.Start(startInfo);
                Console.WriteLine($"正在通过Steam连接到服务器: {serverIp}:{serverPort}...");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"自动连接失败: {ex.Message}");
                Console.WriteLine("提示：如果游戏未自动连接服务器，请在游戏内控制台(按~键)输入:");
                Console.WriteLine($"connect {serverIp}:{serverPort}");
                if (!string.IsNullOrEmpty(password))
                {
                    Console.WriteLine($"password {password}");
                }
            }

            Console.WriteLine("\n按任意键退出...");
            Console.ReadKey();
        }
    }
}