using System.Diagnostics;
using System.IO.Compression;

namespace CS2Launcher.Updater;

internal static class Program
{
    private static readonly object LogSync = new object();
    private static string _logFilePath = Path.Combine(Path.GetTempPath(), "CS2Launcher", "updater", "logs", "updater.log");

    private static async Task<int> Main(string[] args)
    {
        try
        {
            var options = ParseArgs(args);
            InitializeLogging(options.TargetDir);

            if (!options.IsValid(out string validationError))
            {
                Log($"参数错误: {validationError}");
                return 2;
            }

            Log("启动更新流程...");
            await WaitForTargetProcessExitAsync(options.ProcessId, timeoutMs: 60000);

            if (!File.Exists(options.PackagePath))
            {
                Log($"更新包不存在: {options.PackagePath}");
                return 3;
            }

            if (!Directory.Exists(options.TargetDir))
            {
                Log($"目标目录不存在: {options.TargetDir}");
                return 4;
            }

            string packageExtension = Path.GetExtension(options.PackagePath);
            if (!string.Equals(packageExtension, ".zip", StringComparison.OrdinalIgnoreCase))
            {
                Log("当前仅支持 zip 更新包。");
                return 5;
            }

            string extractDir = CreateTempExtractDirectory();
            try
            {
                Log("正在解压更新包...");
                ZipFile.ExtractToDirectory(options.PackagePath, extractDir, overwriteFiles: true);

                string payloadRoot = ResolvePayloadRoot(extractDir, options.ExeName);
                Log($"检测到更新内容根目录: {payloadRoot}");

                Log("正在替换目标文件...");
                CopyDirectoryWithRetry(payloadRoot, options.TargetDir);
            }
            finally
            {
                TryDeleteDirectory(extractDir);
            }

            string targetExePath = Path.Combine(options.TargetDir, options.ExeName);
            if (!File.Exists(targetExePath))
            {
                Log($"未找到主程序: {targetExePath}");
                return 6;
            }

            Log("更新完成，正在重启主程序...");
            Process.Start(new ProcessStartInfo
            {
                FileName = targetExePath,
                WorkingDirectory = options.TargetDir,
                UseShellExecute = true
            });

            Log("主程序已拉起，Updater 即将退出。");
            return 0;
        }
        catch (Exception ex)
        {
            Log($"更新失败: {ex.Message}", ex);
            return 1;
        }
    }

    private static void InitializeLogging(string targetDir)
    {
        try
        {
            string logDir;
            if (!string.IsNullOrWhiteSpace(targetDir) && Directory.Exists(targetDir))
            {
                logDir = Path.Combine(targetDir, "logs");
            }
            else
            {
                logDir = Path.Combine(Path.GetTempPath(), "CS2Launcher", "updater", "logs");
            }

            Directory.CreateDirectory(logDir);
            _logFilePath = Path.Combine(logDir, $"updater-{DateTime.Now:yyyyMMdd-HHmmss}.log");
            Log($"日志初始化完成: {_logFilePath}");
        }
        catch
        {
            // If logging path setup fails, keep fallback path.
        }
    }

