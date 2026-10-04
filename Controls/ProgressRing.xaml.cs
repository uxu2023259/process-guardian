using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace ProcessGuardian.Controls;

public partial class ProgressRing : System.Windows.Controls.UserControl
{
    private bool _isSpinning;

    public ProgressRing()
    {
        InitializeComponent();
        Loaded += (_, _) => UpdateAnimation();
        Unloaded += (_, _) => StopAnimation();
        IsVisibleChanged += (_, _) => UpdateAnimation();
    }

    private void UpdateAnimation()
    {
        if (IsLoaded && IsVisible)
        {
            if (_isSpinning) return;
            ((Storyboard)Resources["SpinStoryboard"]).Begin(this, true);
            _isSpinning = true;
            return;
        }

        StopAnimation();
    }

    private void StopAnimation()
    {
        if (!_isSpinning) return;
        ((Storyboard)Resources["SpinStoryboard"]).Stop(this);
        _isSpinning = false;
    }
}
