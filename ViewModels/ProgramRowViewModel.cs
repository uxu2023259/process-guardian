using System.Windows.Input;
using ProcessGuardian.Models;

namespace ProcessGuardian.ViewModels;

public sealed class ProgramRowViewModel : ObservableObject
{
    private ProgramDefinition _definition;
    private ProgramStatus _status;

    public ProgramRowViewModel(ProgramDefinition definition, ProgramStatus status,
        Func<Guid, bool, Task> setEnabled, Func<Guid, Task> restart,
        Action<ProgramRowViewModel> edit, Func<Guid, Task> remove)
    {
        _definition = definition;
        _status = status;
        ToggleCommand = new AsyncRelayCommand(() => setEnabled(Id, !Enabled));
        RestartCommand = new AsyncRelayCommand(() => restart(Id));
        EditCommand = new RelayCommand(() => edit(this));
        RemoveCommand = new AsyncRelayCommand(() => remove(Id));
    }

    public Guid Id => _definition.Id;
    public ProgramDefinition Definition => _definition.Copy();
    public string Name => string.IsNullOrWhiteSpace(_definition.Name) ? "未命名程序" : _definition.Name;
    public string ExecutablePath => _definition.ExecutablePath;
    public string Arguments => _definition.Arguments;
    public string WorkingDirectory => _definition.WorkingDirectory;
    public bool Enabled => _definition.Enabled;
    public ProgramStatus Status => _status;
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
    public string StateGlyph => _status.State switch
    {
        ProgramState.Running => "●",
        ProgramState.WaitingToRestart => "↻",
        ProgramState.PausedAfterFailures => "!",
        ProgramState.Starting => "○",
        _ => "Ⅱ"
    };
    public bool IsRunning => _status.State == ProgramState.Running;

    public ICommand ToggleCommand { get; }
    public ICommand RestartCommand { get; }
    public ICommand EditCommand { get; }
    public ICommand RemoveCommand { get; }

    public void UpdateDefinition(ProgramDefinition definition)
    {
        _definition = definition;
        Raise(nameof(Name));
        Raise(nameof(ExecutablePath));
        Raise(nameof(Arguments));
        Raise(nameof(WorkingDirectory));
        Raise(nameof(Enabled));
        Raise(nameof(ToggleText));
    }

    public void UpdateStatus(ProgramStatus status)
    {
        _status = status;
        _definition.Enabled = status.State is ProgramState.Starting or ProgramState.Running or ProgramState.WaitingToRestart;
        Raise(nameof(Status));
        Raise(nameof(StateText));
        Raise(nameof(StatusDetail));
        Raise(nameof(RestartCountText));
        Raise(nameof(StateGlyph));
        Raise(nameof(IsRunning));
        Raise(nameof(Enabled));
        Raise(nameof(ToggleText));
    }
}
