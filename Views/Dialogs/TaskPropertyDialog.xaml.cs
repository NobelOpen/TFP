using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using TaskFlow.Helpers;
using TaskFlow.Resources;
using System.Windows.Media;
using Microsoft.Win32;
using Mat = OpenCvSharp.Mat;
using Cv2 = OpenCvSharp.Cv2;
using TaskFlow.Models.TaskCards;
using TaskFlow.ViewModels;

namespace TaskFlow.Views.Dialogs
{
    public partial class TaskPropertyDialog : Window
    {
        private readonly TaskCardBase _task;
        private readonly MainViewModel _viewModel;
        private readonly Dictionary<string, Control> _propertyControls = new();

        public TaskPropertyDialog(TaskCardBase task, MainViewModel viewModel)
        {
            InitializeComponent();
            ApplyLocalization();
            _task = task;
            _viewModel = viewModel;

            TitleText.Text = string.Format(TaskFlow.Resources.Strings.Prop_EditPrefix, task.Name);
            SubtitleText.Text = task.TaskTypeName;

            BuildPropertyControls();
        }

        private void BuildPropertyControls()
        {
            PropertyPanel.Children.Clear();
            _propertyControls.Clear();

            // 通用属性：名称
            AddTextProperty("Name", TaskFlow.Resources.Strings.Prop_TaskName, _task.Name);


            // 根据任务类型添加特定属性
            switch (_task)
            {
                case IfElseBranchTaskCard ifCard when ifCard.BranchRole == BranchRole.IfStart:
                    AddConditionExpressionProperty(ifCard);
                    // 动态显示所有elif卡片的条件设置
                    AddElifConditionProperties(ifCard);
                    break;

                case IfElseBranchTaskCard elifCard when elifCard.BranchRole == BranchRole.ElifStart:
                    AddConditionExpressionProperty(elifCard, "Elif");
                    break;

                case ForLoopTaskCard loopCard when loopCard.BranchRole == BranchRole.ForLoopStart:
                    // 合并输入：直接输入数字、@变量 或 #任务引用
                    AddTextProperty("LoopCountExpression", TaskFlow.Resources.Strings.Prop_LoopCount,
                        !string.IsNullOrWhiteSpace(loopCard.LoopCountExpression)
                            ? loopCard.LoopCountExpression
                            : loopCard.LoopCount.ToString());
                    break;

                case PauseTaskCard pauseCard:
                    // 合并输入：直接输入数字、@变量 或 #任务引用
                    AddTextProperty("PauseDurationExpression", TaskFlow.Resources.Strings.Prop_PauseDuration,
                        !string.IsNullOrWhiteSpace(pauseCard.PauseDurationExpression)
                            ? pauseCard.PauseDurationExpression
                            : pauseCard.PauseDurationMs.ToString());
                    break;

                case GetTimestampTaskCard timestampCard:
                    {
                        var label = new TextBlock { Text = TaskFlow.Resources.Strings.Prop_OutputFormat, Style = FindResource("PropertyLabel") as Style };
                        var combo = new ComboBox { Style = FindResource("PropertyComboBox") as Style };
                        combo.Items.Add(new ComboBoxItem { Content = TaskFlow.Resources.Strings.Prop_TimeFmt_HMS, Tag = TimestampFormat.HourMinuteSecond });
                        combo.Items.Add(new ComboBoxItem { Content = TaskFlow.Resources.Strings.Prop_TimeFmt_DHMS, Tag = TimestampFormat.DayHourMinuteSecond });
                        combo.Items.Add(new ComboBoxItem { Content = TaskFlow.Resources.Strings.Prop_TimeFmt_MDHMS, Tag = TimestampFormat.MonthDayHourMinuteSecond });
                        combo.Items.Add(new ComboBoxItem { Content = TaskFlow.Resources.Strings.Prop_TimeFmt_YMDHMS, Tag = TimestampFormat.YearMonthDayHourMinuteSecond });
                        combo.SelectedIndex = (int)timestampCard.TimestampFormat;
                        PropertyPanel.Children.Add(label);
                        PropertyPanel.Children.Add(combo);
                        _propertyControls["TimestampFormat"] = combo;
                        break;
                    }

                case WinLaunchAppTaskCard launchCard:
                    AddFilePathProperty("ExePath", "EXE路径表达式", launchCard.ExePath, TaskFlow.Resources.Strings.Filter_ExeFile, true);
                    AddTextProperty("Arguments", TaskFlow.Resources.Strings.Prop_Arguments, launchCard.Arguments);
                    break;

                case WinCloseAppTaskCard closeAppCard:
                    AddTextProperty("ProcessName", TaskFlow.Resources.Strings.Prop_ProcessName, closeAppCard.ProcessName);
                    break;

                case WinScreenshotTaskCard screenshotCard:
                    AddTextProperty("ProcessName", TaskFlow.Resources.Strings.Prop_ProcessName, screenshotCard.ProcessName);
                    AddCheckboxProperty("IncludeTitleBar", TaskFlow.Resources.Strings.Prop_IncludeTitleBar, screenshotCard.IncludeTitleBar);
                    AddTextProperty("CropTopHeightExpression", TaskFlow.Resources.Strings.Prop_CropTopHeight, screenshotCard.CropTopHeightExpression);
                    AddCheckboxProperty("ConvertToGrayscale", TaskFlow.Resources.Strings.Prop_ConvertGrayscale, screenshotCard.ConvertToGrayscale);
                    break;

                case WinClickTaskCard clickCard:
                    AddClickProperties(clickCard);
                    break;

                case AdbConnectTaskCard connectCard:
                    AddTextProperty("DeviceIp", TaskFlow.Resources.Strings.Prop_DeviceIp, connectCard.DeviceIp);
                    AddIntProperty("DevicePort", TaskFlow.Resources.Strings.Prop_DevicePort, connectCard.DevicePort);
                    break;

                case AdbLaunchAppTaskCard adbLaunchCard:
                    AddTextProperty("DeviceSerial", TaskFlow.Resources.Strings.Prop_DeviceSerial, adbLaunchCard.DeviceSerial);
                    AddTextProperty("PackageName", TaskFlow.Resources.Strings.Prop_PackageName, adbLaunchCard.PackageName);
                    AddTextProperty("ActivityName", TaskFlow.Resources.Strings.Prop_ActivityName, adbLaunchCard.ActivityName);
                    break;

                case AdbScreenshotTaskCard adbScreenshotCard:
                    AddTextProperty("DeviceSerial", TaskFlow.Resources.Strings.Prop_DeviceSerial, adbScreenshotCard.DeviceSerial);
                    AddCheckboxProperty("ConvertToGrayscale", TaskFlow.Resources.Strings.Prop_ConvertGrayscale, adbScreenshotCard.ConvertToGrayscale);
                    break;

                case AdbClickTaskCard adbClickCard:
                    AddTextProperty("DeviceSerial", TaskFlow.Resources.Strings.Prop_DeviceSerial, adbClickCard.DeviceSerial);
                    AddAdbClickProperties(adbClickCard);
                    break;

                case AdbCloseAppTaskCard adbCloseCard:
                    AddTextProperty("DeviceSerial", TaskFlow.Resources.Strings.Prop_DeviceSerial, adbCloseCard.DeviceSerial);
                    AddTextProperty("PackageName", TaskFlow.Resources.Strings.Prop_PackageName, adbCloseCard.PackageName);
                    break;

                case AdbDisconnectTaskCard adbDisconnectCard:
                    AddTextProperty("DeviceSerial", TaskFlow.Resources.Strings.Prop_DeviceSerial, adbDisconnectCard.DeviceSerial);
                    break;

                case WinUiAutomationTaskCard uiAutoCard:
                    AddTextProperty("ProcessName", TaskFlow.Resources.Strings.Prop_ProcessName, uiAutoCard.ProcessName);
                    AddUiAutomationProperties(uiAutoCard);
                    break;

                case WinSimulateInputTaskCard simCard:
                    AddSimulateInputProperties(simCard);
                    break;

                case WinSubtitleTaskCard subtitleCard:
                    AddSubtitleProperties(subtitleCard);
                    break;

                case ImgCropTaskCard cropCard:
                    AddImageSourceProperty(cropCard);
                    AddRoiProperty(cropCard);
                    break;

                case ImgTemplateMatchTaskCard matchCard:
                    AddImageSourcePropertyMatch(matchCard);
                    AddTemplateProperty(matchCard);
                    AddDoubleProperty("MatchThreshold", TaskFlow.Resources.Strings.Prop_MatchThreshold, matchCard.MatchThreshold);
                    AddIntProperty("MaxMatchCount", TaskFlow.Resources.Strings.Prop_MaxMatchCount, matchCard.MaxMatchCount);
                    break;

                case ImgOnnxDetectTaskCard detectCard:
                    AddOnnxDetectProperties(detectCard);
                    break;

                case CallSubFlowTaskCard callSubFlowCard:
                    AddCallSubFlowProperties(callSubFlowCard);
                    break;

                case SubFlowOutputTaskCard subFlowOutputCard:
                    AddSubFlowOutputProperties(subFlowOutputCard);
                    break;

                case ImgOcrTaskCard ocrCard:
                    AddImageSourcePropertyOcr(ocrCard);
                    // 间隔分割线
                    PropertyPanel.Children.Add(new Separator { Margin = new Thickness(0, 8, 0, 8), Background = new SolidColorBrush(Color.FromRgb(232, 230, 220)) });
                    // 检查包含文本（条件显示目标文本）
                    var targetTextLabel = new TextBlock { Text = TaskFlow.Resources.Strings.Prop_TargetText, Style = FindResource("PropertyLabel") as Style };
                    var targetTextBox = new TextBox { Text = ocrCard.TargetText, Style = FindResource("PropertyTextBox") as Style };
                    AutoCompleteHelper.Attach(targetTextBox, _viewModel.VariableStore.Variables, _viewModel.TaskCards);
                    _propertyControls["TargetText"] = targetTextBox;
                    void UpdateTargetVis(bool show) { targetTextLabel.Visibility = targetTextBox.Visibility = show ? Visibility.Visible : Visibility.Collapsed; }
                    var checkTextCb = new CheckBox { Content = TaskFlow.Resources.Strings.Prop_CheckContainsText, IsChecked = ocrCard.CheckContainsText, Style = FindResource("PropertyCheckBox") as Style };
                    checkTextCb.Checked += (s, e) => UpdateTargetVis(true);
                    checkTextCb.Unchecked += (s, e) => UpdateTargetVis(false);
                    PropertyPanel.Children.Add(checkTextCb);
                    _propertyControls["CheckContainsText"] = checkTextCb;
                    PropertyPanel.Children.Add(targetTextLabel);
                    PropertyPanel.Children.Add(targetTextBox);
                    UpdateTargetVis(ocrCard.CheckContainsText);
                    break;
                    
                case LlmTranslateTaskCard llmCard:
                    AddLlmTranslateProperties(llmCard);
                    break;

                case LlmVisionTaskCard visionCard:
                    AddLlmVisionProperties(visionCard);
                    break;

                case ArrayBuilderTaskCard arrayBuilderCard:
                    AddArrayBuilderProperties(arrayBuilderCard);
                    break;

                case LlmFileTranslateTaskCard fileTransCard:
                    AddLlmFileTranslateProperties(fileTransCard);
                    break;

                case FileReadTaskCard fileReadCard:
                    AddFileReadProperties(fileReadCard);
                    break;

                case EventListenerTaskCard eventCard:
                    AddEventListenerProperties(eventCard);
                    break;

                case ArraySearchTaskCard searchCard:
                    AddArraySearchProperties(searchCard);
                    break;

                case WinFindFileTaskCard findFileCard:
                    AddWinFindFileProperties(findFileCard);
                    break;

                case InputComboTaskCard comboCard:
                    AddInputComboProperties(comboCard);
                    break;

                case WinTextInputTaskCard textInputCard:
                    AddWinTextInputProperties(textInputCard);
                    break;

                case ImgColorDetectTaskCard colorCard:
                    AddImageSourcePropertyColor(colorCard);
                    AddColorDetectProperties(colorCard);
                    break;

                case ImgColorSegmentTaskCard segCard:
                    AddImageSourcePropertyColorSegment(segCard);
                    AddColorSegmentProperties(segCard);
                    break;

                case ImgPreprocessTaskCard prepCard:
                    AddImageSourceProperty_Generic(prepCard.UseSourceTaskImage, prepCard.SourceTaskIdForImage, prepCard.ImageFilePath);
                    AddCheckboxProperty("EnableGrayscale", TaskFlow.Resources.Strings.Prop_EnableGrayscale, prepCard.EnableGrayscale);
                    AddEnumComboProperty<BinarizeMethod>("BinarizeMethod", TaskFlow.Resources.Strings.Prop_BinarizeMethod, prepCard.BinarizeMethod, new Dictionary<BinarizeMethod, string>
                    {
                        { BinarizeMethod.None, TaskFlow.Resources.Strings.Prop_BinarizeNone },
                        { BinarizeMethod.Binary, TaskFlow.Resources.Strings.Prop_BinarizeBinary },
                        { BinarizeMethod.BinaryInv, TaskFlow.Resources.Strings.Prop_BinarizeBinaryInv },
                        { BinarizeMethod.Otsu, TaskFlow.Resources.Strings.Prop_BinarizeOtsu },
                        { BinarizeMethod.Triangle, TaskFlow.Resources.Strings.Prop_BinarizeTriangle }
                    });
                    AddIntProperty("BinarizeThreshold", TaskFlow.Resources.Strings.Prop_BinarizeThreshold, prepCard.BinarizeThreshold);
                    AddEnumComboProperty<MorphologyMethod>("MorphologyMethod", TaskFlow.Resources.Strings.Prop_MorphMethod, prepCard.MorphologyMethod, new Dictionary<MorphologyMethod, string>
                    {
                        { MorphologyMethod.None, TaskFlow.Resources.Strings.Prop_MorphNone },
                        { MorphologyMethod.Open, TaskFlow.Resources.Strings.Prop_MorphOpen },
                        { MorphologyMethod.Close, TaskFlow.Resources.Strings.Prop_MorphClose },
                        { MorphologyMethod.Dilate, TaskFlow.Resources.Strings.Prop_MorphDilate },
                        { MorphologyMethod.Erode, TaskFlow.Resources.Strings.Prop_MorphErode }
                    });
                    AddIntProperty("MorphologyKernelSize", TaskFlow.Resources.Strings.Prop_MorphKernelSize, prepCard.MorphologyKernelSize);
                    break;

                case ImgBlobAnalysisTaskCard blobCard:
                    AddImageSourceProperty_Generic(blobCard.UseSourceTaskImage, blobCard.SourceTaskIdForImage, blobCard.ImageFilePath);
                    // ROI区域（紧凑布局）
                    AddRoiCompactProperties(blobCard.RoiX, blobCard.RoiY, blobCard.RoiWidth, blobCard.RoiHeight);
                    // 框选区域 + 绘制掩膜
                    _currentMaskPath = blobCard.MaskImagePath;
                    var blobRoiBtn = new Button
                    {
                        Content = TaskFlow.Resources.Strings.Prop_SelectRoiMask,
                        Height = 32,
                        Margin = new Thickness(0, 0, 0, 4),
                        Style = FindResource("ActionButton") as Style
                    };
                    blobRoiBtn.Click += (s, e) => SelectRoiGeneric(blobCard.UseSourceTaskImage, blobCard.SourceTaskIdForImage, blobCard.ImageFilePath);
                    PropertyPanel.Children.Add(blobRoiBtn);
                    // 分割线
                    PropertyPanel.Children.Add(new Separator { Margin = new Thickness(0, 8, 0, 8), Background = new SolidColorBrush(Color.FromRgb(232, 230, 220)) });
                    AddIntProperty("MinArea", TaskFlow.Resources.Strings.Prop_MinArea, blobCard.MinArea);
                    AddIntProperty("MaxArea", TaskFlow.Resources.Strings.Prop_MaxArea, blobCard.MaxArea);
                    AddEnumComboProperty<BlobSortMode>("SortMode", TaskFlow.Resources.Strings.Prop_SortMode, blobCard.SortMode, new Dictionary<BlobSortMode, string>
                    {
                        { BlobSortMode.AreaDesc, TaskFlow.Resources.Strings.Prop_SortAreaDesc },
                        { BlobSortMode.AreaAsc, TaskFlow.Resources.Strings.Prop_SortAreaAsc },
                        { BlobSortMode.LeftToRight, TaskFlow.Resources.Strings.Prop_SortLeftToRight },
                        { BlobSortMode.TopToBottom, TaskFlow.Resources.Strings.Prop_SortTopToBottom }
                    });
                    AddIntProperty("MaxBlobCount", TaskFlow.Resources.Strings.Prop_MaxBlobCount, blobCard.MaxBlobCount);
                    AddCheckboxProperty("InvertBinary", TaskFlow.Resources.Strings.Prop_InvertBinary, blobCard.InvertBinary);
                    break;

                case ImgResizeTaskCard resizeCard:
                    AddImageSourceProperty_Generic(resizeCard.UseSourceTaskImage, resizeCard.SourceTaskIdForImage, resizeCard.ImageFilePath);
                    AddIntProperty("TargetWidth", TaskFlow.Resources.Strings.Prop_TargetWidth, resizeCard.TargetWidth);
                    AddIntProperty("TargetHeight", TaskFlow.Resources.Strings.Prop_TargetHeight, resizeCard.TargetHeight);
                    break;

                case ImgCaliperMeasureTaskCard caliperCard:
                    AddImageSourceProperty_Generic(caliperCard.UseSourceTaskImage, caliperCard.SourceTaskIdForImage, caliperCard.ImageFilePath);
                    AddRoiCompactProperties(caliperCard.RoiX, caliperCard.RoiY, caliperCard.RoiWidth, caliperCard.RoiHeight);
                    
                    var caliperRoiBtn = new Button { Content = TaskFlow.Resources.Strings.Prop_SelectRoiArea, Height = 32, Margin = new Thickness(0, 0, 0, 12), Style = FindResource("ActionButton") as Style };
                    caliperRoiBtn.Click += (s, e) => SelectRoiGeneric(caliperCard.UseSourceTaskImage, caliperCard.SourceTaskIdForImage, caliperCard.ImageFilePath);
                    PropertyPanel.Children.Add(caliperRoiBtn);
                    
                    var searchDirDict = new Dictionary<SearchDirection, string>
                    {
                        { SearchDirection.LeftToRight, "从左向右寻找(水平)" },
                        { SearchDirection.RightToLeft, "从右向左寻找(水平)" },
                        { SearchDirection.TopToBottom, "从上向下寻找(垂直)" },
                        { SearchDirection.BottomToTop, "从下向上寻找(垂直)" }
                    };
                    AddEnumComboProperty("SearchDirection", "测量搜索方向", caliperCard.SearchDirection, searchDirDict);

                    var polarityDict = new Dictionary<EdgePolarity, string>
                    {
                        { EdgePolarity.Any, "任意极性 (Any)" },
                        { EdgePolarity.DarkToLight, "黑到白 (Dark -> Light)" },
                        { EdgePolarity.LightToDark, "白到黑 (Light -> Dark)" }
                    };
                    
                    var selectionDict = new Dictionary<EdgeSelection, string>
                    {
                        { EdgeSelection.First, "第一边缘 (First)" },
                        { EdgeSelection.Last, "最后边缘 (Last)" },
                        { EdgeSelection.Best, "最佳边缘 (Best/Max Contrast)" },
                        { EdgeSelection.Darkest, "最暗边缘 (Darkest)" },
                        { EdgeSelection.Brightest, "最亮边缘 (Brightest)" }
                    };
                    
                    AddEnumComboProperty("Edge1Polarity", "第一边缘极性", caliperCard.Edge1Polarity, polarityDict);
                    AddEnumComboProperty("Edge1Selection", "第一边缘筛选", caliperCard.Edge1Selection, selectionDict);
                    
                    // 分隔符或空行稍微隔开
                    PropertyPanel.Children.Add(new Border { Height = 1, Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(220, 220, 220)), Margin = new System.Windows.Thickness(0, 4, 0, 8) });
                    
                    AddEnumComboProperty("Edge2Polarity", "第二边缘极性", caliperCard.Edge2Polarity, polarityDict);
                    AddEnumComboProperty("Edge2Selection", "第二边缘筛选", caliperCard.Edge2Selection, selectionDict);
                    break;


                case ExpressionEvalTaskCard exprCard:
                    AddExpressionEvalProperties(exprCard);
                    break;

                case BreakLoopTaskCard breakCard:
                    AddBreakLoopProperties(breakCard);
                    break;

                case StringSubstringTaskCard substringCard:
                    AddStringSubstringProperties(substringCard);
                    break;

                case TypeConvertTaskCard typeConvertCard:
                    AddTypeConvertProperties(typeConvertCard);
                    break;

                case ArrayParseTaskCard arrayParseCard:
                    AddArrayParseProperties(arrayParseCard);
                    break;

                case BrowserGetTextTaskCard browserGetCard:
                    AddBrowserGetTextProperties(browserGetCard);
                    break;

                case BrowserExecuteJsTaskCard browserJsCard:
                    AddBrowserExecuteJsProperties(browserJsCard);
                    break;

                case BrowserWaitForElementTaskCard browserWaitCard:
                    AddBrowserWaitForElementProperties(browserWaitCard);
                    break;

                case BrowserNativeClickTaskCard browserNativeClickCard:
                    AddBrowserNativeClickProperties(browserNativeClickCard);
                    break;

                case BrowserNativeInputTaskCard browserNativeInputCard:
                    AddBrowserNativeInputProperties(browserNativeInputCard);
                    break;

                case BrowserSimulatedClickTaskCard browserSimulatedClickCard:
                    AddBrowserSimulatedClickProperties(browserSimulatedClickCard);
                    break;

                case BrowserCdpCommandTaskCard browserCdpCommandCard:
                    AddBrowserCdpCommandProperties(browserCdpCommandCard);
                    break;
                case BrowserScreenshotTaskCard browserScreenshotCard:
                    AddBrowserScreenshotProperties(browserScreenshotCard);
                    break;

                case HttpRequestTaskCard httpRequestCard:
                    AddHttpRequestProperties(httpRequestCard);
                    break;
            }
        }

        #region Property Builder Methods

        private void AddTextProperty(string propertyName, string label, string value)
        {
            var labelBlock = new TextBlock { Text = label, Style = FindResource("PropertyLabel") as Style };
            var textBox = new TextBox { Text = value, Style = FindResource("PropertyTextBox") as Style };

            // 为文本属性输入框附加自动补全
            AutoCompleteHelper.Attach(textBox, _viewModel.VariableStore.Variables, _viewModel.TaskCards);

            PropertyPanel.Children.Add(labelBlock);
            PropertyPanel.Children.Add(textBox);
            _propertyControls[propertyName] = textBox;
        }

        private void AddIntProperty(string propertyName, string label, int value)
        {
            var labelBlock = new TextBlock { Text = label, Style = FindResource("PropertyLabel") as Style };
            var textBox = new TextBox { Text = value.ToString(), Style = FindResource("PropertyTextBox") as Style };

            PropertyPanel.Children.Add(labelBlock);
            PropertyPanel.Children.Add(textBox);
            _propertyControls[propertyName] = textBox;
        }

        /// <summary>
        /// 将已添加的 TextBox 包裹到 Grid 中，并在右侧添加 ... 浏览按钮（与模版匹配的图像路径一致）
        /// </summary>
        private void AddFileBrowseButton(string propertyName)
        {
            if (!_propertyControls.TryGetValue(propertyName, out var ctrl) || ctrl is not TextBox fileBox)
                return;

            // 从 PropertyPanel 中移除原始 TextBox
            PropertyPanel.Children.Remove(fileBox);

            // 创建 Grid 容器，与模版匹配的图像文件路径布局一致
            var fileGrid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            fileGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            fileGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            fileBox.Margin = new Thickness(0);
            Grid.SetColumn(fileBox, 0);

            var browseBtn = new Button { Content = "...", Width = 32, Margin = new Thickness(4, 0, 0, 0), VerticalAlignment = VerticalAlignment.Stretch, Style = FindResource("ActionButton") as Style };
            browseBtn.Click += (s, e) =>
            {
                var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "所有文件|*.*|文本文件|*.txt" };
                if (dlg.ShowDialog() == true) fileBox.Text = dlg.FileName;
            };
            Grid.SetColumn(browseBtn, 1);

