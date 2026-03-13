using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using OpenCvSharp;

namespace TaskFlow.Services
{
    public interface IAdbService
    {
        string AdbPath { get; }
        Task<(bool Success, string Message)> ConnectAsync(string ip, int port);
        Task<(bool Success, string Message)> LaunchAppAsync(string serial, string packageName, string activityName);
        Task<(bool Success, string Message)> ForceStopAppAsync(string serial, string packageName);
        Task<(bool Success, Mat? Image)> ScreenshotAsync(string serial);
        Task<(bool Success, string Message)> ClickAsync(string serial, int x, int y);
        Task<(bool Success, string Message)> DoubleClickAsync(string serial, int x, int y);
        Task<(bool Success, string Message)> SwipeAsync(string serial, int x1, int y1, int x2, int y2, int durationMs);
        Task<(bool Success, string Message)> DisconnectAsync(string serial);
        Task<string[]> GetConnectedDevicesAsync();
    }

    public class AdbService : IAdbService
    {
        public string AdbPath { get; }

        public AdbService()
        {
            // 使用bin目录下的adb
            var basePath = AppDomain.CurrentDomain.BaseDirectory;
            AdbPath = Path.Combine(basePath, "platform-tools", "adb.exe");
        }

        private async Task<(int ExitCode, string Output, string Error)> ExecuteAdbCommandAsync(string arguments)
        {
            if (!File.Exists(AdbPath))
            {
                return (-1, "", $"ADB not found at: {AdbPath}");
            }

            var psi = new ProcessStartInfo
            {
                FileName = AdbPath,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            using var process = new Process { StartInfo = psi };
            var output = new StringBuilder();
            var error = new StringBuilder();

            process.OutputDataReceived += (s, e) => { if (e.Data != null) output.AppendLine(e.Data); };
            process.ErrorDataReceived += (s, e) => { if (e.Data != null) error.AppendLine(e.Data); };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync();

            return (process.ExitCode, output.ToString().Trim(), error.ToString().Trim());
        }

        public async Task<(bool Success, string Message)> ConnectAsync(string ip, int port)
        {
            var result = await ExecuteAdbCommandAsync($"connect {ip}:{port}");
            var success = result.ExitCode == 0 &&
                         (result.Output.Contains("connected") || result.Output.Contains("already connected"));
            return (success, success ? result.Output : result.Error);
        }

        public async Task<(bool Success, string Message)> LaunchAppAsync(string serial, string packageName, string activityName)
        {
            var args = string.IsNullOrEmpty(serial)
                ? $"shell am start -n {packageName}/{activityName}"
                : $"-s {serial} shell am start -n {packageName}/{activityName}";

            var result = await ExecuteAdbCommandAsync(args);
            var success = result.ExitCode == 0 && !result.Output.Contains("Error");
            return (success, success ? result.Output : result.Error);
        }

        public async Task<(bool Success, string Message)> ForceStopAppAsync(string serial, string packageName)
        {
            var args = string.IsNullOrEmpty(serial)
                ? $"shell am force-stop {packageName}"
                : $"-s {serial} shell am force-stop {packageName}";

            var result = await ExecuteAdbCommandAsync(args);
            var success = result.ExitCode == 0;
            return (success, success ? "应用已关闭" : result.Error);
        }

        public async Task<(bool Success, Mat? Image)> ScreenshotAsync(string serial)
        {
            try
            {
                var args = string.IsNullOrEmpty(serial)
                    ? "exec-out screencap -p"
                    : $"-s {serial} exec-out screencap -p";

                if (!File.Exists(AdbPath))
                {
                    return (false, null);
                }

                var psi = new ProcessStartInfo
                {
                    FileName = AdbPath,
                    Arguments = args,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = new Process { StartInfo = psi };
                process.Start();

                using var ms = new MemoryStream();
                await process.StandardOutput.BaseStream.CopyToAsync(ms);
                await process.WaitForExitAsync();

                if (process.ExitCode != 0 || ms.Length == 0)
                {
                    return (false, null);
                }

                var imageData = ms.ToArray();
                var mat = Cv2.ImDecode(imageData, ImreadModes.Color);

                if (mat.Empty())
                {
                    return (false, null);
                }

                return (true, mat);
            }
            catch (Exception)
            {
                return (false, null);
            }
        }

        public async Task<(bool Success, string Message)> ClickAsync(string serial, int x, int y)
        {
            var args = string.IsNullOrEmpty(serial)
                ? $"shell input tap {x} {y}"
                : $"-s {serial} shell input tap {x} {y}";

            var result = await ExecuteAdbCommandAsync(args);
            return (result.ExitCode == 0, result.ExitCode == 0 ? "点击成功" : result.Error);
        }

        public async Task<(bool Success, string Message)> DoubleClickAsync(string serial, int x, int y)
        {
            await ClickAsync(serial, x, y);
            await Task.Delay(100);
            return await ClickAsync(serial, x, y);
        }

        public async Task<(bool Success, string Message)> SwipeAsync(string serial, int x1, int y1, int x2, int y2, int durationMs)
        {
            var args = string.IsNullOrEmpty(serial)
                ? $"shell input swipe {x1} {y1} {x2} {y2} {durationMs}"
                : $"-s {serial} shell input swipe {x1} {y1} {x2} {y2} {durationMs}";

            var result = await ExecuteAdbCommandAsync(args);
            return (result.ExitCode == 0, result.ExitCode == 0 ? "滑动成功" : result.Error);
        }

        public async Task<(bool Success, string Message)> DisconnectAsync(string serial)
        {
            if (string.IsNullOrEmpty(serial))
            {
                return (false, "设备序列号为空");
            }

            var result = await ExecuteAdbCommandAsync($"disconnect {serial}");
            var success = result.ExitCode == 0;
            return (success, success ? $"已断开设备: {serial}" : result.Error);
        }

        public async Task<string[]> GetConnectedDevicesAsync()
        {
            var result = await ExecuteAdbCommandAsync("devices");
            if (result.ExitCode != 0)
            {
                return Array.Empty<string>();
            }

            var lines = result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            var devices = new System.Collections.Generic.List<string>();

            foreach (var line in lines)
            {
                if (line.StartsWith("List of devices") || string.IsNullOrWhiteSpace(line))
                    continue;

                var parts = line.Split('\t');
                if (parts.Length >= 2 && parts[1].Trim() == "device")
                {
                    devices.Add(parts[0].Trim());
                }
            }

            return devices.ToArray();
        }
    }
}
