using System.Windows.Input;
using ProcessGuardian.Models;
using ProcessGuardian.Services;

namespace ProcessGuardian.ViewModels;

public sealed class ProgramRowViewModel : ObservableObject
{
    private ProgramDefinition _definition;
    private ProgramStatus _status;

    public ProgramRowViewModel(ProgramDefinition definition, ProgramStatus status,
        Func<Guid, bool, Task> setEnabled, Func<Guid, Task> restart,
        Func<Guid, Task> remove)
    {
        _definition = definition;
        _status = status;
        ToggleCommand = new AsyncRelayCommand(() => setEnabled(Id, !Enabled));
        RestartCommand = new AsyncRelayCommand(() => restart(Id));
        RemoveCommand = new AsyncRelayCommand(() => remove(Id));
    }

    public Guid Id => _definition.Id;
    public string Name => string.IsNullOrWhiteSpace(_definition.Name) ? "未命名程序" : _definition.Name;
    public string ExecutablePath => _definition.ExecutablePath;
    public System.Windows.Media.Imaging.BitmapSource ProgramIcon => ExecutableIconProvider.Get(ExecutablePath);
    public bool Enabled => _definition.Enabled;
    public string StateText => _status.State switch
    {
        ProgramState.Disabled => "已暂停",
        ProgramState.Starting => "正在启动",
        ProgramState.Running => "运行中",
        ProgramState.WaitingToRestart => "等待重启",
        ProgramState.PausedAfterFailures => "故障暂停",
        _ => "异常"
    };
    public string StatusDetail => _status.Detail;
    public string RestartCountText => _status.RestartCount == 1 ? "已恢复 1 次" : $"已恢复 {_status.RestartCount} 次";
    public string ToggleText => Enabled ? "停止" : "启动";
    public bool IsLoading => _status.State is ProgramState.Starting or ProgramState.WaitingToRestart;
    public string StateBrush => _status.State switch
    {
        ProgramState.Running => "#107C10",
        ProgramState.Starting or ProgramState.WaitingToRestart => "#0078D4",
        ProgramState.PausedAfterFailures => "#A4262C",
        _ => "#605E5C"
    };
    public string StateGlyph => _status.State switch
    {
        ProgramState.Running => "●",
        ProgramState.WaitingToRestart => "↻",
        ProgramState.PausedAfterFailures => "!",
        ProgramState.Starting => "○",
        _ => "Ⅱ"
    };

    public ICommand ToggleCommand { get; }
    public ICommand RestartCommand { get; }
    public ICommand RemoveCommand { get; }

    public void UpdateStatus(ProgramStatus status)
    {
        _status = status;
        _definition.Enabled = status.State is ProgramState.Starting or ProgramState.Running or ProgramState.WaitingToRestart;
        Raise(nameof(StateText));
        Raise(nameof(StatusDetail));
        Raise(nameof(RestartCountText));
        Raise(nameof(StateGlyph));
        Raise(nameof(IsLoading));
        Raise(nameof(StateBrush));
        Raise(nameof(Enabled));
        Raise(nameof(ToggleText));
    }
}
