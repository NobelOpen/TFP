using System;
using TaskFlow.Resources;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using OpenCvSharp;
using TaskFlow.Models;

namespace TaskFlow.Services
{
    /// <summary>
    /// 微信 OCR 服务，通过 wcocr.dll 调用微信本地 OCR 能力
    /// </summary>
    public class WeChatOcrService : IDisposable
    {
        // P/Invoke 委托
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void SetResultDelegate(IntPtr result);

        [DllImport("wcocr.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern bool wechat_ocr(
            [MarshalAs(UnmanagedType.LPWStr)] string ocr_exe,
            [MarshalAs(UnmanagedType.LPWStr)] string wechat_dir,
            byte[] imgfn,
            SetResultDelegate set_res);

        [DllImport("wcocr.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern void stop_ocr();

        private readonly AppSettings _settings;
        private bool _disposed;

        // 全局静态锁和回调委托，防止委托被垃圾回收导致非托管代码崩溃（AccessViolationException）
        private static readonly object _ocrLock = new object();
        private static string _currentOcrResult = string.Empty;
        private static readonly SetResultDelegate _staticCallback = (IntPtr ptr) =>
        {
            if (ptr == IntPtr.Zero) return;
            try
            {
                int length = 0;
                while (Marshal.ReadByte(ptr, length) != 0) length++;
                byte[] byteArray = new byte[length];
                Marshal.Copy(ptr, byteArray, 0, length);
                _currentOcrResult = Encoding.UTF8.GetString(byteArray);
            }
            catch { }
        };

        public WeChatOcrService(AppSettings settings)
        {
            _settings = settings;
        }

        /// <summary>
        /// 微信 OCR 是否可用（路径已配置且已通过验证）
        /// </summary>
        public bool IsAvailable =>
            _settings.WeChatOcrVerified
            && !string.IsNullOrEmpty(_settings.WeChatOcrExePath)
            && !string.IsNullOrEmpty(_settings.WeChatOcrDirPath);

        /// <summary>
        /// 执行 OCR 识别
        /// </summary>
        public async Task<(bool Success, string Text, System.Collections.Generic.List<TaskFlow.Models.TaskCards.OcrResultItem>? Items, string? Error)> RecognizeAsync(Mat image)
        {
            return await Task.Run(() =>
            {
                if (!IsAvailable)
                    return (false, string.Empty, null, "微信 OCR 未配置或未通过验证，请先在设置中配置");

                if (image == null || image.Empty())
                    return (false, string.Empty, null, "输入图像为空");

                // 创建临时 PNG 文件
                string tempPath = Path.Combine(Path.GetTempPath(), $"taskflow_ocr_{Guid.NewGuid():N}.png");

                try
                {
                    // 保存 Mat 为临时文件
                    Cv2.ImWrite(tempPath, image);

                    string ocrResult = string.Empty;
                    bool success = false;

                    lock (_ocrLock)
                    {
                        _currentOcrResult = string.Empty;
                        success = wechat_ocr(
                            _settings.WeChatOcrExePath!,
                            _settings.WeChatOcrDirPath!,
                            Encoding.UTF8.GetBytes(tempPath + "\0"),
                            _staticCallback);
                        
                        ocrResult = _currentOcrResult;
                    }

                    if (!success)
                        return (false, string.Empty, null, "微信 OCR 调用失败");

                    // 解析结果文本
                    var parseResult = ParseOcrResult(ocrResult);
                    return (true, parseResult.Text, parseResult.Items, (string?)null);
                }
                catch (DllNotFoundException)
                {
                    return (false, string.Empty, null, "未找到 wcocr.dll，请确保该文件在应用程序目录下");
                }
                catch (Exception ex)
                {
                    return (false, string.Empty, null, $"微信 OCR 出错: {ex.Message}");
                }
                finally
                {
                    // 清理临时文件
                    try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
                }
            });
        }

        /// <summary>
        /// 测试微信 OCR 是否可用（用于设置页面的测试按钮）
        /// </summary>
        public async Task<(bool Success, string Message)> TestAsync(string ocrExePath, string ocrDirPath)
        {
            return await Task.Run(() =>
            {
                // 创建一张包含测试文字的简单图片
                string tempPath = Path.Combine(Path.GetTempPath(), $"taskflow_ocr_test_{Guid.NewGuid():N}.png");

                try
                {
                    // 创建一张白色背景、含黑色文字的测试图片
                    using var testImage = new Mat(100, 300, MatType.CV_8UC3, new Scalar(255, 255, 255));
                    Cv2.PutText(testImage, "OCR Test 123", new OpenCvSharp.Point(20, 60),
                        HersheyFonts.HersheySimplex, 1.0, new Scalar(0, 0, 0), 2);
                    Cv2.ImWrite(tempPath, testImage);

                    // 调用微信 OCR
                    string ocrResult = string.Empty;
                    bool success = false;

                    lock (_ocrLock)
                    {
                        _currentOcrResult = string.Empty;
                        success = wechat_ocr(ocrExePath, ocrDirPath,
                            Encoding.UTF8.GetBytes(tempPath + "\0"), _staticCallback);
                            
                        ocrResult = _currentOcrResult;
                    }

                    if (success && !string.IsNullOrEmpty(ocrResult))
                    {
                        var parseResult = ParseOcrResult(ocrResult);
                        return (true, $"测试成功！识别结果: {parseResult.Text}");
                    }

                    return (false, "测试失败：微信 OCR 未返回结果");
                }
                catch (DllNotFoundException)
                {
                    return (false, "测试失败：未找到 wcocr.dll");
                }
                catch (Exception ex)
                {
                    return (false, string.Format(Strings.Svc_TestFailedException, ex.Message));
                }
                finally
                {
                    try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
                }
            });
        }

        /// <summary>
        /// 自动探测微信 OCR 路径
        /// </summary>
        public static (string? ocrExePath, string? ocrDirPath) AutoDetectPaths()
        {
            string? ocrExePath = null;
            string? ocrDirPath = null;

            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

            // 微信 4.0+ (xwechat)
            string xwechatPluginDir = Path.Combine(appData, "Tencent", "xwechat", "XPlugin", "plugins", "WeChatOcr");
            if (Directory.Exists(xwechatPluginDir))
            {
                // 查找最新版本的 wxocr.dll
                foreach (var versionDir in Directory.GetDirectories(xwechatPluginDir)
                    .OrderByDescending(d => d))
                {
                    string extractedDir = Path.Combine(versionDir, "extracted");
                    string wxocrPath = Path.Combine(extractedDir, "wxocr.dll");
                    if (File.Exists(wxocrPath))
                    {
                        ocrExePath = wxocrPath;
                        break;
                    }
                }

                // 微信 4.0 运行时目录
                string[] weixinDirs = {
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Tencent", "Weixin"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Tencent", "Weixin")
                };
                foreach (var baseDir in weixinDirs)
                {
                    if (!Directory.Exists(baseDir)) continue;
                    // 查找最新版本目录
                    foreach (var dir in Directory.GetDirectories(baseDir)
                        .OrderByDescending(d => d))
                    {
                        ocrDirPath = dir;
                        break;
                    }
                    if (ocrDirPath != null) break;
                }
            }

            // 如果 4.0 没找到，尝试 3.x
            if (ocrExePath == null)
            {
                string wechatPluginDir = Path.Combine(appData, "Tencent", "WeChat", "XPlugin", "Plugins", "WeChatOCR");
                if (Directory.Exists(wechatPluginDir))
                {
                    foreach (var versionDir in Directory.GetDirectories(wechatPluginDir)
                        .OrderByDescending(d => d))
                    {
                        string extractedDir = Path.Combine(versionDir, "extracted");
                        string ocrExe = Path.Combine(extractedDir, "WeChatOCR.exe");
                        if (File.Exists(ocrExe))
                        {
                            ocrExePath = ocrExe;
                            break;
                        }
                    }

                    // 微信 3.x 运行时目录
                    string[] wechatDirs = {
                        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Tencent", "WeChat"),
                        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Tencent", "WeChat")
                    };
                    foreach (var baseDir in wechatDirs)
                    {
                        if (!Directory.Exists(baseDir)) continue;
                        foreach (var dir in Directory.GetDirectories(baseDir)
                            .Where(d => !d.EndsWith("XPlugin", StringComparison.OrdinalIgnoreCase))
                            .OrderByDescending(d => d))
                        {
                            ocrDirPath = dir;
                            break;
                        }
                        if (ocrDirPath != null) break;
                    }
                }
            }

            return (ocrExePath, ocrDirPath);
        }

        /// <summary>
        /// 解析微信 OCR 返回的 JSON 结果，提取纯文本和坐标数据
        /// </summary>
        private static (string Text, System.Collections.Generic.List<TaskFlow.Models.TaskCards.OcrResultItem> Items) ParseOcrResult(string jsonResult)
        {
            var itemsList = new System.Collections.Generic.List<TaskFlow.Models.TaskCards.OcrResultItem>();
            if (string.IsNullOrEmpty(jsonResult))
                return (string.Empty, itemsList);

            try
            {
                // 微信 OCR 返回 JSON 格式，包含 ocrResult 数组
                // 每个元素有 text 字段
                using var doc = System.Text.Json.JsonDocument.Parse(jsonResult);
                var sb = new StringBuilder();

                if (doc.RootElement.TryGetProperty("ocr_response", out var ocrArray)
                    && ocrArray.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    foreach (var item in ocrArray.EnumerateArray())
                    {
                        if (item.TryGetProperty("text", out var textProp))
                        {
                            if (sb.Length > 0) sb.Append('\n');
                            var lineText = textProp.GetString() ?? string.Empty;
                            sb.Append(lineText);
                            
                            int left = 0, top = 0, right = 0, bottom = 0;
                            double rate = 1.0;
                            
                            if (item.TryGetProperty("left", out var l)) left = l.ValueKind == System.Text.Json.JsonValueKind.Number ? l.GetInt32() : (int)l.GetDouble();
                            if (item.TryGetProperty("top", out var t)) top = t.ValueKind == System.Text.Json.JsonValueKind.Number ? t.GetInt32() : (int)t.GetDouble();
                            if (item.TryGetProperty("right", out var r)) right = r.ValueKind == System.Text.Json.JsonValueKind.Number ? r.GetInt32() : (int)r.GetDouble();
                            if (item.TryGetProperty("bottom", out var b)) bottom = b.ValueKind == System.Text.Json.JsonValueKind.Number ? b.GetInt32() : (int)b.GetDouble();
                            if (item.TryGetProperty("rate", out var rt)) rate = rt.ValueKind == System.Text.Json.JsonValueKind.Number ? rt.GetDouble() : 1.0;
                            
                            int w = Math.Abs(right - left);
                            int h = Math.Abs(bottom - top);
                            
                            itemsList.Add(new TaskFlow.Models.TaskCards.OcrResultItem 
                            {
                                Text = lineText,
                                X = left + w / 2,
                                Y = top + h / 2,
                                Width = w,
                                Height = h,
                                Confidence = rate
                            });
                        }
                    }
                    // ocr_response 已成功解析，返回拼接文本和结构体
                    return (sb.ToString(), itemsList);
                }

                // JSON 格式不符合预期，返回原始内容
                return (jsonResult, itemsList);
            }
            catch
            {
                // 如果 JSON 解析失败，直接返回原始文本
                return (jsonResult, itemsList);
            }
        }

        /// <summary>
        /// 静态方法：停止微信 OCR 后台进程（用于应用退出时调用）
        /// </summary>
        public static void Shutdown()
        {
            try
            {
                stop_ocr();
            }
            catch { }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            // 不要在这里调用 stop_ocr()。如果在验证设置后立刻调用 stop_ocr() 销毁进程，
            // 之后用户通过卡片再次发起 wechat_ocr() 请求时，动态库可能会遇到 IPC 状态损坏或双重释放，
            // 进而过几秒后在后台抛出访问违例导致整个程序无报错硬闪退。
            // 真正的清理应该放在应用退出时（Shutdown()）。
        }
    }
}
