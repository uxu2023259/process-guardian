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
    private AppLogger? _logger;
    private MainWindow? _mainWindow;

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
            var viewModel = new MainViewModel(_supervisor, settings, store, startup, _logger, Dispatcher);
            _mainWindow = new MainWindow(viewModel);
            _mainWindow.Closing += OnMainWindowClosing;
            SetupTrayIcon();

            await _supervisor.StartAsync();
            _mainWindow.Show();
            if (e.Args.Any(argument => string.Equals(argument, "--startup", StringComparison.OrdinalIgnoreCase)))
                _mainWindow.Hide();
        }
        catch (Exception exception)
        {
            System.Windows.MessageBox.Show($"进程守护器启动失败：{exception.Message}", "启动失败",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            Shutdown();
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
