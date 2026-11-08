# ShutdownClock · 定时关机（WinUI 3）

一个 Windows 桌面小工具：选好时间，到点自动**关机 / 重启 / 休眠 / 睡眠**。界面使用 **WinUI 3** 构建，跟随 Windows 11 原生视觉风格。
倒计时小窗可以缩到很小、贴在屏幕角落，随时能看到时间在流逝，又不会挡着工作。

> 这是把「定时关机」和「休眠倒计时」两个小工具合并重做的一版，界面与实现全部重写。

## 功能

- **四种动作**：关机、重启、休眠、睡眠
- **灵活的时间输入**
  - `2026-11-10 23:30`：完整日期时间
  - `23:30`：今天（若已过则自动顺延到明天）
  - `+90` / `+2h` / `+30s`：相对时间
  - 也可以点快捷按钮：5 / 15 / 30 / 60 / 120 分钟
- **三种倒计时样式**：大号数字、进度环、极简文字
- **跟随系统深浅色**：深色模式深底白字，浅色模式白底深字，不会出现白底白字
- **WinUI 3 + Mica 材质**：支持时自动套用半透明亚克力背景，标题栏与控件为原生风格
- **悬浮小窗**
  - 始终置顶，可拖到屏幕任意角落
  - 支持缩到 300×110 的扁长条，数字占满窗口，不遮挡工作区
  - 不抢焦点（`WS_EX_NOACTIVATE`），打字时不会被打断
  - 进度环样式使用正方形画布，环直径等于容器边长
  - 内置「撤单」按钮，不必回主窗口就能撤销系统已排定的关机 / 重启
  - 关掉悬浮窗即视为取消本次定时，主窗倒计时同步停止
  - 关闭主窗口会连同悬浮窗一起收掉，不留孤儿窗口
- **可靠计时**：按绝对截止时间计算，不受系统卡顿、休眠唤醒影响
- **关机/重启交给系统计时**：即使关掉本程序，到点依然会关机

## 运行要求

- Windows 10 / 11（x64）
- 无需安装 .NET 运行时，也无需预装 Windows App Runtime（发布包为自包含）

## 下载使用

到 [Releases](../../releases) 下载压缩包，解压后双击 `ShutdownClock.exe` 运行。

> WinUI 3 应用依赖一组原生 DLL（Windows App SDK 运行时），因此发布的是**整个目录**而不是单文件。
> 解压后请不要只拷走 exe，需要连同同目录的文件一起使用。

> 程序会请求管理员权限（UAC 确认一次）。关机、重启、休眠都需要 `SeShutdownPrivilege`，
> 普通权限下部分环境会直接返回「拒绝访问(5)」，所以清单里声明为 `requireAdministrator`。

## 从源码构建

```bash
git clone <repo-url>
cd ShutdownClock
dotnet build ShutdownClock.sln -c Release
```

运行测试：

```bash
# 核心逻辑测试（30 项）
dotnet run --project tests/ShutdownClock.Tests
```

发布自包含目录：

```bash
dotnet publish winui/ShutdownClock.WinUI/ShutdownClock.WinUI.csproj -c Release -r win-x64 --self-contained true -p:WindowsAppSDKSelfContained=true -o publish
```

## 项目结构

```
ShutdownClock/
├─ src/ShutdownClock.Core/        与界面无关的核心逻辑（可单测）
│  ├─ Models/
│  │  ├─ PowerAction.cs           动作枚举与显示名
│  │  ├─ TimeInputParser.cs       时间输入解析
│  │  └─ ScheduledTask.cs         定时任务与倒计时状态
│  └─ Services/
│     ├─ NativePower.cs           Win32 关机 / 挂起 API
│     ├─ PowerService.cs          电源操作入口（API + shutdown.exe 双路径）
│     └─ ElevationService.cs      管理员权限检测与提权
├─ winui/ShutdownClock.WinUI/     WinUI 3 界面层
│  ├─ Views/
│  │  ├─ MainWindow.xaml          设置界面
│  │  └─ FloatingClockWindow.xaml 悬浮倒计时小窗
│  ├─ Services/
│  │  ├─ Theme.cs                 跟随系统深浅色的配色
│  │  └─ WindowHelper.cs          窗口尺寸 / Mica / 置顶 / 贴角
│  └─ app.manifest                DPI 感知 + requireAdministrator
└─ tests/ShutdownClock.Tests/     核心逻辑测试（30 项，不依赖测试框架）
```

## 实现要点

**倒计时不会越走越快。** 用绝对截止时间做差（`target - DateTime.Now`），而不是每次 `-= 1`。
早期用批处理的 `choice /t 1` 做秒表时，本机实测每次只等 0.35 秒，倒计时会飞快跑完——这也是放弃批处理的原因。

**在 WPF 进程里派生 `shutdown.exe` 会踩坑。** 实测发现：

| 启动方式 | 结果 |
| --- | --- |
| `UseShellExecute = true` + `CreateNoWindow = true` | 成功 |
| `UseShellExecute = false`（含管道重定向） | 拒绝访问(5) / 1190 |

所以 `PowerService` 只用第一种组合，并且把参数作为**单个字符串**传入——
若拆成数组，含空格的 `/c` 注释会被拆散，`shutdown` 会打印帮助并失败。

**优先走 Win32 API，失败回退。** `InitiateSystemShutdownEx` 权限足够时最稳；
非管理员时可能返回拒绝访问，此时自动回退到 `shutdown.exe`。

## 已知限制

- 休眠 / 睡眠无法交给系统计时，需要程序保持运行；若关闭悬浮窗或主程序，任务不会再触发（界面会提示）
- 若系统启用了休眠，「睡眠」可能实际进入休眠。想要真正的睡眠：以管理员执行 `powercfg /hibernate off`
- 仅支持 Windows

## 许可证

[MIT](LICENSE)