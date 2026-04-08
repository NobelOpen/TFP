using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace TaskFlow.Services
{
    public class TextractorService : IDisposable
    {
        private static readonly Lazy<TextractorService> _instance = new Lazy<TextractorService>(() => new TextractorService());
        public static TextractorService Instance => _instance.Value;

        private const string TextractorUrl = "https://github.com/Artikash/Textractor/releases/download/v5.2.0/Textractor-5.2.0-Zip-Version-English-Only.zip";
        private readonly string _textractorDir;
        private Process? _textractorProcess;
        private bool _isInitialized;

        public event Action<string>? OnTextReceived;
        public event Action<string>? OnConsoleOutput;

        private TextractorService()
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            _textractorDir = Path.Combine(localAppData, "TaskFlow", "Textractor");
        }

        public async Task EnsureTextractorAsync(Action<string>? progressCallback = null)
        {
            if (_isInitialized) return;

            var cliPath = Path.Combine(_textractorDir, "Textractor", "x86", "TextractorCLI.exe");
            if (!File.Exists(cliPath))
            {
                progressCallback?.Invoke("Downloading Textractor from GitHub (v5.2.0)...");
                try
                {
                    if (Directory.Exists(_textractorDir))
                    {
                        Directory.Delete(_textractorDir, true);
                    }
                    Directory.CreateDirectory(_textractorDir);

                    var zipPath = Path.Combine(_textractorDir, "Textractor.zip");
                    using (var client = new HttpClient())
                    {
                        using (var response = await client.GetAsync(TextractorUrl, HttpCompletionOption.ResponseHeadersRead))
                        {
                            response.EnsureSuccessStatusCode();
                            using (var fs = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None))
                            {
                                await response.Content.CopyToAsync(fs);
                            }
                        }
                    }

                    progressCallback?.Invoke("Extracting Textractor...");
                    ZipFile.ExtractToDirectory(zipPath, _textractorDir);
                    File.Delete(zipPath);
                    progressCallback?.Invoke("Textractor downloaded and extracted.");
                }
                catch (Exception ex)
                {
                    progressCallback?.Invoke($"Failed to download Textractor: {ex.Message}");
                    throw new Exception("Failed to download Textractor", ex);
                }
            }

            _isInitialized = true;
        }

        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true, CallingConvention = System.Runtime.InteropServices.CallingConvention.Winapi)]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        private static extern bool IsWow64Process(
            [System.Runtime.InteropServices.In] IntPtr process,
            [System.Runtime.InteropServices.Out] out bool wow64Process);

        private static bool Is64Bit(Process process)
        {
            if (!Environment.Is64BitOperatingSystem) return false;
            try
            {
                if (!IsWow64Process(process.Handle, out bool isWow64))
                    return false;
                return !isWow64; 
            }
            catch
            {
                return false;
            }
        }

        public async Task StartAsync(int processId)
        {
            if (!_isInitialized)
            {
                await EnsureTextractorAsync();
            }

            Stop();

            var targetProcess = Process.GetProcessById(processId);
            bool is64 = Is64Bit(targetProcess);
            string archDir = is64 ? "x64" : "x86";

            var cliPath = Path.Combine(_textractorDir, "Textractor", archDir, "TextractorCLI.exe");

            var startInfo = new ProcessStartInfo
            {
                FileName = cliPath,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.Unicode,
                StandardErrorEncoding = Encoding.Unicode
            };

            _textractorProcess = new Process { StartInfo = startInfo };
            
            _textractorProcess.OutputDataReceived += (s, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    ParseLine(e.Data);
                }
            };

            _textractorProcess.ErrorDataReceived += (s, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    OnConsoleOutput?.Invoke($"[ERROR] {e.Data}");
                }
            };

            _textractorProcess.Start();
            _textractorProcess.BeginOutputReadLine();
            _textractorProcess.BeginErrorReadLine();

            // Send attach command
            await _textractorProcess.StandardInput.WriteAsync($"attach -P{processId}\n");
            await _textractorProcess.StandardInput.FlushAsync();
        }

        private void ParseLine(string line)
        {
            // The format from TextractorCLI is usually:
            // [threadNum:address:ctx:ctx2:name:code] text
            // Or just console output without brackets.

            if (line.StartsWith("["))
            {
                int endBracket = line.IndexOf("] ");
                if (endBracket != -1)
                {
                    var header = line.Substring(1, endBracket - 1);
                    var text = line.Substring(endBracket + 2);
                    
                    if (header.Contains("Console"))
                    {
                        OnConsoleOutput?.Invoke(text);
                    }
                    else if (header.Contains("Clipboard"))
                    {
                        // Ignore clipboard from textractor since we have our own, or we can pipe it.
                        // Usually CLI also outputs clipboard events if enabled.
                    }
                    else
                    {
                        // It's actual extracted text from a hook
                        OnTextReceived?.Invoke(text);
                    }
                    return;
                }
            }

            // Fallback for unstructured text
            OnConsoleOutput?.Invoke(line);
        }

        public void Stop()
        {
            if (_textractorProcess != null)
            {
                try
                {
                    if (!_textractorProcess.HasExited)
                    {
                        _textractorProcess.StandardInput.WriteLine("detach");
                        _textractorProcess.Kill();
                    }
                }
                catch { }
                finally
                {
                    _textractorProcess.Dispose();
                    _textractorProcess = null;
                }
            }
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
