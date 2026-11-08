# ShutdownClock · 翻页时钟定时关机

一个 Windows 桌面小工具：选好时间，到点自动**关机 / 重启 / 休眠 / 睡眠**。
倒计时用**翻页时钟**呈现，窗口可以缩到很小、贴在屏幕角落，随时能看到时间在流逝，又不会挡着工作。

> 这是把「定时关机」和「休眠倒计时」两个小工具合并重做的一版，界面与实现全部重写。

## 功能

- **四种动作**：关机、重启、休眠、睡眠
- **灵活的时间输入**
  - `2026-11-10 23:30`：完整日期时间
  - `23:30`：今天（若已过则自动顺延到明天）
  - `+90` / `+2h` / `+30s`：相对时间
  - 也可以点快捷按钮：5 / 15 / 30 / 60 / 120 分钟
- **四种倒计时样式**：翻页时钟、大号数字、进度环、极简文字
- **悬浮小窗**
  - 始终置顶，可拖到屏幕任意角落
  - 支持缩到 220×100，不遮挡工作区
  - 不抢焦点（`WS_EX_NOACTIVATE`），打字时不会被打断
- **可靠计时**：按绝对截止时间计算，不受系统卡顿、休眠唤醒影响
- **关机/重启交给系统计时**：即使关掉本程序，到点依然会关机

## 运行要求

- Windows 10 / 11（x64）
- 无需安装 .NET 运行时（发布包为自包含单文件）

## 下载使用

到 [Releases](../../releases) 下载 `ShutdownClock.exe`，双击运行。

> 程序会请求管理员权限（UAC 确认一次）。关机、重启、休眠都需要 `SeShutdownPrivilege`，
> 普通权限下部分环境会直接返回「拒绝访问(5)」，所以清单里声明为 `requireAdministrator`。

## 从源码构建

```bash
git clone <repo-url>
cd ShutdownClock
dotnet build ShutdownClock.sln -c Release
```

运行测试（27 项核心逻辑测试）：

```bash
dotnet run --project tests/ShutdownClock.Tests
```

发布单文件 exe：

```bash
dotnet publish src/ShutdownClock/ShutdownClock.csproj -c Release -r win-x64 \
  --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

## 项目结构

```
ShutdownClock/
├─ src/ShutdownClock/
│  ├─ Models/
│  │  ├─ PowerAction.cs       动作枚举与显示名
│  │  ├─ TimeInputParser.cs   时间输入解析
│  │  └─ ScheduledTask.cs     定时任务与倒计时状态
│  ├─ Services/
│  │  ├─ NativePower.cs        Win32 关机 / 挂起 API
│  │  ├─ PowerService.cs       电源操作入口（API + shutdown.exe 双路径）
│  │  └─ ElevationService.cs   管理员权限检测与提权
│  ├─ Controls/
│  │  └─ FlipClock.cs          翻页时钟控件
│  ├─ Views/
│  │  ├─ MainWindow.xaml       设置界面
│  │  └─ FloatingClockWindow.xaml  悬浮倒计时小窗
│  └─ app.manifest             DPI 感知 + requireAdministrator
└─ tests/ShutdownClock.Tests/  不依赖测试框架的极简运行器
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

- 休眠 / 睡眠无法交给系统计时，需要程序保持运行；若关闭悬浮窗，任务不会再触发（界面会提示）
- 若系统启用了休眠，「睡眠」可能实际进入休眠。想要真正的睡眠：以管理员执行 `powercfg /hibernate off`
- 仅支持 Windows

## 许可证

[MIT](LICENSE)