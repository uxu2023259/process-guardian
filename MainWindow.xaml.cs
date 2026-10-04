using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ProcessGuardian.Models;
using ProcessGuardian.ViewModels;

namespace ProcessGuardian;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private bool _addPanelOpen;
    private bool _transitionRunning;
    private bool _addingProgram;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        AddPanel.AddRequested += AddPanel_AddRequested;
        AddPanel.CancelRequested += AddPanel_CancelRequested;
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
        transform.Y = 12;

        AddProgramOverlay.BeginAnimation(UIElement.OpacityProperty,
            CreateAnimation(0, 1, 190, EasingMode.EaseOut));
        PopupCard.BeginAnimation(UIElement.OpacityProperty,
            CreateAnimation(0, 1, 190, EasingMode.EaseOut));
        transform.BeginAnimation(TranslateTransform.YProperty,
            CreateAnimation(12, 0, 220, EasingMode.EaseOut));
        await Task.Delay(230);
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

    private async void AddPanel_CancelRequested() => await HideAddPanelAsync();

    private async void AddProgramOverlay_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, AddProgramOverlay))
            await HideAddPanelAsync();
    }

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
        AddProgramOverlay.BeginAnimation(UIElement.OpacityProperty,
            CreateAnimation(AddProgramOverlay.Opacity, 0, 140, EasingMode.EaseIn));
        PopupCard.BeginAnimation(UIElement.OpacityProperty,
            CreateAnimation(PopupCard.Opacity, 0, 150, EasingMode.EaseIn));
        transform.BeginAnimation(TranslateTransform.YProperty,
            CreateAnimation(transform.Y, 8, 150, EasingMode.EaseIn));
        await Task.Delay(160);

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

    private static DoubleAnimation CreateAnimation(double from, double to, int durationMs, EasingMode easingMode) =>
        new(from, to, TimeSpan.FromMilliseconds(durationMs))
        {
            EasingFunction = new CubicEase { EasingMode = easingMode }
        };
}
