using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TaskFlow.Models.TaskCards;

namespace TaskFlow.Services
{
    public partial class TaskExecutionService
    {
        private async Task<bool> ExecuteCallSubFlowAsync(CallSubFlowTaskCard task, IList<TaskCardBase> allTasks, CancellationToken cancellationToken)
        {
            if (SubFlowResolver == null)
            {
                task.ErrorMessage = "未注入 SubFlowResolver";
                return false;
            }

            if (!task.TargetSubFlowId.HasValue)
            {
                task.ErrorMessage = "未指定目标子流程";
                return false;
            }

            var subFlowTasks = SubFlowResolver.Invoke(task.TargetSubFlowId.Value);
            if (subFlowTasks == null || subFlowTasks.Count == 0)
            {
                task.ErrorMessage = "找不到目标子流程或子流程中没有卡片";
                return false;
            }

            // 0. 提前重置所有任务状态，以备参数注入
            foreach (var t in subFlowTasks)
            {
                t.Reset();
            }

            // 1. 找到置顶的输入卡片，注入参数
            var inputCard = subFlowTasks.FirstOrDefault(t => t.TaskType == TaskType.SubFlowInput) as SubFlowInputTaskCard;
            if (inputCard == null)
            {
                task.ErrorMessage = "子流程缺少输入(SubFlowInput)卡片";
                return false;
            }

            // 注入图像
            if (task.SourceTaskIdForImage.HasValue)
            {
                var src = allTasks.FirstOrDefault(t => t.Id == task.SourceTaskIdForImage.Value);
                if (src != null && src.OutputImage != null)
                {
                    inputCard.OutputImage = src.OutputImage.Clone();
                }
            }
            // 注入文本
            if (task.SourceTaskIdForText.HasValue)
            {
                var src = allTasks.FirstOrDefault(t => t.Id == task.SourceTaskIdForText.Value);
                if (src != null) inputCard.OutputText = src.OutputText;
            }
            // 注入坐标X
            if (task.SourceTaskIdForX.HasValue)
            {
                var src = allTasks.FirstOrDefault(t => t.Id == task.SourceTaskIdForX.Value);
                if (src != null) inputCard.OutputX = src.OutputX;
            }
            // 注入坐标Y
            if (task.SourceTaskIdForY.HasValue)
            {
                var src = allTasks.FirstOrDefault(t => t.Id == task.SourceTaskIdForY.Value);
                if (src != null) inputCard.OutputY = src.OutputY;
            }

            // 2. 递归重入执行子流程，挂起当前
            Log($"[{DateTime.Now:HH:mm:ss}] -- 进入子流程 --");
            await ExecuteTaskCollectionAsync(subFlowTasks, cancellationToken, subFlowTasks, skipReset: true);
            Log($"[{DateTime.Now:HH:mm:ss}] -- 退出子流程 --");

            // 3. 寻找成功执行的输出卡片拿返回值
            // 因为当遇到 TaskType.SubFlowOutput 时我们会 break 跳出内部执行循环并将其自身 Status 标为 Success。
            // 因此查找最后一个状态为 Success 的 SubFlowOutput 即为实际触发返回的那个出口。
            var outputCard = subFlowTasks.LastOrDefault(t => t.TaskType == TaskType.SubFlowOutput && t.Status == Models.TaskCards.TaskStatus.Success) as SubFlowOutputTaskCard;

            if (outputCard != null)
            {
                task.OutputImage?.Dispose();
                task.OutputImage = outputCard.OutputImage?.Clone();
                task.OutputText = outputCard.OutputText;
                task.OutputX = outputCard.OutputX;
                task.OutputY = outputCard.OutputY;
                task.OutputResult = outputCard.OutputResult;
            }

            return true;
        }

        private bool ExecuteSubFlowOutput(SubFlowOutputTaskCard task, IList<TaskCardBase> allTasks)
        {
            // 在遇到这块卡片时，它需要自己从子流程获取属性存到自己的Output里留给父流程来捡取
            task.OutputImage?.Dispose();
            task.OutputImage = null;

            // 图像
            if (task.SourceTaskIdForImage.HasValue)
            {
                var src = allTasks.FirstOrDefault(t => t.Id == task.SourceTaskIdForImage.Value);
                if (src != null && src.OutputImage != null) task.OutputImage = src.OutputImage.Clone();
            }
            // 文本
            if (task.SourceTaskIdForText.HasValue)
            {
                var src = allTasks.FirstOrDefault(t => t.Id == task.SourceTaskIdForText.Value);
                if (src != null) task.OutputText = src.OutputText;
            }
            // X
            if (task.SourceTaskIdForX.HasValue)
            {
                var src = allTasks.FirstOrDefault(t => t.Id == task.SourceTaskIdForX.Value);
                if (src != null) task.OutputX = src.OutputX;
            }
            // Y
            if (task.SourceTaskIdForY.HasValue)
            {
                var src = allTasks.FirstOrDefault(t => t.Id == task.SourceTaskIdForY.Value);
                if (src != null) task.OutputY = src.OutputY;
            }
            // Result
            if (task.SourceTaskIdForResult.HasValue)
            {
                var src = allTasks.FirstOrDefault(t => t.Id == task.SourceTaskIdForResult.Value);
                if (src != null) task.OutputResult = src.OutputResult;
            }

            return true;
        }
    }
}
