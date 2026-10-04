using System.Windows;
using ProcessGuardian.ViewModels;

namespace ProcessGuardian;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        AppVersion.Text = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "";
        Loaded += (_, _) => FitToWorkArea();
        StateChanged += (_, _) => MaximizeButton.Content = WindowState == WindowState.Maximized ? "❐" : "□";
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => MinimizeToTaskbar();

    public void MinimizeToTaskbar() => WindowState = WindowState.Minimized;

    private void MaximizeRestore_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

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

    private async void AddProgram_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new AddProgramWindow { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Result is null) return;

        AddProgramButton.IsEnabled = false;
        try
        {
            await _viewModel.AddAsync(dialog.Result);
        }
        catch (Exception exception)
        {
            System.Windows.MessageBox.Show(this, exception.Message, "添加失败",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            AddProgramButton.IsEnabled = true;
        }
    }
}
