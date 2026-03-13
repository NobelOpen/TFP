using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json;
using TaskFlow.Models.TaskCards;

namespace TaskFlow.Models
{
    /// <summary>
    /// 工作流分页，每个分页包含独立的任务卡片集合、变量和文件路径
    /// </summary>
    public partial class WorkflowTab : ObservableObject
    {
        [ObservableProperty]
        private string _name = "流程1";

        [JsonIgnore]
        [ObservableProperty]
        private bool _isSelected;

        /// <summary>
        /// 该分页的任务卡片集合
        /// </summary>
        public ObservableCollection<TaskCardBase> TaskCards { get; set; } = new();

        /// <summary>
        /// 下一个任务编号
        /// </summary>
        public int NextTaskNumber { get; set; } = 1;

        /// <summary>
        /// 该分页关联的文件路径，为 null 表示新建未保存
        /// </summary>
        public string? FilePath { get; set; }
    }
}
