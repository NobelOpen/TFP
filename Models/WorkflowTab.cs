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

        private ObservableCollection<TaskCardBase> _taskCards = new();

        /// <summary>
        /// 该分页的任务卡片集合
        /// </summary>
        public ObservableCollection<TaskCardBase> TaskCards
        {
            get => _taskCards;
            set
            {
                if (_taskCards != null) _taskCards.CollectionChanged -= TaskCards_CollectionChanged;
                SetProperty(ref _taskCards, value);
                if (_taskCards != null) _taskCards.CollectionChanged += TaskCards_CollectionChanged;
                UpdateVisibleTaskCards();
            }
        }

        [JsonIgnore]
        public ObservableCollection<TaskCardBase> VisibleTaskCards { get; } = new();

        public WorkflowTab()
        {
            _taskCards.CollectionChanged += TaskCards_CollectionChanged;
        }

        private void TaskCards_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            UpdateVisibleTaskCards();
        }

        /// <summary>
        /// 更新可见任务卡片集合。使用 Diff 算法只触发 Add/Remove/Move，
        /// 避免 Reset 导致 UI 虚拟化容器全部重建。
        /// </summary>
        public void UpdateVisibleTaskCards()
        {
            var newVisibleList = TaskCards.Where(c => !c.IsHiddenByCollapse).ToList();

            // 1. 移除不再可见的卡片
            for (int i = VisibleTaskCards.Count - 1; i >= 0; i--)
            {
                if (!newVisibleList.Contains(VisibleTaskCards[i]))
                {
                    VisibleTaskCards.RemoveAt(i);
                }
            }

            // 2. 插入新可见的卡片并调整顺序
            for (int i = 0; i < newVisibleList.Count; i++)
            {
                var card = newVisibleList[i];
                int currentIndex = VisibleTaskCards.IndexOf(card);

                if (currentIndex == -1)
                {
                    VisibleTaskCards.Insert(i, card);
                }
                else if (currentIndex != i)
                {
                    VisibleTaskCards.Move(currentIndex, i);
                }
            }
        }

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
