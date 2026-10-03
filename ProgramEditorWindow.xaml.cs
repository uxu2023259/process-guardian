using System.IO;
using System.Windows;
using Microsoft.Win32;
using ProcessGuardian.Models;
using Forms = System.Windows.Forms;

namespace ProcessGuardian;

public partial class ProgramEditorWindow : Window
{
    private readonly ProgramDefinition _original;
    public ProgramDefinition? Result { get; private set; }

    public ProgramEditorWindow(ProgramDefinition? definition = null)
    {
        InitializeComponent();
        _original = definition?.Copy() ?? new ProgramDefinition();
        if (definition is null) return;
        Heading.Text = "编辑守护程序";
        NameBox.Text = definition.Name;
        PathBox.Text = definition.ExecutablePath;
        ArgumentsBox.Text = definition.Arguments;
        WorkingDirectoryBox.Text = definition.WorkingDirectory;
        EnabledBox.IsChecked = definition.Enabled;
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
        if (string.IsNullOrWhiteSpace(NameBox.Text)) NameBox.Text = Path.GetFileNameWithoutExtension(dialog.FileName);
        if (string.IsNullOrWhiteSpace(WorkingDirectoryBox.Text)) WorkingDirectoryBox.Text = Path.GetDirectoryName(dialog.FileName);
    }

    private void BrowseDirectory_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new Forms.FolderBrowserDialog { Description = "选择程序的工作目录" };
        if (dialog.ShowDialog() == Forms.DialogResult.OK) WorkingDirectoryBox.Text = dialog.SelectedPath;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var name = NameBox.Text.Trim();
        var executable = Environment.ExpandEnvironmentVariables(PathBox.Text.Trim());
        var workingDirectory = Environment.ExpandEnvironmentVariables(WorkingDirectoryBox.Text.Trim());
        if (string.IsNullOrWhiteSpace(name)) { ShowValidation("请填写显示名称。"); return; }
        if (string.IsNullOrWhiteSpace(executable) || !File.Exists(executable)) { ShowValidation("请选择一个存在的程序文件。"); return; }
        if (!string.IsNullOrWhiteSpace(workingDirectory) && !Directory.Exists(workingDirectory))
        {
            ShowValidation("工作目录不存在，请重新选择或留空使用程序所在目录。");
            return;
        }

        Result = new ProgramDefinition
        {
            Id = _original.Id,
            Name = name,
            ExecutablePath = executable,
            Arguments = ArgumentsBox.Text.Trim(),
            WorkingDirectory = workingDirectory,
            Enabled = EnabledBox.IsChecked == true
        };
        DialogResult = true;
    }

    private void ShowValidation(string message) => System.Windows.MessageBox.Show(this, message, "检查设置",
        MessageBoxButton.OK, MessageBoxImage.Information);
}
