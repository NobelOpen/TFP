using System;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TaskFlow.Models.TaskCards;

namespace TaskFlow.ViewModels
{
    /// <summary>
    /// 任务卡片ViewModel
    /// </summary>
    public partial class TaskCardViewModel : ObservableObject
    {
        [ObservableProperty]
        private TaskCardBase _taskCard;

        public TaskCardViewModel(TaskCardBase taskCard)
        {
            _taskCard = taskCard;
        }

        public Guid Id => TaskCard.Id;
        public string Name => TaskCard.Name;
        public int Order => TaskCard.Order;
        public TaskType TaskType => TaskCard.TaskType;
        public string TaskTypeName => TaskCard.TaskTypeName;
    }
}
