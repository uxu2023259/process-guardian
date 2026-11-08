# 进程守护器（ProcessGuardian）

这是一个 Windows 桌面进程监控工具。它可启动并监控指定程序，在程序意外退出时按退避策略重试；也可管理 Windows 登录启动、系统保持唤醒和托盘操作。项目使用 WPF 编写，并通过本地命名管道提供一个受当前 Windows 用户限制的控制接口。

## 运行环境

- Windows x64
- 开发和构建需要 .NET 10 SDK
- 已发布的 `PLUGIN/ProcessGuardian.exe` 使用 win-x64、自包含、单文件发布配置；该预置文件未在本次文档整理中重新构建或验证。

## 构建与发布

构建主程序：

```powershell
dotnet build .\ProcessGuardian.csproj -c Release
```

使用仓库的 `SingleFile` 发布配置生成 win-x64 自包含单文件程序：

```powershell
dotnet publish .\ProcessGuardian.csproj -p:PublishProfile=SingleFile
```

发布配置位于 `Properties/PublishProfiles/SingleFile.pubxml`。发布产物写入 .NET 默认的 `bin/Release/net10.0-windows/win-x64/publish/` 目录；项目没有自动复制到 `PLUGIN/` 的发布脚本。

## 使用说明

首次启动后，可在主窗口添加程序并设置显示名称、可执行文件路径、启动参数和工作目录。可选“接管已运行程序”会尝试监控相同可执行文件路径（或同一 Windows 应用包）下已经运行的实例。Windows 应用包路径会由程序解析，以适应应用版本目录变化。

也可在“添加程序”面板按住“拖动选取进程”，把准星拖到桌面上的应用窗口。窗口高亮后会显示程序路径和 PID；松开鼠标即可将该进程加入保护列表并接管该 PID。按 Esc 或单击右键可取消。无法读取程序路径、目标已退出或目标已在保护列表中时，不会添加重复项。Windows 打包应用若无法映射到实际进程会明确拒绝接管，请改选传统桌面程序窗口。

每个已启用目标每 2 秒检查一次。程序意外退出或启动失败后，守护器按 2 秒起步的指数退避重试，最长等待 60 秒；连续失败 5 次后自动暂停并通过托盘通知提醒。稳定运行满 5 分钟会清零连续失败计数。

可将“关闭窗口”理解为隐藏窗口；窗口关闭后程序仍在系统托盘运行。可从托盘重新打开主窗口，或选择“退出守护器”结束监控。退出守护器本身不会结束受管程序。通过界面的停止、重启等操作会尝试关闭受管程序；若主窗口未能在 3 秒内正常退出，守护器会强制结束该进程及其子进程。接管的已有程序在普通暂停或移除时会保留运行。

“随 Windows 启动”默认开启，使用当前用户的登录启动注册表项；由登录启动参数启动时，主窗口会隐藏到托盘。“保持唤醒”默认关闭；启用后会阻止系统和显示器休眠，并暂时关闭屏幕保护程序。关闭此选项或退出程序时会尝试恢复原屏幕保护状态和电源管理。手动锁屏或组织策略仍可能生效。

## 命令行控制

`tools/ProcessGuardian.Control/` 是可单独构建的命令行控制程序；使用时需要桌面守护器已在当前用户会话中运行。命名管道仅允许当前 Windows 用户连接。

```powershell
dotnet run --project .\tools\ProcessGuardian.Control\ProcessGuardian.Control.csproj -- list
dotnet run --project .\tools\ProcessGuardian.Control\ProcessGuardian.Control.csproj -- minimize
dotnet run --project .\tools\ProcessGuardian.Control\ProcessGuardian.Control.csproj -- add --name "程序名称" --path "C:\路径\程序.exe" [--arguments "参数"] [--working-directory "工作目录"] [--attach-existing]
```

`list` 查看守护目标和状态；`minimize` 隐藏主窗口；`add` 添加并启用程序。`--attach-existing` 会尝试接管已运行的匹配进程。

## 数据与日志

- 设置：`%APPDATA%\ProcessGuardian\settings.json`
- 日志：`%LOCALAPPDATA%\ProcessGuardian\Logs\process-guardian.log`
- 日志最多保留约 2 MiB 的当前文件及 5 个轮转文件；界面保留最近 150 条记录。

设置以 JSON 保存程序列表和登录启动/保持唤醒选项。读取到格式损坏的 JSON 时，程序会先备份损坏文件，再按默认设置启动。

## 目录结构

```text
Models/                         # 程序配置与运行状态模型
Services/                       # 进程监控、设置、日志、托盘辅助和本地控制服务
ViewModels/                     # WPF 界面状态与命令
Controls/                       # 通用界面控件
tools/ProcessGuardian.Control/  # 命名管道命令行客户端
tests/ProcessGuardian.Tests/    # 自动化检查代码
Properties/PublishProfiles/     # 单文件发布配置
```