    private static void Log(string message, Exception? ex = null)
    {
        string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [Updater] {message}";
        Console.WriteLine(line);

        if (ex != null)
        {
            Console.WriteLine(ex.ToString());
        }

        try
        {
            lock (LogSync)
            {
                string? dir = Path.GetDirectoryName(_logFilePath);
                if (!string.IsNullOrWhiteSpace(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                File.AppendAllText(_logFilePath, line + Environment.NewLine);
                if (ex != null)
                {
                    File.AppendAllText(_logFilePath, ex + Environment.NewLine);
                }
            }
        }
        catch
        {
            // Best-effort logging.
        }
    }

    private static async Task WaitForTargetProcessExitAsync(int processId, int timeoutMs)
    {
        if (processId <= 0)
        {
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.ElapsedMilliseconds < timeoutMs)
        {
            try
            {
                using Process process = Process.GetProcessById(processId);
                if (process.HasExited)
                {
                    return;
                }
            }
            catch (ArgumentException)
            {
                // Process no longer exists.
                return;
            }

            await Task.Delay(500);
        }

        throw new TimeoutException("等待主程序退出超时。");
    }

    private static string CreateTempExtractDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "CS2Launcher", "updater", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static string ResolvePayloadRoot(string extractDir, string exeName)
    {
        string current = extractDir;

        for (int depth = 0; depth < 5; depth++)
        {
            if (File.Exists(Path.Combine(current, exeName)))
            {
                return current;
            }

            string[] files = Directory.GetFiles(current);
            string[] dirs = Directory.GetDirectories(current);

            if (dirs.Length == 1)
            {
                string onlyDir = dirs[0];

                if (files.Length == 0 || File.Exists(Path.Combine(onlyDir, exeName)))
                {
                    current = onlyDir;
                    continue;
                }
            }

            break;
        }

        return current;
    }

    private static void CopyDirectoryWithRetry(string sourceDir, string targetDir)
    {
        int copied = 0;

        foreach (string sourcePath in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            string relativePath = Path.GetRelativePath(sourceDir, sourcePath);

            // Preserve local runtime config generated by user usage.
            if (string.Equals(relativePath, "config.json", StringComparison.OrdinalIgnoreCase) &&
                File.Exists(Path.Combine(targetDir, relativePath)))
            {
                continue;
            }

            string targetPath = Path.Combine(targetDir, relativePath);
            string? parent = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrWhiteSpace(parent))
            {
                Directory.CreateDirectory(parent);
            }

            CopyFileWithRetry(sourcePath, targetPath, retryCount: 10, delayMs: 300);
            copied++;
        }

        Log($"文件替换完成，已复制 {copied} 个文件。");
    }

    private static void CopyFileWithRetry(string sourcePath, string targetPath, int retryCount, int delayMs)
    {
        for (int i = 0; i < retryCount; i++)
        {
            try
            {
                File.Copy(sourcePath, targetPath, overwrite: true);
                return;
            }
            catch (IOException) when (i < retryCount - 1)
            {
                Thread.Sleep(delayMs);
            }
            catch (UnauthorizedAccessException) when (i < retryCount - 1)
            {
                Thread.Sleep(delayMs);
            }
        }

        File.Copy(sourcePath, targetPath, overwrite: true);
    }

    private static void TryDeleteDirectory(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup.
        }
    }

    private static Options ParseArgs(string[] args)
    {
        var options = new Options();

        for (int i = 0; i < args.Length; i++)
        {
            string current = args[i];
            if (!current.StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            string key = current.Substring(2);
            string value = i + 1 < args.Length ? args[++i] : string.Empty;

            switch (key)
            {
                case "package":
                    options.PackagePath = value;
                    break;
                case "targetDir":
                    options.TargetDir = value;
                    break;
                case "processId":
                    if (int.TryParse(value, out int pid))
                    {
                        options.ProcessId = pid;
                    }

                    break;
                case "exeName":
                    options.ExeName = value;
                    break;
            }
        }

        return options;
    }

    private sealed class Options
    {
        public string PackagePath { get; set; } = string.Empty;
        public string TargetDir { get; set; } = string.Empty;
        public int ProcessId { get; set; }
        public string ExeName { get; set; } = string.Empty;

        public bool IsValid(out string error)
        {
            if (string.IsNullOrWhiteSpace(PackagePath))
            {
                error = "缺少 --package";
                return false;
            }

            if (string.IsNullOrWhiteSpace(TargetDir))
            {
                error = "缺少 --targetDir";
                return false;
            }

            if (ProcessId <= 0)
            {
                error = "缺少或无效 --processId";
                return false;
            }

            if (string.IsNullOrWhiteSpace(ExeName))
            {
                error = "缺少 --exeName";
                return false;
            }

            error = string.Empty;
            return true;
        }
    }
}