            fileGrid.Children.Add(fileBox);
            fileGrid.Children.Add(browseBtn);
            PropertyPanel.Children.Add(fileGrid);
        }

        /// <summary>
        /// 以紧凑 2×2 网格布局添加 ROI 四个属性
        /// </summary>
        private void AddRoiCompactProperties(int roiX, int roiY, int roiW, int roiH)
        {
            var headerLabel = new TextBlock
            {
                Text = TaskFlow.Resources.Strings.Prop_RoiArea,
                Style = FindResource("PropertyLabel") as Style,
                Margin = new Thickness(0, 4, 0, 2)
            };
            PropertyPanel.Children.Add(headerLabel);

            var grid = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            // 4 列：每列一个 label+textbox 对，中间用间距列隔开
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4, GridUnitType.Pixel) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4, GridUnitType.Pixel) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4, GridUnitType.Pixel) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var items = new[]
            {
                ("RoiX", "X:", roiX, 0),
                ("RoiY", "Y:", roiY, 2),
                ("RoiWidth", TaskFlow.Resources.Strings.Prop_RoiW + ":", roiW, 4),
                ("RoiHeight", TaskFlow.Resources.Strings.Prop_RoiH + ":", roiH, 6),
            };

            foreach (var (key, label, val, col) in items)
            {
                var dock = new DockPanel();
                var lbl = new TextBlock
                {
                    Text = label,
                    Foreground = new SolidColorBrush(Color.FromRgb(107, 106, 101)),
                    VerticalAlignment = VerticalAlignment.Center,
                    FontSize = 12
                };
                DockPanel.SetDock(lbl, Dock.Left);
                var tb = new TextBox
                {
                    Text = val.ToString(),
                    Style = FindResource("PropertyTextBox") as Style,
                    Margin = new Thickness(4, 0, 0, 0)
                };
                dock.Children.Add(lbl);
                dock.Children.Add(tb);
                Grid.SetRow(dock, 0);
                Grid.SetColumn(dock, col);
                grid.Children.Add(dock);
                _propertyControls[key] = tb;
            }

            PropertyPanel.Children.Add(grid);
        }

        /// <summary>
        /// 以紧凑 3×2 网格布局添加 HSV 上下限属性
        /// </summary>
        private void AddHsvCompactProperties(int lH, int lS, int lV, int uH, int uS, int uV)
        {
            var headerLabel = new TextBlock
            {
                Text = TaskFlow.Resources.Strings.Prop_HsvRange,
                Style = FindResource("PropertyLabel") as Style,
                Margin = new Thickness(0, 4, 0, 2)
            };
            PropertyPanel.Children.Add(headerLabel);

            var grid = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4, GridUnitType.Pixel) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var items = new[]
            {
                ("HsvLowerH", TaskFlow.Resources.Strings.Prop_HsvLowerH, lH, 0, 0),
                ("HsvUpperH", TaskFlow.Resources.Strings.Prop_HsvUpperH, uH, 0, 2),
                ("HsvLowerS", TaskFlow.Resources.Strings.Prop_HsvLowerS, lS, 1, 0),
                ("HsvUpperS", TaskFlow.Resources.Strings.Prop_HsvUpperS, uS, 1, 2),
                ("HsvLowerV", TaskFlow.Resources.Strings.Prop_HsvLowerV, lV, 2, 0),
                ("HsvUpperV", TaskFlow.Resources.Strings.Prop_HsvUpperV, uV, 2, 2),
            };

            foreach (var (key, label, val, row, col) in items)
            {
                var dock = new DockPanel { Margin = new Thickness(0, 1, 0, 1) };
                var lbl = new TextBlock
                {
                    Text = label,
                    Foreground = new SolidColorBrush(Color.FromRgb(107, 106, 101)),
                    VerticalAlignment = VerticalAlignment.Center,
                    FontSize = 12
                };
                DockPanel.SetDock(lbl, Dock.Left);
                var tb = new TextBox
                {
                    Text = val.ToString(),
                    Style = FindResource("PropertyTextBox") as Style,
                    Margin = new Thickness(4, 0, 0, 0)
                };
                dock.Children.Add(lbl);
                dock.Children.Add(tb);
                Grid.SetRow(dock, row);
                Grid.SetColumn(dock, col);
                grid.Children.Add(dock);
                _propertyControls[key] = tb;
            }

            PropertyPanel.Children.Add(grid);
        }

        private void AddDoubleProperty(string propertyName, string label, double value)
        {
            var labelBlock = new TextBlock { Text = label, Style = FindResource("PropertyLabel") as Style };
            var textBox = new TextBox { Text = value.ToString("F2"), Style = FindResource("PropertyTextBox") as Style };

            PropertyPanel.Children.Add(labelBlock);
            PropertyPanel.Children.Add(textBox);
            _propertyControls[propertyName] = textBox;
        }

        private void AddCheckboxProperty(string propertyName, string label, bool value)
        {
            var checkBox = new CheckBox { Content = label, IsChecked = value, Style = FindResource("PropertyCheckBox") as Style };

            PropertyPanel.Children.Add(checkBox);
            _propertyControls[propertyName] = checkBox;
        }

        private void AddFilePathProperty(string propertyName, string label, string value, string filter, bool supportExpression = false)
        {
            var labelBlock = new TextBlock { Text = label, Style = FindResource("PropertyLabel") as Style };

            var grid = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var textBox = new TextBox
            {
                Text = value,
                Style = FindResource("PropertyTextBox") as Style,
                Margin = new Thickness(0)
            };
            Grid.SetColumn(textBox, 0);

            var browseButton = new Button
            {
                Content = "...",
                Width = 32,
                Margin = new Thickness(4, 0, 0, 0),
                VerticalAlignment = System.Windows.VerticalAlignment.Stretch,
                Style = FindResource("ActionButton") as Style
            };
            browseButton.Click += (s, e) =>
            {
                var dialog = new OpenFileDialog { Filter = filter };
                if (dialog.ShowDialog() == true)
                {
                    textBox.Text = dialog.FileName;
                }
            };
            Grid.SetColumn(browseButton, 1);

            if (supportExpression)
            {
                AutoCompleteHelper.Attach(textBox, _viewModel.VariableStore.Variables, _viewModel.TaskCards);
            }

            grid.Children.Add(textBox);
            grid.Children.Add(browseButton);

            PropertyPanel.Children.Add(labelBlock);
            PropertyPanel.Children.Add(grid);
            _propertyControls[propertyName] = textBox;
        }

        /// <summary>
        /// 添加条件表达式输入属性
        /// </summary>
        private void AddConditionExpressionProperty(IfElseBranchTaskCard card, string prefix = "")
        {
            string keyPrefix = string.IsNullOrEmpty(prefix) ? "" : prefix + "_";
            string labelPrefix = string.IsNullOrEmpty(prefix) ? "" : prefix + " ";

            // 条件表达式输入框
            var exprLabel = new TextBlock { Text = string.Format(TaskFlow.Resources.Strings.Prop_ConditionExpr, labelPrefix), Style = FindResource("PropertyLabel") as Style };
            var exprTextBox = new TextBox { Text = card.ConditionExpression, Style = FindResource("PropertyTextBox") as Style };

            // 为条件表达式输入框附加自动补全
            AutoCompleteHelper.Attach(exprTextBox, _viewModel.VariableStore.Variables, _viewModel.TaskCards);

            PropertyPanel.Children.Add(exprLabel);
            PropertyPanel.Children.Add(exprTextBox);
            _propertyControls[$"{keyPrefix}ConditionExpression"] = exprTextBox;

        }

        /// <summary>
        /// 在IfStart属性中动态显示同组所有elif卡片的条件设置
        /// </summary>
        private void AddElifConditionProperties(IfElseBranchTaskCard ifStartCard)
        {
            if (!ifStartCard.BranchGroupId.HasValue) return;

            var elifCards = _viewModel.TaskCards
                .Where(t => t.BranchGroupId == ifStartCard.BranchGroupId && t.BranchRole == BranchRole.ElifStart)
                .Cast<IfElseBranchTaskCard>()
                .ToList();

            for (int idx = 0; idx < elifCards.Count; idx++)
            {
                var elifCard = elifCards[idx];

                // 分隔线
                var separator = new Separator { Margin = new Thickness(0, 8, 0, 8), Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(232, 230, 220)) };
                PropertyPanel.Children.Add(separator);

                // 标题
                var titleBlock = new TextBlock
                {
                    Text = string.Format(TaskFlow.Resources.Strings.Prop_ElifCondition, idx + 1, elifCard.Order),
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(217, 119, 87)),
                    FontSize = 13,
                    Margin = new Thickness(0, 0, 0, 8),
                    Style = FindResource("PropertyLabel") as Style
                };
                PropertyPanel.Children.Add(titleBlock);

                AddConditionExpressionProperty(elifCard, $"Elif{idx}");
            }
        }

        private void AddClickProperties(WinClickTaskCard card)
        {
            // X/Y 坐标表达式
            AddTextProperty("StartXInput", TaskFlow.Resources.Strings.Prop_XExpr,
                !string.IsNullOrWhiteSpace(card.StartXExpression)
                    ? card.StartXExpression
                    : card.StartX.ToString());
            AddTextProperty("StartYInput", TaskFlow.Resources.Strings.Prop_YExpr,
                !string.IsNullOrWhiteSpace(card.StartYExpression)
                    ? card.StartYExpression
                    : card.StartY.ToString());

            // 点击类型
            var clickTypeLabel = new TextBlock { Text = TaskFlow.Resources.Strings.Prop_ClickType, Style = FindResource("PropertyLabel") as Style };
            var clickTypeCombo = new ComboBox { Style = FindResource("PropertyComboBox") as Style };
            clickTypeCombo.Items.Add(new ComboBoxItem { Content = TaskFlow.Resources.Strings.Prop_ClickSingle, Tag = ClickType.Single });
            clickTypeCombo.Items.Add(new ComboBoxItem { Content = TaskFlow.Resources.Strings.Prop_ClickDouble, Tag = ClickType.Double });
            clickTypeCombo.Items.Add(new ComboBoxItem { Content = TaskFlow.Resources.Strings.Prop_ClickSwipe, Tag = ClickType.Swipe });
            clickTypeCombo.SelectedIndex = (int)card.ClickType;

            PropertyPanel.Children.Add(clickTypeLabel);
            PropertyPanel.Children.Add(clickTypeCombo);
            _propertyControls["ClickType"] = clickTypeCombo;

            // 滑动专属面板
            var swipePanel = new StackPanel();
            var endXLabel = new TextBlock { Text = TaskFlow.Resources.Strings.Prop_EndXExpr, Style = FindResource("PropertyLabel") as Style };
            var endXBox = new TextBox { Text = card.EndX.ToString(), Style = FindResource("PropertyTextBox") as Style };
            AutoCompleteHelper.Attach(endXBox, _viewModel.VariableStore.Variables, _viewModel.TaskCards);
            var endYLabel = new TextBlock { Text = TaskFlow.Resources.Strings.Prop_EndYExpr, Style = FindResource("PropertyLabel") as Style };
            var endYBox = new TextBox { Text = card.EndY.ToString(), Style = FindResource("PropertyTextBox") as Style };
            AutoCompleteHelper.Attach(endYBox, _viewModel.VariableStore.Variables, _viewModel.TaskCards);
            var swipeDurLabel = new TextBlock { Text = TaskFlow.Resources.Strings.Prop_SwipeDuration, Style = FindResource("PropertyLabel") as Style };
            var swipeDurBox = new TextBox { Text = card.SwipeDurationMs.ToString(), Style = FindResource("PropertyTextBox") as Style };
            swipePanel.Children.Add(endXLabel);
            swipePanel.Children.Add(endXBox);
            swipePanel.Children.Add(endYLabel);
            swipePanel.Children.Add(endYBox);
            swipePanel.Children.Add(swipeDurLabel);
            swipePanel.Children.Add(swipeDurBox);
            _propertyControls["EndX"] = endXBox;
            _propertyControls["EndY"] = endYBox;
            _propertyControls["SwipeDurationMs"] = swipeDurBox;
            PropertyPanel.Children.Add(swipePanel);

            // 双击专属面板
            var doubleClickPanel = new StackPanel();
            var multiClickCheck = new CheckBox
            {
                Content = TaskFlow.Resources.Strings.Prop_MultiClick,
                IsChecked = card.MultiClickEnabled,
                Style = FindResource("PropertyCheckBox") as Style,
                Margin = new Thickness(0, 6, 0, 4)
            };
            var multiCountLabel = new TextBlock { Text = TaskFlow.Resources.Strings.Prop_ClickCount, Style = FindResource("PropertyLabel") as Style };
            var multiCountBox = new TextBox { Text = card.MultiClickCount.ToString(), Style = FindResource("PropertyTextBox") as Style };
            doubleClickPanel.Children.Add(multiClickCheck);
            doubleClickPanel.Children.Add(multiCountLabel);
            doubleClickPanel.Children.Add(multiCountBox);
            var intervalLabel = new TextBlock { Text = TaskFlow.Resources.Strings.Prop_ClickInterval, Style = FindResource("PropertyLabel") as Style };
            var intervalBox = new TextBox { Text = card.ClickIntervalMs.ToString(), Style = FindResource("PropertyTextBox") as Style };
            doubleClickPanel.Children.Add(intervalLabel);
            doubleClickPanel.Children.Add(intervalBox);
            _propertyControls["MultiClickEnabled"] = multiClickCheck;
            _propertyControls["MultiClickCount"] = multiCountBox;
            _propertyControls["ClickIntervalMs"] = intervalBox;
            PropertyPanel.Children.Add(doubleClickPanel);

            // 根据当前选择显隐
            swipePanel.Visibility = card.ClickType == ClickType.Swipe ? Visibility.Visible : Visibility.Collapsed;
            doubleClickPanel.Visibility = card.ClickType == ClickType.Double ? Visibility.Visible : Visibility.Collapsed;

            // 联动切换
            clickTypeCombo.SelectionChanged += (s, e) =>
            {
                if (clickTypeCombo.SelectedItem is ComboBoxItem item && item.Tag is ClickType ct)
                {
                    swipePanel.Visibility = ct == ClickType.Swipe ? Visibility.Visible : Visibility.Collapsed;
                    doubleClickPanel.Visibility = ct == ClickType.Double ? Visibility.Visible : Visibility.Collapsed;
                }
            };

            // 离屏点击功能
            PropertyPanel.Children.Add(new Separator { Margin = new Thickness(0, 8, 0, 8), Background = new SolidColorBrush(Color.FromRgb(232, 230, 220)) });
            var offScreenCheck = new CheckBox
            {
                Content = TaskFlow.Resources.Strings.Prop_EnableOffscreen,
                IsChecked = card.EnableOffScreenClick,
                Style = FindResource("PropertyCheckBox") as Style,
                Margin = new Thickness(0, 6, 0, 4)
            };
            var processLabel = new TextBlock { Text = TaskFlow.Resources.Strings.Prop_ProcessName, Style = FindResource("PropertyLabel") as Style };
            var processBox = new TextBox { Text = card.ProcessName, Style = FindResource("PropertyTextBox") as Style };

            _propertyControls["EnableOffScreenClick"] = offScreenCheck;
            _propertyControls["ProcessName"] = processBox;

            PropertyPanel.Children.Add(offScreenCheck);
            PropertyPanel.Children.Add(processLabel);
            PropertyPanel.Children.Add(processBox);

            // 联动隐藏进程名输入
            processLabel.Visibility = card.EnableOffScreenClick ? Visibility.Visible : Visibility.Collapsed;
            processBox.Visibility = card.EnableOffScreenClick ? Visibility.Visible : Visibility.Collapsed;
            offScreenCheck.Checked += (s, e) => { processLabel.Visibility = Visibility.Visible; processBox.Visibility = Visibility.Visible; };
            offScreenCheck.Unchecked += (s, e) => { processLabel.Visibility = Visibility.Collapsed; processBox.Visibility = Visibility.Collapsed; };
        }

        /// <summary>
        /// WinUI自动化任务卡片的属性编辑控件
        /// </summary>
        private void AddUiAutomationProperties(WinUiAutomationTaskCard card)
        {
            // 查找方式下拉框
            var searchByLabel = new TextBlock { Text = TaskFlow.Resources.Strings.Prop_SearchBy, Style = FindResource("PropertyLabel") as Style };
            var searchByCombo = new ComboBox { Style = FindResource("PropertyComboBox") as Style };
            searchByCombo.Items.Add(new ComboBoxItem { Content = TaskFlow.Resources.Strings.Prop_SearchByName, Tag = UiSearchBy.Name });
            searchByCombo.Items.Add(new ComboBoxItem { Content = TaskFlow.Resources.Strings.Prop_SearchByAutomationId, Tag = UiSearchBy.AutomationId });
            searchByCombo.SelectedIndex = (int)card.SearchBy;
            PropertyPanel.Children.Add(searchByLabel);
            PropertyPanel.Children.Add(searchByCombo);
            _propertyControls["SearchBy"] = searchByCombo;

            // 按钮名称输入框
            var btnNameLabel = new TextBlock { Text = TaskFlow.Resources.Strings.Prop_ButtonName, Style = FindResource("PropertyLabel") as Style };
            var btnNameBox = new TextBox { Text = card.ButtonName, Style = FindResource("PropertyTextBox") as Style };
            PropertyPanel.Children.Add(btnNameLabel);
            PropertyPanel.Children.Add(btnNameBox);
            _propertyControls["ButtonName"] = btnNameBox;

            // 匹配方式下拉框
            var matchModeLabel = new TextBlock { Text = TaskFlow.Resources.Strings.Prop_MatchMode, Style = FindResource("PropertyLabel") as Style };
            var matchModeCombo = new ComboBox { Style = FindResource("PropertyComboBox") as Style };
            matchModeCombo.Items.Add(new ComboBoxItem { Content = TaskFlow.Resources.Strings.Prop_MatchExact, Tag = UiMatchMode.Exact });
            matchModeCombo.Items.Add(new ComboBoxItem { Content = TaskFlow.Resources.Strings.Prop_MatchContains, Tag = UiMatchMode.Contains });
            matchModeCombo.Items.Add(new ComboBoxItem { Content = TaskFlow.Resources.Strings.Prop_MatchRegex, Tag = UiMatchMode.Regex });
            matchModeCombo.SelectedIndex = (int)card.MatchMode;
            PropertyPanel.Children.Add(matchModeLabel);
            PropertyPanel.Children.Add(matchModeCombo);
            _propertyControls["MatchMode"] = matchModeCombo;

            // AutomationId 输入框
            var autoIdLabel = new TextBlock { Text = "AutomationId", Style = FindResource("PropertyLabel") as Style };
            var autoIdBox = new TextBox { Text = card.AutomationId, Style = FindResource("PropertyTextBox") as Style };
            PropertyPanel.Children.Add(autoIdLabel);
            PropertyPanel.Children.Add(autoIdBox);
            _propertyControls["AutomationId"] = autoIdBox;

            // 联动显隐：按名称时显示按钮名称+匹配方式，隐藏AutomationId；反之亦然
            void UpdateVisibility(UiSearchBy searchBy)
            {
                var byName = searchBy == UiSearchBy.Name ? Visibility.Visible : Visibility.Collapsed;
                var byAutoId = searchBy == UiSearchBy.AutomationId ? Visibility.Visible : Visibility.Collapsed;
                btnNameLabel.Visibility = byName;
                btnNameBox.Visibility = byName;
                matchModeLabel.Visibility = byName;
                matchModeCombo.Visibility = byName;
                autoIdLabel.Visibility = byAutoId;
                autoIdBox.Visibility = byAutoId;
            }
            UpdateVisibility(card.SearchBy);

            searchByCombo.SelectionChanged += (s, e) =>
            {
                if (searchByCombo.SelectedItem is ComboBoxItem item && item.Tag is UiSearchBy sb)
                {
                    UpdateVisibility(sb);
                }
            };

            // ===== 拾取控件按钮 =====
            PropertyPanel.Children.Add(new Separator { Margin = new Thickness(0, 8, 0, 8), Background = new SolidColorBrush(Color.FromRgb(232, 230, 220)) });

            var inspectButton = new Button
            {
                Content = new TextBlock 
                { 
                    Text = "拾取控件（右键点击目标控件）", 
                    Margin = new Thickness(12, 6, 12, 6),
                    HorizontalAlignment = HorizontalAlignment.Center
                },
                Style = FindResource("SecondaryButton") as Style,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 4, 0, 8),
                Cursor = Cursors.Hand
            };

            // 拾取结果展示区（初始隐藏）
            var inspectResultPanel = new StackPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 4, 0, 0) };
            var inspectResultBox = new TextBox
            {
                IsReadOnly = true,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                MaxHeight = 160,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Style = FindResource("PropertyTextBox") as Style,
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(80, 80, 80))
            };
            inspectResultPanel.Children.Add(inspectResultBox);

            inspectButton.Click += async (s, e) =>
            {
                // 记住窗口所有者，避免最小化后丢失
                var ownerWindow = this.Owner;

                // 最小化属性对话框，让出屏幕空间
                this.WindowState = WindowState.Minimized;
                // 同时最小化主窗口
                ownerWindow?.Dispatcher.Invoke(() =>
                {
                    if (ownerWindow.WindowState != WindowState.Minimized)
                        ownerWindow.WindowState = WindowState.Minimized;
                });

                // 等待一小段时间让窗口最小化动画完成
                await System.Threading.Tasks.Task.Delay(400);

                try
                {
                    // 等待右键按下
                    while (!Win32Helper.IsMouseRightButtonDown())
                    {
                        await System.Threading.Tasks.Task.Delay(50);
                    }

                    // 获取鼠标坐标
                    var (cursorX, cursorY) = Win32Helper.GetCurrentCursorPosition();

                    // 等待右键释放，避免干扰目标程序
                    while (Win32Helper.IsMouseRightButtonDown())
                    {
                        await System.Threading.Tasks.Task.Delay(30);
                    }

                    // 通过 UI Automation 获取该坐标处的控件信息
                    var point = new System.Windows.Point(cursorX, cursorY);
                    var element = System.Windows.Automation.AutomationElement.FromPoint(point);

                    if (element != null)
                    {
                        string elName = element.Current.Name ?? "";
                        string elAutoId = element.Current.AutomationId ?? "";
                        string elClassName = element.Current.ClassName ?? "";
                        string elControlType = element.Current.ControlType?.ProgrammaticName ?? "";
                        int elProcessId = element.Current.ProcessId;

                        // 获取进程名称
                        string elProcessName = "";
                        try
                        {
                            var proc = System.Diagnostics.Process.GetProcessById(elProcessId);
                            elProcessName = proc.ProcessName;
                        }
                        catch { /* 进程可能已退出 */ }

                        // 恢复窗口
                        this.WindowState = WindowState.Normal;
                        this.Activate();
                        ownerWindow?.Dispatcher.Invoke(() =>
                        {
                            if (ownerWindow.WindowState == WindowState.Minimized)
                                ownerWindow.WindowState = WindowState.Normal;
                        });

                        // 自动填入进程名
                        if (!string.IsNullOrEmpty(elProcessName) &&
                            _propertyControls.TryGetValue("ProcessName", out var procCtrl) &&
                            procCtrl is TextBox procTextBox)
                        {
                            procTextBox.Text = elProcessName;
                        }

                        // 根据当前查找方式自动填入对应字段
                        if (!string.IsNullOrEmpty(elAutoId))
                        {
                            // 如果有 AutomationId，优先切换到 AutomationId 模式并填入
                            searchByCombo.SelectedIndex = (int)UiSearchBy.AutomationId;
                            autoIdBox.Text = elAutoId;
                        }
                        else if (!string.IsNullOrEmpty(elName))
                        {
                            // 否则使用名称模式
                            searchByCombo.SelectedIndex = (int)UiSearchBy.Name;
                            btnNameBox.Text = elName;
                        }

                        // 显示完整的拾取结果
                        inspectResultBox.Text =
                            $"Name: {elName}\n" +
                            $"AutomationId: {elAutoId}\n" +
                            $"ClassName: {elClassName}\n" +
                            $"ControlType: {elControlType}\n" +
                            $"ProcessName: {elProcessName}\n" +
                            $"ProcessId: {elProcessId}\n" +
                            $"Position: ({cursorX}, {cursorY})";
                        inspectResultPanel.Visibility = Visibility.Visible;
                    }
                    else
                    {
                        // 恢复窗口
                        this.WindowState = WindowState.Normal;
                        this.Activate();
                        MessageBox.Show("未能获取到控件信息", "", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
                catch (Exception ex)
                {
                    // 确保窗口恢复
                    this.WindowState = WindowState.Normal;
                    this.Activate();
                    ownerWindow?.Dispatcher.Invoke(() =>
                    {
                        if (ownerWindow.WindowState == WindowState.Minimized)
                            ownerWindow.WindowState = WindowState.Normal;
                    });
                    MessageBox.Show($"拾取失败: {ex.Message}", "", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            };

            PropertyPanel.Children.Add(inspectButton);
            PropertyPanel.Children.Add(inspectResultPanel);
        }

        private void AddSimulateInputProperties(WinSimulateInputTaskCard card)
        {
            AddEnumComboProperty<ModifierKeyType>("ModifierKey", TaskFlow.Resources.Strings.Prop_ModifierKey, card.ModifierKey, new Dictionary<ModifierKeyType, string>
            {
                { ModifierKeyType.None, TaskFlow.Resources.Strings.Prop_BinarizeNone },
                { ModifierKeyType.Ctrl, "Ctrl" },
                { ModifierKeyType.Shift, "Shift" },
                { ModifierKeyType.Alt, "Alt" },
                { ModifierKeyType.CtrlShift, "Ctrl + Shift" },
                { ModifierKeyType.CtrlAlt, "Ctrl + Alt" }
            });

            // 输入动作
            var actionLabel = new TextBlock { Text = TaskFlow.Resources.Strings.Prop_InputAction, Style = FindResource("PropertyLabel") as Style };
            var actionCombo = new ComboBox { Style = FindResource("PropertyComboBox") as Style };
            actionCombo.Items.Add(new ComboBoxItem { Content = TaskFlow.Resources.Strings.Prop_ScrollDown, Tag = InputActionType.ScrollDown });
            actionCombo.Items.Add(new ComboBoxItem { Content = TaskFlow.Resources.Strings.Prop_ScrollUp, Tag = InputActionType.ScrollUp });
            actionCombo.Items.Add(new ComboBoxItem { Content = TaskFlow.Resources.Strings.Prop_KeyPress, Tag = InputActionType.KeyPress });

            foreach (ComboBoxItem item in actionCombo.Items)
            {
                if ((InputActionType)item.Tag == card.ActionType)
                {
                    actionCombo.SelectedItem = item;
                    break;
                }
            }
            PropertyPanel.Children.Add(actionLabel);
            PropertyPanel.Children.Add(actionCombo);
            _propertyControls["ActionType"] = actionCombo;

            // 按键名称
            var keyNameLabel = new TextBlock { Text = TaskFlow.Resources.Strings.Prop_KeyName, Style = FindResource("PropertyLabel") as Style };
            var keyNameBox = new TextBox { Text = card.KeyName, Style = FindResource("PropertyTextBox") as Style };
            PropertyPanel.Children.Add(keyNameLabel);
            PropertyPanel.Children.Add(keyNameBox);
            _propertyControls["KeyName"] = keyNameBox;

            // 滚动量
            var scrollLabel = new TextBlock { Text = TaskFlow.Resources.Strings.Prop_ScrollAmount, Style = FindResource("PropertyLabel") as Style };
            var scrollBox = new TextBox { Text = card.ScrollAmount.ToString(), Style = FindResource("PropertyTextBox") as Style };
            PropertyPanel.Children.Add(scrollLabel);
            PropertyPanel.Children.Add(scrollBox);
            _propertyControls["ScrollAmount"] = scrollBox;

            // 更新显示逻辑
            void UpdateVisibility(InputActionType action)
            {
                var showKey = action == InputActionType.KeyPress ? Visibility.Visible : Visibility.Collapsed;
                var showScroll = (action == InputActionType.ScrollUp || action == InputActionType.ScrollDown) ? Visibility.Visible : Visibility.Collapsed;

                keyNameLabel.Visibility = showKey;
                keyNameBox.Visibility = showKey;

                scrollLabel.Visibility = showScroll;
                scrollBox.Visibility = showScroll;
            }

            UpdateVisibility(card.ActionType);

            actionCombo.SelectionChanged += (s, e) =>
            {
                if (actionCombo.SelectedItem is ComboBoxItem item && item.Tag is InputActionType act)
                {
                    UpdateVisibility(act);
                }
            };

            AddIntProperty("RepeatCount", TaskFlow.Resources.Strings.Prop_RepeatCount, card.RepeatCount);
            AddIntProperty("IntervalMs", TaskFlow.Resources.Strings.Prop_RepeatInterval, card.IntervalMs);
        }

        private void AddLlmTranslateProperties(LlmTranslateTaskCard card)
        {
            // 模型选择ComboBox
            var modelLabel = new TextBlock { Text = TaskFlow.Resources.Strings.Prop_SelectModel, Style = FindResource("PropertyLabel") as Style };
            var modelCombo = new ComboBox
            {
                Style = FindResource("PropertyComboBox") as Style,
                DisplayMemberPath = "DisplayName",
                SelectedValuePath = "Id",
                Margin = new Thickness(0, 0, 0, 8)
            };

            // 加载模型列表并绑定

            modelCombo.ItemsSource = TaskFlow.Helpers.LlmModelManager.Models;

            if (!string.IsNullOrEmpty(card.ModelId))
            {
                modelCombo.SelectedValue = card.ModelId;
            }

            modelCombo.SelectionChanged += (s, e) =>
            {
                var selectedId = modelCombo.SelectedValue?.ToString() ?? "";
                if (card.ModelId != selectedId)
                {
                    card.ModelId = selectedId;
                }
            };

            PropertyPanel.Children.Add(modelLabel);
            PropertyPanel.Children.Add(modelCombo);

            // 待翻译文本，支持补全
            AddTextProperty("SourceTextExpression", TaskFlow.Resources.Strings.Prop_SourceText, card.SourceTextExpression);
            
            AddTextProperty("TargetLanguage", TaskFlow.Resources.Strings.Prop_TargetLanguage, card.TargetLanguage);
            
            // System Prompt 使用多行TextBox
            var promptLabel = new TextBlock { Text = TaskFlow.Resources.Strings.Prop_SystemPrompt, Style = FindResource("PropertyLabel") as Style };
            var promptTextBox = new TextBox
            {
                Text = card.SystemPrompt,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                Height = 80,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Background = Brushes.White,
                Padding = new Thickness(6, 4, 6, 4),
                Margin = new Thickness(0, 0, 0, 8)
            };
            
            promptTextBox.TextChanged += (s, e) => 
            {
                if (card.SystemPrompt != promptTextBox.Text)
                {
                    card.SystemPrompt = promptTextBox.Text;
                }
            };

            PropertyPanel.Children.Add(promptLabel);
            PropertyPanel.Children.Add(promptTextBox);
        }

        /// <summary>
        /// 多模态识图任务卡片的属性编辑控件
        /// </summary>
        private void AddLlmVisionProperties(LlmVisionTaskCard card)
        {
            // 模型选择ComboBox
            var modelLabel = new TextBlock { Text = TaskFlow.Resources.Strings.Prop_SelectModel, Style = FindResource("PropertyLabel") as Style };
            var modelCombo = new ComboBox
            {
                Style = FindResource("PropertyComboBox") as Style,
                DisplayMemberPath = "DisplayName",
                SelectedValuePath = "Id",
                Margin = new Thickness(0, 0, 0, 8)
            };

            // 加载模型列表并绑定

            modelCombo.ItemsSource = TaskFlow.Helpers.LlmModelManager.Models;

            if (!string.IsNullOrEmpty(card.ModelId))
            {
                modelCombo.SelectedValue = card.ModelId;
            }

            modelCombo.SelectionChanged += (s, e) =>
            {
                var selectedId = modelCombo.SelectedValue?.ToString() ?? "";
                if (card.ModelId != selectedId)
                {
                    card.ModelId = selectedId;
                }
            };

            PropertyPanel.Children.Add(modelLabel);
            PropertyPanel.Children.Add(modelCombo);

            // 图像来源（复用通用图像来源控件）
            PropertyPanel.Children.Add(new Separator { Margin = new Thickness(0, 8, 0, 8), Background = new SolidColorBrush(Color.FromRgb(232, 230, 220)) });
            AddImageSourceProperty_Generic(card.UseSourceTaskImage, card.SourceTaskIdForImage, card.ImageFilePath);

            // 提示词，支持补全
            PropertyPanel.Children.Add(new Separator { Margin = new Thickness(0, 8, 0, 8), Background = new SolidColorBrush(Color.FromRgb(232, 230, 220)) });
            AddTextProperty("PromptExpression", TaskFlow.Resources.Strings.Prop_Prompt, card.PromptExpression);

            // System Prompt 使用多行TextBox
            var promptLabel = new TextBlock { Text = TaskFlow.Resources.Strings.Prop_SystemPrompt, Style = FindResource("PropertyLabel") as Style };
            var promptTextBox = new TextBox
            {
                Text = card.SystemPrompt,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                Height = 80,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Background = Brushes.White,
                Padding = new Thickness(6, 4, 6, 4),
                Margin = new Thickness(0, 0, 0, 8)
            };

            promptTextBox.TextChanged += (s, e) =>
            {
                if (card.SystemPrompt != promptTextBox.Text)
                {
                    card.SystemPrompt = promptTextBox.Text;
                }
            };

            PropertyPanel.Children.Add(promptLabel);
            PropertyPanel.Children.Add(promptTextBox);
        }

        /// <summary>
        /// Win字幕提示任务卡片的属性编辑控件
        /// </summary>
        private void AddSubtitleProperties(WinSubtitleTaskCard card)
        {
            // ==================== 显示文本表达式 ====================
            AddTextProperty("DisplayText", TaskFlow.Resources.Strings.Prop_DisplayText, card.DisplayText);

            // ==================== 是否指定窗口 ====================
            // 用于控制进程名称和框选显示区域按钮的条件显隐
            var windowSpecPanel = new StackPanel(); // 容纳进程名 + 框选按钮

            var useWindowCheck = new CheckBox
            {
                Content = TaskFlow.Resources.Strings.Prop_SpecifyWindow,
                IsChecked = card.UseSpecifiedWindow,
                Style = FindResource("PropertyCheckBox") as Style,
                Foreground = new SolidColorBrush(Color.FromRgb(217, 119, 87)),
                Margin = new Thickness(0, 8, 0, 4)
            };
            PropertyPanel.Children.Add(useWindowCheck);
            _propertyControls["UseSpecifiedWindow"] = useWindowCheck;

            // 进程名称
            var procLabel = new TextBlock { Text = TaskFlow.Resources.Strings.Prop_ProcessName, Style = FindResource("PropertyLabel") as Style };
            var procBox = new TextBox { Text = card.ProcessName, Style = FindResource("PropertyTextBox") as Style };
            AutoCompleteHelper.Attach(procBox, _viewModel.VariableStore.Variables, _viewModel.TaskCards);
            windowSpecPanel.Children.Add(procLabel);
            windowSpecPanel.Children.Add(procBox);
            _propertyControls["ProcessName"] = procBox;

            // 框选显示区域按钮
            var selectRegionBtn = new Button
            {
                Content = TaskFlow.Resources.Strings.Prop_SelectSubtitleRegion,
                Style = FindResource("ToolbarButton") as Style,
                Margin = new Thickness(0, 4, 0, 8),
                HorizontalAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(12, 6, 12, 6)
            };
            selectRegionBtn.Click += async (s, e) =>
            {
                string procName = procBox.Text.Trim();
                if (string.IsNullOrEmpty(procName))
                {
                    MessageBox.Show(TaskFlow.Resources.Strings.Prop_FillProcessFirst, "", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var screenshotService = new Services.ScreenshotService();
                var result = await screenshotService.CaptureWindowAsync(procName);
                if (!result.Success || result.Image == null)
                {
                    MessageBox.Show(string.Format(TaskFlow.Resources.Strings.Prop_ScreenshotFailed, result.Error), "", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                int initX = 0, initY = 0, initW = 0, initH = 0;
                if (_propertyControls.TryGetValue("OffsetX", out var oxCtrl) && oxCtrl is TextBox oxBox)
                    int.TryParse(oxBox.Text, out initX);
                if (_propertyControls.TryGetValue("OffsetY", out var oyCtrl) && oyCtrl is TextBox oyBox)
                    int.TryParse(oyBox.Text, out initY);
                if (_propertyControls.TryGetValue("SubtitleWidth", out var swCtrl) && swCtrl is TextBox swBox)
                    int.TryParse(swBox.Text, out initW);
                if (_propertyControls.TryGetValue("SubtitleHeight", out var shCtrl) && shCtrl is TextBox shBox)
                    int.TryParse(shBox.Text, out initH);

                var roiWindow = new RoiSelectionWindow(result.Image, initX, initY, initW, initH, "");
                roiWindow.Owner = this;
                roiWindow.Title = TaskFlow.Resources.Strings.Prop_SelectSubtitleArea;
                if (roiWindow.ShowDialog() == true)
                {
                    if (_propertyControls.TryGetValue("OffsetX", out var ox) && ox is TextBox oxTb)
                        oxTb.Text = roiWindow.RoiX.ToString();
                    if (_propertyControls.TryGetValue("OffsetY", out var oy) && oy is TextBox oyTb)
                        oyTb.Text = roiWindow.RoiY.ToString();
                    if (_propertyControls.TryGetValue("SubtitleWidth", out var sw) && sw is TextBox swTb)
                        swTb.Text = roiWindow.RoiWidth.ToString();
                    if (_propertyControls.TryGetValue("SubtitleHeight", out var sh) && sh is TextBox shTb)
                        shTb.Text = roiWindow.RoiHeight.ToString();
                }
                result.Image.Dispose();
            };
            windowSpecPanel.Children.Add(selectRegionBtn);

            // 条件显隐：勾选"是否指定窗口"后才显示进程名和框选按钮
            windowSpecPanel.Visibility = card.UseSpecifiedWindow ? Visibility.Visible : Visibility.Collapsed;
            useWindowCheck.Checked += (s, e) => windowSpecPanel.Visibility = Visibility.Visible;
            useWindowCheck.Unchecked += (s, e) => windowSpecPanel.Visibility = Visibility.Collapsed;
            PropertyPanel.Children.Add(windowSpecPanel);

            // ==================== X偏移量 + Y偏移量（同一行） ====================
            var offsetLabel = new TextBlock
            {
                Text = TaskFlow.Resources.Strings.Prop_XYOffset,
                Style = FindResource("PropertyLabel") as Style,
                Margin = new Thickness(0, 4, 0, 2)
            };
            PropertyPanel.Children.Add(offsetLabel);

            var offsetGrid = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            offsetGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            offsetGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8, GridUnitType.Pixel) });
            offsetGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var offsetXBox = new TextBox { Text = card.OffsetX.ToString(), Style = FindResource("PropertyTextBox") as Style };
            Grid.SetColumn(offsetXBox, 0);
            offsetGrid.Children.Add(offsetXBox);
            _propertyControls["OffsetX"] = offsetXBox;

            var offsetYBox = new TextBox { Text = card.OffsetY.ToString(), Style = FindResource("PropertyTextBox") as Style };
            Grid.SetColumn(offsetYBox, 2);
            offsetGrid.Children.Add(offsetYBox);
            _propertyControls["OffsetY"] = offsetYBox;

            PropertyPanel.Children.Add(offsetGrid);

            // ==================== 字幕宽度 + 字幕高度（同一行） ====================
            var sizeLabel = new TextBlock
            {
                Text = TaskFlow.Resources.Strings.Prop_SubtitleWidthHeight,
                Style = FindResource("PropertyLabel") as Style,
                Margin = new Thickness(0, 4, 0, 2)
            };
            PropertyPanel.Children.Add(sizeLabel);

            var sizeGrid = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            sizeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            sizeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8, GridUnitType.Pixel) });
            sizeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var widthBox = new TextBox { Text = card.SubtitleWidth.ToString(), Style = FindResource("PropertyTextBox") as Style };
            Grid.SetColumn(widthBox, 0);
            sizeGrid.Children.Add(widthBox);
            _propertyControls["SubtitleWidth"] = widthBox;

            var heightBox = new TextBox { Text = card.SubtitleHeight.ToString(), Style = FindResource("PropertyTextBox") as Style };
            Grid.SetColumn(heightBox, 2);
            sizeGrid.Children.Add(heightBox);
            _propertyControls["SubtitleHeight"] = heightBox;

            PropertyPanel.Children.Add(sizeGrid);

            // ==================== 字体大小 + 字体颜色（颜色选择器） ====================
            AddTextProperty("FontSize", TaskFlow.Resources.Strings.Prop_FontSize, card.FontSize.ToString());

            // 字体颜色行：标签 + 颜色文本框 + 颜色选择按钮
            var colorLabel = new TextBlock { Text = TaskFlow.Resources.Strings.Prop_FontColor, Style = FindResource("PropertyLabel") as Style };
            PropertyPanel.Children.Add(colorLabel);

            var colorGrid = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            colorGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            colorGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8, GridUnitType.Pixel) });
            colorGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var colorBox = new TextBox { Text = card.TextColor, Style = FindResource("PropertyTextBox") as Style };
            Grid.SetColumn(colorBox, 0);
            colorGrid.Children.Add(colorBox);
            _propertyControls["TextColor"] = colorBox;

            // 颜色预览色块按钮（统一样式 + 亮度反转图标）
            var colorPreviewBtn = CreateColorPickerButton(colorBox, card.TextColor, TaskFlow.Resources.Strings.Prop_SelectColor);

            Grid.SetColumn(colorPreviewBtn, 2);
            colorGrid.Children.Add(colorPreviewBtn);
            PropertyPanel.Children.Add(colorGrid);

            // ==================== 背景样式下拉框 ====================
            var bgLabel = new TextBlock { Text = TaskFlow.Resources.Strings.Prop_BackgroundStyle, Style = FindResource("PropertyLabel") as Style };
            var bgCombo = new ComboBox { Style = FindResource("PropertyComboBox") as Style };
            bgCombo.Items.Add(new ComboBoxItem { Content = TaskFlow.Resources.Strings.Prop_BgAcrylic, Tag = SubtitleBackground.Acrylic });
            bgCombo.Items.Add(new ComboBoxItem { Content = TaskFlow.Resources.Strings.Prop_BgSolid, Tag = SubtitleBackground.SolidColor });
            bgCombo.Items.Add(new ComboBoxItem { Content = TaskFlow.Resources.Strings.Prop_BgTransparent, Tag = SubtitleBackground.Transparent });
            bgCombo.Items.Add(new ComboBoxItem { Content = TaskFlow.Resources.Strings.Prop_BgAutoSample, Tag = SubtitleBackground.AutoSample });
            bgCombo.SelectedIndex = (int)card.Background;
            PropertyPanel.Children.Add(bgLabel);
            PropertyPanel.Children.Add(bgCombo);
            _propertyControls["Background"] = bgCombo;

            // ==================== 背景色（仅毛玻璃/纯色背景时显示）+ 颜色选择按钮 ====================
            var bgColorPanel = new StackPanel();
            var bgColorLabel = new TextBlock { Text = TaskFlow.Resources.Strings.Prop_BgColor, Style = FindResource("PropertyLabel") as Style };

            var bgColorGrid = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            bgColorGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            bgColorGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8, GridUnitType.Pixel) });
            bgColorGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var bgColorBox = new TextBox { Text = card.BackgroundColor, Style = FindResource("PropertyTextBox") as Style };
            Grid.SetColumn(bgColorBox, 0);
            bgColorGrid.Children.Add(bgColorBox);
            _propertyControls["BackgroundColor"] = bgColorBox;

            // 背景色颜色选择按钮（统一样式 + 亮度反转图标）
            var bgColorPreviewBtn = CreateColorPickerButton(bgColorBox, card.BackgroundColor, TaskFlow.Resources.Strings.Prop_SelectBgColor);

            Grid.SetColumn(bgColorPreviewBtn, 2);
            bgColorGrid.Children.Add(bgColorPreviewBtn);

            bgColorPanel.Children.Add(bgColorLabel);
            bgColorPanel.Children.Add(bgColorGrid);

            // 初始可见性
            bool showBgColor = card.Background == SubtitleBackground.Acrylic || card.Background == SubtitleBackground.SolidColor;
            bgColorPanel.Visibility = showBgColor ? Visibility.Visible : Visibility.Collapsed;

            // 切换背景样式时条件显隐
            bgCombo.SelectionChanged += (s, e) =>
            {
                if (bgCombo.SelectedItem is ComboBoxItem item && item.Tag is SubtitleBackground bg)
                {
                    bool show = bg == SubtitleBackground.Acrylic || bg == SubtitleBackground.SolidColor;
                    bgColorPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
                }
            };
            PropertyPanel.Children.Add(bgColorPanel);

            // ==================== 不显示吸色排除膜（SampleMaskPath 移除） ====================

            // ==================== 显示时长 ====================
            AddTextProperty("DurationMs", TaskFlow.Resources.Strings.Prop_Duration, card.DurationMs.ToString());

            // ==================== 等待字幕关闭后再继续执行（暖红色字体） ====================
            var waitCheck = new CheckBox
            {
                Content = TaskFlow.Resources.Strings.Prop_WaitClose,
                IsChecked = card.WaitUntilClosed,
                Style = FindResource("PropertyCheckBox") as Style,
                Foreground = new SolidColorBrush(Color.FromRgb(217, 119, 87)),
                Margin = new Thickness(0, 4, 0, 4)
            };
            PropertyPanel.Children.Add(waitCheck);
            _propertyControls["WaitUntilClosed"] = waitCheck;
        }

        /// <summary>
        /// 为颜色选择器按钮创建纯色块模板
        /// </summary>
        private FrameworkElementFactory CreateColorBlockTemplate()
        {
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
            border.SetValue(Border.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(200, 200, 200)));
            border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            return border;
        }

        /// <summary>
        /// 创建带🎨图标的颜色选择按钮模板
        /// </summary>
        private FrameworkElementFactory CreateColorBlockWithIconTemplate()
        {
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
            border.SetValue(Border.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(200, 200, 200)));
            border.SetValue(Border.BorderThicknessProperty, new Thickness(1));

            var contentPresenter = new FrameworkElementFactory(typeof(ContentPresenter));
            contentPresenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            contentPresenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);

            border.AppendChild(contentPresenter);
            return border;
        }

        /// <summary>
        /// 创建颜色选择按钮（统一样式：固定高度、🎨图标、亮度反转）
        /// </summary>
        private Button CreateColorPickerButton(TextBox colorBox, string initialColor, string tooltip)
        {
            var iconText = new TextBlock { Text = "🎨", FontSize = 13 };
            var btn = new Button
            {
                Width = 36,
                Height = 32,
                Content = iconText,
                Cursor = System.Windows.Input.Cursors.Hand,
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromRgb(232, 230, 220)),
                Margin = new Thickness(0),
                ToolTip = tooltip
            };

            btn.Template = new ControlTemplate(typeof(Button))
            {
                VisualTree = CreateColorBlockWithIconTemplate()
            };

            // 设置初始颜色和图标颜色
            UpdateColorButtonAppearance(btn, iconText, initialColor);

            // 文本框变化时同步更新按钮
            colorBox.TextChanged += (s, e) => UpdateColorButtonAppearance(btn, iconText, colorBox.Text);

            // 点击弹出系统颜色选择器
            btn.Click += (s, e) =>
            {
                var dlg = new System.Windows.Forms.ColorDialog();
                try
                {
                    var cur = (Color)System.Windows.Media.ColorConverter.ConvertFromString(colorBox.Text.Trim());
                    dlg.Color = System.Drawing.Color.FromArgb(cur.A, cur.R, cur.G, cur.B);
                }
                catch { }
                if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    var chosen = dlg.Color;
                    string hex = $"#{chosen.R:X2}{chosen.G:X2}{chosen.B:X2}";
                    colorBox.Text = hex;
                }
            };

            return btn;
        }

        /// <summary>
        /// 更新颜色按钮背景和根据亮度反转图标颜色
        /// </summary>
        private static void UpdateColorButtonAppearance(Button btn, TextBlock iconText, string colorStr)
        {
            try
            {
                var c = (Color)System.Windows.Media.ColorConverter.ConvertFromString(colorStr.Trim());
                btn.Background = new SolidColorBrush(c);
                // 计算相对亮度 (ITU-R BT.709)
                double brightness = 0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B;
                iconText.Foreground = new SolidColorBrush(brightness < 128 ? Colors.White : Colors.Black);
            }
            catch
            {
                btn.Background = new SolidColorBrush(Colors.Gray);
                iconText.Foreground = new SolidColorBrush(Colors.White);
            }
        }

        private void AddAdbClickProperties(AdbClickTaskCard card)
        {
            // X/Y 坐标表达式
            AddTextProperty("StartXInput", TaskFlow.Resources.Strings.Prop_XExpr,
                !string.IsNullOrWhiteSpace(card.StartXExpression)
                    ? card.StartXExpression
                    : card.StartX.ToString());
            AddTextProperty("StartYInput", TaskFlow.Resources.Strings.Prop_YExpr,
                !string.IsNullOrWhiteSpace(card.StartYExpression)
                    ? card.StartYExpression
                    : card.StartY.ToString());

            var clickTypeLabel = new TextBlock { Text = TaskFlow.Resources.Strings.Prop_ClickType, Style = FindResource("PropertyLabel") as Style };
            var clickTypeCombo = new ComboBox { Style = FindResource("PropertyComboBox") as Style };
            clickTypeCombo.Items.Add(new ComboBoxItem { Content = TaskFlow.Resources.Strings.Prop_ClickSingle, Tag = ClickType.Single });
            clickTypeCombo.Items.Add(new ComboBoxItem { Content = TaskFlow.Resources.Strings.Prop_ClickDouble, Tag = ClickType.Double });
            clickTypeCombo.Items.Add(new ComboBoxItem { Content = TaskFlow.Resources.Strings.Prop_ClickSwipe, Tag = ClickType.Swipe });
            clickTypeCombo.SelectedIndex = (int)card.ClickType;

            PropertyPanel.Children.Add(clickTypeLabel);
            PropertyPanel.Children.Add(clickTypeCombo);
            _propertyControls["ClickType"] = clickTypeCombo;

            // 滑动专属面板
            var swipePanel = new StackPanel();
            var endXLabel = new TextBlock { Text = TaskFlow.Resources.Strings.Prop_EndXExpr, Style = FindResource("PropertyLabel") as Style };
            var endXBox = new TextBox { Text = card.EndX.ToString(), Style = FindResource("PropertyTextBox") as Style };
            AutoCompleteHelper.Attach(endXBox, _viewModel.VariableStore.Variables, _viewModel.TaskCards);
            var endYLabel = new TextBlock { Text = TaskFlow.Resources.Strings.Prop_EndYExpr, Style = FindResource("PropertyLabel") as Style };
            var endYBox = new TextBox { Text = card.EndY.ToString(), Style = FindResource("PropertyTextBox") as Style };
            AutoCompleteHelper.Attach(endYBox, _viewModel.VariableStore.Variables, _viewModel.TaskCards);
            var swipeDurLabel = new TextBlock { Text = TaskFlow.Resources.Strings.Prop_SwipeDuration, Style = FindResource("PropertyLabel") as Style };
            var swipeDurBox = new TextBox { Text = card.SwipeDurationMs.ToString(), Style = FindResource("PropertyTextBox") as Style };
            swipePanel.Children.Add(endXLabel);
            swipePanel.Children.Add(endXBox);
            swipePanel.Children.Add(endYLabel);
            swipePanel.Children.Add(endYBox);
            swipePanel.Children.Add(swipeDurLabel);
            swipePanel.Children.Add(swipeDurBox);
            _propertyControls["EndX"] = endXBox;
            _propertyControls["EndY"] = endYBox;
            _propertyControls["SwipeDurationMs"] = swipeDurBox;
            PropertyPanel.Children.Add(swipePanel);

            // 双击专属面板
            var doubleClickPanel = new StackPanel();
            var multiClickCheck = new CheckBox
            {
                Content = TaskFlow.Resources.Strings.Prop_MultiClick,
                IsChecked = card.MultiClickEnabled,
                Style = FindResource("PropertyCheckBox") as Style,
                Margin = new Thickness(0, 6, 0, 4)
            };
            var multiCountLabel = new TextBlock { Text = TaskFlow.Resources.Strings.Prop_ClickCount, Style = FindResource("PropertyLabel") as Style };
            var multiCountBox = new TextBox { Text = card.MultiClickCount.ToString(), Style = FindResource("PropertyTextBox") as Style };
            doubleClickPanel.Children.Add(multiClickCheck);
            doubleClickPanel.Children.Add(multiCountLabel);
            doubleClickPanel.Children.Add(multiCountBox);
            var intervalLabel = new TextBlock { Text = TaskFlow.Resources.Strings.Prop_ClickInterval, Style = FindResource("PropertyLabel") as Style };
            var intervalBox = new TextBox { Text = card.ClickIntervalMs.ToString(), Style = FindResource("PropertyTextBox") as Style };
            doubleClickPanel.Children.Add(intervalLabel);
            doubleClickPanel.Children.Add(intervalBox);
            _propertyControls["MultiClickEnabled"] = multiClickCheck;
            _propertyControls["MultiClickCount"] = multiCountBox;
            _propertyControls["ClickIntervalMs"] = intervalBox;
            PropertyPanel.Children.Add(doubleClickPanel);

            // 根据当前选择显隐
            swipePanel.Visibility = card.ClickType == ClickType.Swipe ? Visibility.Visible : Visibility.Collapsed;
            doubleClickPanel.Visibility = card.ClickType == ClickType.Double ? Visibility.Visible : Visibility.Collapsed;

            // 联动切换
            clickTypeCombo.SelectionChanged += (s, e) =>
            {
                if (clickTypeCombo.SelectedItem is ComboBoxItem item && item.Tag is ClickType ct)
                {
                    swipePanel.Visibility = ct == ClickType.Swipe ? Visibility.Visible : Visibility.Collapsed;
                    doubleClickPanel.Visibility = ct == ClickType.Double ? Visibility.Visible : Visibility.Collapsed;
                }
            };
        }

        private void AddImageSourceProperty(ImgCropTaskCard card)
        {
            // 图像来源任务下拉框
            var taskLabel = new TextBlock { Text = TaskFlow.Resources.Strings.Prop_ImageSourceTask, Style = FindResource("PropertyLabel") as Style };
            var taskCombo = new ComboBox { Style = FindResource("PropertyComboBox") as Style };
            taskCombo.Items.Add(new ComboBoxItem { Content = TaskFlow.Resources.Strings.Prop_SelectTask, Tag = null });
            foreach (var task in _viewModel.GetImageOutputTasks().Where(t => t.Id != _task.Id))
                taskCombo.Items.Add(new ComboBoxItem { Content = $"#{task.Order} {task.Name}", Tag = task.Id });
            taskCombo.SelectedIndex = 0;
            if (card.SourceTaskIdForImage.HasValue)
                for (int i = 1; i < taskCombo.Items.Count; i++)
                    if (((ComboBoxItem)taskCombo.Items[i]).Tag is Guid id && id == card.SourceTaskIdForImage) { taskCombo.SelectedIndex = i; break; }

            // 图像文件路径
            var fileLabel = new TextBlock { Text = TaskFlow.Resources.Strings.Prop_ImageFilePath, Style = FindResource("PropertyLabel") as Style };
            var fileGrid = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            fileGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            fileGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var fileBox = new TextBox { Text = card.ImageFilePath ?? "", Style = FindResource("PropertyTextBox") as Style, Margin = new Thickness(0) };
            Grid.SetColumn(fileBox, 0);
            var browseBtn = new Button { Content = "...", Width = 32, Margin = new Thickness(4, 0, 0, 0), VerticalAlignment = VerticalAlignment.Stretch, Style = FindResource("ActionButton") as Style };
            browseBtn.Click += (s, e) => { var dlg = new OpenFileDialog { Filter = TaskFlow.Resources.Strings.Filter_ImageFile }; if (dlg.ShowDialog() == true) fileBox.Text = dlg.FileName; };
            Grid.SetColumn(browseBtn, 1);
            fileGrid.Children.Add(fileBox); fileGrid.Children.Add(browseBtn);
            _propertyControls["ImageFilePath"] = fileBox;

            void UpdateVis(bool chk) { taskLabel.Visibility = taskCombo.Visibility = chk ? Visibility.Visible : Visibility.Collapsed; fileLabel.Visibility = fileGrid.Visibility = chk ? Visibility.Collapsed : Visibility.Visible; }
            var cb = new CheckBox { Content = TaskFlow.Resources.Strings.Prop_UseSourceTaskImage, IsChecked = card.UseSourceTaskImage, Style = FindResource("PropertyCheckBox") as Style };
            cb.Checked += (s, e) => UpdateVis(true); cb.Unchecked += (s, e) => UpdateVis(false);
            PropertyPanel.Children.Add(cb); _propertyControls["UseSourceTaskImage"] = cb;
            PropertyPanel.Children.Add(taskLabel); PropertyPanel.Children.Add(taskCombo); _propertyControls["SourceTaskIdForImage"] = taskCombo;
            PropertyPanel.Children.Add(fileLabel); PropertyPanel.Children.Add(fileGrid);
            UpdateVis(card.UseSourceTaskImage);
        }

        private void AddOnnxDetectProperties(ImgOnnxDetectTaskCard card)
        {
            AddImageSourceProperty_Generic(card.UseSourceTaskImage, card.SourceTaskIdForImage, card.ImageFilePath);

            // 分割线
            PropertyPanel.Children.Add(new Separator { Margin = new Thickness(0, 8, 0, 8), Background = new SolidColorBrush(Color.FromRgb(232, 230, 220)) });

            var modelLabel = new TextBlock { Text = TaskFlow.Resources.Strings.Main_VisionModels, Style = FindResource("PropertyLabel") as Style };
            var modelCombo = new ComboBox { Style = FindResource("PropertyComboBox") as Style };
            modelCombo.DisplayMemberPath = "DisplayName";
            modelCombo.SelectedValuePath = "Id";
            modelCombo.ItemsSource = TaskFlow.Helpers.OnnxModelManager.Models;
            modelCombo.SelectedValue = card.OnnxModelId;

            PropertyPanel.Children.Add(modelLabel);
            PropertyPanel.Children.Add(modelCombo);
            _propertyControls["OnnxModelId"] = modelCombo;

            AddTextProperty("FilterClassName", "过滤类别 (多选逗号分隔)", card.FilterClassName ?? "");
            AddDoubleProperty("ConfidenceOverride", "置信度覆盖 (0=默认)", card.ConfidenceOverride);
        }

        private void AddImageSourcePropertyMatch(ImgTemplateMatchTaskCard card)
        {
            var taskLabel = new TextBlock { Text = TaskFlow.Resources.Strings.Prop_ImageSourceTask, Style = FindResource("PropertyLabel") as Style };
            var taskCombo = new ComboBox { Style = FindResource("PropertyComboBox") as Style };
            taskCombo.Items.Add(new ComboBoxItem { Content = TaskFlow.Resources.Strings.Prop_SelectTask, Tag = null });
            foreach (var task in _viewModel.GetImageOutputTasks().Where(t => t.Id != _task.Id))
                taskCombo.Items.Add(new ComboBoxItem { Content = $"#{task.Order} {task.Name}", Tag = task.Id });
            taskCombo.SelectedIndex = 0;
            if (card.SourceTaskIdForImage.HasValue)
                for (int i = 1; i < taskCombo.Items.Count; i++)
                    if (((ComboBoxItem)taskCombo.Items[i]).Tag is Guid id && id == card.SourceTaskIdForImage) { taskCombo.SelectedIndex = i; break; }

            var fileLabel = new TextBlock { Text = TaskFlow.Resources.Strings.Prop_ImageFilePath, Style = FindResource("PropertyLabel") as Style };
            var fileGrid = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            fileGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            fileGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var fileBox = new TextBox { Text = card.ImageFilePath ?? "", Style = FindResource("PropertyTextBox") as Style, Margin = new Thickness(0) };
            Grid.SetColumn(fileBox, 0);
            var browseBtn = new Button { Content = "...", Width = 32, Margin = new Thickness(4, 0, 0, 0), VerticalAlignment = VerticalAlignment.Stretch, Style = FindResource("ActionButton") as Style };
            browseBtn.Click += (s, e) => { var dlg = new OpenFileDialog { Filter = TaskFlow.Resources.Strings.Filter_ImageFile }; if (dlg.ShowDialog() == true) fileBox.Text = dlg.FileName; };
            Grid.SetColumn(browseBtn, 1);
            fileGrid.Children.Add(fileBox); fileGrid.Children.Add(browseBtn);
            _propertyControls["ImageFilePath"] = fileBox;

            void UpdateVis(bool chk) { taskLabel.Visibility = taskCombo.Visibility = chk ? Visibility.Visible : Visibility.Collapsed; fileLabel.Visibility = fileGrid.Visibility = chk ? Visibility.Collapsed : Visibility.Visible; }
            var cb = new CheckBox { Content = TaskFlow.Resources.Strings.Prop_UseSourceTaskImage, IsChecked = card.UseSourceTaskImage, Style = FindResource("PropertyCheckBox") as Style };
            cb.Checked += (s, e) => UpdateVis(true); cb.Unchecked += (s, e) => UpdateVis(false);
            PropertyPanel.Children.Add(cb); _propertyControls["UseSourceTaskImage"] = cb;
            PropertyPanel.Children.Add(taskLabel); PropertyPanel.Children.Add(taskCombo); _propertyControls["SourceTaskIdForImage"] = taskCombo;
            PropertyPanel.Children.Add(fileLabel); PropertyPanel.Children.Add(fileGrid);
            UpdateVis(card.UseSourceTaskImage);
        }

        // 当前 OCR/模板匹配的掩膜路径
        private string? _currentMaskPath;

        private void AddImageSourcePropertyOcr(ImgOcrTaskCard card)
        {
            // OCR 引擎选择
            var engineLabel = new TextBlock { Text = TaskFlow.Resources.Strings.Prop_OcrEngine, Style = FindResource("PropertyLabel") as Style };
            PropertyPanel.Children.Add(engineLabel);
            var engineCombo = new ComboBox { Style = FindResource("PropertyComboBox") as Style };
            engineCombo.Items.Add(new ComboBoxItem { Content = TaskFlow.Resources.Strings.Prop_PaddleOcr, Tag = OcrEngine.PaddleOCR });

            // 微信 OCR 选项：根据设置中的验证状态决定是否可选
            var wechatItem = new ComboBoxItem { Content = TaskFlow.Resources.Strings.Prop_WeChatOcr, Tag = OcrEngine.WeChatOCR };
            if (!_viewModel.Settings.WeChatOcrVerified)
            {
                wechatItem.IsEnabled = false;
                wechatItem.ToolTip = TaskFlow.Resources.Strings.Prop_WeChatOcrTip;
            }
            engineCombo.Items.Add(wechatItem);

            engineCombo.SelectedIndex = (int)card.OcrEngine;
            PropertyPanel.Children.Add(engineCombo);
            _propertyControls["OcrEngine"] = engineCombo;

            // 图像来源（条件显示）
            var taskLabel = new TextBlock { Text = TaskFlow.Resources.Strings.Prop_ImageSourceTask, Style = FindResource("PropertyLabel") as Style };
            var taskCombo = new ComboBox { Style = FindResource("PropertyComboBox") as Style };
            taskCombo.Items.Add(new ComboBoxItem { Content = TaskFlow.Resources.Strings.Prop_SelectTask, Tag = null });
            foreach (var task in _viewModel.GetImageOutputTasks().Where(t => t.Id != _task.Id))
                taskCombo.Items.Add(new ComboBoxItem { Content = $"#{task.Order} {task.Name}", Tag = task.Id });
            taskCombo.SelectedIndex = 0;
            if (card.SourceTaskIdForImage.HasValue)
                for (int i = 1; i < taskCombo.Items.Count; i++)
                    if (((ComboBoxItem)taskCombo.Items[i]).Tag is Guid id && id == card.SourceTaskIdForImage) { taskCombo.SelectedIndex = i; break; }

            var fileLabel = new TextBlock { Text = TaskFlow.Resources.Strings.Prop_ImageFilePath, Style = FindResource("PropertyLabel") as Style };
            var fileGrid = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            fileGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            fileGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var fileBox = new TextBox { Text = card.ImageFilePath ?? "", Style = FindResource("PropertyTextBox") as Style, Margin = new Thickness(0) };
            Grid.SetColumn(fileBox, 0);
            var browseBtn = new Button { Content = "...", Width = 32, Margin = new Thickness(4, 0, 0, 0), VerticalAlignment = VerticalAlignment.Stretch, Style = FindResource("ActionButton") as Style };
            browseBtn.Click += (s, e) => { var dlg = new OpenFileDialog { Filter = TaskFlow.Resources.Strings.Filter_ImageFile }; if (dlg.ShowDialog() == true) fileBox.Text = dlg.FileName; };
            Grid.SetColumn(browseBtn, 1);
            fileGrid.Children.Add(fileBox); fileGrid.Children.Add(browseBtn);
            _propertyControls["ImageFilePath"] = fileBox;

            void UpdateVis(bool chk) { taskLabel.Visibility = taskCombo.Visibility = chk ? Visibility.Visible : Visibility.Collapsed; fileLabel.Visibility = fileGrid.Visibility = chk ? Visibility.Collapsed : Visibility.Visible; }
            var cb = new CheckBox { Content = TaskFlow.Resources.Strings.Prop_UseSourceTaskImage, IsChecked = card.UseSourceTaskImage, Style = FindResource("PropertyCheckBox") as Style };
            cb.Checked += (s, e) => UpdateVis(true); cb.Unchecked += (s, e) => UpdateVis(false);
            PropertyPanel.Children.Add(cb); _propertyControls["UseSourceTaskImage"] = cb;
            PropertyPanel.Children.Add(taskLabel); PropertyPanel.Children.Add(taskCombo); _propertyControls["SourceTaskIdForImage"] = taskCombo;
            PropertyPanel.Children.Add(fileLabel); PropertyPanel.Children.Add(fileGrid);
            UpdateVis(card.UseSourceTaskImage);

            // ROI区域（紧凑布局）
            AddRoiCompactProperties(card.RoiX, card.RoiY, card.RoiWidth, card.RoiHeight);

            // 合并按钮：框选识别区域 + 绘制掩膜
            _currentMaskPath = card.MaskImagePath;
            var selectRoiBtn = new Button
            {
                Content = TaskFlow.Resources.Strings.Prop_SelectRoiMask,
                Height = 32,
                Margin = new Thickness(0, 0, 0, 4),
                Style = FindResource("ActionButton") as Style
            };
            selectRoiBtn.Click += (s, e) => SelectRoiGeneric(card.UseSourceTaskImage, card.SourceTaskIdForImage, card.ImageFilePath);
            PropertyPanel.Children.Add(selectRoiBtn);
        }

        /// <summary>
        /// 添加掩膜画笔按钮（OCR 和模板匹配共用）
        /// </summary>
        private void AddMaskPaintButton(bool useSourceTaskImage, Guid? sourceTaskIdForImage, string? imageFilePath)
        {
            // 掩膜状态显示
            var maskStatusLabel = new TextBlock
            {
                Text = string.IsNullOrEmpty(_currentMaskPath) ? TaskFlow.Resources.Strings.Prop_MaskNotSet : TaskFlow.Resources.Strings.Prop_MaskSet,
                Foreground = string.IsNullOrEmpty(_currentMaskPath)
                    ? new SolidColorBrush(Color.FromRgb(176, 174, 165))
                    : new SolidColorBrush(Color.FromRgb(120, 140, 93)),
                FontSize = 11,
                Margin = new Thickness(0, 4, 0, 4)
            };
            PropertyPanel.Children.Add(maskStatusLabel);

            var maskBtn = new Button
            {
                Content = TaskFlow.Resources.Strings.Prop_MaskPaint,
                Height = 32,
                Margin = new Thickness(0, 0, 0, 4),
                Style = FindResource("ActionButton") as Style
            };
            maskBtn.Click += (s, e) =>
            {
                OpenMaskPaintWindow(useSourceTaskImage, sourceTaskIdForImage, imageFilePath, maskStatusLabel);
            };
            PropertyPanel.Children.Add(maskBtn);

            // 清除掩膜按钮
            var clearMaskBtn = new Button
            {
                Content = TaskFlow.Resources.Strings.Prop_ClearMask,
                Height = 32,
                Margin = new Thickness(0, 0, 0, 12),
                Style = FindResource("DangerButton") as Style
            };
            clearMaskBtn.Click += (s, e) =>
            {
                _currentMaskPath = null;
                maskStatusLabel.Text = TaskFlow.Resources.Strings.Prop_MaskNotSet;
                maskStatusLabel.Foreground = new SolidColorBrush(Color.FromRgb(176, 174, 165));
            };
            PropertyPanel.Children.Add(clearMaskBtn);
        }

        /// <summary>
        /// 打开掩膜画笔窗口
        /// </summary>
        private void OpenMaskPaintWindow(bool useSourceTaskImage, Guid? sourceTaskIdForImage, string? imageFilePath,
            TextBlock maskStatusLabel)
        {
            Mat? sourceImage = null;

            if (useSourceTaskImage && sourceTaskIdForImage.HasValue)
            {
                var sourceTask = _viewModel.TaskCards.FirstOrDefault(t => t.Id == sourceTaskIdForImage.Value);
                if (sourceTask?.OutputImage != null && !sourceTask.OutputImage.Empty())
                {
                    sourceImage = sourceTask.OutputImage.Clone();
                }
            }

            if (sourceImage == null && !string.IsNullOrEmpty(imageFilePath) && System.IO.File.Exists(imageFilePath))
            {
                sourceImage = Cv2.ImRead(imageFilePath);
            }

            if (sourceImage == null || sourceImage.Empty())
            {
                AnthropicMessageDialog.ShowInfo("", TaskFlow.Resources.Strings.Prop_SetImageFirst, this);
                sourceImage?.Dispose();
                return;
            }

            try
            {
                var maskWindow = new MaskPaintWindow(sourceImage, _currentMaskPath)
                {
                    Owner = this
                };

                if (maskWindow.ShowDialog() == true)
                {
                    _currentMaskPath = maskWindow.MaskPath;
                    if (string.IsNullOrEmpty(_currentMaskPath))
                    {
                        maskStatusLabel.Text = TaskFlow.Resources.Strings.Prop_MaskNotSet;
                        maskStatusLabel.Foreground = new SolidColorBrush(Color.FromRgb(176, 174, 165));
                    }
                    else
                    {
                        maskStatusLabel.Text = TaskFlow.Resources.Strings.Prop_MaskSet;
                        maskStatusLabel.Foreground = new SolidColorBrush(Color.FromRgb(120, 140, 93));
                    }
                }
            }
            finally
            {
                sourceImage.Dispose();
            }
        }

        private void AddRoiProperty(ImgCropTaskCard card)
        {
            // ROI 区域：支持表达式引用
            var headerLabel = new TextBlock
            {
                Text = TaskFlow.Resources.Strings.Prop_RoiArea,
                Style = FindResource("PropertyLabel") as Style,
                Margin = new Thickness(0, 4, 0, 2)
            };
            PropertyPanel.Children.Add(headerLabel);

            // 将值/表达式转化为显示文本
            string GetDisplayValue(string expr, int val) =>
                !string.IsNullOrWhiteSpace(expr) ? expr : val.ToString();

            AddTextProperty("RoiXInput", TaskFlow.Resources.Strings.Prop_XExpr, GetDisplayValue(card.RoiXExpression, card.RoiX));
            AddTextProperty("RoiYInput", TaskFlow.Resources.Strings.Prop_YExpr, GetDisplayValue(card.RoiYExpression, card.RoiY));
            AddTextProperty("RoiWidthInput", TaskFlow.Resources.Strings.Prop_WidthExpr, GetDisplayValue(card.RoiWidthExpression, card.RoiWidth));
            AddTextProperty("RoiHeightInput", TaskFlow.Resources.Strings.Prop_HeightExpr, GetDisplayValue(card.RoiHeightExpression, card.RoiHeight));

            var selectButton = new Button
            {
                Content = TaskFlow.Resources.Strings.Prop_SelectRoiArea,
                Height = 32,
                Margin = new Thickness(0, 0, 0, 12),
                Style = FindResource("ActionButton") as Style
            };
            selectButton.Click += (s, e) => SelectRoi(card);
            PropertyPanel.Children.Add(selectButton);
        }

        private void AddTemplateProperty(ImgTemplateMatchTaskCard card)
        {
            // ==================== 模板设置分区标题 ====================
            var sectionTitle = new TextBlock
            {
                Text = TaskFlow.Resources.Strings.Prop_TemplateSectionTitle,
                Style = FindResource("PropertyLabel") as Style,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 8, 0, 4)
            };
            PropertyPanel.Children.Add(sectionTitle);

            // ==================== 勾选：引用其他任务输出作为模板 ====================
            var templateTaskLabel = new TextBlock { Text = TaskFlow.Resources.Strings.Prop_TemplateSourceTask, Style = FindResource("PropertyLabel") as Style };
            var templateTaskCombo = new ComboBox { Style = FindResource("PropertyComboBox") as Style };
            templateTaskCombo.Items.Add(new ComboBoxItem { Content = TaskFlow.Resources.Strings.Prop_SelectTask, Tag = null });
            foreach (var task in _viewModel.GetImageOutputTasks().Where(t => t.Id != _task.Id))
                templateTaskCombo.Items.Add(new ComboBoxItem { Content = $"#{task.Order} {task.Name}", Tag = task.Id });
            templateTaskCombo.SelectedIndex = 0;
            if (card.SourceTaskIdForTemplate.HasValue)
                for (int i = 1; i < templateTaskCombo.Items.Count; i++)
                    if (((ComboBoxItem)templateTaskCombo.Items[i]).Tag is Guid id && id == card.SourceTaskIdForTemplate) { templateTaskCombo.SelectedIndex = i; break; }
            _propertyControls["SourceTaskIdForTemplate"] = templateTaskCombo;

            // 静态模板面板（模板预览 + 框选按钮）
            var staticTemplatePanel = new StackPanel();

            // 模板图像路径（隐藏控件，仅用于保存时读取）
            var templatePathBox = new TextBox { Text = card.TemplateImagePath ?? string.Empty, Visibility = Visibility.Collapsed };
            staticTemplatePanel.Children.Add(templatePathBox);
            _propertyControls["TemplateImagePath"] = templatePathBox;

            // 显示模板图像预览
            if (!string.IsNullOrEmpty(card.TemplateImagePath) && System.IO.File.Exists(card.TemplateImagePath))
            {
                try
                {
                    var templateLabel = new TextBlock
                    {
                        Text = TaskFlow.Resources.Strings.Prop_TemplatePreview,
                        Style = FindResource("PropertyLabel") as Style
                    };
                    staticTemplatePanel.Children.Add(templateLabel);

                    var bitmap = new System.Windows.Media.Imaging.BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(card.TemplateImagePath, UriKind.Absolute);
                    bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                    bitmap.EndInit();
                    bitmap.Freeze();

                    var templateImage = new System.Windows.Controls.Image
                    {
                        Source = bitmap,
                        Stretch = Stretch.Uniform,
                        MaxHeight = 200,
                        HorizontalAlignment = HorizontalAlignment.Left,
                        Margin = new Thickness(0, 0, 0, 12)
                    };

                    var border = new Border
                    {
                        Background = new SolidColorBrush(Color.FromRgb(26, 26, 42)),
                        BorderBrush = new SolidColorBrush(Color.FromRgb(68, 68, 68)),
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(4),
                        Padding = new Thickness(4),
                        Child = templateImage
                    };
                    staticTemplatePanel.Children.Add(border);
                }
                catch { /* 图像加载失败则跳过预览 */ }
            }

            var selectButton = new Button
            {
                Content = TaskFlow.Resources.Strings.Prop_SelectTemplate,
                Height = 32,
                Margin = new Thickness(0, 0, 0, 12),
                Style = FindResource("ActionButton") as Style
            };
            selectButton.Click += (s, e) => SelectTemplateRoi(card);
            staticTemplatePanel.Children.Add(selectButton);

            // 联动显隐
            void UpdateTemplateVis(bool useDynamic)
            {
                templateTaskLabel.Visibility = templateTaskCombo.Visibility = useDynamic ? Visibility.Visible : Visibility.Collapsed;
                staticTemplatePanel.Visibility = useDynamic ? Visibility.Collapsed : Visibility.Visible;
            }

            var useSourceTemplateCb = new CheckBox
            {
                Content = TaskFlow.Resources.Strings.Prop_UseSourceTaskTemplate,
                IsChecked = card.UseSourceTaskTemplate,
                Style = FindResource("PropertyCheckBox") as Style,
                Margin = new Thickness(0, 0, 0, 4)
            };
            useSourceTemplateCb.Checked += (s, e) => UpdateTemplateVis(true);
            useSourceTemplateCb.Unchecked += (s, e) => UpdateTemplateVis(false);
            _propertyControls["UseSourceTaskTemplate"] = useSourceTemplateCb;

            PropertyPanel.Children.Add(useSourceTemplateCb);
            PropertyPanel.Children.Add(templateTaskLabel);
            PropertyPanel.Children.Add(templateTaskCombo);
            PropertyPanel.Children.Add(staticTemplatePanel);
            UpdateTemplateVis(card.UseSourceTaskTemplate);

            // ROI区域框选
            AddRoiCompactProperties(card.RoiX, card.RoiY, card.RoiWidth, card.RoiHeight);

            // 合并按钮：框选搜索区域 + 绘制掩膜
            _currentMaskPath = card.MaskImagePath;
            var selectRoiBtn = new Button
            {
                Content = TaskFlow.Resources.Strings.Prop_SelectSearchRoiMask,
                Height = 32,
                Margin = new Thickness(0, 0, 0, 4),
                Style = FindResource("ActionButton") as Style
            };
            selectRoiBtn.Click += (s, e) => SelectRoiGeneric(card.UseSourceTaskImage, card.SourceTaskIdForImage, card.ImageFilePath);
            PropertyPanel.Children.Add(selectRoiBtn);
        }

        private void SelectRoi(ImgCropTaskCard card)
        {
            // 获取源图像
            Mat? sourceImage = null;

            if (card.UseSourceTaskImage && card.SourceTaskIdForImage.HasValue)
            {
                var sourceTask = _viewModel.TaskCards.FirstOrDefault(t => t.Id == card.SourceTaskIdForImage.Value);
                if (sourceTask?.OutputImage != null && !sourceTask.OutputImage.Empty())
                {
                    sourceImage = sourceTask.OutputImage.Clone();
                }
            }

            if (sourceImage == null && !string.IsNullOrEmpty(card.ImageFilePath) && System.IO.File.Exists(card.ImageFilePath))
            {
                sourceImage = Cv2.ImRead(card.ImageFilePath);
            }

            if (sourceImage == null || sourceImage.Empty())
            {
                AnthropicMessageDialog.ShowInfo("", TaskFlow.Resources.Strings.Prop_SetImageFirst, this);
                sourceImage?.Dispose();
                return;
            }

            try
            {
                var roiWindow = new RoiSelectionWindow(sourceImage)
                {
                    Owner = this
                };

                if (roiWindow.ShowDialog() == true)
                {
                    // 更新ROI坐标文本框
                    if (_propertyControls.TryGetValue("RoiXInput", out var xCtrl) && xCtrl is TextBox xBox)
                        xBox.Text = roiWindow.RoiX.ToString();
                    if (_propertyControls.TryGetValue("RoiYInput", out var yCtrl) && yCtrl is TextBox yBox)
                        yBox.Text = roiWindow.RoiY.ToString();
                    if (_propertyControls.TryGetValue("RoiWidthInput", out var wCtrl) && wCtrl is TextBox wBox)
                        wBox.Text = roiWindow.RoiWidth.ToString();
                    if (_propertyControls.TryGetValue("RoiHeightInput", out var hCtrl) && hCtrl is TextBox hBox)
                        hBox.Text = roiWindow.RoiHeight.ToString();
                }
            }
            finally
            {
                sourceImage.Dispose();
            }
        }

        /// <summary>
        /// 通用ROI框选+掩膜绘制方法（合并窗口）
        /// </summary>
        private void SelectRoiGeneric(bool useSourceTaskImage, Guid? sourceTaskIdForImage, string? imageFilePath)
        {
            Mat? sourceImage = null;

            if (useSourceTaskImage && sourceTaskIdForImage.HasValue)
            {
                var sourceTask = _viewModel.TaskCards.FirstOrDefault(t => t.Id == sourceTaskIdForImage.Value);
                if (sourceTask?.OutputImage != null && !sourceTask.OutputImage.Empty())
                {
                    sourceImage = sourceTask.OutputImage.Clone();
                }
            }

            if (sourceImage == null && !string.IsNullOrEmpty(imageFilePath) && System.IO.File.Exists(imageFilePath))
            {
                sourceImage = Cv2.ImRead(imageFilePath);
            }

            if (sourceImage == null || sourceImage.Empty())
            {
                AnthropicMessageDialog.ShowInfo("", TaskFlow.Resources.Strings.Prop_SetImageFirst, this);
                sourceImage?.Dispose();
                return;
            }

            try
            {
                // 读取已有ROI值
                int initX = 0, initY = 0, initW = 0, initH = 0;
                if (_propertyControls.TryGetValue("RoiX", out var xCtrl) && xCtrl is TextBox xBox && int.TryParse(xBox.Text, out int rx)) initX = rx;
                if (_propertyControls.TryGetValue("RoiY", out var yCtrl) && yCtrl is TextBox yBox && int.TryParse(yBox.Text, out int ry)) initY = ry;
                if (_propertyControls.TryGetValue("RoiWidth", out var wCtrl) && wCtrl is TextBox wBox && int.TryParse(wBox.Text, out int rw)) initW = rw;
                if (_propertyControls.TryGetValue("RoiHeight", out var hCtrl) && hCtrl is TextBox hBox && int.TryParse(hBox.Text, out int rh)) initH = rh;

                var roiWindow = new RoiSelectionWindow(sourceImage, initX, initY, initW, initH, _currentMaskPath)
                {
                    Owner = this
                };

                if (roiWindow.ShowDialog() == true)
                {
                    // 回写 ROI 值
                    if (_propertyControls.TryGetValue("RoiX", out var xCtrl2) && xCtrl2 is TextBox xBox2)
                        xBox2.Text = roiWindow.RoiX.ToString();
                    if (_propertyControls.TryGetValue("RoiY", out var yCtrl2) && yCtrl2 is TextBox yBox2)
                        yBox2.Text = roiWindow.RoiY.ToString();
                    if (_propertyControls.TryGetValue("RoiWidth", out var wCtrl2) && wCtrl2 is TextBox wBox2)
                        wBox2.Text = roiWindow.RoiWidth.ToString();
                    if (_propertyControls.TryGetValue("RoiHeight", out var hCtrl2) && hCtrl2 is TextBox hBox2)
                        hBox2.Text = roiWindow.RoiHeight.ToString();

                    // 回写掩膜路径
                    _currentMaskPath = roiWindow.MaskPath;
                }
            }
            finally
            {
                sourceImage.Dispose();
            }
        }

        private void SelectTemplateRoi(ImgTemplateMatchTaskCard card)
        {
            // 获取源图像
            Mat? sourceImage = null;

            // 优先从任务结果获取
            if (card.UseSourceTaskImage && card.SourceTaskIdForImage.HasValue)
            {
                var sourceTask = _viewModel.TaskCards.FirstOrDefault(t => t.Id == card.SourceTaskIdForImage.Value);
                if (sourceTask?.OutputImage != null && !sourceTask.OutputImage.Empty())
                {
                    sourceImage = sourceTask.OutputImage.Clone();
                }
            }

            // 从文件路径加载
            if (sourceImage == null && !string.IsNullOrEmpty(card.ImageFilePath) && System.IO.File.Exists(card.ImageFilePath))
            {
                sourceImage = Cv2.ImRead(card.ImageFilePath);
            }

            if (sourceImage == null || sourceImage.Empty())
            {
                AnthropicMessageDialog.ShowInfo("", TaskFlow.Resources.Strings.Prop_SetImageFirst, this);
                sourceImage?.Dispose();
                return;
            }

            try
            {
                var trainingWindow = new TemplateTrainingWindow(sourceImage, card.Id)
                {
                    Owner = this
                };

                if (trainingWindow.ShowDialog() == true && !string.IsNullOrEmpty(trainingWindow.TemplatePath))
                {
                    // 更新模板路径
                    if (_propertyControls.TryGetValue("TemplateImagePath", out var control) && control is TextBox pathBox)
                    {
                        pathBox.Text = trainingWindow.TemplatePath;
                    }
                }
            }
            finally
            {
                sourceImage.Dispose();
            }
        }

        #endregion

        #region Button Handlers

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 保存通用属性
                if (_propertyControls.TryGetValue("Name", out var nameControl) && nameControl is TextBox nameBox)
                {
                    string safeName = new string(nameBox.Text.Where(c => !char.IsPunctuation(c) && !char.IsSymbol(c)).ToArray());
                    if (string.IsNullOrWhiteSpace(safeName)) safeName = _task.TaskTypeName;
                    _task.Name = safeName;
                }

                // 保存特定属性
                switch (_task)
                {
                    case LlmTranslateTaskCard llmCard:
                        SaveLlmTranslateProperties(llmCard);
                        break;

                    case LlmVisionTaskCard visionCard:
                        SaveLlmVisionProperties(visionCard);
                        break;

                    case ArrayBuilderTaskCard arrayBuilderCard:
                        SaveArrayBuilderProperties(arrayBuilderCard);
                        break;

                    case LlmFileTranslateTaskCard fileTransCard:
                        SaveLlmFileTranslateProperties(fileTransCard);
                        break;

                    case FileReadTaskCard fileReadCard:
                        SaveFileReadProperties(fileReadCard);
                        break;

                    case EventListenerTaskCard eventCard:
                        SaveEventListenerProperties(eventCard);
                        break;

                    case ArraySearchTaskCard searchCard:
                        SaveArraySearchProperties(searchCard);
                        break;

                    case WinFindFileTaskCard findFileCard:
                        SaveWinFindFileProperties(findFileCard);
                        break;

                    case BrowserGetTextTaskCard browserGetCard:
                        SaveBrowserGetTextProperties(browserGetCard);
                        break;

                    case BrowserExecuteJsTaskCard browserJsCard:
                        SaveBrowserExecuteJsProperties(browserJsCard);
                        break;

                    case BrowserWaitForElementTaskCard browserWaitCard:
                        SaveBrowserWaitForElementProperties(browserWaitCard);
                        break;

                    case BrowserNativeClickTaskCard browserNativeClickCard:
                        SaveBrowserNativeClickProperties(browserNativeClickCard);
                        break;

                    case BrowserNativeInputTaskCard browserNativeInputCard:
                        SaveBrowserNativeInputProperties(browserNativeInputCard);
                        break;

                    case BrowserSimulatedClickTaskCard browserSimulatedClickCard:
                        SaveBrowserSimulatedClickProperties(browserSimulatedClickCard);
                        break;

                    case BrowserCdpCommandTaskCard browserCdpCommandCard:
                        SaveBrowserCdpCommandProperties(browserCdpCommandCard);
                        break;
                    case BrowserScreenshotTaskCard browserScreenshotCard:
                        SaveBrowserScreenshotProperties(browserScreenshotCard);
                        break;

                    case HttpRequestTaskCard httpRequestCard:
                        SaveHttpRequestProperties(httpRequestCard);
                        break;

                    case InputComboTaskCard comboCard:
                        SaveInputComboProperties(comboCard);
                        break;

                    case WinTextInputTaskCard textInputCard:
                        SaveWinTextInputProperties(textInputCard);
                        break;

                    case IfElseBranchTaskCard ifCard when ifCard.BranchRole == BranchRole.IfStart:
                        SaveIfElseProperties(ifCard);
                        SaveElifProperties(ifCard);
                        break;

                    case IfElseBranchTaskCard elifCard when elifCard.BranchRole == BranchRole.ElifStart:
                        SaveIfElseProperties(elifCard, "Elif");
                        break;

                    case ForLoopTaskCard loopCard when loopCard.BranchRole == BranchRole.ForLoopStart:
                        if (GetStringValue("LoopCountExpression", out string loopInput))
                        {
                            loopInput = loopInput.Trim();
                            if (int.TryParse(loopInput, out int directCount))
                            {
                                // 直接输入数字
                                loopCard.LoopCount = directCount;
                                loopCard.LoopCountExpression = string.Empty;
                                loopCard.UseExpressionLoopCount = false;
                            }
                            else
                            {
                                // @变量 或 #任务引用
                                loopCard.LoopCountExpression = loopInput;
                                loopCard.UseExpressionLoopCount = true;
                            }
                        }
                        break;

                    case PauseTaskCard pauseCard:
                        if (GetStringValue("PauseDurationExpression", out string pauseInput))
                        {
                            pauseInput = pauseInput.Trim();
                            if (int.TryParse(pauseInput, out int directMs))
                            {
                                // 直接输入数字
                                pauseCard.PauseDurationMs = directMs;
                                pauseCard.PauseDurationExpression = string.Empty;
                            }
                            else
                            {
                                // @变量 或 #任务引用
                                pauseCard.PauseDurationExpression = pauseInput;
                            }
                        }
                        break;

                    case GetTimestampTaskCard timestampCard:
                        if (_propertyControls.TryGetValue("TimestampFormat", out var fmtControl) && fmtControl is ComboBox fmtCombo)
                        {
                            if (fmtCombo.SelectedIndex >= 0 && Enum.IsDefined(typeof(TimestampFormat), fmtCombo.SelectedIndex))
                                timestampCard.TimestampFormat = (TimestampFormat)fmtCombo.SelectedIndex;
                        }
                        break;

                    case WinLaunchAppTaskCard launchCard:
                        if (GetStringValue("ExePath", out string exePath))
                            launchCard.ExePath = exePath;
                        if (GetStringValue("Arguments", out string args))
                            launchCard.Arguments = args;
                        break;

                    case WinScreenshotTaskCard screenshotCard:
                        if (GetStringValue("ProcessName", out string procName))
                            screenshotCard.ProcessName = procName;
                        if (GetBoolValue("IncludeTitleBar", out bool includeTitleBar))
                            screenshotCard.IncludeTitleBar = includeTitleBar;
                        if (GetStringValue("CropTopHeightExpression", out string cropTopExpr))
                            screenshotCard.CropTopHeightExpression = cropTopExpr;
                        if (GetBoolValue("ConvertToGrayscale", out bool winGray))
                            screenshotCard.ConvertToGrayscale = winGray;
                        break;

                    case WinClickTaskCard clickCard:
                        SaveClickProperties(clickCard);
                        break;

                    case WinCloseAppTaskCard closeAppCard:
                        if (GetStringValue("ProcessName", out string closeAppProc))
                            closeAppCard.ProcessName = closeAppProc;
                        break;

                    case AdbConnectTaskCard connectCard:
                        if (GetStringValue("DeviceIp", out string ip))
                            connectCard.DeviceIp = ip;
                        if (GetIntValue("DevicePort", out int port))
                            connectCard.DevicePort = port;
                        break;

                    case AdbLaunchAppTaskCard adbLaunchCard:
                        if (GetStringValue("DeviceSerial", out string serial1))
                            adbLaunchCard.DeviceSerial = serial1;
                        if (GetStringValue("PackageName", out string pkg))
                            adbLaunchCard.PackageName = pkg;
                        if (GetStringValue("ActivityName", out string act))
                            adbLaunchCard.ActivityName = act;
                        break;

                    case AdbScreenshotTaskCard adbScreenshotCard:
                        if (GetStringValue("DeviceSerial", out string serial2))
                            adbScreenshotCard.DeviceSerial = serial2;
                        if (GetBoolValue("ConvertToGrayscale", out bool adbGray))
                            adbScreenshotCard.ConvertToGrayscale = adbGray;
                        break;

                    case AdbClickTaskCard adbClickCard:
                        if (GetStringValue("DeviceSerial", out string serial3))
                            adbClickCard.DeviceSerial = serial3;
                        SaveAdbClickProperties(adbClickCard);
                        break;

                    case AdbCloseAppTaskCard adbCloseCard:
                        if (GetStringValue("DeviceSerial", out string serial4))
                            adbCloseCard.DeviceSerial = serial4;
                        if (GetStringValue("PackageName", out string closePkg))
                            adbCloseCard.PackageName = closePkg;
                        break;

                    case AdbDisconnectTaskCard adbDisconnectCard:
                        if (GetStringValue("DeviceSerial", out string serialDisc))
                            adbDisconnectCard.DeviceSerial = serialDisc;
                        break;

                    case WinUiAutomationTaskCard uiAutoCard:
                        if (GetStringValue("ProcessName", out string uiProcess))
                            uiAutoCard.ProcessName = uiProcess;
                        if (GetStringValue("ButtonName", out string uiButton))
                            uiAutoCard.ButtonName = uiButton;
                        if (GetStringValue("AutomationId", out string uiAutoId))
                            uiAutoCard.AutomationId = uiAutoId;
                        if (_propertyControls.TryGetValue("SearchBy", out var searchByCtrl) &&
                            searchByCtrl is ComboBox searchByCombo &&
                            searchByCombo.SelectedItem is ComboBoxItem searchByItem &&
                            searchByItem.Tag is UiSearchBy searchByVal)
                        {
                            uiAutoCard.SearchBy = searchByVal;
                        }
                        if (_propertyControls.TryGetValue("MatchMode", out var matchModeCtrl) &&
                            matchModeCtrl is ComboBox matchModeCombo &&
                            matchModeCombo.SelectedItem is ComboBoxItem matchModeItem &&
                            matchModeItem.Tag is UiMatchMode matchModeVal)
                        {
                            uiAutoCard.MatchMode = matchModeVal;
                        }
                        break;

                    case WinSimulateInputTaskCard simCard:
                    {
                        if (GetEnumValue<ModifierKeyType>("ModifierKey", out var mod)) simCard.ModifierKey = mod;
                        if (GetEnumValue<InputActionType>("ActionType", out var simAct)) simCard.ActionType = simAct;
                        if (GetStringValue("KeyName", out string kn)) simCard.KeyName = kn;
                        if (GetIntValue("ScrollAmount", out int sa)) simCard.ScrollAmount = sa;
                        if (GetIntValue("RepeatCount", out int rc)) simCard.RepeatCount = rc;
                        if (GetIntValue("IntervalMs", out int im)) simCard.IntervalMs = im;
                        break;
                    }

                    case WinSubtitleTaskCard subtitleCard:
                    {
                        if (GetBoolValue("UseSpecifiedWindow", out bool useWin)) subtitleCard.UseSpecifiedWindow = useWin;
                        if (GetStringValue("ProcessName", out string subProc)) subtitleCard.ProcessName = subProc;
                        if (GetStringValue("DisplayText", out string subText)) subtitleCard.DisplayText = subText;
                        if (GetIntValue("OffsetX", out int subOx)) subtitleCard.OffsetX = subOx;
                        if (GetIntValue("OffsetY", out int subOy)) subtitleCard.OffsetY = subOy;
                        if (GetIntValue("SubtitleWidth", out int subW)) subtitleCard.SubtitleWidth = subW;
                        if (GetIntValue("SubtitleHeight", out int subH)) subtitleCard.SubtitleHeight = subH;
                        if (GetIntValue("FontSize", out int subFs)) subtitleCard.FontSize = subFs;
                        if (GetStringValue("TextColor", out string subTc)) subtitleCard.TextColor = subTc;
                        if (_propertyControls.TryGetValue("Background", out var bgCtrl) &&
                            bgCtrl is ComboBox bgCombo &&
                            bgCombo.SelectedItem is ComboBoxItem bgItem &&
                            bgItem.Tag is SubtitleBackground bgVal)
                        {
                            subtitleCard.Background = bgVal;
                        }
                        if (GetStringValue("BackgroundColor", out string subBc)) subtitleCard.BackgroundColor = subBc;
                        if (GetIntValue("DurationMs", out int subDur)) subtitleCard.DurationMs = subDur;
                        if (_propertyControls.TryGetValue("WaitUntilClosed", out var waitCtrl) &&
                            waitCtrl is CheckBox waitCheck)
                        {
                            subtitleCard.WaitUntilClosed = waitCheck.IsChecked == true;
                        }
                        break;
                    }

                    case ImgCropTaskCard cropCard:
                        SaveImageCropProperties(cropCard);
                        break;

                    case ImgTemplateMatchTaskCard matchCard:
                        SaveTemplateMatchProperties(matchCard);
                        break;

                    case ImgOnnxDetectTaskCard detectCard:
                        SaveOnnxDetectProperties(detectCard);
                        break;

                    case ImgOcrTaskCard ocrCard:
                        SaveOcrProperties(ocrCard);
                        break;

                    case ImgColorDetectTaskCard colorCard:
                        SaveColorDetectProperties(colorCard);
                        break;

                    case ImgColorSegmentTaskCard segCard:
                        SaveColorSegmentProperties(segCard);
                        break;

                    case ImgPreprocessTaskCard prepCard:
                        SaveGenericImageSource(prepCard);
                        if (GetBoolValue("EnableGrayscale", out bool eg)) prepCard.EnableGrayscale = eg;
                        if (GetEnumValue<BinarizeMethod>("BinarizeMethod", out var bm)) prepCard.BinarizeMethod = bm;
                        if (GetIntValue("BinarizeThreshold", out int bt)) prepCard.BinarizeThreshold = bt;
                        if (GetEnumValue<MorphologyMethod>("MorphologyMethod", out var mm)) prepCard.MorphologyMethod = mm;
                        if (GetIntValue("MorphologyKernelSize", out int mk)) prepCard.MorphologyKernelSize = mk;
                        break;

                    case ImgBlobAnalysisTaskCard blobCard:
                        SaveGenericImageSource(blobCard);
                        if (GetIntValue("RoiX", out int bRoiX)) blobCard.RoiX = bRoiX;
                        if (GetIntValue("RoiY", out int bRoiY)) blobCard.RoiY = bRoiY;
                        if (GetIntValue("RoiWidth", out int bRoiW)) blobCard.RoiWidth = bRoiW;
                        if (GetIntValue("RoiHeight", out int bRoiH)) blobCard.RoiHeight = bRoiH;
                        blobCard.MaskImagePath = _currentMaskPath;
                        if (GetIntValue("MinArea", out int minA)) blobCard.MinArea = minA;
                        if (GetIntValue("MaxArea", out int maxA)) blobCard.MaxArea = maxA;
                        if (GetEnumValue<BlobSortMode>("SortMode", out var sm)) blobCard.SortMode = sm;
                        if (GetIntValue("MaxBlobCount", out int mc)) blobCard.MaxBlobCount = mc;
                        if (GetBoolValue("InvertBinary", out bool inv)) blobCard.InvertBinary = inv;
                        break;

                    case ImgResizeTaskCard resizeCard:
                        SaveGenericImageSource(resizeCard);
                        if (GetIntValue("TargetWidth", out int tw)) resizeCard.TargetWidth = tw;
                        if (GetIntValue("TargetHeight", out int th)) resizeCard.TargetHeight = th;
                        break;

                    case ImgCaliperMeasureTaskCard caliperCard:
                        SaveGenericImageSource(caliperCard);
                        if (GetIntValue("RoiX", out int cRoiX)) caliperCard.RoiX = cRoiX;
                        if (GetIntValue("RoiY", out int cRoiY)) caliperCard.RoiY = cRoiY;
                        if (GetIntValue("RoiWidth", out int cRoiW)) caliperCard.RoiWidth = cRoiW;
                        if (GetIntValue("RoiHeight", out int cRoiH)) caliperCard.RoiHeight = cRoiH;
                        if (GetEnumValue<SearchDirection>("SearchDirection", out var sd)) caliperCard.SearchDirection = sd;
                        if (GetEnumValue<EdgePolarity>("Edge1Polarity", out var e1p)) caliperCard.Edge1Polarity = e1p;
                        if (GetEnumValue<EdgeSelection>("Edge1Selection", out var e1s)) caliperCard.Edge1Selection = e1s;
                        if (GetEnumValue<EdgePolarity>("Edge2Polarity", out var e2p)) caliperCard.Edge2Polarity = e2p;
                        if (GetEnumValue<EdgeSelection>("Edge2Selection", out var e2s)) caliperCard.Edge2Selection = e2s;
                        break;

                    case ExpressionEvalTaskCard exprCard:
                        SaveExpressionEvalProperties(exprCard);
                        break;

                    case BreakLoopTaskCard breakCard:
                        SaveBreakLoopProperties(breakCard);
                        break;

                    case CallSubFlowTaskCard callSubFlowCard:
                        SaveCallSubFlowProperties(callSubFlowCard);
                        break;

                    case SubFlowOutputTaskCard subFlowOutputCard:
                        SaveSubFlowOutputProperties(subFlowOutputCard);
                        break;

                    case StringSubstringTaskCard substringCard:
                        SaveStringSubstringProperties(substringCard);
                        break;

                    case TypeConvertTaskCard typeConvertCard:
                        SaveTypeConvertProperties(typeConvertCard);
                        break;

                    case ArrayParseTaskCard arrayParseCard:
                        SaveArrayParseProperties(arrayParseCard);
                        break;
                }

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format(TaskFlow.Resources.Strings.Prop_SaveFailed, ex.Message), "", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SaveIfElseProperties(IfElseBranchTaskCard card, string prefix = "")
        {
            string keyPrefix = string.IsNullOrEmpty(prefix) ? "" : prefix + "_";

            if (_propertyControls.TryGetValue($"{keyPrefix}ConditionExpression", out var exprControl) && exprControl is TextBox exprBox)
            {
                card.ConditionExpression = exprBox.Text;
            }
        }

        /// <summary>
        /// 保存IfStart属性窗口中所有elif卡片的条件
        /// </summary>
        private void SaveElifProperties(IfElseBranchTaskCard ifStartCard)
        {
            if (!ifStartCard.BranchGroupId.HasValue) return;

            var elifCards = _viewModel.TaskCards
                .Where(t => t.BranchGroupId == ifStartCard.BranchGroupId && t.BranchRole == BranchRole.ElifStart)
                .Cast<IfElseBranchTaskCard>()
                .ToList();

            for (int idx = 0; idx < elifCards.Count; idx++)
            {
                SaveIfElseProperties(elifCards[idx], $"Elif{idx}");
            }
        }

        private void SaveClickProperties(WinClickTaskCard card)
        {

            // 合并保存 X/Y 坐标
            if (GetStringValue("StartXInput", out string xInput))
            {
                xInput = xInput.Trim();
                if (int.TryParse(xInput, out int directX))
                {
                    card.StartX = directX;
                    card.StartXExpression = string.Empty;
                    card.UseVariableCoordinates = false;
                }
                else
                {
                    card.StartXExpression = xInput;
                    card.UseVariableCoordinates = true;
                }
            }
            if (GetStringValue("StartYInput", out string yInput))
            {
                yInput = yInput.Trim();
                if (int.TryParse(yInput, out int directY))
                {
                    card.StartY = directY;
                    card.StartYExpression = string.Empty;
                }
                else
                {
                    card.StartYExpression = yInput;
                    card.UseVariableCoordinates = true;
                }
            }
            if (GetIntValue("EndX", out int endX)) card.EndX = endX;
            if (GetIntValue("EndY", out int endY)) card.EndY = endY;
            if (GetIntValue("SwipeDurationMs", out int swipeDur) && swipeDur >= 0)
                card.SwipeDurationMs = swipeDur;

            if (_propertyControls.TryGetValue("ClickType", out var clickControl) && clickControl is ComboBox clickCombo)
            {
                if (clickCombo.SelectedItem is ComboBoxItem item && item.Tag is ClickType clickType)
                    card.ClickType = clickType;
            }

            // 多次点击属性
            if (GetBoolValue("MultiClickEnabled", out bool multiEnabled))
                card.MultiClickEnabled = multiEnabled;
            if (GetIntValue("MultiClickCount", out int multiCount) && multiCount > 0)
                card.MultiClickCount = multiCount;
            if (GetIntValue("ClickIntervalMs", out int interval) && interval >= 0)
                card.ClickIntervalMs = interval;

            if (GetBoolValue("EnableOffScreenClick", out bool enableOffScreen))
                card.EnableOffScreenClick = enableOffScreen;
            if (GetStringValue("ProcessName", out string processName))
                card.ProcessName = processName;
        }

        private void SaveAdbClickProperties(AdbClickTaskCard card)
        {

            // 合并保存 X/Y 坐标
            if (GetStringValue("StartXInput", out string xInput2))
            {
                xInput2 = xInput2.Trim();
                if (int.TryParse(xInput2, out int directX))
                {
                    card.StartX = directX;
                    card.StartXExpression = string.Empty;
                    card.UseVariableCoordinates = false;
                }
                else
                {
                    card.StartXExpression = xInput2;
                    card.UseVariableCoordinates = true;
                }
            }
            if (GetStringValue("StartYInput", out string yInput2))
            {
                yInput2 = yInput2.Trim();
                if (int.TryParse(yInput2, out int directY))
                {
                    card.StartY = directY;
                    card.StartYExpression = string.Empty;
                }
                else
                {
                    card.StartYExpression = yInput2;
                    card.UseVariableCoordinates = true;
                }
            }
            if (GetIntValue("EndX", out int endX)) card.EndX = endX;
            if (GetIntValue("EndY", out int endY)) card.EndY = endY;
            if (GetIntValue("SwipeDurationMs", out int duration)) card.SwipeDurationMs = duration;

            if (_propertyControls.TryGetValue("ClickType", out var clickControl) && clickControl is ComboBox clickCombo)
            {
                if (clickCombo.SelectedItem is ComboBoxItem item && item.Tag is ClickType clickType)
                    card.ClickType = clickType;
            }

            // 多次点击属性
            if (GetBoolValue("MultiClickEnabled", out bool multiEnabled))
                card.MultiClickEnabled = multiEnabled;
            if (GetIntValue("MultiClickCount", out int multiCount) && multiCount > 0)
                card.MultiClickCount = multiCount;
            if (GetIntValue("ClickIntervalMs", out int interval) && interval >= 0)
                card.ClickIntervalMs = interval;
        }

        private void SaveImageCropProperties(ImgCropTaskCard card)
        {
            if (GetBoolValue("UseSourceTaskImage", out bool useSrc))
                card.UseSourceTaskImage = useSrc;

            if (_propertyControls.TryGetValue("SourceTaskIdForImage", out var taskControl) && taskControl is ComboBox taskCombo)
            {
                if (taskCombo.SelectedItem is ComboBoxItem item && item.Tag is Guid taskId)
                    card.SourceTaskIdForImage = taskId;
                else
                    card.SourceTaskIdForImage = null;
            }

            if (GetStringValue("ImageFilePath", out string path)) card.ImageFilePath = path;

            // ROI 表达式保存
            SaveRoiExpressionValue("RoiXInput", v => card.RoiX = v, e => card.RoiXExpression = e);
            SaveRoiExpressionValue("RoiYInput", v => card.RoiY = v, e => card.RoiYExpression = e);
            SaveRoiExpressionValue("RoiWidthInput", v => card.RoiWidth = v, e => card.RoiWidthExpression = e);
            SaveRoiExpressionValue("RoiHeightInput", v => card.RoiHeight = v, e => card.RoiHeightExpression = e);
        }

        /// <summary>
        /// 保存 ROI 表达式或直接数值
        /// </summary>
        private void SaveRoiExpressionValue(string controlKey, Action<int> setInt, Action<string> setExpr)
        {
            if (GetStringValue(controlKey, out string input))
            {
                input = input.Trim();
                if (int.TryParse(input, out int directValue))
                {
                    setInt(directValue);
                    setExpr(string.Empty);
                }
                else
                {
                    setExpr(input);
                }
            }
        }

        private void SaveOnnxDetectProperties(ImgOnnxDetectTaskCard card)
        {
            SaveGenericImageSource(card);

            if (_propertyControls.TryGetValue("OnnxModelId", out var modelCtrl) && modelCtrl is ComboBox modelCombo)
            {
                card.OnnxModelId = modelCombo.SelectedValue as string;
            }

            if (GetStringValue("FilterClassName", out string filter))
                card.FilterClassName = filter;

            if (GetDoubleValue("ConfidenceOverride", out double conf))
                card.ConfidenceOverride = conf;
        }

        private void SaveTemplateMatchProperties(ImgTemplateMatchTaskCard card)
        {
            if (GetBoolValue("UseSourceTaskImage", out bool useSrc))
                card.UseSourceTaskImage = useSrc;

            if (_propertyControls.TryGetValue("SourceTaskIdForImage", out var taskControl) && taskControl is ComboBox taskCombo)
            {
                if (taskCombo.SelectedItem is ComboBoxItem item && item.Tag is Guid taskId)
                    card.SourceTaskIdForImage = taskId;
                else
                    card.SourceTaskIdForImage = null;
            }

            if (GetStringValue("ImageFilePath", out string path)) card.ImageFilePath = path;

            // 保存动态模板来源设置
            if (GetBoolValue("UseSourceTaskTemplate", out bool useSourceTemplate))
                card.UseSourceTaskTemplate = useSourceTemplate;

            if (_propertyControls.TryGetValue("SourceTaskIdForTemplate", out var templateTaskCtrl) && templateTaskCtrl is ComboBox templateTaskCombo2)
            {
                if (templateTaskCombo2.SelectedItem is ComboBoxItem tItem && tItem.Tag is Guid tId)
                    card.SourceTaskIdForTemplate = tId;
                else
                    card.SourceTaskIdForTemplate = null;
            }

            if (GetStringValue("TemplateImagePath", out string templatePath)) card.TemplateImagePath = templatePath;
            if (GetDoubleValue("MatchThreshold", out double threshold)) card.MatchThreshold = threshold;
            if (GetIntValue("MaxMatchCount", out int maxMatch)) card.MaxMatchCount = Math.Max(1, maxMatch);

            // 保存ROI
            if (GetIntValue("RoiX", out int roiX)) card.RoiX = roiX;
            if (GetIntValue("RoiY", out int roiY)) card.RoiY = roiY;
            if (GetIntValue("RoiWidth", out int roiW)) card.RoiWidth = roiW;
            if (GetIntValue("RoiHeight", out int roiH)) card.RoiHeight = roiH;

            // 保存掩膜路径
            card.MaskImagePath = _currentMaskPath;
        }

        private void SaveOcrProperties(ImgOcrTaskCard card)
        {
            // 保存 OCR 引擎选择
            if (_propertyControls.TryGetValue("OcrEngine", out var engineControl) && engineControl is ComboBox engineCombo)
            {
                if (engineCombo.SelectedItem is ComboBoxItem item && item.Tag is OcrEngine engine)
                    card.OcrEngine = engine;
            }

            if (GetBoolValue("UseSourceTaskImage", out bool useSrc))
                card.UseSourceTaskImage = useSrc;

            if (_propertyControls.TryGetValue("SourceTaskIdForImage", out var taskControl) && taskControl is ComboBox taskCombo)
            {
                if (taskCombo.SelectedItem is ComboBoxItem item && item.Tag is Guid taskId)
                    card.SourceTaskIdForImage = taskId;
                else
                    card.SourceTaskIdForImage = null;
            }

            if (GetStringValue("ImageFilePath", out string path)) card.ImageFilePath = path;
            if (GetBoolValue("CheckContainsText", out bool check)) card.CheckContainsText = check;
            if (GetStringValue("TargetText", out string target)) card.TargetText = target;

            // 保存ROI
            if (GetIntValue("RoiX", out int roiX)) card.RoiX = roiX;
            if (GetIntValue("RoiY", out int roiY)) card.RoiY = roiY;
            if (GetIntValue("RoiWidth", out int roiW)) card.RoiWidth = roiW;
            if (GetIntValue("RoiHeight", out int roiH)) card.RoiHeight = roiH;

            // 保存掩膜路径
            card.MaskImagePath = _currentMaskPath;
        }

        private void AddImageSourcePropertyColor(ImgColorDetectTaskCard card)
        {
            var taskLabel = new TextBlock { Text = TaskFlow.Resources.Strings.Prop_ImageSourceTask, Style = FindResource("PropertyLabel") as Style };
            var taskCombo = new ComboBox { Style = FindResource("PropertyComboBox") as Style };
            taskCombo.Items.Add(new ComboBoxItem { Content = TaskFlow.Resources.Strings.Prop_SelectTask, Tag = null });
            foreach (var task in _viewModel.GetImageOutputTasks().Where(t => t.Id != _task.Id))
                taskCombo.Items.Add(new ComboBoxItem { Content = $"#{task.Order} {task.Name}", Tag = task.Id });
            taskCombo.SelectedIndex = 0;
            if (card.SourceTaskIdForImage.HasValue)
                for (int i = 1; i < taskCombo.Items.Count; i++)
                    if (((ComboBoxItem)taskCombo.Items[i]).Tag is Guid id && id == card.SourceTaskIdForImage) { taskCombo.SelectedIndex = i; break; }

            var fileLabel = new TextBlock { Text = TaskFlow.Resources.Strings.Prop_ImageFilePath, Style = FindResource("PropertyLabel") as Style };
            var fileGrid = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            fileGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            fileGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var fileBox = new TextBox { Text = card.ImageFilePath ?? "", Style = FindResource("PropertyTextBox") as Style, Margin = new Thickness(0) };
            Grid.SetColumn(fileBox, 0);
            var browseBtn = new Button { Content = "...", Width = 32, Margin = new Thickness(4, 0, 0, 0), VerticalAlignment = VerticalAlignment.Stretch, Style = FindResource("ActionButton") as Style };
            browseBtn.Click += (s, e) => { var dlg = new OpenFileDialog { Filter = TaskFlow.Resources.Strings.Filter_ImageFile }; if (dlg.ShowDialog() == true) fileBox.Text = dlg.FileName; };
            Grid.SetColumn(browseBtn, 1);
            fileGrid.Children.Add(fileBox); fileGrid.Children.Add(browseBtn);
            _propertyControls["ImageFilePath"] = fileBox;

            void UpdateVis(bool chk) { taskLabel.Visibility = taskCombo.Visibility = chk ? Visibility.Visible : Visibility.Collapsed; fileLabel.Visibility = fileGrid.Visibility = chk ? Visibility.Collapsed : Visibility.Visible; }
            var cb = new CheckBox { Content = TaskFlow.Resources.Strings.Prop_UseSourceTaskImage, IsChecked = card.UseSourceTaskImage, Style = FindResource("PropertyCheckBox") as Style };
            cb.Checked += (s, e) => UpdateVis(true); cb.Unchecked += (s, e) => UpdateVis(false);
            PropertyPanel.Children.Add(cb); _propertyControls["UseSourceTaskImage"] = cb;
            PropertyPanel.Children.Add(taskLabel); PropertyPanel.Children.Add(taskCombo); _propertyControls["SourceTaskIdForImage"] = taskCombo;
            PropertyPanel.Children.Add(fileLabel); PropertyPanel.Children.Add(fileGrid);
            UpdateVis(card.UseSourceTaskImage);

            // ROI识别区域
            AddRoiCompactProperties(card.RoiX, card.RoiY, card.RoiWidth, card.RoiHeight);
            var selectRoiBtn = new Button { Content = TaskFlow.Resources.Strings.Prop_SelectRoiArea, Height = 32, Margin = new Thickness(0, 0, 0, 12), Style = FindResource("ActionButton") as Style };
            selectRoiBtn.Click += (s, e) => SelectRoiGeneric(card.UseSourceTaskImage, card.SourceTaskIdForImage, card.ImageFilePath);
            PropertyPanel.Children.Add(selectRoiBtn);
        }

        private void AddColorDetectProperties(ImgColorDetectTaskCard card)
        {
            AddHsvCompactProperties(card.HsvLowerH, card.HsvLowerS, card.HsvLowerV, card.HsvUpperH, card.HsvUpperS, card.HsvUpperV);

            // 颜色吸笔按钮
            var eyedropperBtn = new Button
            {
                Content = TaskFlow.Resources.Strings.Prop_ColorPicker,
                Height = 32,
                Margin = new Thickness(0, 4, 0, 4),
                Style = FindResource("ActionButton") as Style
            };
            eyedropperBtn.Click += (s, e) => EyedropperPickColorDetect(card);
            PropertyPanel.Children.Add(eyedropperBtn);
        }

        private void SaveColorDetectProperties(ImgColorDetectTaskCard card)
        {
            if (GetBoolValue("UseSourceTaskImage", out bool useSrc))
                card.UseSourceTaskImage = useSrc;

            if (_propertyControls.TryGetValue("SourceTaskIdForImage", out var taskControl) && taskControl is ComboBox taskCombo)
            {
                if (taskCombo.SelectedItem is ComboBoxItem item && item.Tag is Guid taskId)
                    card.SourceTaskIdForImage = taskId;
                else
                    card.SourceTaskIdForImage = null;
            }

            if (GetStringValue("ImageFilePath", out string path)) card.ImageFilePath = path;
            if (GetIntValue("HsvLowerH", out int lh)) card.HsvLowerH = lh;
            if (GetIntValue("HsvLowerS", out int ls)) card.HsvLowerS = ls;
            if (GetIntValue("HsvLowerV", out int lv)) card.HsvLowerV = lv;
            if (GetIntValue("HsvUpperH", out int uh)) card.HsvUpperH = uh;
            if (GetIntValue("HsvUpperS", out int us)) card.HsvUpperS = us;
            if (GetIntValue("HsvUpperV", out int uv)) card.HsvUpperV = uv;
            if (GetIntValue("RoiX", out int roiX)) card.RoiX = roiX;
            if (GetIntValue("RoiY", out int roiY)) card.RoiY = roiY;
            if (GetIntValue("RoiWidth", out int roiW)) card.RoiWidth = roiW;
            if (GetIntValue("RoiHeight", out int roiH)) card.RoiHeight = roiH;
        }

        private void AddImageSourcePropertyColorSegment(ImgColorSegmentTaskCard card)
        {
            var taskLabel = new TextBlock { Text = TaskFlow.Resources.Strings.Prop_ImageSourceTask, Style = FindResource("PropertyLabel") as Style };
            var taskCombo = new ComboBox { Style = FindResource("PropertyComboBox") as Style };
            taskCombo.Items.Add(new ComboBoxItem { Content = TaskFlow.Resources.Strings.Prop_SelectTask, Tag = null });
            foreach (var task in _viewModel.GetImageOutputTasks().Where(t => t.Id != _task.Id))
                taskCombo.Items.Add(new ComboBoxItem { Content = $"#{task.Order} {task.Name}", Tag = task.Id });
            taskCombo.SelectedIndex = 0;
            if (card.SourceTaskIdForImage.HasValue)
                for (int i = 1; i < taskCombo.Items.Count; i++)
                    if (((ComboBoxItem)taskCombo.Items[i]).Tag is Guid id && id == card.SourceTaskIdForImage) { taskCombo.SelectedIndex = i; break; }

            var fileLabel = new TextBlock { Text = TaskFlow.Resources.Strings.Prop_ImageFilePath, Style = FindResource("PropertyLabel") as Style };
            var fileGrid = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            fileGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            fileGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var fileBox = new TextBox { Text = card.ImageFilePath ?? "", Style = FindResource("PropertyTextBox") as Style, Margin = new Thickness(0) };
            Grid.SetColumn(fileBox, 0);
            var browseBtn = new Button { Content = "...", Width = 32, Margin = new Thickness(4, 0, 0, 0), VerticalAlignment = VerticalAlignment.Stretch, Style = FindResource("ActionButton") as Style };
            browseBtn.Click += (s, e) => { var dlg = new OpenFileDialog { Filter = TaskFlow.Resources.Strings.Filter_ImageFile }; if (dlg.ShowDialog() == true) fileBox.Text = dlg.FileName; };
            Grid.SetColumn(browseBtn, 1);
            fileGrid.Children.Add(fileBox); fileGrid.Children.Add(browseBtn);
            _propertyControls["ImageFilePath"] = fileBox;

            void UpdateVis(bool chk) { taskLabel.Visibility = taskCombo.Visibility = chk ? Visibility.Visible : Visibility.Collapsed; fileLabel.Visibility = fileGrid.Visibility = chk ? Visibility.Collapsed : Visibility.Visible; }
            var cb = new CheckBox { Content = TaskFlow.Resources.Strings.Prop_UseSourceTaskImage, IsChecked = card.UseSourceTaskImage, Style = FindResource("PropertyCheckBox") as Style };
            cb.Checked += (s, e) => UpdateVis(true); cb.Unchecked += (s, e) => UpdateVis(false);
            PropertyPanel.Children.Add(cb); _propertyControls["UseSourceTaskImage"] = cb;
            PropertyPanel.Children.Add(taskLabel); PropertyPanel.Children.Add(taskCombo); _propertyControls["SourceTaskIdForImage"] = taskCombo;
            PropertyPanel.Children.Add(fileLabel); PropertyPanel.Children.Add(fileGrid);
            UpdateVis(card.UseSourceTaskImage);
        }

        private void AddColorSegmentProperties(ImgColorSegmentTaskCard card)
        {
            AddHsvCompactProperties(card.HsvLowerH, card.HsvLowerS, card.HsvLowerV, card.HsvUpperH, card.HsvUpperS, card.HsvUpperV);

            // 颜色吸笔按钮
            var eyedropperBtn = new Button
            {
                Content = "💧 颜色吸笔（从图像拾取HSV范围）",
                Height = 32,
                Margin = new Thickness(0, 4, 0, 4),
                Style = FindResource("ActionButton") as Style
            };
            eyedropperBtn.Click += (s, e) => EyedropperPick(card);
            PropertyPanel.Children.Add(eyedropperBtn);
        }

        /// <summary>
        /// 颜色吸笔：从图像上拾取像素的 HSV 值，并设置为当前 HSV 范围的上下限 (±10)
        /// </summary>
        private void EyedropperPick(ImgColorSegmentTaskCard card)
        {
            // 获取源图像
            Mat? sourceImage = null;
            if (card.UseSourceTaskImage && card.SourceTaskIdForImage.HasValue)
            {
                var sourceTask = _viewModel.TaskCards.FirstOrDefault(t => t.Id == card.SourceTaskIdForImage.Value);
                if (sourceTask?.OutputImage != null && !sourceTask.OutputImage.Empty())
                    sourceImage = sourceTask.OutputImage.Clone();
            }
            if (sourceImage == null && !string.IsNullOrEmpty(card.ImageFilePath) && System.IO.File.Exists(card.ImageFilePath))
                sourceImage = Cv2.ImRead(card.ImageFilePath);

            if (sourceImage == null || sourceImage.Empty())
            {
                AnthropicMessageDialog.ShowInfo("", TaskFlow.Resources.Strings.Prop_SetImageFirst, this);
                sourceImage?.Dispose();
                return;
            }

            try
            {
                // 打开 ROI 选择窗口让用户点击像素
                var picker = new ColorPickerWindow(sourceImage) { Owner = this };
                if (picker.ShowDialog() == true)
                {
                    // 获取点击位置的 HSV 值
                    int h = picker.PickedH, s = picker.PickedS, v = picker.PickedV;

                    // 设置 HSV 上下限 (±10，约束在合法范围内)
                    int lH = Math.Max(0, h - 10), uH = Math.Min(180, h + 10);
                    int lS = Math.Max(0, s - 10), uS = Math.Min(255, s + 10);
                    int lV = Math.Max(0, v - 10), uV = Math.Min(255, v + 10);

                    // 回写到控件
                    if (_propertyControls.TryGetValue("HsvLowerH", out var c1) && c1 is TextBox t1) t1.Text = lH.ToString();
                    if (_propertyControls.TryGetValue("HsvUpperH", out var c2) && c2 is TextBox t2) t2.Text = uH.ToString();
                    if (_propertyControls.TryGetValue("HsvLowerS", out var c3) && c3 is TextBox t3) t3.Text = lS.ToString();
                    if (_propertyControls.TryGetValue("HsvUpperS", out var c4) && c4 is TextBox t4) t4.Text = uS.ToString();
                    if (_propertyControls.TryGetValue("HsvLowerV", out var c5) && c5 is TextBox t5) t5.Text = lV.ToString();
                    if (_propertyControls.TryGetValue("HsvUpperV", out var c6) && c6 is TextBox t6) t6.Text = uV.ToString();
                }
            }
            finally
            {
                sourceImage.Dispose();
            }
        }

        /// <summary>
        /// 颜色吸笔：从图像上拾取像素的 HSV 值（颜色识别卡片专用）
        /// </summary>
        private void EyedropperPickColorDetect(ImgColorDetectTaskCard card)
        {
            Mat? sourceImage = null;
            if (card.UseSourceTaskImage && card.SourceTaskIdForImage.HasValue)
            {
                var sourceTask = _viewModel.TaskCards.FirstOrDefault(t => t.Id == card.SourceTaskIdForImage.Value);
                if (sourceTask?.OutputImage != null && !sourceTask.OutputImage.Empty())
                    sourceImage = sourceTask.OutputImage.Clone();
            }
            if (sourceImage == null && !string.IsNullOrEmpty(card.ImageFilePath) && System.IO.File.Exists(card.ImageFilePath))
                sourceImage = Cv2.ImRead(card.ImageFilePath);

            if (sourceImage == null || sourceImage.Empty())
            {
                AnthropicMessageDialog.ShowInfo("", TaskFlow.Resources.Strings.Prop_SetImageFirst, this);
                sourceImage?.Dispose();
                return;
            }

            try
            {
                var picker = new ColorPickerWindow(sourceImage) { Owner = this };
                if (picker.ShowDialog() == true)
                {
                    int h = picker.PickedH, s = picker.PickedS, v = picker.PickedV;
                    int lH = Math.Max(0, h - 10), uH = Math.Min(180, h + 10);
                    int lS = Math.Max(0, s - 10), uS = Math.Min(255, s + 10);
                    int lV = Math.Max(0, v - 10), uV = Math.Min(255, v + 10);

                    if (_propertyControls.TryGetValue("HsvLowerH", out var c1) && c1 is TextBox t1) t1.Text = lH.ToString();
                    if (_propertyControls.TryGetValue("HsvUpperH", out var c2) && c2 is TextBox t2) t2.Text = uH.ToString();
                    if (_propertyControls.TryGetValue("HsvLowerS", out var c3) && c3 is TextBox t3) t3.Text = lS.ToString();
                    if (_propertyControls.TryGetValue("HsvUpperS", out var c4) && c4 is TextBox t4) t4.Text = uS.ToString();
                    if (_propertyControls.TryGetValue("HsvLowerV", out var c5) && c5 is TextBox t5) t5.Text = lV.ToString();
                    if (_propertyControls.TryGetValue("HsvUpperV", out var c6) && c6 is TextBox t6) t6.Text = uV.ToString();
                }
            }
            finally
            {
                sourceImage.Dispose();
            }
        }

        private void SaveColorSegmentProperties(ImgColorSegmentTaskCard card)
        {
            if (GetBoolValue("UseSourceTaskImage", out bool useSrc))
                card.UseSourceTaskImage = useSrc;

            if (_propertyControls.TryGetValue("SourceTaskIdForImage", out var taskControl) && taskControl is ComboBox taskCombo)
            {
                if (taskCombo.SelectedItem is ComboBoxItem item && item.Tag is Guid taskId)
                    card.SourceTaskIdForImage = taskId;
                else
                    card.SourceTaskIdForImage = null;
            }

            if (GetStringValue("ImageFilePath", out string path)) card.ImageFilePath = path;
            if (GetIntValue("HsvLowerH", out int lh)) card.HsvLowerH = lh;
            if (GetIntValue("HsvLowerS", out int ls)) card.HsvLowerS = ls;
            if (GetIntValue("HsvLowerV", out int lv)) card.HsvLowerV = lv;
            if (GetIntValue("HsvUpperH", out int uh)) card.HsvUpperH = uh;
            if (GetIntValue("HsvUpperS", out int us)) card.HsvUpperS = us;
            if (GetIntValue("HsvUpperV", out int uv)) card.HsvUpperV = uv;
        }

        private void AddExpressionEvalProperties(ExpressionEvalTaskCard card)
        {
            var labelBlock = new TextBlock { Text = TaskFlow.Resources.Strings.Prop_AssignExpr, Style = FindResource("PropertyLabel") as Style };
            PropertyPanel.Children.Add(labelBlock);

            // 提示文本
            var hintBlock = new TextBlock
            {
                Text = TaskFlow.Resources.Strings.Prop_AssignHint,
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(140, 140, 160)),
                Margin = new Thickness(0, 0, 0, 6),
                TextWrapping = TextWrapping.Wrap
            };
            PropertyPanel.Children.Add(hintBlock);

            // 多行输入框
            var textBox = new TextBox
            {
                Text = card.Expression,
                Style = FindResource("PropertyTextBox") as Style,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                MinHeight = 80,
                MaxHeight = 200,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalContentAlignment = VerticalAlignment.Top
            };
            // 为赋值表达式输入框附加自动补全
            AutoCompleteHelper.Attach(textBox, _viewModel.VariableStore.Variables, _viewModel.TaskCards);

            PropertyPanel.Children.Add(textBox);
            _propertyControls["Expression"] = textBox;
        }

        private void SaveExpressionEvalProperties(ExpressionEvalTaskCard card)
        {
            if (GetStringValue("Expression", out string expr))
                card.Expression = expr;
        }

        /// <summary>
        /// 添加中止循环属性（选择目标循环）
        /// </summary>
        private void AddBreakLoopProperties(BreakLoopTaskCard card)
        {
            var label = new TextBlock { Text = TaskFlow.Resources.Strings.Prop_TargetLoop, Style = FindResource("PropertyLabel") as Style };
            var combo = new ComboBox { Style = FindResource("PropertyComboBox") as Style };
            combo.Items.Add(new ComboBoxItem { Content = TaskFlow.Resources.Strings.Prop_SelectLoop, Tag = null });

            // 列出所有 ForLoopStart 卡片
            var loopTasks = _viewModel.TaskCards
                .Where(t => t.BranchRole == BranchRole.ForLoopStart)
                .ToList();

            foreach (var loopTask in loopTasks)
            {
                combo.Items.Add(new ComboBoxItem { Content = $"#{loopTask.Order} {loopTask.Name}", Tag = loopTask.Id });
            }

            // 设置选中项
            if (card.TargetLoopId.HasValue)
            {
                for (int i = 1; i < combo.Items.Count; i++)
                {
                    if (((ComboBoxItem)combo.Items[i]).Tag is Guid id && id == card.TargetLoopId)
                    {
                        combo.SelectedIndex = i;
                        break;
                    }
                }
            }
            else
            {
                combo.SelectedIndex = 0;
            }

            PropertyPanel.Children.Add(label);
            PropertyPanel.Children.Add(combo);
            _propertyControls["TargetLoopId"] = combo;
        }

        private void SaveBreakLoopProperties(BreakLoopTaskCard card)
        {
            if (_propertyControls.TryGetValue("TargetLoopId", out var control) && control is ComboBox combo)
            {
                if (combo.SelectedItem is ComboBoxItem item && item.Tag is Guid loopId)
                {
                    card.TargetLoopId = loopId;
                }
                else
                {
                    card.TargetLoopId = null;
                }
            }
        }

        private void AddCallSubFlowProperties(CallSubFlowTaskCard card)
        {
            // 目标子流程
            var subFlowLabel = new TextBlock { Text = TaskFlow.Resources.Strings.Prop_TargetSubFlow, Style = FindResource("PropertyLabel") as Style };
            var subFlowCombo = new ComboBox { Style = FindResource("PropertyComboBox") as Style };
            subFlowCombo.Items.Add(new ComboBoxItem { Content = TaskFlow.Resources.Strings.Prop_SelectTask, Tag = null });
            
            // _viewModel 存在 .Tabs 集合，筛选出子流程类型的标签
            if (_viewModel is TaskFlow.ViewModels.MainViewModel mainVm)
            {
                foreach (var tab in mainVm.Tabs.Where(t => t.Type == TaskFlow.Models.FlowType.SubFlow))
                    subFlowCombo.Items.Add(new ComboBoxItem { Content = tab.Name, Tag = tab.Id });
            }
            subFlowCombo.SelectedIndex = 0;
            if (card.TargetSubFlowId.HasValue)
                for (int i = 1; i < subFlowCombo.Items.Count; i++)
                    if (((ComboBoxItem)subFlowCombo.Items[i]).Tag is Guid id && id == card.TargetSubFlowId.Value) { subFlowCombo.SelectedIndex = i; break; }

            _propertyControls["TargetSubFlowId"] = subFlowCombo;
            PropertyPanel.Children.Add(subFlowLabel);
            PropertyPanel.Children.Add(subFlowCombo);

            // 分割线
            PropertyPanel.Children.Add(new Separator { Margin = new Thickness(0, 8, 0, 8), Background = new SolidColorBrush(Color.FromRgb(232, 230, 220)) });

            AddComboPropertyWithTasks("SourceTaskIdForImage", TaskFlow.Resources.Strings.Prop_SubFlowInputImage, card.SourceTaskIdForImage, _viewModel.GetImageOutputTasks());
            AddComboPropertyWithTasks("SourceTaskIdForText", TaskFlow.Resources.Strings.Prop_SubFlowInputText, card.SourceTaskIdForText, _viewModel.GetTextOutputTasks());
            AddComboPropertyWithTasks("SourceTaskIdForX", TaskFlow.Resources.Strings.Prop_SubFlowInputX, card.SourceTaskIdForX, _viewModel.GetCoordinateOutputTasks());
            AddComboPropertyWithTasks("SourceTaskIdForY", TaskFlow.Resources.Strings.Prop_SubFlowInputY, card.SourceTaskIdForY, _viewModel.GetCoordinateOutputTasks());
        }

        private void SaveCallSubFlowProperties(CallSubFlowTaskCard card)
        {
            if (_propertyControls.TryGetValue("TargetSubFlowId", out var ctrl) && ctrl is ComboBox combo && combo.SelectedItem is ComboBoxItem item && item.Tag is Guid id)
                card.TargetSubFlowId = id;
            else
                card.TargetSubFlowId = null;

            card.SourceTaskIdForImage = GetTaskIdFromCombo("SourceTaskIdForImage");
            card.SourceTaskIdForText = GetTaskIdFromCombo("SourceTaskIdForText");
            card.SourceTaskIdForX = GetTaskIdFromCombo("SourceTaskIdForX");
            card.SourceTaskIdForY = GetTaskIdFromCombo("SourceTaskIdForY");
        }

        private void AddSubFlowOutputProperties(SubFlowOutputTaskCard card)
        {
            AddComboPropertyWithTasks("SourceTaskIdForImage", TaskFlow.Resources.Strings.Prop_SubFlowInputImage, card.SourceTaskIdForImage, _viewModel.GetImageOutputTasks());
            AddComboPropertyWithTasks("SourceTaskIdForText", TaskFlow.Resources.Strings.Prop_SubFlowInputText, card.SourceTaskIdForText, _viewModel.GetTextOutputTasks());
            AddComboPropertyWithTasks("SourceTaskIdForX", TaskFlow.Resources.Strings.Prop_SubFlowInputX, card.SourceTaskIdForX, _viewModel.GetCoordinateOutputTasks());
            AddComboPropertyWithTasks("SourceTaskIdForY", TaskFlow.Resources.Strings.Prop_SubFlowInputY, card.SourceTaskIdForY, _viewModel.GetCoordinateOutputTasks());
            AddComboPropertyWithTasks("SourceTaskIdForResult", TaskFlow.Resources.Strings.Prop_SubFlowOutputResult, card.SourceTaskIdForResult, _viewModel.TaskCards.Where(t => t.OutputsBoolResult));
        }

        private void SaveSubFlowOutputProperties(SubFlowOutputTaskCard card)
        {
            card.SourceTaskIdForImage = GetTaskIdFromCombo("SourceTaskIdForImage");
            card.SourceTaskIdForText = GetTaskIdFromCombo("SourceTaskIdForText");
            card.SourceTaskIdForX = GetTaskIdFromCombo("SourceTaskIdForX");
            card.SourceTaskIdForY = GetTaskIdFromCombo("SourceTaskIdForY");
            card.SourceTaskIdForResult = GetTaskIdFromCombo("SourceTaskIdForResult");
        }

        private void AddComboPropertyWithTasks(string key, string labelText, Guid? currentValue, IEnumerable<TaskCardBase> tasks)
        {
            var label = new TextBlock { Text = labelText, Style = FindResource("PropertyLabel") as Style };
            var combo = new ComboBox { Style = FindResource("PropertyComboBox") as Style };
            combo.Items.Add(new ComboBoxItem { Content = TaskFlow.Resources.Strings.Prop_SelectTask, Tag = null });
            
            var validTasks = tasks.Where(t => t.Id != _task.Id).ToList();
            foreach (var task in validTasks)
                combo.Items.Add(new ComboBoxItem { Content = $"#{task.Order} {task.Name}", Tag = task.Id });
                
            combo.SelectedIndex = 0;
            if (currentValue.HasValue)
                for (int i = 1; i < combo.Items.Count; i++)
                    if (((ComboBoxItem)combo.Items[i]).Tag is Guid id && id == currentValue.Value) { combo.SelectedIndex = i; break; }

            _propertyControls[key] = combo;
            PropertyPanel.Children.Add(label);
            PropertyPanel.Children.Add(combo);
        }

        private Guid? GetTaskIdFromCombo(string key)
        {
            if (_propertyControls.TryGetValue(key, out var ctrl) && ctrl is ComboBox combo && combo.SelectedItem is ComboBoxItem item && item.Tag is Guid id)
                return id;
            return null;
        }

        private void AddStringSubstringProperties(StringSubstringTaskCard card)
        {
            // 文本来源任务
            var taskLabel = new TextBlock { Text = TaskFlow.Resources.Strings.Prop_TextSourceTask, Style = FindResource("PropertyLabel") as Style };
            var taskCombo = new ComboBox { Style = FindResource("PropertyComboBox") as Style };
            taskCombo.Items.Add(new ComboBoxItem { Content = TaskFlow.Resources.Strings.Prop_SelectTask, Tag = null });

            foreach (var task in _viewModel.GetTextOutputTasks().Where(t => t.Id != _task.Id))
            {
                taskCombo.Items.Add(new ComboBoxItem { Content = $"#{task.Order} {task.Name}", Tag = task.Id });
            }

            taskCombo.SelectedIndex = 0;
            if (card.SourceTaskIdForText.HasValue)
            {
                for (int i = 1; i < taskCombo.Items.Count; i++)
                {
                    if (((ComboBoxItem)taskCombo.Items[i]).Tag is Guid id && id == card.SourceTaskIdForText)
                    {
                        taskCombo.SelectedIndex = i;
                        break;
                    }
                }
            }

            PropertyPanel.Children.Add(taskLabel);
            PropertyPanel.Children.Add(taskCombo);
            _propertyControls["SourceTaskIdForText"] = taskCombo;

            // 手动输入文本
            AddTextProperty("InputText", TaskFlow.Resources.Strings.Prop_InputText, card.InputText);

            // 起始位置模式
            var modeLabel = new TextBlock { Text = TaskFlow.Resources.Strings.Prop_StartMode, Style = FindResource("PropertyLabel") as Style };
            var modeCombo = new ComboBox { Style = FindResource("PropertyComboBox") as Style };
            modeCombo.Items.Add(new ComboBoxItem { Content = TaskFlow.Resources.Strings.Prop_ManualMode, Tag = StartIndexMode.Manual });
            modeCombo.Items.Add(new ComboBoxItem { Content = TaskFlow.Resources.Strings.Prop_FindCharMode, Tag = StartIndexMode.FindChar });
            modeCombo.SelectedIndex = (int)card.StartMode;

            PropertyPanel.Children.Add(modeLabel);
            PropertyPanel.Children.Add(modeCombo);
            _propertyControls["StartMode"] = modeCombo;

            // 手动起始位置（仅手动指定模式显示）
            var manualLabel = new TextBlock { Text = TaskFlow.Resources.Strings.Prop_ManualStart, Style = FindResource("PropertyLabel") as Style };
            var manualBox = new TextBox { Text = card.ManualStartIndex.ToString(), Style = FindResource("PropertyTextBox") as Style };
            PropertyPanel.Children.Add(manualLabel);
            PropertyPanel.Children.Add(manualBox);
            _propertyControls["ManualStartIndex"] = manualBox;

            // 查找字符（仅查找字符模式显示）
            var searchCharLabel = new TextBlock { Text = TaskFlow.Resources.Strings.Prop_SearchChar, Style = FindResource("PropertyLabel") as Style };
            var searchCharBox = new TextBox { Text = card.SearchChar ?? "", Style = FindResource("PropertyTextBox") as Style };
            PropertyPanel.Children.Add(searchCharLabel);
            PropertyPanel.Children.Add(searchCharBox);
            _propertyControls["SearchChar"] = searchCharBox;

            // 查找字符偏移量（仅查找字符模式显示）
            var searchOffsetLabel = new TextBlock { Text = TaskFlow.Resources.Strings.Prop_SearchOffset, Style = FindResource("PropertyLabel") as Style };
            var searchOffsetBox = new TextBox { Text = card.SearchCharOffset.ToString(), Style = FindResource("PropertyTextBox") as Style };
            PropertyPanel.Children.Add(searchOffsetLabel);
            PropertyPanel.Children.Add(searchOffsetBox);
            _propertyControls["SearchCharOffset"] = searchOffsetBox;

            // 截取长度
            AddIntProperty("SubstringLength", TaskFlow.Resources.Strings.Prop_SubstringLength, card.SubstringLength);

            // 根据模式显示/隐藏对应控件
            void UpdateModeVisibility()
            {
                bool isManual = modeCombo.SelectedIndex == 0;
                manualLabel.Visibility = isManual ? Visibility.Visible : Visibility.Collapsed;
                manualBox.Visibility = isManual ? Visibility.Visible : Visibility.Collapsed;
                searchCharLabel.Visibility = !isManual ? Visibility.Visible : Visibility.Collapsed;
                searchCharBox.Visibility = !isManual ? Visibility.Visible : Visibility.Collapsed;
                searchOffsetLabel.Visibility = !isManual ? Visibility.Visible : Visibility.Collapsed;
                searchOffsetBox.Visibility = !isManual ? Visibility.Visible : Visibility.Collapsed;
            }

            UpdateModeVisibility();
            modeCombo.SelectionChanged += (s, e) => UpdateModeVisibility();
        }

        private void SaveStringSubstringProperties(StringSubstringTaskCard card)
        {
            if (_propertyControls.TryGetValue("SourceTaskIdForText", out var taskControl) && taskControl is ComboBox taskCombo)
            {
                if (taskCombo.SelectedItem is ComboBoxItem item && item.Tag is Guid taskId)
                    card.SourceTaskIdForText = taskId;
                else
                    card.SourceTaskIdForText = null;
            }

            if (GetStringValue("InputText", out string inputText))
                card.InputText = inputText;

            if (_propertyControls.TryGetValue("StartMode", out var modeControl) && modeControl is ComboBox modeCombo)
            {
                if (modeCombo.SelectedItem is ComboBoxItem modeItem && modeItem.Tag is StartIndexMode mode)
                    card.StartMode = mode;
            }

            if (GetIntValue("ManualStartIndex", out int startIndex))
                card.ManualStartIndex = startIndex;

            if (GetStringValue("SearchChar", out string searchChar))
                card.SearchChar = searchChar;

            if (GetIntValue("SearchCharOffset", out int offset))
                card.SearchCharOffset = offset;

            if (GetIntValue("SubstringLength", out int length))
                card.SubstringLength = length;
        }

        private void AddTypeConvertProperties(TypeConvertTaskCard card)
        {
            // 文本来源任务
            var taskLabel = new TextBlock { Text = TaskFlow.Resources.Strings.Prop_TextSourceTask, Style = FindResource("PropertyLabel") as Style };
            var taskCombo = new ComboBox { Style = FindResource("PropertyComboBox") as Style };
            taskCombo.Items.Add(new ComboBoxItem { Content = TaskFlow.Resources.Strings.Prop_SelectTask, Tag = null });

            foreach (var task in _viewModel.GetTextOutputTasks().Where(t => t.Id != _task.Id))
            {
                taskCombo.Items.Add(new ComboBoxItem { Content = $"#{task.Order} {task.Name}", Tag = task.Id });
            }

            taskCombo.SelectedIndex = 0;
            if (card.SourceTaskIdForText.HasValue)
            {
                for (int i = 1; i < taskCombo.Items.Count; i++)
                {
                    if (((ComboBoxItem)taskCombo.Items[i]).Tag is Guid id && id == card.SourceTaskIdForText)
                    {
                        taskCombo.SelectedIndex = i;
                        break;
                    }
                }
            }

            PropertyPanel.Children.Add(taskLabel);
            PropertyPanel.Children.Add(taskCombo);
            _propertyControls["SourceTaskIdForText"] = taskCombo;

            // 手动输入表达式
            AddTextProperty("InputExpression", TaskFlow.Resources.Strings.Prop_InputExpr, card.InputExpression);
        }

        private void SaveTypeConvertProperties(TypeConvertTaskCard card)
        {
            if (_propertyControls.TryGetValue("SourceTaskIdForText", out var taskControl) && taskControl is ComboBox taskCombo)
            {
                if (taskCombo.SelectedItem is ComboBoxItem item && item.Tag is Guid taskId)
                    card.SourceTaskIdForText = taskId;
                else
                    card.SourceTaskIdForText = null;
            }

            if (GetStringValue("InputExpression", out string expr))
                card.InputExpression = expr;
        }

        private void AddArrayParseProperties(ArrayParseTaskCard card)
        {
            // 数组类型选择
            var typeLabel = new TextBlock { Text = TaskFlow.Resources.Strings.Prop_ArrayType, Style = FindResource("PropertyLabel") as Style };
            var typeCombo = new ComboBox { Style = FindResource("PropertyComboBox") as Style };
            typeCombo.Items.Add(new ComboBoxItem { Content = TaskFlow.Resources.Strings.Prop_ArrayInt, Tag = ArrayDataType.Int });
            typeCombo.Items.Add(new ComboBoxItem { Content = TaskFlow.Resources.Strings.Prop_ArrayString, Tag = ArrayDataType.String });
            typeCombo.Items.Add(new ComboBoxItem { Content = TaskFlow.Resources.Strings.Prop_ArrayCoord, Tag = ArrayDataType.Coordinate });
            typeCombo.Items.Add(new ComboBoxItem { Content = TaskFlow.Resources.Strings.Prop_ArrayDouble, Tag = ArrayDataType.Double });
            typeCombo.SelectedIndex = (int)card.ArrayDataType;

            PropertyPanel.Children.Add(typeLabel);
            PropertyPanel.Children.Add(typeCombo);
            _propertyControls["ArrayDataType"] = typeCombo;

            // 数组来源任务下拉框
            var arrayLabel = new TextBlock { Text = TaskFlow.Resources.Strings.Prop_ArraySourceTask, Style = FindResource("PropertyLabel") as Style };
            var arrayCombo = new ComboBox { Style = FindResource("PropertyComboBox") as Style };
            arrayCombo.Items.Add(new ComboBoxItem { Content = TaskFlow.Resources.Strings.Prop_SelectTask, Tag = null });

            foreach (var task in _viewModel.GetArrayOutputTasks().Where(t => t.Id != _task.Id))
            {
                if (task is ImgTemplateMatchTaskCard)
                {
                    arrayCombo.Items.Add(new ComboBoxItem { Content = $"#{task.Order} {task.Name} (匹配坐标)", Tag = $"{task.Id}|匹配坐标" });
                    arrayCombo.Items.Add(new ComboBoxItem { Content = $"#{task.Order} {task.Name} (结果分数)", Tag = $"{task.Id}|结果分数" });
                }
                else if (task is ImgBlobAnalysisTaskCard)
                {
                    arrayCombo.Items.Add(new ComboBoxItem { Content = $"#{task.Order} {task.Name} (边界框)", Tag = $"{task.Id}|边界框" });
                }
                else
                {
                    arrayCombo.Items.Add(new ComboBoxItem { Content = $"#{task.Order} {task.Name}", Tag = $"{task.Id}|" });
                }
            }

            arrayCombo.SelectedIndex = 0;
            if (card.SourceTaskIdForArray.HasValue)
            {
                string targetTag = $"{card.SourceTaskIdForArray.Value}|{card.SourcePropertyForArray ?? ""}";
                for (int i = 1; i < arrayCombo.Items.Count; i++)
                {
                    if (arrayCombo.Items[i] is ComboBoxItem cbItem && cbItem.Tag is string tag && tag == targetTag)
                    {
                        arrayCombo.SelectedIndex = i;
                        break;
                    }
                }
                
                // 向后兼容：如果找不到完全匹配的（例如旧版本只保存了Guid没保存属性名）
                if (arrayCombo.SelectedIndex == 0)
                {
                    for (int i = 1; i < arrayCombo.Items.Count; i++)
                    {
                        if (arrayCombo.Items[i] is ComboBoxItem cbItem && cbItem.Tag is string tag && tag.StartsWith(card.SourceTaskIdForArray.Value.ToString()))
                        {
                            arrayCombo.SelectedIndex = i;
                            break;
                        }
                    }
                }
            }

            PropertyPanel.Children.Add(arrayLabel);
            PropertyPanel.Children.Add(arrayCombo);
            _propertyControls["SourceTaskIdForArray"] = arrayCombo;

            // 索引设置（合并为单一输入框）
            // 显示逻辑：如果使用表达式索引，显示表达式；否则显示数字索引
            string indexDisplay = card.UseExpressionIndex && !string.IsNullOrWhiteSpace(card.ParseIndexExpression)
                ? card.ParseIndexExpression
                : card.ParseIndex.ToString();
            AddTextProperty("ParseIndexUnified", TaskFlow.Resources.Strings.Prop_OutputIndex, indexDisplay);
        }

        private void SaveLlmTranslateProperties(LlmTranslateTaskCard card)
        {
            if (GetStringValue("SourceTextExpression", out string sourceText))
            {
                card.SourceTextExpression = sourceText;
            }
            if (GetStringValue("TargetLanguage", out string targetLang))
            {
                card.TargetLanguage = targetLang;
            }
        }

        /// <summary>
        /// 保存多模态识图任务卡片属性
        /// </summary>
        private void SaveLlmVisionProperties(LlmVisionTaskCard card)
        {
            // 保存图像来源属性
            SaveGenericImageSource(card);

            // 保存提示词
            if (GetStringValue("PromptExpression", out string prompt))
            {
                card.PromptExpression = prompt;
            }
        }

        #region ArrayBuilder 属性

        private void AddArrayBuilderProperties(ArrayBuilderTaskCard card)
        {
            // 数据表达式（支持补全）
            AddTextProperty("InputExpression", TaskFlow.Resources.Strings.Prop_ArrayBuilderInputExpr, card.InputExpression);

            // 索引表达式
            AddTextProperty("IndexExpression", TaskFlow.Resources.Strings.Prop_ArrayBuilderIndexExpr, card.IndexExpression);

            // 自动导出文件路径
            AddTextProperty("AutoExportPath", TaskFlow.Resources.Strings.Prop_ArrayBuilderExportPath, card.AutoExportPath);

            // 为导出路径增加文件夹选择按钮
            if (_propertyControls.TryGetValue("AutoExportPath", out var exportCtrl) && exportCtrl is TextBox exportBox)
            {
                PropertyPanel.Children.Remove(exportBox);
                var exportGrid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
                exportGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                exportGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                exportBox.Margin = new Thickness(0);
                Grid.SetColumn(exportBox, 0);
                var folderBtn = new Button { Content = "...", Width = 32, Margin = new Thickness(4, 0, 0, 0), VerticalAlignment = VerticalAlignment.Stretch, Style = FindResource("ActionButton") as Style };
                folderBtn.Click += (s, e) =>
                {
                    var dlg = new System.Windows.Forms.FolderBrowserDialog();
                    if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                    {
                        // 根据选取的文件夹路径自动拼接文件名
                        exportBox.Text = System.IO.Path.Combine(dlg.SelectedPath, $"array_export_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
                    }
                };
                Grid.SetColumn(folderBtn, 1);
                exportGrid.Children.Add(exportBox);
                exportGrid.Children.Add(folderBtn);
                PropertyPanel.Children.Add(exportGrid);
            }

            // 清空数组开关表达式（支持补全）
            AddTextProperty("ClearExpression", TaskFlow.Resources.Strings.Prop_ArrayBuilderClearExpr, card.ClearExpression);

            // 为清空表达式增加手动清除按钮
            if (_propertyControls.TryGetValue("ClearExpression", out var clearCtrl) && clearCtrl is TextBox clearBox)
            {
                PropertyPanel.Children.Remove(clearBox);
                var clearGrid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
                clearGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                clearGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                clearBox.Margin = new Thickness(0);
                Grid.SetColumn(clearBox, 0);
                var manualClearBtn = new Button
                {
                    Content = "🗑",
                    Width = 32,
                    Margin = new Thickness(4, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Stretch,
                    Style = FindResource("ActionButton") as Style
                };
                manualClearBtn.Click += (s, e) =>
                {
                    if (TaskFlow.Services.TaskExecutionService._arrayBuilderData.TryGetValue(card.Id, out var data))
                    {
                        data.Clear();
                        MessageBox.Show("数组已清空。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        MessageBox.Show("当前无数据可清空。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                };
                Grid.SetColumn(manualClearBtn, 1);
                clearGrid.Children.Add(clearBox);
                clearGrid.Children.Add(manualClearBtn);
                PropertyPanel.Children.Add(clearGrid);
            }

            // 手动导出按钮（与框选区域按钮风格一致）
            var exportBtn = new Button
            {
                Content = TaskFlow.Resources.Strings.Prop_ArrayBuilderExportBtn,
                Height = 32,
                Margin = new Thickness(0, 4, 0, 4),
                Style = FindResource("ActionButton") as Style
            };
            exportBtn.Click += (s, e) =>
            {
                // 从静态数据字典获取运行时数据
                if (!TaskFlow.Services.TaskExecutionService._arrayBuilderData.TryGetValue(card.Id, out var data) || data.Count == 0)
                {
                    MessageBox.Show("当前无数据可导出。请先运行流程收集数据。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var dlg = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = "文本文件|*.txt|所有文件|*.*",
                    DefaultExt = ".txt",
                    FileName = $"array_export_{DateTime.Now:yyyyMMdd_HHmmss}"
                };
                if (dlg.ShowDialog() == true)
                {
                    System.IO.File.WriteAllText(dlg.FileName, string.Join("\n", data), System.Text.Encoding.UTF8);
                    MessageBox.Show($"已导出 {data.Count} 条数据到:\n{dlg.FileName}", "导出成功", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            };
            PropertyPanel.Children.Add(exportBtn);
        }

        private void SaveArrayBuilderProperties(ArrayBuilderTaskCard card)
        {
            if (GetStringValue("InputExpression", out string inputExpr))
                card.InputExpression = inputExpr;
            if (GetStringValue("IndexExpression", out string indexExpr))
                card.IndexExpression = indexExpr;
            if (GetStringValue("AutoExportPath", out string exportPath))
                card.AutoExportPath = exportPath;
            if (GetStringValue("ClearExpression", out string clearExpr))
                card.ClearExpression = clearExpr;
        }

        #endregion

        #region LlmFileTranslate 属性

        private void AddLlmFileTranslateProperties(LlmFileTranslateTaskCard card)
        {
            // 模型选择
            var modelLabel = new TextBlock { Text = TaskFlow.Resources.Strings.Prop_SelectModel, Style = FindResource("PropertyLabel") as Style };
            var modelCombo = new ComboBox
            {
                Style = FindResource("PropertyComboBox") as Style,
                DisplayMemberPath = "DisplayName",
                SelectedValuePath = "Id",
                Margin = new Thickness(0, 0, 0, 8)
            };

            modelCombo.ItemsSource = TaskFlow.Helpers.LlmModelManager.Models;
            if (!string.IsNullOrEmpty(card.ModelId)) modelCombo.SelectedValue = card.ModelId;
            modelCombo.SelectionChanged += (s, e) =>
            {
                var selectedId = modelCombo.SelectedValue?.ToString() ?? "";
                if (card.ModelId != selectedId) card.ModelId = selectedId;
            };
            PropertyPanel.Children.Add(modelLabel);
            PropertyPanel.Children.Add(modelCombo);

            // 输入文件路径
            AddTextProperty("InputFilePath", TaskFlow.Resources.Strings.Prop_InputFilePath, card.InputFilePath);
            AddFileBrowseButton("InputFilePath");

            // 输出文件路径
            AddTextProperty("OutputFilePath", TaskFlow.Resources.Strings.Prop_OutputFilePath, card.OutputFilePath);
            AddFileBrowseButton("OutputFilePath");

            // 目标语言
            AddTextProperty("TargetLanguage", TaskFlow.Resources.Strings.Prop_TargetLanguage, card.TargetLanguage);

            // 每批最大字符数
            AddIntProperty("MaxCharsPerBatch", TaskFlow.Resources.Strings.Prop_MaxCharsPerBatch, card.MaxCharsPerBatch);

            // System Prompt 多行文本框
            var promptLabel = new TextBlock { Text = TaskFlow.Resources.Strings.Prop_SystemPrompt, Style = FindResource("PropertyLabel") as Style };
            var promptTextBox = new TextBox
            {
                Text = card.SystemPrompt,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                Height = 100,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Background = Brushes.White,
                Padding = new Thickness(6, 4, 6, 4),
                Margin = new Thickness(0, 0, 0, 8)
            };
            promptTextBox.TextChanged += (s, e) =>
            {
                card.SystemPrompt = promptTextBox.Text;
            };
            PropertyPanel.Children.Add(promptLabel);
            PropertyPanel.Children.Add(promptTextBox);
            _propertyControls["SystemPrompt"] = promptTextBox;
        }

        private void SaveLlmFileTranslateProperties(LlmFileTranslateTaskCard card)
        {
            if (GetStringValue("InputFilePath", out string inputPath))
                card.InputFilePath = inputPath;
            if (GetStringValue("OutputFilePath", out string outputPath))
                card.OutputFilePath = outputPath;
            if (GetStringValue("TargetLanguage", out string targetLang))
                card.TargetLanguage = targetLang;
            if (_propertyControls.TryGetValue("MaxCharsPerBatch", out var maxCharsCtrl) && maxCharsCtrl is TextBox maxCharsTb)
            {
                if (int.TryParse(maxCharsTb.Text, out int maxChars) && maxChars > 0)
                    card.MaxCharsPerBatch = maxChars;
            }
        }

        #endregion

        private void SaveArrayParseProperties(ArrayParseTaskCard card)
        {
            // 保存数组类型
            if (_propertyControls.TryGetValue("ArrayDataType", out var typeControl) && typeControl is ComboBox typeCombo)
            {
                if (typeCombo.SelectedItem is ComboBoxItem typeItem && typeItem.Tag is ArrayDataType dataType)
                    card.ArrayDataType = dataType;
            }

            // 保存数组来源任务ID及属性名
            if (_propertyControls.TryGetValue("SourceTaskIdForArray", out var arrayCtrl) && arrayCtrl is ComboBox arrayCombo)
            {
                if (arrayCombo.SelectedItem is ComboBoxItem item && item.Tag is string tagValue)
                {
                    var parts = tagValue.Split('|');
                    if (Guid.TryParse(parts[0], out Guid taskId))
                        card.SourceTaskIdForArray = taskId;
                    card.SourcePropertyForArray = parts.Length > 1 ? parts[1] : string.Empty;
                }
                else
                {
                    card.SourceTaskIdForArray = null;
                    card.SourcePropertyForArray = string.Empty;
                }
            }

            // 保存索引：智能判断是整数还是表达式
            if (GetStringValue("ParseIndexUnified", out string idxInput))
            {
                idxInput = idxInput.Trim();
                if (int.TryParse(idxInput, out int directIndex))
                {
                    // 纯数字，使用整数索引
                    card.ParseIndex = directIndex;
                    card.UseExpressionIndex = false;
                    card.ParseIndexExpression = string.Empty;
                }
                else
                {
                    // 含 @变量或表达式
                    card.UseExpressionIndex = true;
                    card.ParseIndexExpression = idxInput;
                }
            }
        }

        private bool GetStringValue(string key, out string value)
        {
            value = string.Empty;
            if (_propertyControls.TryGetValue(key, out var control) && control is TextBox textBox)
            {
                value = textBox.Text;
                return true;
            }
            return false;
        }

        private bool GetIntValue(string key, out int value)
        {
            value = 0;
            if (_propertyControls.TryGetValue(key, out var control) && control is TextBox textBox)
            {
                return int.TryParse(textBox.Text, out value);
            }
            return false;
        }

        private bool GetDoubleValue(string key, out double value)
        {
            value = 0;
            if (_propertyControls.TryGetValue(key, out var control) && control is TextBox textBox)
            {
                return double.TryParse(textBox.Text, out value);
            }
            return false;
        }

        private bool GetBoolValue(string key, out bool value)
        {
            value = false;
            if (_propertyControls.TryGetValue(key, out var control) && control is CheckBox checkBox)
            {
                value = checkBox.IsChecked ?? false;
                return true;
            }
            return false;
        }

        /// <summary>
        /// 通用图像来源属性（适用于所有带 UseSourceTaskImage/SourceTaskIdForImage/ImageFilePath 的卡片）
        /// </summary>
        private void AddImageSourceProperty_Generic(bool useSource, Guid? sourceTaskId, string? imageFilePath)
        {
            // 图像来源任务下拉框
            var taskLabel = new TextBlock { Text = "图像来源任务", Style = FindResource("PropertyLabel") as Style };
            var taskCombo = new ComboBox { Style = FindResource("PropertyComboBox") as Style };
            taskCombo.Items.Add(new ComboBoxItem { Content = TaskFlow.Resources.Strings.Prop_SelectTask, Tag = null });

            foreach (var task in _viewModel.GetImageOutputTasks().Where(t => t.Id != _task.Id))
            {
                taskCombo.Items.Add(new ComboBoxItem { Content = $"#{task.Order} {task.Name}", Tag = task.Id });
            }

            taskCombo.SelectedIndex = 0;
            if (sourceTaskId.HasValue)
            {
                for (int i = 1; i < taskCombo.Items.Count; i++)
                {
                    if (((ComboBoxItem)taskCombo.Items[i]).Tag is Guid id && id == sourceTaskId)
                    {
                        taskCombo.SelectedIndex = i;
                        break;
                    }
                }
            }

            // 图像文件路径
            var fileLabel = new TextBlock { Text = "图像文件路径", Style = FindResource("PropertyLabel") as Style };
            var fileGrid = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            fileGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            fileGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var fileBox = new TextBox { Text = imageFilePath ?? "", Style = FindResource("PropertyTextBox") as Style, Margin = new Thickness(0) };
            Grid.SetColumn(fileBox, 0);
            var browseBtn = new Button { Content = "...", Width = 32, Margin = new Thickness(4, 0, 0, 0), VerticalAlignment = VerticalAlignment.Stretch, Style = FindResource("ActionButton") as Style };
            browseBtn.Click += (s, e) =>
            {
                var dlg = new OpenFileDialog { Filter = "图像文件|*.png;*.jpg;*.bmp" };
                if (dlg.ShowDialog() == true) fileBox.Text = dlg.FileName;
            };
            Grid.SetColumn(browseBtn, 1);
            fileGrid.Children.Add(fileBox);
            fileGrid.Children.Add(browseBtn);
            _propertyControls["ImageFilePath"] = fileBox;

            // 根据勾选状态切换显示
            void UpdateVisibility(bool isChecked)
            {
                var vis = isChecked ? Visibility.Visible : Visibility.Collapsed;
                taskLabel.Visibility = vis;
                taskCombo.Visibility = vis;
                fileLabel.Visibility = isChecked ? Visibility.Collapsed : Visibility.Visible;
                fileGrid.Visibility = isChecked ? Visibility.Collapsed : Visibility.Visible;
            }

            // 复选框（默认勾选）
            var checkBox = new CheckBox { Content = "使用其他任务输出的图像", IsChecked = useSource, Style = FindResource("PropertyCheckBox") as Style };
            checkBox.Checked += (s, e) => UpdateVisibility(true);
            checkBox.Unchecked += (s, e) => UpdateVisibility(false);
            PropertyPanel.Children.Add(checkBox);
            _propertyControls["UseSourceTaskImage"] = checkBox;

            PropertyPanel.Children.Add(taskLabel);
            PropertyPanel.Children.Add(taskCombo);
            _propertyControls["SourceTaskIdForImage"] = taskCombo;

            PropertyPanel.Children.Add(fileLabel);
            PropertyPanel.Children.Add(fileGrid);

            // 设置初始可见性
            UpdateVisibility(useSource);
        }

        /// <summary>
        /// 添加枚举下拉框属性
        /// </summary>
        private void AddEnumComboProperty<T>(string propertyName, string label, T currentValue, Dictionary<T, string> displayNames) where T : struct, Enum
        {
            var lbl = new TextBlock { Text = label, Style = FindResource("PropertyLabel") as Style };
            var combo = new ComboBox { Style = FindResource("PropertyComboBox") as Style };

            int selectedIndex = 0;
            int idx = 0;
            foreach (var kvp in displayNames)
            {
                combo.Items.Add(new ComboBoxItem { Content = kvp.Value, Tag = kvp.Key });
                if (kvp.Key.Equals(currentValue)) selectedIndex = idx;
                idx++;
            }
            combo.SelectedIndex = selectedIndex;

            PropertyPanel.Children.Add(lbl);
            PropertyPanel.Children.Add(combo);
            _propertyControls[propertyName] = combo;
        }

        /// <summary>
        /// 保存通用图像来源属性
        /// </summary>
        private void SaveGenericImageSource(dynamic card)
        {
            if (GetBoolValue("UseSourceTaskImage", out bool useSrc))
                card.UseSourceTaskImage = useSrc;

            if (_propertyControls.TryGetValue("SourceTaskIdForImage", out var taskControl) && taskControl is ComboBox taskCombo)
            {
                if (taskCombo.SelectedItem is ComboBoxItem item && item.Tag is Guid taskId)
                    card.SourceTaskIdForImage = taskId;
                else
                    card.SourceTaskIdForImage = null;
            }

            if (GetStringValue("ImageFilePath", out string path)) card.ImageFilePath = path;
        }

        /// <summary>
        /// 从 ComboBox 中读取枚举值
        /// </summary>
        private bool GetEnumValue<T>(string propertyName, out T value) where T : struct, Enum
        {
            value = default;
            if (_propertyControls.TryGetValue(propertyName, out var control) && control is ComboBox combo)
            {
                if (combo.SelectedItem is ComboBoxItem item && item.Tag is T enumVal)
                {
                    value = enumVal;
                    return true;
                }
            }
            return false;
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        #endregion

        private void ApplyLocalization()
        {
            Title = Strings.UI_TaskProperty;
            TitleText.Text = Strings.UI_EditProperties;
            SubtitleText.Text = Strings.UI_Properties;
            SaveButton.Content = Strings.UI_Save;
        }

        #region FileRead 属性

        private void AddFileReadProperties(FileReadTaskCard card)
        {
            // 文件路径表达式
            AddTextProperty("FilePathExpression", Strings.Prop_FilePathExpr, card.FilePathExpression);
            AddFileBrowseButton("FilePathExpression");

            // 分隔符
            AddTextProperty("Delimiter", Strings.Prop_Delimiter, card.Delimiter);
        }

        private void SaveFileReadProperties(FileReadTaskCard card)
        {
            if (GetStringValue("FilePathExpression", out string filePath))
                card.FilePathExpression = filePath;
            if (GetStringValue("Delimiter", out string delimiter))
                card.Delimiter = delimiter;
        }

        #endregion

        #region EventListener 属性

        private void AddEventListenerProperties(EventListenerTaskCard card)
        {
            // 事件类型下拉选择
            var eventTypes = new (string Value, string Display)[]
            {
                ("MouseLeft", Strings.EventType_MouseLeft),
                ("MouseRight", Strings.EventType_MouseRight),
                ("Enter", Strings.EventType_Enter),
                ("Space", Strings.EventType_Space)
            };

            var label = new TextBlock { Text = Strings.Prop_EventType, Style = FindResource("PropertyLabel") as Style };
            var combo = new ComboBox { Style = FindResource("PropertyComboBox") as Style };
            foreach (var et in eventTypes)
            {
                combo.Items.Add(new ComboBoxItem { Content = et.Display, Tag = et.Value });
            }
            // 选中当前值
            for (int i = 0; i < combo.Items.Count; i++)
            {
                if (combo.Items[i] is ComboBoxItem ci && (string)ci.Tag == card.EventType)
                {
                    combo.SelectedIndex = i;
                    break;
                }
            }
            if (combo.SelectedIndex < 0) combo.SelectedIndex = 0;

            PropertyPanel.Children.Add(label);
            PropertyPanel.Children.Add(combo);
            _propertyControls["EventType"] = combo;
        }

        private void SaveEventListenerProperties(EventListenerTaskCard card)
        {
            if (_propertyControls.TryGetValue("EventType", out var ctrl) && ctrl is ComboBox combo
                && combo.SelectedItem is ComboBoxItem ci)
            {
                card.EventType = (string)ci.Tag;
            }
        }

        #endregion

        #region ArraySearch 属性

        private void AddArraySearchProperties(ArraySearchTaskCard card)
        {
            // 搜索文本表达式
            AddTextProperty("SearchExpression", Strings.Prop_SearchExpr, card.SearchExpression);

            // 数组来源任务下拉框
            var arrayLabel = new TextBlock { Text = Strings.Prop_ArraySourceTask, Style = FindResource("PropertyLabel") as Style };
            var arrayCombo = new ComboBox { Style = FindResource("PropertyComboBox") as Style };
            arrayCombo.Items.Add(new ComboBoxItem { Content = Strings.Prop_SelectTask, Tag = null });

            foreach (var task in _viewModel.GetStringArrayOutputTasks().Where(t => t.Id != _task.Id))
            {
                arrayCombo.Items.Add(new ComboBoxItem { Content = $"#{task.Order} {task.Name}", Tag = task.Id });
            }

            arrayCombo.SelectedIndex = 0;
            if (card.SourceTaskIdForArray.HasValue)
            {
                for (int i = 1; i < arrayCombo.Items.Count; i++)
                {
                    if (((ComboBoxItem)arrayCombo.Items[i]).Tag is Guid id && id == card.SourceTaskIdForArray)
                    {
                        arrayCombo.SelectedIndex = i;
                        break;
                    }
                }
            }

            PropertyPanel.Children.Add(arrayLabel);
            PropertyPanel.Children.Add(arrayCombo);
            _propertyControls["SourceTaskIdForArray"] = arrayCombo;

            // 匹配模式下拉选择
            var matchModes = new (string Value, string Display)[]
            {
                ("Exact", Strings.MatchMode_Exact),
                ("Contains", Strings.MatchMode_Contains),
                ("Best", Strings.MatchMode_Best)
            };

            var label = new TextBlock { Text = Strings.Prop_MatchMode, Style = FindResource("PropertyLabel") as Style };
            var combo = new ComboBox { Style = FindResource("PropertyComboBox") as Style };
            foreach (var mm in matchModes)
            {
                combo.Items.Add(new ComboBoxItem { Content = mm.Display, Tag = mm.Value });
            }
            for (int i = 0; i < combo.Items.Count; i++)
            {
                if (combo.Items[i] is ComboBoxItem ci && (string)ci.Tag == card.MatchMode)
                {
                    combo.SelectedIndex = i;
                    break;
                }
            }
            if (combo.SelectedIndex < 0) combo.SelectedIndex = 1; // 默认 Contains

            PropertyPanel.Children.Add(label);
            PropertyPanel.Children.Add(combo);
            _propertyControls["MatchMode"] = combo;
        }

        private void SaveArraySearchProperties(ArraySearchTaskCard card)
        {
            if (GetStringValue("SearchExpression", out string searchExpr))
                card.SearchExpression = searchExpr;

            // 保存数组来源任务ID
            if (_propertyControls.TryGetValue("SourceTaskIdForArray", out var arrayCtrl) && arrayCtrl is ComboBox arrayCombo)
            {
                if (arrayCombo.SelectedItem is ComboBoxItem item && item.Tag is Guid taskId)
                    card.SourceTaskIdForArray = taskId;
                else
                    card.SourceTaskIdForArray = null;
            }

            if (_propertyControls.TryGetValue("MatchMode", out var ctrl) && ctrl is ComboBox combo
                && combo.SelectedItem is ComboBoxItem ci)
            {
                card.MatchMode = (string)ci.Tag;
            }
        }

        private void AddWinFindFileProperties(WinFindFileTaskCard card)
        {
            // 文件名称（支持表达式补全）
            AddTextProperty("FileName", Strings.Prop_WinFindFileName, card.FileName);

            // 搜索根目录（带文件夹浏览按钮）
            AddTextProperty("SearchRootPath", Strings.Prop_WinFindFileRoot, card.SearchRootPath);

            // 替换 TextBox 为 Grid + 文件夹浏览按钮
            if (_propertyControls.TryGetValue("SearchRootPath", out var rootCtrl) && rootCtrl is TextBox rootBox)
            {
                PropertyPanel.Children.Remove(rootBox);

                var fileGrid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
                fileGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                fileGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                rootBox.Margin = new Thickness(0);
                Grid.SetColumn(rootBox, 0);

                var browseBtn = new Button
                {
                    Content = "...",
                    Width = 32,
                    Margin = new Thickness(4, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Stretch,
                    Style = FindResource("ActionButton") as Style
                };
                browseBtn.Click += (s, e) =>
                {
                    var dlg = new System.Windows.Forms.FolderBrowserDialog();
                    if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                        rootBox.Text = dlg.SelectedPath;
                };
                Grid.SetColumn(browseBtn, 1);

                fileGrid.Children.Add(rootBox);
                fileGrid.Children.Add(browseBtn);
                PropertyPanel.Children.Add(fileGrid);
            }

            // 最大搜索深度
            AddIntProperty("MaxDepth", Strings.Prop_WinFindFileDepth, card.MaxDepth);

            // 启用通配符
            AddCheckboxProperty("UseWildcard", Strings.Prop_WinFindFileWildcard, card.UseWildcard);
        }

        private void SaveWinFindFileProperties(WinFindFileTaskCard card)
        {
            if (GetStringValue("FileName", out string fileName))
                card.FileName = fileName;

            if (GetStringValue("SearchRootPath", out string rootPath))
                card.SearchRootPath = rootPath;

            if (_propertyControls.TryGetValue("MaxDepth", out var depthCtrl) && depthCtrl is TextBox depthBox
                && int.TryParse(depthBox.Text, out int maxDepth))
                card.MaxDepth = maxDepth;

            if (_propertyControls.TryGetValue("UseWildcard", out var wildCtrl) && wildCtrl is CheckBox wildCheck)
                card.UseWildcard = wildCheck.IsChecked == true;
        }

        #endregion

        #region InputCombo 属性

        private readonly List<(TextBox keyBox, ComboBox modeCombo, TextBox delayBox)> _comboActionRows = new();

        private void AddInputComboProperties(InputComboTaskCard card)
        {
            // 按键动作列表标签
            var actionsLabel = new TextBlock { Text = Strings.Prop_InputComboActions, Style = FindResource("PropertyLabel") as Style };
            PropertyPanel.Children.Add(actionsLabel);

            // 动作列表容器
            var actionsPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };

            // 添加已有的动作
            foreach (var action in card.Actions)
            {
                AddComboActionRow(actionsPanel, action.Key, action.Mode, action.DelayAfterMs);
            }

            // 默认至少一行
            if (card.Actions.Count == 0)
            {
                AddComboActionRow(actionsPanel, "W", InputComboMode.Tap, 100);
            }

            PropertyPanel.Children.Add(actionsPanel);

            // 添加按键按钮
            var addBtn = new Button
            {
                Content = Strings.Prop_InputComboAddAction,
                Style = FindResource("ActionButton") as Style,
                Margin = new Thickness(0, 0, 0, 12),
                HorizontalAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(12, 4, 12, 4)
            };
            addBtn.Click += (s, e) => AddComboActionRow(actionsPanel, "W", InputComboMode.Tap, 100);
            PropertyPanel.Children.Add(addBtn);

            // 重复次数
            AddIntProperty("RepeatCount", Strings.Prop_InputComboRepeat, card.RepeatCount);

            // 终止条件表达式
            AddTextProperty("StopExpression", Strings.Prop_InputComboStop, card.StopExpression);

            // 最大执行时长
            AddIntProperty("TotalDurationMs", Strings.Prop_InputComboDuration, card.TotalDurationMs);
        }

        private void AddComboActionRow(StackPanel parent, string key, InputComboMode mode, int delayMs)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // 按键名称输入框
            var keyBox = new TextBox
            {
                Text = key,
                Style = FindResource("PropertyTextBox") as Style,
                Margin = new Thickness(0, 0, 4, 0),
                ToolTip = "按键名称（如 W、Space、LeftClick）"
            };
            Grid.SetColumn(keyBox, 0);
            row.Children.Add(keyBox);

            // 动作类型下拉框
            var modeCombo = new ComboBox
            {
                Style = FindResource("PropertyComboBox") as Style,
                Margin = new Thickness(0, 0, 4, 0)
            };
            modeCombo.Items.Add(new ComboBoxItem { Content = Strings.Prop_InputComboTap, Tag = InputComboMode.Tap });
            modeCombo.Items.Add(new ComboBoxItem { Content = Strings.Prop_InputComboHold, Tag = InputComboMode.Hold });
            modeCombo.SelectedIndex = mode == InputComboMode.Hold ? 1 : 0;
            Grid.SetColumn(modeCombo, 1);
            row.Children.Add(modeCombo);

            // 延迟输入框
            var delayBox = new TextBox
            {
                Text = delayMs.ToString(),
                Style = FindResource("PropertyTextBox") as Style,
                Margin = new Thickness(0, 0, 4, 0),
                ToolTip = "延迟 (ms)"
            };
            Grid.SetColumn(delayBox, 2);
            row.Children.Add(delayBox);

            // 删除按钮
            var delBtn = new Button
            {
                Content = "✕",
                Width = 24,
                Height = 24,
                Background = System.Windows.Media.Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Foreground = FindResource("TextSecondaryBrush") as System.Windows.Media.Brush,
                Cursor = System.Windows.Input.Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center
            };
            delBtn.Click += (s, e) =>
            {
                var idx = _comboActionRows.FindIndex(r => r.keyBox == keyBox);
                if (idx >= 0) _comboActionRows.RemoveAt(idx);
                parent.Children.Remove(row);
            };
            Grid.SetColumn(delBtn, 3);
            row.Children.Add(delBtn);

            parent.Children.Add(row);
            _comboActionRows.Add((keyBox, modeCombo, delayBox));
        }

        private void SaveInputComboProperties(InputComboTaskCard card)
        {
            // 保存动作列表
            card.Actions.Clear();
            foreach (var (keyBox, modeCombo, delayBox) in _comboActionRows)
            {
                var action = new InputComboAction
                {
                    Key = keyBox.Text,
                    Mode = modeCombo.SelectedItem is ComboBoxItem ci && ci.Tag is InputComboMode m ? m : InputComboMode.Tap,
                    DelayAfterMs = int.TryParse(delayBox.Text, out int d) ? d : 100
                };
                card.Actions.Add(action);
            }

            // 保存其他属性
            if (GetIntValue("RepeatCount", out int repeat)) card.RepeatCount = repeat;
            if (GetStringValue("StopExpression", out string stopExpr)) card.StopExpression = stopExpr;
            if (GetIntValue("TotalDurationMs", out int totalDur)) card.TotalDurationMs = totalDur;
        }

        #endregion

        #region WinTextInput 属性

        private void AddWinTextInputProperties(WinTextInputTaskCard card)
        {
            // 输入文本（支持表达式引用）
            AddTextProperty("InputText", Strings.Prop_WinTextInputText, card.InputText);

            // 输入方式下拉框
            var modeLabel = new TextBlock { Text = Strings.Prop_WinTextInputMode, Style = FindResource("PropertyLabel") as Style };
            var modeCombo = new ComboBox { Style = FindResource("PropertyComboBox") as Style };
            modeCombo.Items.Add(new ComboBoxItem { Content = Strings.Prop_WinTextInputCharByChar, Tag = TextInputMode.CharByChar });
            modeCombo.Items.Add(new ComboBoxItem { Content = Strings.Prop_WinTextInputClipboard, Tag = TextInputMode.Clipboard });
            modeCombo.SelectedIndex = card.InputMode == TextInputMode.Clipboard ? 1 : 0;
            PropertyPanel.Children.Add(modeLabel);
            PropertyPanel.Children.Add(modeCombo);
            _propertyControls["InputMode"] = modeCombo;

            // 按键间隔（仅逐字符模式）
            AddIntProperty("CharIntervalMs", Strings.Prop_WinTextInputInterval, card.CharIntervalMs);
        }

        private void SaveWinTextInputProperties(WinTextInputTaskCard card)
        {
            if (GetStringValue("InputText", out string text)) card.InputText = text;

            if (_propertyControls.TryGetValue("InputMode", out var ctrl) && ctrl is ComboBox combo
                && combo.SelectedItem is ComboBoxItem ci && ci.Tag is TextInputMode mode)
            {
                card.InputMode = mode;
            }

            if (GetIntValue("CharIntervalMs", out int interval)) card.CharIntervalMs = interval;
        }

        #endregion

        #region 浏览器自动化属性

        /// <summary>浏览器取文本：选择器类型、选择器表达式、属性名、CDP端口</summary>
        private void AddBrowserGetTextProperties(BrowserGetTextTaskCard card)
        {
            // 选择器类型
            var selectorTypeLabel = new TextBlock { Text = "选择器类型", Style = FindResource("PropertyLabel") as Style };
            var selectorTypeCombo = new ComboBox { Style = FindResource("PropertyComboBox") as Style };
            selectorTypeCombo.Items.Add(new ComboBoxItem { Content = "CSS 选择器", Tag = BrowserSelectorType.Css });
            selectorTypeCombo.Items.Add(new ComboBoxItem { Content = "XPath", Tag = BrowserSelectorType.XPath });
            selectorTypeCombo.SelectedIndex = (int)card.SelectorType;
            PropertyPanel.Children.Add(selectorTypeLabel);
            PropertyPanel.Children.Add(selectorTypeCombo);
            _propertyControls["SelectorType"] = selectorTypeCombo;

            // 选择器表达式
            AddTextProperty("Selector", "选择器表达式", card.Selector);

            // 属性名（留空=innerText）
            AddTextProperty("AttributeName", "属性名（留空表示取文本）", card.AttributeName);

            // CDP 端口
            AddIntProperty("CdpPort", "CDP 端口（默认 9222）", card.CdpPort);
        }

        private void SaveBrowserGetTextProperties(BrowserGetTextTaskCard card)
        {
            if (_propertyControls.TryGetValue("SelectorType", out var stCtrl) && stCtrl is ComboBox stCombo
                && stCombo.SelectedItem is ComboBoxItem stItem && stItem.Tag is BrowserSelectorType st)
                card.SelectorType = st;

            if (GetStringValue("Selector", out string sel)) card.Selector = sel;
            if (GetStringValue("AttributeName", out string attr)) card.AttributeName = attr;
            if (GetIntValue("CdpPort", out int port)) card.CdpPort = port;
        }

        /// <summary>浏览器执行脚本：JS脚本、CDP端口</summary>
        private void AddBrowserExecuteJsProperties(BrowserExecuteJsTaskCard card)
        {
            // 脚本内容（多行文本框）
            var scriptLabel = new TextBlock { Text = "要执行的 JavaScript", Style = FindResource("PropertyLabel") as Style };
            var scriptBox = new TextBox
            {
                Text = card.Script,
                Style = FindResource("PropertyTextBox") as Style,
                AcceptsReturn = true,
                MinHeight = 80,
                VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto,
                TextWrapping = TextWrapping.Wrap
            };
            PropertyPanel.Children.Add(scriptLabel);
            PropertyPanel.Children.Add(scriptBox);
            _propertyControls["Script"] = scriptBox;

            // CDP 端口
            AddIntProperty("CdpPort", "CDP 端口（默认 9222）", card.CdpPort);
        }

        private void SaveBrowserExecuteJsProperties(BrowserExecuteJsTaskCard card)
        {
            if (GetStringValue("Script", out string script)) card.Script = script;
            if (GetIntValue("CdpPort", out int port)) card.CdpPort = port;
        }

        /// <summary>浏览器等待元素：选择器类型/内容、等待模式、超时、CDP端口</summary>
        private void AddBrowserWaitForElementProperties(BrowserWaitForElementTaskCard card)
        {
            // 选择器类型
            var selectorTypeLabel = new TextBlock { Text = "选择器类型", Style = FindResource("PropertyLabel") as Style };
            var selectorTypeCombo = new ComboBox { Style = FindResource("PropertyComboBox") as Style };
            selectorTypeCombo.Items.Add(new ComboBoxItem { Content = "CSS 选择器", Tag = BrowserSelectorType.Css });
            selectorTypeCombo.Items.Add(new ComboBoxItem { Content = "XPath", Tag = BrowserSelectorType.XPath });
            selectorTypeCombo.SelectedIndex = (int)card.SelectorType;
            PropertyPanel.Children.Add(selectorTypeLabel);
            PropertyPanel.Children.Add(selectorTypeCombo);
            _propertyControls["SelectorType"] = selectorTypeCombo;

            // 选择器表达式
            AddTextProperty("Selector", "选择器表达式", card.Selector);

            // 等待模式
            var waitModeLabel = new TextBlock { Text = "等待模式", Style = FindResource("PropertyLabel") as Style };
            var waitModeCombo = new ComboBox { Style = FindResource("PropertyComboBox") as Style };
            waitModeCombo.Items.Add(new ComboBoxItem { Content = "等待元素出现（可见）", Tag = BrowserWaitMode.Visible });
            waitModeCombo.Items.Add(new ComboBoxItem { Content = "等待元素消失（隐藏）", Tag = BrowserWaitMode.Hidden });
            waitModeCombo.SelectedIndex = (int)card.WaitMode;
            PropertyPanel.Children.Add(waitModeLabel);
            PropertyPanel.Children.Add(waitModeCombo);
            _propertyControls["WaitMode"] = waitModeCombo;

            // 超时时间
            AddIntProperty("TimeoutMs", "超时时间（毫秒）", card.TimeoutMs);

            // CDP 端口
            AddIntProperty("CdpPort", "CDP 端口（默认 9222）", card.CdpPort);
        }

        private void SaveBrowserWaitForElementProperties(BrowserWaitForElementTaskCard card)
        {
            if (_propertyControls.TryGetValue("SelectorType", out var stCtrl) && stCtrl is ComboBox stCombo
                && stCombo.SelectedItem is ComboBoxItem stItem && stItem.Tag is BrowserSelectorType st)
                card.SelectorType = st;

            if (GetStringValue("Selector", out string sel)) card.Selector = sel;

            if (_propertyControls.TryGetValue("WaitMode", out var wmCtrl) && wmCtrl is ComboBox wmCombo
                && wmCombo.SelectedItem is ComboBoxItem wmItem && wmItem.Tag is BrowserWaitMode wm)
                card.WaitMode = wm;

            if (GetIntValue("TimeoutMs", out int timeout)) card.TimeoutMs = timeout;
            if (GetIntValue("CdpPort", out int port)) card.CdpPort = port;
        }

        #endregion

        #region CDP 浏览器自动化高阶操作属性

        private void AddBrowserNativeClickProperties(BrowserNativeClickTaskCard card)
        {
            var selectorTypeLabel = new TextBlock { Text = "起点选择器类型", Style = FindResource("PropertyLabel") as Style };
            var selectorTypeCombo = new ComboBox { Style = FindResource("PropertyComboBox") as Style };
            selectorTypeCombo.Items.Add(new ComboBoxItem { Content = "CSS 选择器", Tag = BrowserSelectorType.Css });
            selectorTypeCombo.Items.Add(new ComboBoxItem { Content = "XPath", Tag = BrowserSelectorType.XPath });
            selectorTypeCombo.SelectedIndex = (int)card.SelectorType;
            PropertyPanel.Children.Add(selectorTypeLabel);
            PropertyPanel.Children.Add(selectorTypeCombo);
            _propertyControls["SelectorType"] = selectorTypeCombo;

            AddTextProperty("Selector", "起点选择器/坐标(如留空则需填写坐标)", card.Selector);
            AddIntProperty("X", "起点X坐标(如果选择器为空则使用)", card.X);
            AddIntProperty("Y", "起点Y坐标", card.Y);

            var clickTypeLabel = new TextBlock { Text = "操作类型", Style = FindResource("PropertyLabel") as Style };
            var clickTypeCombo = new ComboBox { Style = FindResource("PropertyComboBox") as Style };
            clickTypeCombo.Items.Add(new ComboBoxItem { Content = "单击", Tag = ClickType.Single });
            clickTypeCombo.Items.Add(new ComboBoxItem { Content = "双击", Tag = ClickType.Double });
            clickTypeCombo.Items.Add(new ComboBoxItem { Content = "拖动", Tag = ClickType.Swipe });
            clickTypeCombo.SelectedIndex = (int)card.ClickType;
            PropertyPanel.Children.Add(clickTypeLabel);
            PropertyPanel.Children.Add(clickTypeCombo);
            _propertyControls["ClickType"] = clickTypeCombo;

            var swipePanel = new StackPanel();
            var endSelLabel = new TextBlock { Text = "终点选择器", Style = FindResource("PropertyLabel") as Style };
            var endSelBox = new TextBox { Text = card.EndSelector, Style = FindResource("PropertyTextBox") as Style };
            swipePanel.Children.Add(endSelLabel);
            swipePanel.Children.Add(endSelBox);
            _propertyControls["EndSelector"] = endSelBox;

            var endXLabel = new TextBlock { Text = "终点X坐标(如果选择器为空)", Style = FindResource("PropertyLabel") as Style };
            var endXBox = new TextBox { Text = card.EndX.ToString(), Style = FindResource("PropertyTextBox") as Style };
            var endYLabel = new TextBlock { Text = "终点Y坐标", Style = FindResource("PropertyLabel") as Style };
            var endYBox = new TextBox { Text = card.EndY.ToString(), Style = FindResource("PropertyTextBox") as Style };
            swipePanel.Children.Add(endXLabel);
            swipePanel.Children.Add(endXBox);
            swipePanel.Children.Add(endYLabel);
            swipePanel.Children.Add(endYBox);
            _propertyControls["EndX"] = endXBox;
            _propertyControls["EndY"] = endYBox;
            PropertyPanel.Children.Add(swipePanel);

            var doubleClickPanel = new StackPanel();
            var multiCountLabel = new TextBlock { Text = "连击次数", Style = FindResource("PropertyLabel") as Style };
            var multiCountBox = new TextBox { Text = card.MultiClickCount.ToString(), Style = FindResource("PropertyTextBox") as Style };
            doubleClickPanel.Children.Add(multiCountLabel);
            doubleClickPanel.Children.Add(multiCountBox);
            var intervalLabel = new TextBlock { Text = "点击间隔(ms)", Style = FindResource("PropertyLabel") as Style };
            var intervalBox = new TextBox { Text = card.ClickIntervalMs.ToString(), Style = FindResource("PropertyTextBox") as Style };
            doubleClickPanel.Children.Add(intervalLabel);
            doubleClickPanel.Children.Add(intervalBox);
            _propertyControls["MultiClickCount"] = multiCountBox;
            _propertyControls["ClickIntervalMs"] = intervalBox;
            PropertyPanel.Children.Add(doubleClickPanel);

            swipePanel.Visibility = card.ClickType == ClickType.Swipe ? Visibility.Visible : Visibility.Collapsed;
            doubleClickPanel.Visibility = card.ClickType == ClickType.Double ? Visibility.Visible : Visibility.Collapsed;
            clickTypeCombo.SelectionChanged += (s, e) =>
            {
                if (clickTypeCombo.SelectedItem is ComboBoxItem item && item.Tag is ClickType ct)
                {
                    swipePanel.Visibility = ct == ClickType.Swipe ? Visibility.Visible : Visibility.Collapsed;
                    doubleClickPanel.Visibility = ct == ClickType.Double ? Visibility.Visible : Visibility.Collapsed;
                }
            };

            AddIntProperty("CdpPort", "CDP 端口（默认 9222）", card.CdpPort);
        }

        private void SaveBrowserNativeClickProperties(BrowserNativeClickTaskCard card)
        {
            if (_propertyControls.TryGetValue("SelectorType", out var stCtrl) && stCtrl is ComboBox stCombo
                && stCombo.SelectedItem is ComboBoxItem stItem && stItem.Tag is BrowserSelectorType st)
                card.SelectorType = st;

            if (GetStringValue("Selector", out string sel)) card.Selector = sel;
            if (GetIntValue("X", out int x)) card.X = x;
            if (GetIntValue("Y", out int y)) card.Y = y;
            if (GetStringValue("EndSelector", out string esel)) card.EndSelector = esel;
            if (GetIntValue("EndX", out int ex)) card.EndX = ex;
            if (GetIntValue("EndY", out int ey)) card.EndY = ey;
            if (GetIntValue("MultiClickCount", out int mCount)) card.MultiClickCount = mCount;
            if (GetIntValue("ClickIntervalMs", out int mInterval)) card.ClickIntervalMs = mInterval;

            if (_propertyControls.TryGetValue("ClickType", out var ctrl) && ctrl is ComboBox combo
                && combo.SelectedItem is ComboBoxItem ci && ci.Tag is ClickType ct)
                card.ClickType = ct;

            if (GetIntValue("CdpPort", out int port)) card.CdpPort = port;
        }

        private void AddBrowserNativeInputProperties(BrowserNativeInputTaskCard card)
        {
            var selectorTypeLabel = new TextBlock { Text = "选择器类型", Style = FindResource("PropertyLabel") as Style };
            var selectorTypeCombo = new ComboBox { Style = FindResource("PropertyComboBox") as Style };
            selectorTypeCombo.Items.Add(new ComboBoxItem { Content = "CSS 选择器", Tag = BrowserSelectorType.Css });
            selectorTypeCombo.Items.Add(new ComboBoxItem { Content = "XPath", Tag = BrowserSelectorType.XPath });
            selectorTypeCombo.SelectedIndex = (int)card.SelectorType;
            PropertyPanel.Children.Add(selectorTypeLabel);
            PropertyPanel.Children.Add(selectorTypeCombo);
            _propertyControls["SelectorType"] = selectorTypeCombo;

            AddTextProperty("Selector", "选择器表达式", card.Selector);
            AddTextProperty("InputText", "输入文本", card.InputText);

            var modeLabel = new TextBlock { Text = "输入方式", Style = FindResource("PropertyLabel") as Style };
            var modeCombo = new ComboBox { Style = FindResource("PropertyComboBox") as Style };
            modeCombo.Items.Add(new ComboBoxItem { Content = "逐字符输入 (CharByChar)", Tag = TextInputMode.CharByChar });
            modeCombo.Items.Add(new ComboBoxItem { Content = "剪贴板粘贴 (Clipboard)", Tag = TextInputMode.Clipboard });
            modeCombo.SelectedIndex = (int)card.InputMode;
            PropertyPanel.Children.Add(modeLabel);
            PropertyPanel.Children.Add(modeCombo);
            _propertyControls["InputMode"] = modeCombo;

            AddIntProperty("CharIntervalMs", "字符间隔(ms)", card.CharIntervalMs);
            AddIntProperty("CdpPort", "CDP 端口（默认 9222）", card.CdpPort);
        }

        private void SaveBrowserNativeInputProperties(BrowserNativeInputTaskCard card)
        {
            if (_propertyControls.TryGetValue("SelectorType", out var stCtrl) && stCtrl is ComboBox stCombo
                && stCombo.SelectedItem is ComboBoxItem stItem && stItem.Tag is BrowserSelectorType st)
                card.SelectorType = st;

            if (GetStringValue("Selector", out string sel)) card.Selector = sel;
            if (GetStringValue("InputText", out string ipt)) card.InputText = ipt;

            if (_propertyControls.TryGetValue("InputMode", out var mCtrl) && mCtrl is ComboBox mCombo
                && mCombo.SelectedItem is ComboBoxItem mItem && mItem.Tag is TextInputMode tm)
                card.InputMode = tm;

            if (GetIntValue("CharIntervalMs", out int interval)) card.CharIntervalMs = interval;
            if (GetIntValue("CdpPort", out int port)) card.CdpPort = port;
        }

        private void AddBrowserSimulatedClickProperties(BrowserSimulatedClickTaskCard card)
        {
            AddIntProperty("X", "页面全景 X坐标", card.X);
            AddIntProperty("Y", "页面全景 Y坐标", card.Y);

            var clickTypeLabel = new TextBlock { Text = "操作类型", Style = FindResource("PropertyLabel") as Style };
            var clickTypeCombo = new ComboBox { Style = FindResource("PropertyComboBox") as Style };
            clickTypeCombo.Items.Add(new ComboBoxItem { Content = "单击", Tag = ClickType.Single });
            clickTypeCombo.Items.Add(new ComboBoxItem { Content = "双击", Tag = ClickType.Double });
            clickTypeCombo.SelectedIndex = (int)card.ClickType;
            PropertyPanel.Children.Add(clickTypeLabel);
            PropertyPanel.Children.Add(clickTypeCombo);
            _propertyControls["ClickType"] = clickTypeCombo;

            var doubleClickPanel = new StackPanel();
            var multiCountLabel = new TextBlock { Text = "连击次数", Style = FindResource("PropertyLabel") as Style };
            var multiCountBox = new TextBox { Text = card.MultiClickCount.ToString(), Style = FindResource("PropertyTextBox") as Style };
            doubleClickPanel.Children.Add(multiCountLabel);
            doubleClickPanel.Children.Add(multiCountBox);
            var intervalLabel = new TextBlock { Text = "点击间隔(ms)", Style = FindResource("PropertyLabel") as Style };
            var intervalBox = new TextBox { Text = card.ClickIntervalMs.ToString(), Style = FindResource("PropertyTextBox") as Style };
            doubleClickPanel.Children.Add(intervalLabel);
            doubleClickPanel.Children.Add(intervalBox);
            _propertyControls["MultiClickCount"] = multiCountBox;
            _propertyControls["ClickIntervalMs"] = intervalBox;
            PropertyPanel.Children.Add(doubleClickPanel);

            doubleClickPanel.Visibility = card.ClickType == ClickType.Double ? Visibility.Visible : Visibility.Collapsed;
            clickTypeCombo.SelectionChanged += (s, e) =>
            {
                if (clickTypeCombo.SelectedItem is ComboBoxItem item && item.Tag is ClickType ct)
                {
                    doubleClickPanel.Visibility = ct == ClickType.Double ? Visibility.Visible : Visibility.Collapsed;
                }
            };

            AddIntProperty("CdpPort", "CDP 端口（默认 9222）", card.CdpPort);
        }

        private void SaveBrowserSimulatedClickProperties(BrowserSimulatedClickTaskCard card)
        {
            if (GetIntValue("X", out int x)) card.X = x;
            if (GetIntValue("Y", out int y)) card.Y = y;

            if (_propertyControls.TryGetValue("ClickType", out var ctrl) && ctrl is ComboBox combo
                && combo.SelectedItem is ComboBoxItem ci && ci.Tag is ClickType ct)
                card.ClickType = ct;

            if (GetIntValue("MultiClickCount", out int mCount)) card.MultiClickCount = mCount;
            if (GetIntValue("ClickIntervalMs", out int mInterval)) card.ClickIntervalMs = mInterval;
            if (GetIntValue("CdpPort", out int port)) card.CdpPort = port;
        }

        private void AddBrowserCdpCommandProperties(BrowserCdpCommandTaskCard card)
        {
            AddTextProperty("MethodName", "方法名 (如: Page.navigate, Runtime.evaluate)", card.MethodName);
            
            var argsLabel = new TextBlock { Text = "参数(JSON)", Style = FindResource("PropertyLabel") as Style };
            var argsBox = new TextBox
            {
                Text = card.JsonArguments,
                Style = FindResource("PropertyTextBox") as Style,
                AcceptsReturn = true,
                MinHeight = 80,
                VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto,
                TextWrapping = TextWrapping.Wrap
            };
            PropertyPanel.Children.Add(argsLabel);
            PropertyPanel.Children.Add(argsBox);
            _propertyControls["JsonArguments"] = argsBox;

            AddIntProperty("CdpPort", "CDP 端口（默认 9222）", card.CdpPort);
        }

        private void SaveBrowserCdpCommandProperties(BrowserCdpCommandTaskCard card)
        {
            if (GetStringValue("MethodName", out string md)) card.MethodName = md;
            if (GetStringValue("JsonArguments", out string ja)) card.JsonArguments = ja;
            if (GetIntValue("CdpPort", out int port)) card.CdpPort = port;
        }

        private void AddBrowserScreenshotProperties(BrowserScreenshotTaskCard card)
        {
            var fpLabel = new TextBlock { Text = "截图模式", Style = FindResource("PropertyLabel") as Style };
            var fpCombo = new ComboBox { Style = FindResource("PropertyComboBox") as Style };
            fpCombo.Items.Add(new ComboBoxItem { Content = "截取全部全景长图", Tag = true });
            fpCombo.Items.Add(new ComboBoxItem { Content = "仅截取当前可视区域", Tag = false });
            fpCombo.SelectedIndex = card.FullPage ? 0 : 1;
            PropertyPanel.Children.Add(fpLabel);
            PropertyPanel.Children.Add(fpCombo);
            _propertyControls["FullPage"] = fpCombo;

            AddIntProperty("CdpPort", "CDP 端口（默认 9222）", card.CdpPort);
        }

        private void SaveBrowserScreenshotProperties(BrowserScreenshotTaskCard card)
        {
            if (_propertyControls.TryGetValue("FullPage", out var ctrl) && ctrl is ComboBox combo
                && combo.SelectedItem is ComboBoxItem ci && ci.Tag is bool fp)
                card.FullPage = fp;

            if (GetIntValue("CdpPort", out int port)) card.CdpPort = port;
        }

        // ============================================================
        //  HTTP 静默请求
        // ============================================================

        private void AddHttpRequestProperties(HttpRequestTaskCard card)
        {
            AddTextProperty("UrlExpression", Strings.Prop_HttpUrl, card.UrlExpression);

            // HTTP 方法下拉框
            var methodLabel = new TextBlock { Text = Strings.Prop_HttpMethod, Style = FindResource("PropertyLabel") as Style };
            var methodCombo = new ComboBox { Style = FindResource("PropertyComboBox") as Style };
            methodCombo.Items.Add(new ComboBoxItem { Content = "GET", Tag = "GET" });
            methodCombo.Items.Add(new ComboBoxItem { Content = "POST", Tag = "POST" });
            methodCombo.SelectedIndex = card.HttpMethod.Equals("POST", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
            PropertyPanel.Children.Add(methodLabel);
            PropertyPanel.Children.Add(methodCombo);
            _propertyControls["HttpMethod"] = methodCombo;

            AddTextProperty("CustomHeaders", Strings.Prop_HttpHeaders, card.CustomHeaders);
            AddTextProperty("RequestBody", Strings.Prop_HttpBody, card.RequestBody);
            AddIntProperty("TimeoutMs", Strings.Prop_HttpTimeout, card.TimeoutMs);
        }

        private void SaveHttpRequestProperties(HttpRequestTaskCard card)
        {
            if (GetStringValue("UrlExpression", out string url)) card.UrlExpression = url;

            if (_propertyControls.TryGetValue("HttpMethod", out var ctrl) && ctrl is ComboBox combo
                && combo.SelectedItem is ComboBoxItem ci && ci.Tag is string method)
                card.HttpMethod = method;

            if (GetStringValue("CustomHeaders", out string headers)) card.CustomHeaders = headers;
            if (GetStringValue("RequestBody", out string body)) card.RequestBody = body;
            if (GetIntValue("TimeoutMs", out int timeout)) card.TimeoutMs = timeout;
        }

        #endregion
    }
}
