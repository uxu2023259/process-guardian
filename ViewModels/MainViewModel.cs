using System.Collections.ObjectModel;
using System.Windows.Threading;
using ProcessGuardian.Models;
using ProcessGuardian.Services;

namespace ProcessGuardian.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly ProcessSupervisor _supervisor;
    private readonly AppSettings _settings;
    private readonly JsonSettingsStore _settingsStore;
    private readonly StartupRegistration _startupRegistration;
    private readonly Dispatcher _dispatcher;
    private bool _startWithWindows;

    public MainViewModel(ProcessSupervisor supervisor, AppSettings settings, JsonSettingsStore settingsStore,
        StartupRegistration startupRegistration, AppLogger logger, Dispatcher dispatcher)
    {
        _supervisor = supervisor;
        _settings = settings;
        _settingsStore = settingsStore;
        _startupRegistration = startupRegistration;
        _dispatcher = dispatcher;
        _startWithWindows = settings.StartWithWindows;

        foreach (var entry in logger.RecentEntries.Reverse()) RecentActivity.Add(entry);
        Programs = new ObservableCollection<ProgramRowViewModel>();
        var statuses = supervisor.Statuses.ToDictionary(status => status.Id);
        foreach (var definition in supervisor.Programs)
            Programs.Add(CreateRow(definition, statuses[definition.Id]));

        _supervisor.StatusChanged += OnStatusChanged;
        logger.EntryWritten += OnLogEntryWritten;
        AddProgramCommand = new RelayCommand(() => AddRequested?.Invoke(this, EventArgs.Empty));
        ToggleStartupCommand = new AsyncRelayCommand(ToggleStartupAsync);
    }

    public ObservableCollection<ProgramRowViewModel> Programs { get; }
    public ObservableCollection<LogEntry> RecentActivity { get; } = [];
    public event EventHandler? AddRequested;
    public event EventHandler<ProgramRowViewModel>? EditRequested;

    public int ProgramCount => Programs.Count;
    public int RunningCount => Programs.Count(program => program.IsRunning);
    public int RestartCount => Programs.Sum(program => program.Status.RestartCount);
    public int PausedCount => Programs.Count(program => program.Status.State == ProgramState.PausedAfterFailures);
    public bool StartWithWindows
    {
        get => _startWithWindows;
        set
        {
            if (Set(ref _startWithWindows, value))
            {
                _settings.StartWithWindows = value;
                ((AsyncRelayCommand)ToggleStartupCommand).Execute(null);
            }
        }
    }
    public string HeaderStatus => RunningCount == 0 ? "正在等待启动" : $"{RunningCount} 个程序正在守护";
    public System.Windows.Input.ICommand AddProgramCommand { get; }
    public System.Windows.Input.ICommand ToggleStartupCommand { get; }

    public async Task AddAsync(ProgramDefinition definition)
    {
        await _supervisor.AddAsync(definition);
        var status = _supervisor.Statuses.Single(item => item.Id == definition.Id);
        await _dispatcher.InvokeAsync(() =>
        {
            Programs.Add(CreateRow(definition, status));
            RefreshStats();
        });
    }

    public async Task EditAsync(ProgramRowViewModel row, ProgramDefinition definition)
    {
        await _supervisor.UpdateAsync(definition);
        await _dispatcher.InvokeAsync(() =>
        {
            row.UpdateDefinition(definition);
            row.UpdateStatus(_supervisor.Statuses.Single(item => item.Id == definition.Id));
            RefreshStats();
        });
    }

    public void RequestEdit(ProgramRowViewModel row) => EditRequested?.Invoke(this, row);

    public async Task RemoveAsync(Guid id)
    {
        var row = Programs.First(program => program.Id == id);
        var answer = System.Windows.MessageBox.Show(
            $"移除“{row.Name}”后将停止守护，但不会关闭当前运行的程序。要继续吗？",
            "移除守护配置", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question);
        if (answer != System.Windows.MessageBoxResult.Yes) return;
        await _supervisor.RemoveAsync(id);
        await _dispatcher.InvokeAsync(() =>
        {
            Programs.Remove(row);
            RefreshStats();
        });
    }

    private ProgramRowViewModel CreateRow(ProgramDefinition definition, ProgramStatus status) =>
        new(definition, status,
            (id, enabled) => _supervisor.SetEnabledAsync(id, enabled),
            id => _supervisor.RestartAsync(id), RequestEdit, id => RemoveAsync(id));

    private void OnStatusChanged(object? sender, ProgramStatus status)
    {
        _ = _dispatcher.InvokeAsync(() =>
        {
            var row = Programs.FirstOrDefault(program => program.Id == status.Id);
            if (row is null) return;
            row.UpdateStatus(status);
            RefreshStats();
        });
    }

    private void OnLogEntryWritten(object? sender, LogEntry entry)
    {
        _ = _dispatcher.InvokeAsync(() =>
        {
            RecentActivity.Insert(0, entry);
            while (RecentActivity.Count > 12) RecentActivity.RemoveAt(RecentActivity.Count - 1);
        });
    }

    private async Task ToggleStartupAsync()
    {
        _startupRegistration.Apply(_settings.StartWithWindows);
        await _settingsStore.SaveAsync(_settings);
    }

    private void RefreshStats()
    {
        Raise(nameof(ProgramCount));
        Raise(nameof(RunningCount));
        Raise(nameof(RestartCount));
        Raise(nameof(PausedCount));
        Raise(nameof(HeaderStatus));
    }
}
