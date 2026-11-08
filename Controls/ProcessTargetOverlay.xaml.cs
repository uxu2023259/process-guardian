using System.Windows;
using System.Windows.Controls;

namespace ProcessGuardian.Controls;

public sealed record ProcessTargetDisplayState(
    string WindowTitle,
    string? ProcessName,
    int? ProcessId,
    string? ExecutablePath,
    string StatusMessage,
    bool IsLoading,
    bool ShowHighlight,
    bool IsError);

public partial class ProcessTargetOverlay : System.Windows.Controls.UserControl
{
    public static readonly DependencyProperty StateProperty = DependencyProperty.Register(
        nameof(State), typeof(ProcessTargetDisplayState), typeof(ProcessTargetOverlay));

    public ProcessTargetDisplayState? State
    {
        get => (ProcessTargetDisplayState?)GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }

    public ProcessTargetOverlay()
    {
        InitializeComponent();
    }

    public void SetPointerPosition(double left, double top)
    {
        Canvas.SetLeft(PointerMark, left - PointerMark.Width / 2);
        Canvas.SetTop(PointerMark, top - PointerMark.Height / 2);
        PointerMark.Visibility = State?.ShowHighlight == true ? Visibility.Visible : Visibility.Collapsed;
    }
}
