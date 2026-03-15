using System;
using TaskFlow.Resources;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using TaskFlow.Models;
using TaskFlow.Models.TaskCards;

namespace TaskFlow.Helpers
{
    public static class ExpressionEvaluator
    {
        /// <summary>
        /// 解析表达式中的任务引用，格式: #N 名称.输出属性
        /// 支持的输出属性：
        ///   .循环索引  - ForLoop的CurrentLoopIndex
        ///   .执行结果  - OutputResult (true/false)
        ///   .输出文本  - OutputText
        ///   .X / .Y    - OutputX / OutputY
        /// 示例: "#1 循环开始.循环索引>1", "#3 颜色识别.匹配率>0.5"
        /// </summary>
        public static string ResolveExpression(string expression, IList<TaskCardBase> allTasks, VariableStore? variableStore = null)
        {
            // 先替换 @变量引用
            if (variableStore != null)
            {
                expression = variableStore.ResolveVariableReferences(expression);
            }

            // 匹配 #N 名称.属性 的模式（属性名不包含运算符字符）
            var pattern = @"#(\d+)\s+([^.]+)\.([^><=!\s]+)";
            return Regex.Replace(expression, pattern, match =>
            {
                int order = int.Parse(match.Groups[1].Value);
                string taskName = match.Groups[2].Value.Trim();
                string property = match.Groups[3].Value.Trim();

                var referencedTask = allTasks.FirstOrDefault(t => t.Order == order);
                if (referencedTask == null)
                {
                    throw new InvalidOperationException($"找不到序号为 {order} 的任务卡片");
                }

                // 验证任务名称是否匹配
                if (referencedTask.Name != taskName)
                {
                    throw new InvalidOperationException(
                        $"序号 {order} 的任务名称不匹配: 期望 \"{taskName}\"，实际 \"{referencedTask.Name}\"");
                }

                return property switch
                {
                    "循环索引" => referencedTask is ForLoopTaskCard forCard
                        ? forCard.CurrentLoopIndex.ToString()
                        : (referencedTask.OutputLoopIndex?.ToString() ?? "0"),
                    // 优先使用 OutputResult，否则根据执行状态判断
                    "执行结果" => (referencedTask.OutputResult ?? (referencedTask.Status == Models.TaskCards.TaskStatus.Success)).ToString().ToLower(),
                    "输出文本" => $"\"{referencedTask.OutputText ?? ""}\"",
                    "文本" => $"\"{referencedTask.OutputText ?? ""}\"",  // Alias for OutputText
                    "X" or "x" => (referencedTask.OutputX ?? 0).ToString(),
                    "Y" or "y" => (referencedTask.OutputY ?? 0).ToString(),
                    "匹配率" or "当前匹配阈值" => referencedTask switch
                    {
                        ImgColorDetectTaskCard colorCard => colorCard.OutputMatchRatio.ToString(),
                        ImgTemplateMatchTaskCard tmCard => tmCard.OutputMatchScore.ToString(),
                        _ => "0"
                    },
                    "转换结果" or "整数值" => referencedTask is TypeConvertTaskCard tcCard
                        ? tcCard.OutputIntValue.ToString()
                        : referencedTask is ArrayParseTaskCard apCard1
                            ? apCard1.OutputIntValue.ToString()
                            : "0",
                    "当前时间" or "时间戳" => referencedTask is GetTimestampTaskCard tsCard
                        ? tsCard.OutputTimestamp.ToString()
                        : "0",
                    "匹配数量" => referencedTask is ImgTemplateMatchTaskCard tmCard2
                        ? tmCard2.OutputMatchCount.ToString()
                        : "0",
                    "Blob数量" => referencedTask is ImgBlobAnalysisTaskCard blobCard
                        ? blobCard.OutputBlobCount.ToString()
                        : "0",
                    "最佳匹配分数" => referencedTask is ImgTemplateMatchTaskCard tmCard3
                        ? (tmCard3.OutputMatchResults.Count > 0
                            ? tmCard3.OutputMatchResults.OrderByDescending(m => m.Score).First().Score.ToString()
                            : "0")
                        : "0",
                    "解析结果" => referencedTask is ArrayParseTaskCard apCard2
                        ? apCard2.ArrayDataType switch
                        {
                            ArrayDataType.Int => apCard2.OutputIntValue.ToString(),
                            ArrayDataType.String => $"\"{apCard2.OutputStringValue}\"",
                            ArrayDataType.Coordinate => apCard2.OutputX?.ToString() ?? "0",
                            ArrayDataType.Double => apCard2.OutputDoubleValue.ToString(),
                            _ => "0"
                        }
                        : "0",
                    "缩放倍率" => referencedTask is ImgResizeTaskCard resizeCard
                        ? resizeCard.OutputScaleRatio.ToString()
                        : "0",
                    "数组当前容量" => referencedTask is ArrayBuilderTaskCard abCard
                        ? abCard.OutputArrayCount.ToString()
                        : "0",
                    "保存文件路径" => referencedTask is ArrayBuilderTaskCard abCard2
                        ? $"\"{abCard2.OutputSavePath ?? ""}\""
                        : "\"\"",
                    "已翻译文件路径" => referencedTask is LlmFileTranslateTaskCard ftCard
                        ? $"\"{ftCard.OutputTranslatedFilePath ?? ""}\""
                        : "\"\"",
                    "数组元素数量" => referencedTask is FileReadTaskCard frCard
                        ? frCard.OutputArrayCount.ToString()
                        : "0",
                    "匹配索引" => referencedTask is ArraySearchTaskCard asCard
                        ? asCard.OutputMatchIndex.ToString()
                        : "-1",
                    "匹配值" => referencedTask is ArraySearchTaskCard asCard2
                        ? $"\"{asCard2.OutputMatchValue ?? ""}\""
                        : "\"\"",
                    _ => throw new InvalidOperationException($"不支持的输出属性: {property}")
                };
            });
        }

