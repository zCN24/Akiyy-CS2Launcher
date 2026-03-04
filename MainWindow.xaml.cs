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
        private string _steamId64 = string.Empty;
        private bool _isBusy;

        public MainWindow()
        {
            InitializeComponent();
            Loaded += async (_, _) => await RefreshSteamIdInternalAsync();
        }

        private void SetBusy(bool isBusy)
        {
            _isBusy = isBusy;
            LaunchButton.IsEnabled = !isBusy;
            StartSteamButton.IsEnabled = !isBusy;
            RefreshSteamIdButton.IsEnabled = !isBusy;
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
                AppendLog(string.IsNullOrWhiteSpace(_steamId64) ? "未能获取 SteamID64，请确认已登录 Steam。" : "已获取 SteamID64。");
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
                AppendLog("没有可复制的 SteamID64。");
                return;
            }

            Clipboard.SetText(_steamId64);
            AppendLog("SteamID64 已复制。");
        }

        private async void LaunchAndConnect(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(ServerPortText.Text, out int port) || port <= 0 || port > 65535)
            {
                AppendLog("端口无效，请输入 1-65535。");
                return;
            }

            string ip = ServerIpText.Text.Trim();
            string password = ServerPasswordBox.Password.Trim();

            SetBusy(true);
            try
            {
                AppendLog("正在尝试连接服务器...");
                bool ok = await _launcher.LaunchAndConnectAsync(ip, port, string.IsNullOrWhiteSpace(password) ? null : password);

                if (ok)
                {
                    AppendLog($"已调用 Steam 连接 {ip}:{port}，开始监控启动...");
                    await _launcher.MonitorGameLaunchAsync(msg => AppendLog(msg));
                }
                else
                {
                    AppendLog("调用 Steam 连接失败，请确认已安装并登录 Steam。");
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
                    AppendLog("Steam 已在运行。");
                    return;
                }

                bool started = await _launcher.StartSteamAsync();
                AppendLog(started ? "已尝试启动 Steam。" : "启动 Steam 失败，请检查安装路径。");
            }
            finally
            {
                SetBusy(false);
            }
        }

    }
}
