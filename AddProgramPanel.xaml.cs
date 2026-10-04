using System.IO;
using System.Windows;
using Microsoft.Win32;
using ProcessGuardian.Models;
using Forms = System.Windows.Forms;

namespace ProcessGuardian;

public partial class AddProgramPanel : System.Windows.Controls.UserControl
{
    public AddProgramPanel()
    {
        InitializeComponent();
    }

    public event Action<ProgramDefinition>? AddRequested;
    public event Action? CancelRequested;

    public void ResetForm()
    {
        NameBox.Clear();
        PathBox.Clear();
        ArgumentsBox.Clear();
        WorkingDirectoryBox.Clear();
        EnabledBox.IsChecked = true;
        AttachExistingBox.IsChecked = false;
        FormMessage.Text = "";
    }

    public void FocusName() => NameBox.Focus();

    public void ShowError(string message) => FormMessage.Text = message;

    private void BrowseExecutable_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择要守护的程序",
            Filter = "Windows 程序 (*.exe)|*.exe|所有文件 (*.*)|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;

        PathBox.Text = dialog.FileName;
        if (string.IsNullOrWhiteSpace(NameBox.Text))
            NameBox.Text = Path.GetFileNameWithoutExtension(dialog.FileName);
        if (string.IsNullOrWhiteSpace(WorkingDirectoryBox.Text))
            WorkingDirectoryBox.Text = Path.GetDirectoryName(dialog.FileName) ?? "";
    }

    private void BrowseDirectory_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new Forms.FolderBrowserDialog { Description = "选择程序的工作目录" };
        if (dialog.ShowDialog() == Forms.DialogResult.OK)
            WorkingDirectoryBox.Text = dialog.SelectedPath;
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        FormMessage.Text = "";
        var name = NameBox.Text.Trim();
        var executable = Environment.ExpandEnvironmentVariables(PathBox.Text.Trim());
        var workingDirectory = Environment.ExpandEnvironmentVariables(WorkingDirectoryBox.Text.Trim());
        if (string.IsNullOrWhiteSpace(name))
        {
            ShowError("请填写显示名称。");
            NameBox.Focus();
            return;
        }
        if (!Path.IsPathFullyQualified(executable) || !File.Exists(executable))
        {
            ShowError("请选择一个存在的程序文件。");
            PathBox.Focus();
            return;
        }
        if (!string.IsNullOrWhiteSpace(workingDirectory) && !Directory.Exists(workingDirectory))
        {
            ShowError("工作目录不存在，请重新选择或留空。");
            WorkingDirectoryBox.Focus();
            return;
        }

        var definition = new ProgramDefinition
        {
            Name = name,
            ExecutablePath = executable,
            Arguments = ArgumentsBox.Text.Trim(),
            WorkingDirectory = workingDirectory,
            Enabled = EnabledBox.IsChecked == true,
            AttachExisting = AttachExistingBox.IsChecked == true
        };
        AddRequested?.Invoke(definition);
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => CancelRequested?.Invoke();
}
