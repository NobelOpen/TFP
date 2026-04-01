using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using TaskFlow.Helpers;

namespace TaskFlow.Services
{
    /// <summary>
    /// 文件系统服务：为 Orchid AI 提供原生文件读取、目录遍历和文本搜索能力。
    /// 所有操作均为只读、零风险，无需用户审批。
    /// 安全策略：禁止访问系统关键目录，输出结果自动截断防止 Token 爆炸。
    /// </summary>
    public class FileSystemService
    {
        /// <summary>
        /// 单次返回结果最大字符数（防止 Token 爆炸）
        /// </summary>
        private const int MaxResultLength = 8000;

        /// <summary>
        /// 目录遍历最大深度
        /// </summary>
        private const int MaxRecursiveDepth = 3;

        /// <summary>
        /// 搜索结果最大匹配数
        /// </summary>
        private const int MaxSearchMatches = 50;

        /// <summary>
        /// 禁止访问的系统目录前缀（小写，Windows 路径）
        /// </summary>
        private static readonly string[] ForbiddenPaths =
        {
            @"c:\windows",
            @"c:\program files\windowsapps",
            @"c:\$recycle.bin",
        };

        /// <summary>
        /// 路径安全校验：禁止读取系统关键目录
        /// </summary>
        private static bool IsPathAllowed(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;

            try
            {
                var fullPath = Path.GetFullPath(path).ToLower().TrimEnd('\\');
                foreach (var forbidden in ForbiddenPaths)
                {
                    if (fullPath.StartsWith(forbidden))
                        return false;
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 截断过长的结果文本
        /// </summary>
        private static string TruncateResult(string result)
        {
            if (result.Length <= MaxResultLength) return result;
            var head = result[..(MaxResultLength - 100)];
            return $"{head}\n\n... [结果已截断，共 {result.Length} 字符，请缩小查询范围]";
        }

        /// <summary>
        /// 读取文件内容（支持分页）
        /// </summary>
        /// <param name="filePath">文件绝对路径</param>
        /// <param name="startLine">起始行号（从 1 开始）</param>
        /// <param name="count">最多返回行数</param>
        /// <returns>文件内容或错误信息</returns>
        public string ReadFile(string filePath, int startLine = 1, int count = 200)
        {
            if (!IsPathAllowed(filePath))
                return $"❌ 安全拦截：不允许访问路径「{filePath}」";

            if (!File.Exists(filePath))
                return $"❌ 文件不存在：{filePath}";

            try
            {
                // 限制最大行数
                count = Math.Clamp(count, 1, 500);
                startLine = Math.Max(1, startLine);

                var lines = File.ReadLines(filePath, Encoding.UTF8)
                    .Skip(startLine - 1)
                    .Take(count)
                    .ToList();

                if (lines.Count == 0)
                    return $"文件「{Path.GetFileName(filePath)}」从第 {startLine} 行开始没有更多内容。";

                var sb = new StringBuilder();
                var totalLines = File.ReadLines(filePath).Count();
                sb.AppendLine($"📄 {Path.GetFileName(filePath)}（第 {startLine}-{startLine + lines.Count - 1} 行，共 {totalLines} 行）");
                sb.AppendLine("---");

                for (int i = 0; i < lines.Count; i++)
                {
                    sb.AppendLine($"{startLine + i}: {lines[i]}");
                }

                // 分页提示
                var endLine = startLine + lines.Count - 1;
                if (endLine < totalLines)
                {
                    sb.AppendLine($"\n... 还有 {totalLines - endLine} 行未显示。使用 start_line={endLine + 1} 继续查看。");
                }

                AiFlowLogger.Info($"[FileSystem] read_file: {filePath} (行 {startLine}-{endLine}/{totalLines})");
                return TruncateResult(sb.ToString());
            }
            catch (Exception ex)
            {
                AiFlowLogger.Warn($"[FileSystem] read_file 失败: {ex.Message}");
                return $"❌ 读取失败：{ex.Message}";
            }
        }

        /// <summary>
        /// 列出目录内容
        /// </summary>
        /// <param name="path">目录绝对路径</param>
        /// <param name="recursive">是否递归</param>
        /// <returns>目录内容列表或错误信息</returns>
        public string ListDirectory(string path, bool recursive = false)
        {
            if (!IsPathAllowed(path))
                return $"❌ 安全拦截：不允许访问路径「{path}」";

            if (!Directory.Exists(path))
                return $"❌ 目录不存在：{path}";

            try
            {
                var sb = new StringBuilder();
                sb.AppendLine($"📁 {path}");
                sb.AppendLine("---");

                int itemCount = 0;
                ListDirectoryInternal(sb, path, recursive ? MaxRecursiveDepth : 0, 0, ref itemCount);

                if (itemCount == 0)
                    sb.AppendLine("（空目录）");

                AiFlowLogger.Info($"[FileSystem] list_directory: {path} (recursive={recursive}, {itemCount} 项)");
                return TruncateResult(sb.ToString());
            }
            catch (Exception ex)
            {
                AiFlowLogger.Warn($"[FileSystem] list_directory 失败: {ex.Message}");
                return $"❌ 列目录失败：{ex.Message}";
            }
        }

        /// <summary>
        /// 递归列出目录内容（内部方法）
        /// </summary>
        private void ListDirectoryInternal(StringBuilder sb, string path, int maxDepth, int currentDepth, ref int itemCount)
        {
            if (currentDepth > maxDepth) return;

            var indent = new string(' ', currentDepth * 2);

            try
            {
                // 列出子目录
                foreach (var dir in Directory.GetDirectories(path).OrderBy(d => d))
                {
                    if (itemCount >= 500) // 安全上限
                    {
                        sb.AppendLine($"{indent}⚠️ 条目过多（>{itemCount}），已截断。请指定更具体的子目录。");
                        return;
                    }

                    var dirName = Path.GetFileName(dir);
                    // 跳过隐藏目录和常见无关目录
                    if (dirName.StartsWith(".") || dirName is "node_modules" or "bin" or "obj" or ".git" or "__pycache__")
                    {
                        sb.AppendLine($"{indent}📁 {dirName}/ (已跳过)");
                        itemCount++;
                        continue;
                    }

                    var childCount = 0;
                    try { childCount = Directory.GetFileSystemEntries(dir).Length; } catch { }
                    sb.AppendLine($"{indent}📁 {dirName}/ ({childCount} 项)");
                    itemCount++;

                    if (currentDepth < maxDepth)
                        ListDirectoryInternal(sb, dir, maxDepth, currentDepth + 1, ref itemCount);
                }

                // 列出文件
                foreach (var file in Directory.GetFiles(path).OrderBy(f => f))
                {
                    if (itemCount >= 500)
                    {
                        sb.AppendLine($"{indent}⚠️ 条目过多（>{itemCount}），已截断。");
                        return;
                    }

                    var fi = new FileInfo(file);
                    var sizeStr = fi.Length switch
                    {
                        < 1024 => $"{fi.Length} B",
                        < 1048576 => $"{fi.Length / 1024.0:F1} KB",
                        _ => $"{fi.Length / 1048576.0:F1} MB"
                    };
                    sb.AppendLine($"{indent}📄 {fi.Name} ({sizeStr})");
                    itemCount++;
                }
            }
            catch (UnauthorizedAccessException)
            {
                sb.AppendLine($"{indent}⛔ 访问被拒绝");
            }
        }

        /// <summary>
        /// 在指定路径中搜索包含关键词的文件
        /// </summary>
        /// <param name="path">搜索起始路径（文件或目录）</param>
        /// <param name="query">搜索关键词或正则表达式</param>
        /// <param name="isRegex">是否为正则表达式</param>
        /// <param name="includes">文件名过滤（如 *.cs, *.json）</param>
        /// <returns>搜索结果或错误信息</returns>
        public string SearchText(string path, string query, bool isRegex = false, string? includes = null)
        {
            if (!IsPathAllowed(path))
                return $"❌ 安全拦截：不允许访问路径「{path}」";

            if (string.IsNullOrWhiteSpace(query))
                return "❌ 搜索关键词不能为空";

            try
            {
                Regex? regex = null;
                if (isRegex)
                {
                    try { regex = new Regex(query, RegexOptions.IgnoreCase | RegexOptions.Compiled); }
                    catch (Exception ex) { return $"❌ 正则表达式语法错误：{ex.Message}"; }
                }

                IEnumerable<string> files;
                if (File.Exists(path))
                {
                    files = new[] { path };
                }
                else if (Directory.Exists(path))
                {
                    var pattern = !string.IsNullOrWhiteSpace(includes) ? includes : "*.*";
                    files = Directory.EnumerateFiles(path, pattern, SearchOption.AllDirectories)
                        .Where(f =>
                        {
                            var name = Path.GetFileName(f);
                            // 跳过二进制文件和无关目录
                            var ext = Path.GetExtension(f).ToLower();
                            if (ext is ".exe" or ".dll" or ".pdb" or ".obj" or ".png" or ".jpg" or ".gif" or ".ico" or ".onnx" or ".zip")
                                return false;
                            // 跳过 bin/obj/node_modules 等
                            var relPath = f.ToLower();
                            if (relPath.Contains(@"\bin\") || relPath.Contains(@"\obj\") ||
                                relPath.Contains(@"\node_modules\") || relPath.Contains(@"\.git\"))
                                return false;
                            return true;
                        });
                }
                else
                {
                    return $"❌ 路径不存在：{path}";
                }

                var sb = new StringBuilder();
                sb.AppendLine($"🔍 搜索「{query}」在 {path}");
                sb.AppendLine("---");

                int matchCount = 0;
                int fileCount = 0;

                foreach (var file in files)
                {
                    if (matchCount >= MaxSearchMatches) break;

                    try
                    {
                        var lines = File.ReadLines(file, Encoding.UTF8).ToList();
                        bool fileHasMatch = false;

                        for (int i = 0; i < lines.Count; i++)
                        {
                            if (matchCount >= MaxSearchMatches) break;

                            bool isMatch = isRegex && regex != null
                                ? regex.IsMatch(lines[i])
                                : lines[i].Contains(query, StringComparison.OrdinalIgnoreCase);

                            if (isMatch)
                            {
                                if (!fileHasMatch)
                                {
                                    // 显示相对路径（如果在目录内）
                                    var displayPath = Directory.Exists(path)
                                        ? Path.GetRelativePath(path, file)
                                        : Path.GetFileName(file);
                                    sb.AppendLine($"\n📄 {displayPath}");
                                    fileHasMatch = true;
                                    fileCount++;
                                }

                                var lineContent = lines[i].Length > 200
                                    ? lines[i][..200] + "…"
                                    : lines[i];
                                sb.AppendLine($"  L{i + 1}: {lineContent.TrimStart()}");
                                matchCount++;
                            }
                        }
                    }
                    catch
                    {
                        // 跳过无法读取的文件（二进制等）
                    }
                }

                if (matchCount == 0)
                {
                    sb.AppendLine("未找到匹配结果。");
                }
                else
                {
                    sb.AppendLine($"\n共 {matchCount} 处匹配，分布在 {fileCount} 个文件中。");
                    if (matchCount >= MaxSearchMatches)
                        sb.AppendLine($"⚠️ 结果已截断（最多显示 {MaxSearchMatches} 处），请用更精确的关键词或 includes 过滤。");
                }

                AiFlowLogger.Info($"[FileSystem] search_text: query=\"{query}\" path={path} matches={matchCount}");
                return TruncateResult(sb.ToString());
            }
            catch (Exception ex)
            {
                AiFlowLogger.Warn($"[FileSystem] search_text 失败: {ex.Message}");
                return $"❌ 搜索失败：{ex.Message}";
            }
        }
    }
}
