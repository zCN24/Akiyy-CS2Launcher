using System;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace CS2SteamLauncher
{
    public partial class MainWindow : Window
    {
        private readonly CS2Launcher _launcher = new CS2Launcher();
        private readonly UpdateService _updateService = new UpdateService();
        private string _steamId64 = string.Empty;
        private bool _isBusy;
        private CS2Launcher.LauncherConfig _config = new CS2Launcher.LauncherConfig();

        public MainWindow() 
        {
            InitializeComponent();
            Loaded += async (_, _) => await OnLoadedAsync();
        }

        private async Task OnLoadedAsync()
        {
            await RefreshSteamIdInternalAsync();
            await LoadConfigAsync();
            await CheckForUpdatesOnStartupAsync();
        }

        private void SetBusy(bool isBusy)
        {
            _isBusy = isBusy;
            LaunchButton.IsEnabled = !isBusy;
            StartSteamButton.IsEnabled = !isBusy;
            RefreshSteamIdButton.IsEnabled = !isBusy;
            CheckUpdateButton.IsEnabled = !isBusy;
        }

        private void AppendLog(string message)
        {
            Dispatcher.Invoke(() =>
            {
                var builder = new StringBuilder(LogTextBox.Text);
                builder.AppendLine($"[{DateTime.Now:HH:mm:ss}] {message}");
                LogTextBox.Text = builder.ToString();
                LogTextBox.ScrollToEnd();
            });
        }

        private Task RefreshSteamIdInternalAsync()
        {
            try
            {
                SetBusy(true);
                _steamId64 = _launcher.GetActiveSteamUserInfo();
                SteamIdText.Text = string.IsNullOrWhiteSpace(_steamId64) ? "未获取到SteamID" : _steamId64;
                AppendLog(string.IsNullOrWhiteSpace(_steamId64) ? "未能获取 SteamID64，请确认已登录 Steam" : "已获取 SteamID64");
            }
            finally
            {
                SetBusy(false);
            }

            return Task.CompletedTask;
        }

        private async void RefreshSteamId(object sender, RoutedEventArgs e)
        {
            await RefreshSteamIdInternalAsync();
        }

        private void CopySteamId(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_steamId64))
            {
                AppendLog("没有可复制的 SteamID64");
                return;
            }

            Clipboard.SetText(_steamId64);
            AppendLog("SteamID64 已复制");
        }

        private async void LaunchAndConnect(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(ServerPortText.Text, out int port) || port <= 0 || port > 65535)
            {
                AppendLog("端口无效，请输入 1-65535之间的数字");
                return;
            }

            string ip = ServerIpText.Text.Trim();
            string password = ServerPasswordBox.Password.Trim();
            string customArgs = CustomLaunchArgsText.Text.Trim();

            _config.ServerIp = ip;
            _config.ServerPort = port;
            _config.ServerPassword = string.IsNullOrWhiteSpace(password) ? null : password;
            _config.CustomLaunchOptions = string.IsNullOrWhiteSpace(customArgs) ? null : customArgs;
            await SaveConfigAsync();

            SetBusy(true);
            try
            {
                // 只在“首次执行启动游戏命令”时，若 CS2 已运行则先结束进程再重启。
                if (!_config.HasExecutedFirstLaunchCommand)
                {
                    AppendLog("首次启动命令检测：检查 CS2 是否已在运行...");

                    if (_launcher.IsCs2Running())
                    {
                        AppendLog("检测到 CS2 正在运行，按首次策略先结束游戏后重新启动");

                        if (!_launcher.TryStopCs2IfRunning(out int stoppedCount, out string stopError))
                        {
                            AppendLog($"结束 CS2 失败: {stopError}");
                            AppendLog("首次策略未完成，本次已取消启动，请以管理员权限重试");
                            return;
                        }

                        AppendLog($"已结束 CS2 进程数量: {stoppedCount}");
                    }
                    else
                    {
                        AppendLog("首次启动命令检测：未发现正在运行的 CS2");
                    }
                }

                AppendLog("正在尝试启动 CS2...");
                var launchResult = await _launcher.LaunchAndConnectAsync(
                    ip,
                    port,
                    string.IsNullOrWhiteSpace(password) ? null : password,
                    string.IsNullOrWhiteSpace(customArgs) ? null : customArgs,
                    msg => AppendLog(msg));

                if (launchResult.firstLaunchCommandSent && !_config.HasExecutedFirstLaunchCommand)
                {
                    _config.HasExecutedFirstLaunchCommand = true;
                    await SaveConfigAsync();
                    AppendLog("已记录首次启动命令完成，后续将不再自动结束已运行的 CS2");
                }

                if (launchResult.overallSuccess)
                {
                    AppendLog($"已触发连接流程，开始监控 {ip}:{port} ...");
                    await _launcher.MonitorGameLaunchAsync(msg => AppendLog(msg));
                }
                else
                {
                    AppendLog("调用 Steam 连接失败，请确认已安装并登录 Steam");
                }
            }
            catch (Exception ex)
            {
                AppendLog($"连接失败: {ex.Message}");
            }
            finally
            {
                SetBusy(false);
            }
        }

        private async void StartSteam(object sender, RoutedEventArgs e)
        {
            SetBusy(true);
            try
            {
                if (_launcher.IsSteamRunning())
                {
                    AppendLog("Steam 已在运行");
                    return;
                }

                bool started = await _launcher.StartSteamAsync();
                AppendLog(started ? "已尝试启动 Steam" : "启动 Steam 失败，请检查安装路径");
            }
            finally
            {
                SetBusy(false);
            }
        }

        private async void OpenSkinSite(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_steamId64))
            {
                await RefreshSteamIdInternalAsync();
            }

            if (string.IsNullOrWhiteSpace(_steamId64))
            {
                AppendLog("未能获取 SteamID64，无法打开换肤网站");
                return;
            }

            try
            {
                var url = $"https://skin.akiyy.top/?steamid={_steamId64}";
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
                AppendLog("已在浏览器打开换肤网站");
            }
            catch (Exception ex)
            {
                AppendLog($"打开换肤网站失败: {ex.Message}");
            }
        }

        private async Task LoadConfigAsync()
        {
            try
            {
                _config = await CS2Launcher.LauncherConfigManager.LoadAsync();

                ServerIpText.Text = _config.ServerIp;
                ServerPortText.Text = _config.ServerPort.ToString();
                ServerPasswordBox.Password = _config.ServerPassword ?? string.Empty;
                CustomLaunchArgsText.Text = _config.CustomLaunchOptions ?? string.Empty;
            }
            catch (Exception ex)
            {
                AppendLog($"加载配置失败: {ex.Message}");
            }
        }

        private async Task CheckForUpdatesOnStartupAsync()
        {
            if (!_config.AutoCheckUpdates)
            {
                AppendLog("已关闭自动检查更新");
                return;
            }

            await CheckForUpdatesCoreAsync(isManualTrigger: false);
        }

        private async void CheckUpdates(object sender, RoutedEventArgs e)
        {
            await CheckForUpdatesCoreAsync(isManualTrigger: true);
        }

        private async Task CheckForUpdatesCoreAsync(bool isManualTrigger)
        {
            string manifestUrl = _config.UpdateManifestUrl?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(manifestUrl))
            {
                if (isManualTrigger)
                {
                    AppendLog("未配置更新清单地址，请在 config.json 中设置 UpdateManifestUrl");
                }

                return;
            }

            SetBusy(true);
            try
            {
                AppendLog(isManualTrigger ? "正在检查更新..." : "启动后自动检查更新...");

                UpdateCheckResult checkResult = await _updateService.CheckForUpdateAsync(manifestUrl, _config.UpdateChannel);
                if (!string.IsNullOrWhiteSpace(checkResult.ErrorMessage))
                {
                    AppendLog($"检查更新失败: {checkResult.ErrorMessage}");
                    return;
                }

                if (!checkResult.IsUpdateAvailable || checkResult.Manifest == null)
                {
                    if (isManualTrigger)
                    {
                        AppendLog($"当前已是最新版本 ({checkResult.CurrentVersionText})");
                    }

                    return;
                }

                if (!checkResult.Manifest.Mandatory &&
                    !string.IsNullOrWhiteSpace(_config.SkippedVersion) &&
                    string.Equals(_config.SkippedVersion, checkResult.LatestVersionText, StringComparison.OrdinalIgnoreCase))
                {
                    AppendLog($"已跳过版本 {checkResult.LatestVersionText}，本次不再提示");
                    return;
                }

                AppendLog($"发现新版本: {checkResult.LatestVersionText} (当前 {checkResult.CurrentVersionText})");
                await PromptAndInstallUpdateAsync(checkResult.Manifest, checkResult.CurrentVersionText, checkResult.LatestVersionText);
            }
            finally
            {
                SetBusy(false);
            }
        }

        private async Task PromptAndInstallUpdateAsync(UpdateManifest manifest, string currentVersion, string latestVersion)
        {
            string notes = string.IsNullOrWhiteSpace(manifest.ReleaseNotes) ? "无" : manifest.ReleaseNotes.Trim();
            string message =
                $"检测到新版本: {latestVersion}\n" +
                $"当前版本: {currentVersion}\n\n" +
                $"更新说明:\n{notes}\n\n";

            MessageBoxResult decision;
            if (manifest.Mandatory)
            {
                decision = MessageBox.Show(
                    message + "该版本为强制更新，是否现在更新？",
                    "发现更新",
                    MessageBoxButton.OKCancel,
                    MessageBoxImage.Information);

                if (decision != MessageBoxResult.OK)
                {
                    AppendLog("用户取消了强制更新，可能影响后续使用");
                    return;
                }
            }
            else
            {
                decision = MessageBox.Show(
                    message + "选择“是”立即更新，选择“否”跳过此版本。",
                    "发现更新",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Information);

                if (decision == MessageBoxResult.No)
                {
                    _config.SkippedVersion = latestVersion;
                    await SaveConfigAsync();
                    AppendLog($"已跳过版本 {latestVersion}");
                    return;
                }

                if (decision != MessageBoxResult.Yes)
                {
                    AppendLog("已取消更新");
                    return;
                }
            }

            _config.SkippedVersion = null;
            await SaveConfigAsync();

            AppendLog("开始下载更新包...");
            string packagePath = _updateService.BuildDefaultPackagePath(manifest);
            UpdateDownloadResult downloadResult = await _updateService.DownloadPackageAsync(manifest, packagePath);

            if (!downloadResult.Success || string.IsNullOrWhiteSpace(downloadResult.PackageFilePath))
            {
                AppendLog($"下载更新失败: {downloadResult.ErrorMessage}");
                return;
            }

            AppendLog($"更新包下载完成: {downloadResult.PackageFilePath}");

            string updaterPath = UpdateService.GetDefaultUpdaterPath();
            if (_updateService.TryStartUpdater(updaterPath, downloadResult.PackageFilePath, out string startError))
            {
                AppendLog("已启动 Updater，程序即将退出进行更新");
                Application.Current.Shutdown();
                return;
            }

            AppendLog($"启动 Updater 失败: {startError}");
            AppendLog("未检测到 Updater.exe，将尝试在浏览器打开下载链接");

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = manifest.PackageUrl,
                    UseShellExecute = true
                });

                AppendLog("已在浏览器打开更新包下载地址");
            }
            catch (Exception ex)
            {
                AppendLog($"打开下载地址失败: {ex.Message}");
            }
        }

        private async Task SaveConfigAsync()
        {
            try
            {
                await CS2Launcher.LauncherConfigManager.SaveAsync(_config);
            }
            catch (Exception ex)
            {
                AppendLog($"保存配置失败: {ex.Message}");
            }
        }

    }
}