        /// <summary>
        /// 评估简单的比较表达式，支持 ==, !=, >, <, >=, <=, contains/包含, =~
        /// </summary>
        public static bool Evaluate(string expression)
        {
            // 处理 true/false 字面量
            string trimmed = expression.Trim();
            if (trimmed.Equals("true", StringComparison.OrdinalIgnoreCase)) return true;
            if (trimmed.Equals("false", StringComparison.OrdinalIgnoreCase)) return false;

            // 1. Handle OR (||) - lowest precedence
            var orParts = SplitIgnoreQuotes(trimmed, "||");
            if (orParts.Count > 1)
            {
                foreach (var part in orParts)
                {
                    if (Evaluate(part)) return true;
                }
                return false;
            }

            // 2. Handle AND (&&) - higher precedence
            var andParts = SplitIgnoreQuotes(trimmed, "&&");
            if (andParts.Count > 1)
            {
                foreach (var part in andParts)
                {
                    if (!Evaluate(part)) return false;
                }
                return true;
            }
            // 3. 处理 NOT (!) 前缀 - 最高优先级的一元运算符
            if (trimmed.StartsWith("!"))
            {
                string inner = trimmed.Substring(1).TrimStart();
                if (!string.IsNullOrEmpty(inner))
                {
                    return !Evaluate(inner);
                }
            }

            // 4. Handle Comparisons
            // 左侧先尝试匹配完整的引号字符串（避免引号内的 > < = 被误当运算符），
            // 匹配不到引号字符串时再回退到惰性匹配
            var compPattern = @"^(""[^""]*""|.+?)\s*(==|!=|>=|<=|>|<|contains|包含|=~)\s*(.+)$";
            var compMatch = Regex.Match(trimmed, compPattern, RegexOptions.IgnoreCase | RegexOptions.Singleline);

            if (!compMatch.Success)
            {
                // Try bool.TryParse as a fallback for single boolean values not caught by earlier check
                if (bool.TryParse(trimmed, out bool simpleBool))
                {
                    return simpleBool;
                }
                throw new InvalidOperationException(string.Format(Strings.Expr_CannotParse, trimmed));
            }

            string leftStr = compMatch.Groups[1].Value.Trim();
            string op = compMatch.Groups[2].Value.ToLower();
            string rightStr = compMatch.Groups[3].Value.Trim();

            // Check for containment operators first as they are string-specific
            if (op == "contains" || op == "包含" || op == "=~")
            {
                return EvaluateStringContainment(leftStr, rightStr);
            }

            // 尝试作为数字比较
            if (double.TryParse(leftStr, out double leftNum) && double.TryParse(rightStr, out double rightNum))
            {
                return op switch
                {
                    "==" => Math.Abs(leftNum - rightNum) < 0.0001,
                    "!=" => Math.Abs(leftNum - rightNum) >= 0.0001,
                    ">" => leftNum > rightNum,
                    "<" => leftNum < rightNum,
                    ">=" => leftNum >= rightNum,
                    "<=" => leftNum <= rightNum,
                    _ => throw new InvalidOperationException(string.Format(Strings.Expr_UnsupportedOp, op))
                };
            }

            // 字符串比较（去掉引号）
            var leftStrClean = leftStr.Trim('"');
            var rightStrClean = rightStr.Trim('"');

            // bool比较
            if (bool.TryParse(leftStrClean, out bool leftBool) && bool.TryParse(rightStrClean, out bool rightBool))
            {
                return op switch
                {
                    "==" => leftBool == rightBool,
                    "!=" => leftBool != rightBool,
                    _ => throw new InvalidOperationException($"布尔值不支持运算符: {op}")
                };
            }

            // 字符串比较
            return op switch
            {
                "==" => leftStrClean == rightStrClean,
                "!=" => leftStrClean != rightStrClean,
                _ => throw new InvalidOperationException($"字符串不支持运算符: {op}")
            };
        }

        private static bool EvaluateStringContainment(string left, string right)
        {
            // Remove surrounding quotes if present
            var leftClean = left.Trim('"');
            var rightClean = right.Trim('"');

            return leftClean.Contains(rightClean);
        }

        private static List<string> SplitIgnoreQuotes(string input, string separator)
        {
            var list = new List<string>();
            int lastPos = 0;
            bool inQuote = false;
            for (int i = 0; i < input.Length; i++)
            {
                if (input[i] == '"' && (i == 0 || input[i - 1] != '\\'))
                {
                    inQuote = !inQuote;
                }

                if (!inQuote && i + separator.Length <= input.Length)
                {
                    bool match = true;
                    for (int j = 0; j < separator.Length; j++)
                    {
                        if (input[i + j] != separator[j])
                        {
                            match = false;
                            break;
                        }
                    }

                    if (match)
                    {
                        list.Add(input.Substring(lastPos, i - lastPos));
                        lastPos = i + separator.Length;
                        i += separator.Length - 1; // Advance
                    }
                }
            }
            list.Add(input.Substring(lastPos));
            return list;
        }
    }
}
