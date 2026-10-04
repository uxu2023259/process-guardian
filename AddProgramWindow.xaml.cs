using System.IO;
using System.Windows;
using Microsoft.Win32;
using ProcessGuardian.Models;
using Forms = System.Windows.Forms;

namespace ProcessGuardian;

public partial class AddProgramWindow : Window
{
    public AddProgramWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => NameBox.Focus();
    }

    public ProgramDefinition? Result { get; private set; }

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
            ShowFormMessage("请填写显示名称。");
            NameBox.Focus();
            return;
        }
        if (!Path.IsPathFullyQualified(executable) || !File.Exists(executable))
        {
            ShowFormMessage("请选择一个存在的程序文件。");
            PathBox.Focus();
            return;
        }
        if (!string.IsNullOrWhiteSpace(workingDirectory) && !Directory.Exists(workingDirectory))
        {
            ShowFormMessage("工作目录不存在，请重新选择或留空。");
            WorkingDirectoryBox.Focus();
            return;
        }

        Result = new ProgramDefinition
        {
            Name = name,
            ExecutablePath = executable,
            Arguments = ArgumentsBox.Text.Trim(),
            WorkingDirectory = workingDirectory,
            Enabled = EnabledBox.IsChecked == true,
            AttachExisting = AttachExistingBox.IsChecked == true
        };
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void ShowFormMessage(string message) => FormMessage.Text = message;
}
