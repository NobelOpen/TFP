# TaskFlow Pro (TFP)

[English](#english) | [简体中文](#简体中文)

---

## English

**TaskFlow Pro** is a WPF-based visual desktop automation tool. It uses a card-based workflow design that allows you to orchestrate complex automation processes simply by dragging and dropping—no coding required.

### ✨ Features

#### 🖥️ Windows Automation
- **Launch/Close Apps** — Control application lifecycle by process name or path
- **Screenshot** — Capture specific windows with options to crop the title bar or convert to grayscale
- **Simulate Click** — Single click, double click, and swipe. Supports off-screen clicking (PostMessage)
- **Simulate Input** — Keyboard strokes and mouse wheel scrolling, supporting modifier key combinations
- **UI Automation** — Find and interact with UI elements based on AutomationId or Control Name
- **Subtitle Overlay** — Overlay subtitles on target windows with support for frosted glass, solid color, transparent, or auto-color-sampling backgrounds
- **Win Event Listener** — Pause execution and wait for specific mouse or keyboard events (e.g., Left Click, Enter)

#### 📱 ADB Mobile Control
- Connect/Disconnect ADB devices
- Launch/Close Android applications
- Device screenshots and simulated clicks (including swipes)

#### 🖼️ Image Processing (OpenCV)
- **Crop & Resize** — Supports ROI cropping
- **Template Matching** — Single/Multi-target matching with mask support, outputting coordinates and confidence scores
- **Color Detection & Segmentation** — Filter and segment based on the HSV color space
- **Image Preprocessing** — Grayscale, Binarization (Binary/Otsu/Triangle), and Morphological operations
- **Blob Analysis** — Connected-component analysis with area filtering and multiple sorting methods

#### 🔤 OCR (Optical Character Recognition)
- **PaddleOCR** — Built-in offline OCR engine, ready to use out of the box
- **WeChat OCR** — Optional high-precision recognition engine

#### 🤖 AI Capabilities
- **LLM Translation** — Call Large Language Models for intelligent text translation
- **Multimodal Vision** — Send images + prompts to LLMs to achieve visual understanding and judgment
- **LLM File Translation** — Read text files and perform batch translation with automatic segmentation and context awareness

#### 🔀 Control Flow
- **If / Elif / Else Branches** — Conditional execution supporting expression evaluation
- **For Loops** — Fixed count iteration and expression-based loops
- **Break / Pause / End Flow** — Fine-grained control over execution flow
- **Expression Evaluation** — Dynamic variable calculations (e.g., @A = @B + 1)

#### 📊 Data Processing
- String substring, type conversion, timestamp acquisition, file reading, array generation, array parsing, and array searching
- Global variable system (referenced via @Variable) and inter-task data passing (#TaskReference)

#### 🌐 Multilingual
- English / Chinese bilingual interface, switchable in Settings

### 🛠️ Tech Stack

| Component | Technology |
|------|------|
| Framework | .NET 8 / WPF |
| Architecture | MVVM (CommunityToolkit.Mvvm) |
| Image Processing | OpenCvSharp4 |
| OCR Engine | PaddleOCR (Sdcb.PaddleOCR) |
| Serialization | Newtonsoft.Json |
| Language | C# |

### 📋 System Requirements

- Windows 10/11 (x64)
- .NET 8.0 Runtime
- (Optional) ADB Tools — For Android device control
- (Optional) WeChat Client — For WeChat OCR engine

### 🚀 Quick Start

#### Build from Source

`ash
git clone https://github.com/NobelOpen/TFP.git
cd TFP
dotnet build
dotnet run
`

#### Basic Usage

1. **Add Task Card** — Right-click on the canvas and select the desired task type.
2. **Configure Parameters** — Double-click a card to edit its properties (process name, coordinates, expressions, etc.).
3. **Orchestrate Flow** — Drag and drop cards to adjust execution order; use If/For blocks to control the flow.
4. **Run** — Click the Run button to execute the entire workflow.
5. **View Results** — Monitor screenshots, OCR text, and matching results in real-time within the output panel.

### 📂 Project Structure

`
TaskFlow/
├── Models/
│   └── TaskCards/          # Task card models (organized by category)
├── ViewModels/             # MVVM ViewModels
├── Views/
│   ├── Dialogs/            # Dialogs (Settings, Variables, Models, etc.)
│   └── Windows/            # Windows (Subtitle overlays, etc.)
├── Services/               # Business services (Execution, ADB, OCR, OpenCV)
├── Helpers/                # Utilities (Expression Eval, Win32 Interop, Autocomplete)
├── Converters/             # WPF value converters
├── Resources/              # Multilingual resource files
└── Fonts/                  # Custom embedded fonts
`

### 📄 License

This project is for educational and personal use only.

---

## 简体中文

**TaskFlow Pro** 是一款基于 WPF 的可视化桌面自动化工具，采用卡片式工作流设计，通过拖拽编排即可实现复杂的自动化流程——无需编写代码。

### ✨ 功能特性

#### 🖥️ Windows 自动化
- **启动/关闭应用** — 通过进程名或路径控制应用生命周期
- **屏幕截图** — 支持指定窗口截图，可裁剪标题栏、灰度转换
- **模拟点击** — 单击、双击、滑动，支持离屏点击（PostMessage）
- **模拟输入** — 键盘按键、鼠标滚轮，支持修饰键组合
- **UI 自动化** — 基于 AutomationId 或控件名称查找并操作 UI 元素
- **字幕叠层** — 在目标窗口上叠加字幕，支持毛玻璃/纯色/透明/自动吸色背景
- **Win 事件监听** — 暂停流程，等待指定的鼠标或键盘事件触发（如左键单击、回车键等）

#### 📱 ADB 移动设备控制
- 连接/断开 ADB 设备
- 启动/关闭 Android 应用
- 设备截屏与模拟点击（含滑动）

#### 🖼️ 图像处理 (OpenCV)
- **图像裁剪与缩放** — 支持 ROI 区域裁剪
- **模板匹配** — 单目标/多目标匹配，支持掩膜，输出坐标与置信度
- **颜色识别与分割** — HSV 颜色空间筛选与分割
- **图像预处理** — 灰度化、二值化（Binary/Otsu/Triangle）、形态学操作
- **Blob 连通域分析** — 面积筛选、多种排序方式

#### 🔤 OCR 文字识别
- **PaddleOCR** — 内置离线 OCR 引擎，开箱即用
- **微信 OCR** — 可选的高精度识别引擎

#### 🤖 AI 能力
- **LLM 翻译** — 调用大语言模型进行智能翻译
- **多模态识图** — 将图像 + 提示词发送给 LLM，实现视觉理解与判断
- **LLM 文件翻译** — 读取文本文件并进行批量智能分段翻译，保持行数和上下文一致

#### 🔀 控制流
- **If / Elif / Else 条件分支** — 支持表达式条件判断
- **For 循环** — 支持固定次数与表达式循环
- **中止循环 / 暂停 / 结束流程** — 精细流程控制
- **表达式赋值** — 动态变量运算（如 @A = @B + 1）

#### 📊 数据处理
- 字符串截取、类型转换、时间戳获取、读取文件、数组解析、数组生成与匹配查找
- 全局变量系统（@变量 引用），任务间数据传递（#任务引用）

#### 🌐 多语言
- 中文 / English 双语界面，可在设置中切换

### 🛠️ 技术栈

| 组件 | 技术 |
|------|------|
| 框架 | .NET 8 / WPF |
| 架构 | MVVM (CommunityToolkit.Mvvm) |
| 图像处理 | OpenCvSharp4 |
| OCR 引擎 | PaddleOCR (Sdcb.PaddleOCR) |
| 序列化 | Newtonsoft.Json |
| 语言 | C# |

### 📋 系统要求

- Windows 10/11（x64）
- .NET 8.0 Runtime
- （可选）ADB 工具 — 用于 Android 设备控制
- （可选）微信客户端 — 用于微信 OCR 引擎

### 🚀 快速开始

#### 从源码构建

`ash
git clone https://github.com/NobelOpen/TFP.git
cd TFP
dotnet build
dotnet run
`

#### 基本用法

1. **添加任务卡片** — 从右键菜单选择需要的任务类型
2. **配置参数** — 双击卡片编辑属性（进程名、坐标、表达式等）
3. **编排流程** — 拖拽卡片调整执行顺序，使用 If/For 控制流程
4. **运行** — 点击运行按钮执行整个工作流
5. **查看结果** — 在输出面板实时查看截图、OCR 文本、匹配结果

### 📂 项目结构

`
TaskFlow/
├── Models/
│   └── TaskCards/          # 任务卡片模型（按类别组织）
├── ViewModels/             # MVVM 视图模型
├── Views/
│   ├── Dialogs/            # 对话框（设置、变量管理、模型管理等）
│   └── Windows/            # 窗口（字幕叠层等）
├── Services/               # 业务服务（任务执行、ADB、OCR、OpenCV）
├── Helpers/                # 工具类（表达式求值、Win32 互操作、自动补全）
├── Converters/             # WPF 值转换器
├── Resources/              # 多语言资源文件
└── Fonts/                  # 自定义嵌入字体
`

### 📄 许可证

本项目仅供学习和个人使用。
