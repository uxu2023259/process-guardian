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
        viewModel.AddRequested += OnAddRequested;
        viewModel.EditRequested += OnEditRequested;
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void MaximizeRestore_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void FitToWorkArea()
    {
        var workArea = SystemParameters.WorkArea;
        var maxWidth = Math.Max(640, workArea.Width - 32);
        var maxHeight = Math.Max(480, workArea.Height - 32);
        Width = Math.Min(Width, maxWidth);
        Height = Math.Min(Height, maxHeight);
        MinWidth = Math.Min(MinWidth, Width);
        MinHeight = Math.Min(MinHeight, Height);
        Left = workArea.Left + (workArea.Width - Width) / 2;
        Top = workArea.Top + (workArea.Height - Height) / 2;
    }

    private async void OnAddRequested(object? sender, EventArgs e)
    {
        try
        {
            var editor = new ProgramEditorWindow { Owner = this };
            if (editor.ShowDialog() != true || editor.Result is null) return;
            await _viewModel.AddAsync(editor.Result);
        }
        catch (Exception exception) { ShowOperationError(exception); }
    }

    private async void OnEditRequested(object? sender, ProgramRowViewModel row)
    {
        try
        {
            var editor = new ProgramEditorWindow(row.Definition) { Owner = this };
            if (editor.ShowDialog() != true || editor.Result is null) return;
            await _viewModel.EditAsync(row, editor.Result);
        }
        catch (Exception exception) { ShowOperationError(exception); }
    }

    private void ShowOperationError(Exception exception) =>
        System.Windows.MessageBox.Show(this, exception.Message, "操作失败",
            MessageBoxButton.OK, MessageBoxImage.Warning);
}
