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
        viewModel.AddRequested += OnAddRequested;
        viewModel.EditRequested += OnEditRequested;
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
