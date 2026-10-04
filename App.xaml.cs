using System.Threading;
using System.Windows;
using ProcessGuardian.Models;
using ProcessGuardian.Services;
using ProcessGuardian.ViewModels;
using Forms = System.Windows.Forms;

namespace ProcessGuardian;

public partial class App : System.Windows.Application
{
    private Mutex? _instanceMutex;
    private bool _ownsMutex;
    private bool _exitRequested;
    private Forms.NotifyIcon? _trayIcon;
    private ProcessSupervisor? _supervisor;
    private ProcessGuardianControlServer? _controlServer;
    private AppLogger? _logger;
    private MainWindow? _mainWindow;
    private MainViewModel? _viewModel;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _instanceMutex = new Mutex(initiallyOwned: true, "Local\\ProcessGuardian.SingleInstance", out _ownsMutex);
        if (!_ownsMutex)
        {
            System.Windows.MessageBox.Show("进程守护器已经在运行，可从系统托盘打开。", "进程守护器",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            Shutdown();
            return;
        }

        try
        {
            var store = new JsonSettingsStore();
            var settings = await store.LoadAsync();
            _logger = new AppLogger();
            var startup = new StartupRegistration();
            try { startup.Apply(settings.StartWithWindows); }
            catch (Exception exception)
            {
                await _logger.WriteAsync(LogSeverity.Warning, "系统", $"更新登录启动设置失败：{exception.Message}");
            }

            _supervisor = new ProcessSupervisor(settings, store, _logger);
            _supervisor.NotificationRequested += OnNotificationRequested;
            _viewModel = new MainViewModel(_supervisor, settings, store, startup, _logger, Dispatcher);
            _mainWindow = new MainWindow(_viewModel);
            _mainWindow.Closing += OnMainWindowClosing;
            SetupTrayIcon();

            await _supervisor.StartAsync();
            _mainWindow.Show();
            if (e.Args.Any(argument => string.Equals(argument, "--startup", StringComparison.OrdinalIgnoreCase)))
                _mainWindow.Hide();
            _controlServer = new ProcessGuardianControlServer(HandleControlRequestAsync);
            _controlServer.Start();
        }
        catch (Exception exception)
        {
            System.Windows.MessageBox.Show($"进程守护器启动失败：{exception.Message}", "启动失败",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            Shutdown();
        }
    }

    private async Task<ProcessGuardianControlResponse> HandleControlRequestAsync(
        ProcessGuardianControlRequest request, CancellationToken cancellationToken)
    {
        if (_supervisor is null || _viewModel is null)
            return new ProcessGuardianControlResponse(false, "守护器尚未完成启动。");

        switch (request.Command.ToLowerInvariant())
        {
            case "add":
                var program = request.Program;
                if (program is null || string.IsNullOrWhiteSpace(program.Name) ||
                    !Path.IsPathFullyQualified(program.ExecutablePath) || !File.Exists(program.ExecutablePath))
                    return new ProcessGuardianControlResponse(false, "程序名称或绝对路径无效，且目标文件必须存在。");
                if (_supervisor.Programs.Any(existing => string.Equals(existing.Name, program.Name,
                        StringComparison.OrdinalIgnoreCase) && string.Equals(existing.ExecutablePath,
                        program.ExecutablePath, StringComparison.OrdinalIgnoreCase)))
                    return new ProcessGuardianControlResponse(false, "同名程序和路径已存在。");
                cancellationToken.ThrowIfCancellationRequested();
                await _viewModel.AddAsync(program);
                return new ProcessGuardianControlResponse(true, $"已添加并启用“{program.Name}”。");

            case "list":
                var statuses = _supervisor.Statuses.ToDictionary(status => status.Id);
                var programs = _supervisor.Programs.Select(definition =>
                {
                    var status = statuses[definition.Id];
                    return new ProcessGuardianControlProgram(definition.Id, definition.Name,
                        definition.ExecutablePath, definition.Enabled, status.State,
                        status.RestartCount, status.Detail);
                }).ToArray();
                return new ProcessGuardianControlResponse(true, $"共 {programs.Length} 个守护目标。", programs);

            case "minimize":
                await Dispatcher.InvokeAsync(() => _mainWindow?.MinimizeToTaskbar());
                return new ProcessGuardianControlResponse(true, "守护器窗口已最小化。");

            default:
                return new ProcessGuardianControlResponse(false, "不支持的控制命令。");
        }
    }

    private void SetupTrayIcon()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("打开进程守护器", null, (_, _) => Dispatcher.Invoke(ShowMainWindow));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("退出守护器", null, (_, _) => Dispatcher.Invoke(ExitApplication));

        _trayIcon = new Forms.NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Text = "进程守护器 · 后台监控中",
            ContextMenuStrip = menu,
            Visible = true
        };
        _trayIcon.DoubleClick += (_, _) => Dispatcher.Invoke(ShowMainWindow);
    }

    private void OnMainWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_exitRequested) return;
        e.Cancel = true;
        _mainWindow?.Hide();
    }

    private void OnNotificationRequested(object? sender, string message)
    {
        Dispatcher.Invoke(() =>
        {
            if (_trayIcon is null) return;
            _trayIcon.BalloonTipTitle = "进程守护器";
            _trayIcon.BalloonTipText = message;
            _trayIcon.BalloonTipIcon = Forms.ToolTipIcon.Warning;
            _trayIcon.ShowBalloonTip(5000);
        });
    }

    private void ShowMainWindow()
    {
        if (_mainWindow is null) return;
        _mainWindow.Show();
        if (_mainWindow.WindowState == WindowState.Minimized) _mainWindow.WindowState = WindowState.Normal;
        _mainWindow.Activate();
    }

    private void ExitApplication()
    {
        _exitRequested = true;
        _mainWindow?.Close();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_controlServer is not null)
        {
            try { _controlServer.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
            catch { }
        }
        _trayIcon?.Dispose();
        if (_supervisor is not null)
        {
            try { _supervisor.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
            catch { }
        }
        if (_ownsMutex) _instanceMutex?.ReleaseMutex();
        _instanceMutex?.Dispose();
        base.OnExit(e);
    }
}
