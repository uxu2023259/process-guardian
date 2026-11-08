using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ProcessGuardian.Controls;
using ProcessGuardian.Models;
using ProcessGuardian.Services;
using ProcessGuardian.ViewModels;

namespace ProcessGuardian;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private bool _addPanelOpen;
    private bool _transitionRunning;
    private bool _addingProgram;
    private bool _windowPickerActive;
    private bool _closed;
    private readonly DesktopWindowPicker _desktopWindowPicker;
    private readonly ProcessTargetOverlayWindow _processTargetOverlay;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _desktopWindowPicker = new DesktopWindowPicker(Dispatcher);
        _processTargetOverlay = new ProcessTargetOverlayWindow();
        DataContext = viewModel;
        AddPanel.AddRequested += AddPanel_AddRequested;
        AddPanel.CancelRequested += AddPanel_CancelRequested;
        AddPanel.ProcessPickerRequested += AddPanel_ProcessPickerRequested;
        _desktopWindowPicker.PreviewChanged += OnProcessPickerPreviewChanged;
        _desktopWindowPicker.DragStarted += OnProcessPickerDragStarted;
        Closed += (_, _) =>
        {
            _closed = true;
            _desktopWindowPicker.Dispose();
            if (_processTargetOverlay.IsVisible) _processTargetOverlay.Close();
        };
        Loaded += (_, _) => FitToWorkArea();
    }

    public void MinimizeToTaskbar() => WindowState = WindowState.Minimized;

    private void FitToWorkArea()
    {
        var workArea = SystemParameters.WorkArea;
        Width = Math.Min(Width, Math.Max(640, workArea.Width - 32));
        Height = Math.Min(Height, Math.Max(480, workArea.Height - 32));
        MinWidth = Math.Min(MinWidth, Width);
        MinHeight = Math.Min(MinHeight, Height);
        Left = workArea.Left + (workArea.Width - Width) / 2;
        Top = workArea.Top + (workArea.Height - Height) / 2;
    }

    private async void AddProgram_Click(object sender, RoutedEventArgs e) => await ShowAddPanelAsync();

    private async Task ShowAddPanelAsync()
    {
        if (_addPanelOpen || _transitionRunning) return;

        _transitionRunning = true;
        _addPanelOpen = true;
        AddPanel.ResetForm();
        AddPanel.IsEnabled = true;
        AddProgramButton.IsEnabled = false;
        AddProgramOverlay.Visibility = Visibility.Visible;
        AddProgramOverlay.Opacity = 0;
        PopupCard.Opacity = 0;
        var transform = (TranslateTransform)PopupCard.RenderTransform;
        transform.Y = SystemParameters.ClientAreaAnimation ? 12 : 0;

        if (!SystemParameters.ClientAreaAnimation)
        {
            AddProgramOverlay.Opacity = 1;
            PopupCard.Opacity = 1;
            AddPanel.FocusName();
            _transitionRunning = false;
            return;
        }

        AddProgramOverlay.BeginAnimation(UIElement.OpacityProperty,
            CreateAnimation(0, 1, 83));
        PopupCard.BeginAnimation(UIElement.OpacityProperty,
            CreateAnimation(0, 1, 83));
        transform.BeginAnimation(TranslateTransform.YProperty,
            CreateMotionAnimation(12, 0, 167));
        await Task.Delay(170);
        AddPanel.FocusName();
        _transitionRunning = false;
    }

    private async void AddPanel_AddRequested(ProgramDefinition definition)
    {
        if (_addingProgram) return;

        _addingProgram = true;
        AddPanel.IsEnabled = false;
        try
        {
            await _viewModel.AddAsync(definition);
            await HideAddPanelAsync(allowWhileAdding: true);
            AddPanel.ResetForm();
        }
        catch (Exception exception)
        {
            AddPanel.ShowError(exception.Message);
        }
        finally
        {
            AddPanel.IsEnabled = true;
            _addingProgram = false;
        }
    }

    private async void AddPanel_ProcessPickerRequested(bool mouseButtonAlreadyDown)
    {
        if (_windowPickerActive || _addingProgram) return;

        _windowPickerActive = true;
        AddPanel.IsEnabled = false;
        try
        {
            var pickTask = _desktopWindowPicker.Start(waitingForMouseDown: !mouseButtonAlreadyDown);
            if (mouseButtonAlreadyDown)
            {
                Mouse.Capture(null);
                Hide();
            }

            var result = await pickTask;
            if (_closed) return;

            _processTargetOverlay.Hide();
            if (!IsVisible)
            {
                Show();
                if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
                Activate();
            }

            var target = result.Target;
            if (result.Canceled || target is null || target.ErrorMessage is not null ||
                string.IsNullOrWhiteSpace(target.ExecutablePath))
            {
                AddPanel.ShowError(result.Message ?? target?.ErrorMessage ?? "未能读取所选程序路径。");
                return;
            }

            var executablePath = target.ExecutablePath;
            var definition = new ProgramDefinition
            {
                Name = string.IsNullOrWhiteSpace(target.ProcessName)
                    ? Path.GetFileNameWithoutExtension(executablePath)
                    : target.ProcessName,
                ExecutablePath = executablePath,
                WorkingDirectory = Path.GetDirectoryName(executablePath) ?? "",
                Enabled = true,
                AttachExisting = true
            };

            _addingProgram = true;
            var processStartTimeUtcTicks = target.ProcessStartTimeUtcTicks
                ?? throw new InvalidOperationException("无法确认所选程序实例，请重新拖动选择。");
            await _viewModel.AddAndAttachAsync(definition, target.ProcessId, processStartTimeUtcTicks);
            await HideAddPanelAsync(allowWhileAdding: true);
            AddPanel.ResetForm();
        }
        catch (Exception exception)
        {
            _processTargetOverlay.Hide();
            if (!_closed && !IsVisible)
            {
                Show();
                if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
                Activate();
            }
            if (!_closed) AddPanel.ShowError(exception.Message);
        }
        finally
        {
            if (!_closed) AddPanel.IsEnabled = true;
            _addingProgram = false;
            _windowPickerActive = false;
        }
    }

    private void OnProcessPickerPreviewChanged(DesktopWindowPickerPreview preview)
    {
        if (_closed || !_windowPickerActive) return;
        _processTargetOverlay.UpdatePreview(preview);
    }

    private void OnProcessPickerDragStarted()
    {
        if (_closed || !_windowPickerActive) return;
        Mouse.Capture(null);
        if (IsVisible) Hide();
    }

    private async void AddPanel_CancelRequested() => await HideAddPanelAsync();

    private async void MainWindow_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Escape || !_addPanelOpen) return;
        e.Handled = true;
        await HideAddPanelAsync();
    }

    private async Task HideAddPanelAsync(bool allowWhileAdding = false)
    {
        if (!_addPanelOpen || _transitionRunning || (_addingProgram && !allowWhileAdding)) return;

        _transitionRunning = true;
        var transform = (TranslateTransform)PopupCard.RenderTransform;
        if (SystemParameters.ClientAreaAnimation)
        {
            AddProgramOverlay.BeginAnimation(UIElement.OpacityProperty,
                CreateAnimation(AddProgramOverlay.Opacity, 0, 83));
            PopupCard.BeginAnimation(UIElement.OpacityProperty,
                CreateAnimation(PopupCard.Opacity, 0, 167));
            transform.BeginAnimation(TranslateTransform.YProperty,
                CreateMotionAnimation(transform.Y, 8, 167));
            await Task.Delay(170);
        }

        AddProgramOverlay.BeginAnimation(UIElement.OpacityProperty, null);
        PopupCard.BeginAnimation(UIElement.OpacityProperty, null);
        transform.BeginAnimation(TranslateTransform.YProperty, null);
        AddProgramOverlay.Opacity = 0;
        PopupCard.Opacity = 1;
        transform.Y = 10;
        AddProgramOverlay.Visibility = Visibility.Collapsed;
        AddProgramButton.IsEnabled = true;
        AddProgramButton.Focus();
        _addPanelOpen = false;
        _transitionRunning = false;
    }

    private static DoubleAnimation CreateAnimation(double from, double to, int durationMs) =>
        new(from, to, TimeSpan.FromMilliseconds(durationMs));

    private static DoubleAnimation CreateMotionAnimation(double from, double to, int durationMs) =>
        new(from, to, TimeSpan.FromMilliseconds(durationMs))
        {
            EasingFunction = new WindowsFastEase { EasingMode = EasingMode.EaseIn }
        };
}
