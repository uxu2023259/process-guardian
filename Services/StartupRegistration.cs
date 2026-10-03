using Microsoft.Win32;

namespace ProcessGuardian.Services;

public sealed class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "ProcessGuardian";

    public void Apply(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
        if (key is null) throw new InvalidOperationException("无法访问当前用户的登录启动设置。");
        if (!enabled)
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
            return;
        }

        var executable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executable))
            throw new InvalidOperationException("无法确定程序路径，不能配置登录启动。");
        key.SetValue(ValueName, $"\"{executable}\" --startup");
    }
}
