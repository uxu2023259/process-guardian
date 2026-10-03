using System.ComponentModel;
using System.Diagnostics;
using System.Threading.Channels;
using ProcessGuardian.Models;

namespace ProcessGuardian.Services;

public sealed class ProcessSupervisor : IAsyncDisposable
{
    private readonly ProcessSupervisorOptions _options;
    private readonly JsonSettingsStore _settingsStore;
    private readonly AppLogger _logger;
    private readonly object _sync = new();
    private readonly Dictionary<Guid, TargetRuntime> _targets = [];
    private readonly Channel<bool> _wake = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
    {
        FullMode = BoundedChannelFullMode.DropWrite,
        SingleReader = true,
        SingleWriter = false
    });
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private AppSettings _settings;
    private Task? _monitorTask;
    private bool _disposed;

    public ProcessSupervisor(AppSettings settings, JsonSettingsStore settingsStore, AppLogger logger,
        ProcessSupervisorOptions? options = null)
    {
        _settings = settings;
        _settingsStore = settingsStore;
        _logger = logger;
        _options = options ?? ProcessSupervisorOptions.Default;
        _options.Validate();
        foreach (var definition in settings.Programs)
        {
            if (definition.Id == Guid.Empty || _targets.ContainsKey(definition.Id)) continue;
            _targets.Add(definition.Id, new TargetRuntime(definition.Copy()));
        }
    }

    public event EventHandler<ProgramStatus>? StatusChanged;
    public event EventHandler<string>? NotificationRequested;

    public IReadOnlyList<ProgramDefinition> Programs
    {
        get { lock (_sync) return _targets.Values.Select(target => target.Definition.Copy()).ToArray(); }
    }

    public IReadOnlyList<ProgramStatus> Statuses
    {
        get
        {
            lock (_sync)
                return _targets.Values.Select(target => target.Status).ToArray();
        }
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_monitorTask is not null) return;
        _monitorTask = Task.Run(() => MonitorLoopAsync(_lifetime.Token), CancellationToken.None);
        TargetRuntime[] targets;
        lock (_sync) targets = _targets.Values.Where(target => target.Definition.Enabled).ToArray();
        foreach (var target in targets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await target.Gate.WaitAsync(cancellationToken);
            try { await LaunchAsync(target, cancellationToken); }
            finally { target.Gate.Release(); }
        }
    }

    public async Task AddAsync(ProgramDefinition definition, CancellationToken cancellationToken = default)
    {
        var copy = definition.Copy();
        if (copy.Id == Guid.Empty) copy.Id = Guid.NewGuid();
        var runtime = new TargetRuntime(copy);
        lock (_sync)
        {
            if (_targets.ContainsKey(copy.Id)) throw new InvalidOperationException("该程序配置已存在。");
            _targets.Add(copy.Id, runtime);
        }
        await SaveProgramsAsync(cancellationToken);
        if (copy.Enabled && _monitorTask is not null)
        {
            await runtime.Gate.WaitAsync(cancellationToken);
            try { await LaunchAsync(runtime, cancellationToken); }
            finally { runtime.Gate.Release(); }
        }
        else if (!copy.Enabled)
        {
            Publish(runtime, ProgramState.Disabled, "监控已暂停");
        }
    }

    public async Task UpdateAsync(ProgramDefinition definition, CancellationToken cancellationToken = default)
    {
        TargetRuntime runtime;
        lock (_sync)
            runtime = _targets.TryGetValue(definition.Id, out var existing)
                ? existing : throw new KeyNotFoundException("找不到该程序配置。");

        await runtime.Gate.WaitAsync(cancellationToken);
        try
        {
            await StopProcessAsync(runtime, cancellationToken);
            runtime.Definition = definition.Copy();
            runtime.FailureCount = 0;
            runtime.NextAttempt = null;
            if (runtime.Definition.Enabled && _monitorTask is not null)
                await LaunchAsync(runtime, cancellationToken);
            else
                Publish(runtime, ProgramState.Disabled, "配置已更新");
        }
        finally { runtime.Gate.Release(); }
        await SaveProgramsAsync(cancellationToken);
    }

    public async Task RemoveAsync(Guid id, CancellationToken cancellationToken = default)
    {
        TargetRuntime? runtime;
        lock (_sync)
        {
            if (!_targets.Remove(id, out runtime)) return;
        }
        await runtime.Gate.WaitAsync(cancellationToken);
        try
        {
            DetachProcess(runtime);
            runtime.Process?.Dispose();
            runtime.Process = null;
        }
        finally { runtime.Gate.Release(); }
        await SaveProgramsAsync(cancellationToken);
    }

    public async Task SetEnabledAsync(Guid id, bool enabled, CancellationToken cancellationToken = default)
    {
        var runtime = GetRuntime(id);
        await runtime.Gate.WaitAsync(cancellationToken);
        try
        {
            runtime.Definition.Enabled = enabled;
            runtime.FailureCount = 0;
            runtime.NextAttempt = null;
            if (enabled && _monitorTask is not null)
            {
                if (runtime.Process is null) await LaunchAsync(runtime, cancellationToken);
                else Publish(runtime, ProgramState.Running, "正在监控");
            }
            else
            {
                await StopProcessAsync(runtime, cancellationToken);
                Publish(runtime, ProgramState.Disabled, "已停止并暂停监控");
            }
        }
        finally { runtime.Gate.Release(); }
        await SaveProgramsAsync(cancellationToken);
    }

    public async Task RestartAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var runtime = GetRuntime(id);
        await runtime.Gate.WaitAsync(cancellationToken);
        try
        {
            runtime.Definition.Enabled = true;
            runtime.FailureCount = 0;
            runtime.NextAttempt = null;
            await StopProcessAsync(runtime, cancellationToken);
            if (_monitorTask is not null) await LaunchAsync(runtime, cancellationToken);
        }
        finally { runtime.Gate.Release(); }
        await SaveProgramsAsync(cancellationToken);
    }

    private async Task MonitorLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try { await _wake.Reader.ReadAsync(cancellationToken).AsTask().WaitAsync(_options.ReconcileInterval, cancellationToken); }
                catch (TimeoutException) { }
                catch (ChannelClosedException) { return; }

                TargetRuntime[] targets;
                lock (_sync) targets = _targets.Values.ToArray();
                foreach (var runtime in targets)
                {
                    if (cancellationToken.IsCancellationRequested) return;
                    await runtime.Gate.WaitAsync(cancellationToken);
                    try { await ReconcileAsync(runtime, cancellationToken); }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return; }
                    finally { runtime.Gate.Release(); }
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    private async Task ReconcileAsync(TargetRuntime runtime, CancellationToken cancellationToken)
    {
        if (!runtime.Definition.Enabled) return;

        if (runtime.Process is not null)
        {
            bool exited;
            try { exited = runtime.ExitObserved || runtime.Process.HasExited; }
            catch (InvalidOperationException) { exited = true; }

            if (!exited)
            {
                if (!runtime.StabilityReset && runtime.StartedAt is { } started &&
                    DateTimeOffset.UtcNow - started >= _options.StableRunPeriod)
                {
                    runtime.FailureCount = 0;
                    runtime.StabilityReset = true;
                    await _logger.WriteAsync(LogSeverity.Information, runtime.Definition.Name,
                        $"已稳定运行 {_options.StableRunPeriod.TotalMinutes:0.##} 分钟，连续失败计数已清零。", cancellationToken);
                }
                return;
            }

            var exitCode = SafeExitCode(runtime.Process);
            DetachProcess(runtime);
            runtime.Process.Dispose();
            runtime.Process = null;
            if (runtime.StartedAt is { } startedAt && DateTimeOffset.UtcNow - startedAt >= _options.StableRunPeriod)
                runtime.FailureCount = 0;
            await RegisterFailureAsync(runtime, $"程序意外退出（退出码 {exitCode}）", cancellationToken);
        }

        if (runtime.Definition.Enabled && runtime.Process is null &&
            (runtime.NextAttempt is null || DateTimeOffset.UtcNow >= runtime.NextAttempt))
            await LaunchAsync(runtime, cancellationToken);
    }

    private async Task LaunchAsync(TargetRuntime runtime, CancellationToken cancellationToken)
    {
        if (!runtime.Definition.Enabled || runtime.Process is not null) return;
        Publish(runtime, ProgramState.Starting, "正在启动");
        var executable = Environment.ExpandEnvironmentVariables(runtime.Definition.ExecutablePath.Trim());
        var workingDirectory = Environment.ExpandEnvironmentVariables(runtime.Definition.WorkingDirectory.Trim());
        if (string.IsNullOrWhiteSpace(workingDirectory)) workingDirectory = Path.GetDirectoryName(executable) ?? Environment.CurrentDirectory;

        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = executable,
                    Arguments = runtime.Definition.Arguments,
                    WorkingDirectory = workingDirectory,
                    UseShellExecute = false,
                    CreateNoWindow = false
                },
                EnableRaisingEvents = true
            };
            EventHandler handler = (_, _) =>
            {
                runtime.ExitObserved = true;
                _wake.Writer.TryWrite(true);
            };
            process.Exited += handler;
            if (!process.Start())
            {
                process.Exited -= handler;
                process.Dispose();
                await RegisterFailureAsync(runtime, "Windows 未能启动该程序。", cancellationToken);
                return;
            }

            runtime.Process = process;
            runtime.ExitHandler = handler;
            runtime.ExitObserved = false;
            runtime.StartedAt = DateTimeOffset.UtcNow;
            runtime.StabilityReset = false;
            runtime.NextAttempt = null;
            Publish(runtime, ProgramState.Running, $"正在运行 · PID {process.Id}");
            await _logger.WriteAsync(LogSeverity.Information, runtime.Definition.Name,
                $"程序已启动（PID {process.Id}）。", cancellationToken);
        }
        catch (Exception exception) when (exception is Win32Exception or IOException or InvalidOperationException or ArgumentException or NotSupportedException)
        {
            await RegisterFailureAsync(runtime, $"启动失败：{exception.Message}", cancellationToken);
        }
    }

    private async Task RegisterFailureAsync(TargetRuntime runtime, string reason, CancellationToken cancellationToken)
    {
        runtime.FailureCount++;
        runtime.RestartCount++;
        runtime.NextAttempt = null;
        if (runtime.FailureCount >= _options.FailureLimit)
        {
            runtime.Definition.Enabled = false;
            Publish(runtime, ProgramState.PausedAfterFailures, $"连续失败 {_options.FailureLimit} 次，已自动暂停");
            await _logger.WriteAsync(LogSeverity.Error, runtime.Definition.Name,
                $"{reason} 达到连续失败上限 {_options.FailureLimit} 次，守护已暂停。", cancellationToken);
            NotificationRequested?.Invoke(this, $"{runtime.Definition.Name} 连续失败 {_options.FailureLimit} 次，守护已暂停。请检查程序路径、权限或启动条件。");
            await SaveProgramsAsync(cancellationToken);
            return;
        }

        var delay = TimeSpan.FromTicks((long)Math.Min(
            _options.InitialRetryDelay.Ticks * Math.Pow(2, runtime.FailureCount - 1),
            _options.MaximumRetryDelay.Ticks));
        runtime.NextAttempt = DateTimeOffset.UtcNow.Add(delay);
        Publish(runtime, ProgramState.WaitingToRestart, $"{reason} · {Math.Ceiling(delay.TotalSeconds):0} 秒后重试");
        await _logger.WriteAsync(LogSeverity.Warning, runtime.Definition.Name,
            $"{reason} 将在 {delay.TotalSeconds:0.###} 秒后第 {runtime.FailureCount + 1} 次启动。", cancellationToken);
        NotificationRequested?.Invoke(this, $"{runtime.Definition.Name} 已退出，将在 {Math.Ceiling(delay.TotalSeconds):0} 秒后重启。");
    }

    private async Task StopProcessAsync(TargetRuntime runtime, CancellationToken cancellationToken)
    {
        var process = runtime.Process;
        if (process is null) return;
        DetachProcess(runtime);
        runtime.Process = null;
        try
        {
            if (!process.HasExited)
            {
                if (process.CloseMainWindow())
                {
                    try { await process.WaitForExitAsync(cancellationToken).WaitAsync(TimeSpan.FromSeconds(3), cancellationToken); }
                    catch (TimeoutException) { process.Kill(entireProcessTree: true); }
                }
                else
                {
                    process.Kill(entireProcessTree: true);
                }
                await _logger.WriteAsync(LogSeverity.Information, runtime.Definition.Name,
                    "已按用户操作停止受管程序。", cancellationToken);
            }
        }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception exception)
        {
            await _logger.WriteAsync(LogSeverity.Error, runtime.Definition.Name,
                $"停止程序失败：{exception.Message}", cancellationToken);
        }
        finally { process.Dispose(); }
    }

    private static void DetachProcess(TargetRuntime runtime)
    {
        if (runtime.Process is not null && runtime.ExitHandler is not null)
            runtime.Process.Exited -= runtime.ExitHandler;
        runtime.ExitHandler = null;
        runtime.ExitObserved = false;
    }

    private static int SafeExitCode(Process process)
    {
        try { return process.ExitCode; }
        catch (InvalidOperationException) { return -1; }
    }

    private void Publish(TargetRuntime runtime, ProgramState state, string detail)
    {
        runtime.Status = new ProgramStatus(runtime.Definition.Id, state, runtime.RestartCount,
            runtime.FailureCount, runtime.NextAttempt, detail);
        StatusChanged?.Invoke(this, runtime.Status);
    }

    private TargetRuntime GetRuntime(Guid id)
    {
        lock (_sync) return _targets.TryGetValue(id, out var runtime)
            ? runtime : throw new KeyNotFoundException("找不到该程序配置。");
    }

    private async Task SaveProgramsAsync(CancellationToken cancellationToken)
    {
        await _saveGate.WaitAsync(cancellationToken);
        try
        {
            lock (_sync) _settings.Programs = _targets.Values.Select(target => target.Definition.Copy()).ToList();
            await _settingsStore.SaveAsync(_settings, cancellationToken);
        }
        finally { _saveGate.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
        _wake.Writer.TryComplete();
        if (_monitorTask is not null)
        {
            try { await _monitorTask; }
            catch (OperationCanceledException) { }
        }
        TargetRuntime[] targets;
        lock (_sync) targets = _targets.Values.ToArray();
        foreach (var runtime in targets)
        {
            await runtime.Gate.WaitAsync();
            try
            {
                // Leaving the watchdog never terminates the user's programs.
                DetachProcess(runtime);
                runtime.Process?.Dispose();
                runtime.Process = null;
            }
            finally { runtime.Gate.Release(); }
        }
        _lifetime.Dispose();
        _saveGate.Dispose();
    }

    private sealed class TargetRuntime(ProgramDefinition definition)
    {
        public ProgramDefinition Definition { get; set; } = definition;
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public Process? Process { get; set; }
        public EventHandler? ExitHandler { get; set; }
        public bool ExitObserved { get; set; }
        public DateTimeOffset? StartedAt { get; set; }
        public bool StabilityReset { get; set; }
        public int FailureCount { get; set; }
        public int RestartCount { get; set; }
        public DateTimeOffset? NextAttempt { get; set; }
        public ProgramStatus Status { get; set; } = new(definition.Id,
            definition.Enabled ? ProgramState.Starting : ProgramState.Disabled, 0, 0, null,
            definition.Enabled ? "等待启动" : "监控已暂停");
    }
}
