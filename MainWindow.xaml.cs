using System.IO;
using System.Windows;
using Microsoft.Win32;
using ProcessGuardian.Models;
using ProcessGuardian.ViewModels;
using Forms = System.Windows.Forms;

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

    private void BrowseExecutable_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择要守护的程序",
            Filter = "Windows 程序 (*.exe)|*.exe|所有文件 (*.*)|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != true) return;
        PathBox.Text = dialog.FileName;
        if (string.IsNullOrWhiteSpace(NameBox.Text))
            NameBox.Text = Path.GetFileNameWithoutExtension(dialog.FileName);
        if (string.IsNullOrWhiteSpace(WorkingDirectoryBox.Text))
            WorkingDirectoryBox.Text = Path.GetDirectoryName(dialog.FileName) ?? "";
    }

    private void BrowseDirectory_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new Forms.FolderBrowserDialog { Description = "选择程序的工作目录" };
        if (dialog.ShowDialog() == Forms.DialogResult.OK) WorkingDirectoryBox.Text = dialog.SelectedPath;
    }

    private async void Add_Click(object sender, RoutedEventArgs e)
    {
        FormMessage.Text = "";
        var name = NameBox.Text.Trim();
        var executable = Environment.ExpandEnvironmentVariables(PathBox.Text.Trim());
        var workingDirectory = Environment.ExpandEnvironmentVariables(WorkingDirectoryBox.Text.Trim());
        if (string.IsNullOrWhiteSpace(name))
        {
            ShowFormMessage("请填写显示名称。", isError: true);
            NameBox.Focus();
            return;
        }
        if (!Path.IsPathFullyQualified(executable) || !File.Exists(executable))
        {
            ShowFormMessage("请选择一个存在的程序文件。", isError: true);
            PathBox.Focus();
            return;
        }
        if (!string.IsNullOrWhiteSpace(workingDirectory) && !Directory.Exists(workingDirectory))
        {
            ShowFormMessage("工作目录不存在，请重新选择或留空。", isError: true);
            WorkingDirectoryBox.Focus();
            return;
        }

        AddButton.IsEnabled = false;
        try
        {
            await _viewModel.AddAsync(new ProgramDefinition
            {
                Name = name,
                ExecutablePath = executable,
                Arguments = ArgumentsBox.Text.Trim(),
                WorkingDirectory = workingDirectory,
                Enabled = EnabledBox.IsChecked == true,
                AttachExisting = AttachExistingBox.IsChecked == true
            });
            NameBox.Clear();
            PathBox.Clear();
            ArgumentsBox.Clear();
            WorkingDirectoryBox.Clear();
            EnabledBox.IsChecked = true;
            AttachExistingBox.IsChecked = false;
            ShowFormMessage("已添加到守护列表。", isError: false);
            NameBox.Focus();
        }
        catch (Exception exception)
        {
            ShowFormMessage(exception.Message, isError: true);
        }
        finally { AddButton.IsEnabled = true; }
    }

    private void ShowFormMessage(string message, bool isError)
    {
        FormMessage.Text = message;
        FormMessage.Foreground = isError
            ? System.Windows.Media.Brushes.Black
            : System.Windows.Media.Brushes.DimGray;
    }
}
