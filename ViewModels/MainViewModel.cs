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
        StartupRegistration startupRegistration, Dispatcher dispatcher)
    {
        _supervisor = supervisor;
        _settings = settings;
        _settingsStore = settingsStore;
        _startupRegistration = startupRegistration;
        _dispatcher = dispatcher;
        _startWithWindows = settings.StartWithWindows;

        Programs = new ObservableCollection<ProgramRowViewModel>();
        var statuses = supervisor.Statuses.ToDictionary(status => status.Id);
        foreach (var definition in supervisor.Programs)
            Programs.Add(CreateRow(definition, statuses[definition.Id]));

        _supervisor.StatusChanged += OnStatusChanged;
        ToggleStartupCommand = new AsyncRelayCommand(ToggleStartupAsync);
    }

    public ObservableCollection<ProgramRowViewModel> Programs { get; }
    public int ProgramCount => Programs.Count;

    public bool StartWithWindows
    {
        get => _startWithWindows;
        set
        {
            if (!Set(ref _startWithWindows, value)) return;
            _settings.StartWithWindows = value;
            ((AsyncRelayCommand)ToggleStartupCommand).Execute(null);
        }
    }

    public System.Windows.Input.ICommand ToggleStartupCommand { get; }

    public async Task AddAsync(ProgramDefinition definition)
    {
        await _supervisor.AddAsync(definition);
        var status = _supervisor.Statuses.Single(item => item.Id == definition.Id);
        await _dispatcher.InvokeAsync(() =>
        {
            Programs.Add(CreateRow(definition, status));
            Raise(nameof(ProgramCount));
        });
    }

    public async Task RemoveAsync(Guid id)
    {
        await _supervisor.RemoveAsync(id);
        await _dispatcher.InvokeAsync(() =>
        {
            var row = Programs.FirstOrDefault(program => program.Id == id);
            if (row is null) return;
            Programs.Remove(row);
            Raise(nameof(ProgramCount));
        });
    }

    private ProgramRowViewModel CreateRow(ProgramDefinition definition, ProgramStatus status) =>
        new(definition, status,
            (id, enabled) => _supervisor.SetEnabledAsync(id, enabled),
            id => _supervisor.RestartAsync(id),
            id => RemoveAsync(id));

    private void OnStatusChanged(object? sender, ProgramStatus status)
    {
        _ = _dispatcher.InvokeAsync(() =>
        {
            var row = Programs.FirstOrDefault(program => program.Id == status.Id);
            row?.UpdateStatus(status);
        });
    }

    private async Task ToggleStartupAsync()
    {
        _startupRegistration.Apply(_settings.StartWithWindows);
        await _settingsStore.SaveAsync(_settings);
    }
}
