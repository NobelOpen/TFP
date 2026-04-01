using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.Playwright;
using TaskFlow.Helpers;
using TaskFlow.Models.TaskCards;

namespace TaskFlow.Services
{
    /// <summary>
    /// 浏览器操作任务执行器（CDP 附着模式）
    /// </summary>
    public partial class TaskExecutionService
    {
        // ----------------------------------------------------------
        // 浏览器取文本
        // ----------------------------------------------------------

        private async Task<bool> ExecuteBrowserGetTextAsync(
            BrowserGetTextTaskCard task, IList<TaskCardBase> allTasks, CancellationToken ct)
        {
            try
            {
                // 解析选择器表达式
                string selector = _variableStore.ResolveVariableReferences(task.Selector);
                selector = ExpressionEvaluator.ResolveExpression(selector, allTasks, _variableStore);
                selector = selector.Trim().Trim('"');

                if (string.IsNullOrWhiteSpace(selector))
                {
                    task.ErrorMessage = "选择器为空";
                    return false;
                }

                ct.ThrowIfCancellationRequested();

                var page = await BrowserSessionManager.GetActivePageAsync(task.CdpPort);

                string? result;

                if (string.IsNullOrWhiteSpace(task.AttributeName))
                {
                    // 取 innerText
                    result = task.SelectorType == BrowserSelectorType.XPath
                        ? await page.InnerTextAsync($"xpath={selector}")
                        : await page.InnerTextAsync(selector);
                }
                else
                {
                    // 取指定属性
                    result = task.SelectorType == BrowserSelectorType.XPath
                        ? await page.GetAttributeAsync($"xpath={selector}", task.AttributeName)
                        : await page.GetAttributeAsync(selector, task.AttributeName);
                }

                task.OutputText = result ?? string.Empty;
                Log($"[{DateTime.Now:HH:mm:ss}] 浏览器取文本: '{selector}' => \"{task.OutputText}\"");
                return true;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                task.ErrorMessage = ex.Message;
                return false;
            }
        }

        // ----------------------------------------------------------
        // 浏览器执行脚本
        // ----------------------------------------------------------

        private async Task<bool> ExecuteBrowserExecuteJsAsync(
            BrowserExecuteJsTaskCard task, IList<TaskCardBase> allTasks, CancellationToken ct)
        {
            try
            {
                // 解析脚本中的变量引用
                string script = _variableStore.ResolveVariableReferences(task.Script);
                script = ExpressionEvaluator.ResolveExpression(script, allTasks, _variableStore);

                if (string.IsNullOrWhiteSpace(script))
                {
                    task.ErrorMessage = "脚本内容为空";
                    return false;
                }

                ct.ThrowIfCancellationRequested();

                var page = await BrowserSessionManager.GetActivePageAsync(task.CdpPort);

                // 将用户代码包裹为匿名函数执行
                var wrappedScript = $"() => {{ {script} }}";
                var rawResult = await page.EvaluateAsync<object?>(wrappedScript);

                task.OutputText = rawResult?.ToString() ?? string.Empty;
                Log($"[{DateTime.Now:HH:mm:ss}] 浏览器执行脚本 => \"{task.OutputText}\"");
                return true;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                task.ErrorMessage = ex.Message;
                return false;
            }
        }

        // ----------------------------------------------------------
        // 浏览器等待元素
        // ----------------------------------------------------------

        private async Task<bool> ExecuteBrowserWaitForElementAsync(
            BrowserWaitForElementTaskCard task, IList<TaskCardBase> allTasks, CancellationToken ct)
        {
            try
            {
                // 解析选择器表达式
                string selector = _variableStore.ResolveVariableReferences(task.Selector);
                selector = ExpressionEvaluator.ResolveExpression(selector, allTasks, _variableStore);
                selector = selector.Trim().Trim('"');

                if (string.IsNullOrWhiteSpace(selector))
                {
                    task.ErrorMessage = "选择器为空";
                    return false;
                }

                ct.ThrowIfCancellationRequested();

                var page = await BrowserSessionManager.GetActivePageAsync(task.CdpPort);

                // 构建 WaitForSelector 选项
                var state = task.WaitMode == BrowserWaitMode.Hidden
                    ? WaitForSelectorState.Hidden
                    : WaitForSelectorState.Visible;

                var opts = new PageWaitForSelectorOptions
                {
                    State   = state,
                    Timeout = task.TimeoutMs
                };

                // XPath 选择器需要加前缀
                string resolvedSelector = task.SelectorType == BrowserSelectorType.XPath
                    ? $"xpath={selector}"
                    : selector;

                await page.WaitForSelectorAsync(resolvedSelector, opts);

                task.OutputResult = true;
                string modeStr = task.WaitMode == BrowserWaitMode.Hidden ? "消失" : "出现";
                Log($"[{DateTime.Now:HH:mm:ss}] 浏览器等待元素{modeStr}: '{selector}'");
                return true;
            }
            catch (TimeoutException)
            {
                string modeStr = task.WaitMode == BrowserWaitMode.Hidden ? "消失" : "出现";
                task.ErrorMessage = $"等待元素{modeStr}超时（{task.TimeoutMs}ms）: {task.Selector}";
                task.OutputResult = false;
                return false;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                task.ErrorMessage = ex.Message;
                task.OutputResult = false;
                return false;
            }
        }
    }
}
