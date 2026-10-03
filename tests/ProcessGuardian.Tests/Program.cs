using System.Diagnostics;
using System.Reflection;
using ProcessGuardian.Models;
using ProcessGuardian.Services;

if (args.Length > 0 && args[0] == "--child-sleep")
{
    if (args.Length > 1) await File.WriteAllTextAsync(args[1], "ready");
    await Task.Delay(TimeSpan.FromMinutes(30));
    return 0;
}

if (args.Length > 0 && args[0] == "--child-exit") return 0;

var runner = new TestRunner();
var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".artifacts"));
Directory.CreateDirectory(root);
try
{
    await runner.RunAsync("settings round trip and normalization", () => SettingsRoundTripAsync(root));
    await runner.RunAsync("corrupt settings are preserved", () => CorruptSettingsAsync(root));
    await runner.RunAsync("logger persists entries and rotates archives", () => LoggerRotationAsync(root));
    await runner.RunAsync("crashed process is restarted", () => CrashRecoveryAsync(root));
    await runner.RunAsync("repeated launch failures pause target", () => FailureCircuitBreakerAsync(root));
    await runner.RunAsync("stable run resets consecutive failures", () => StableRunResetAsync(root));
    await runner.RunAsync("manual pause stops process and prevents restart", () => ManualPauseAsync(root));
    await runner.RunAsync("disposing supervisor leaves process running", () => DisposeLeavesProcessAsync(root));
    Console.WriteLine($"Tests: {runner.Passed} passed, {runner.Failed} failed");
}
finally
{
    try { Directory.Delete(root, recursive: true); }
    catch (IOException) { }
}

return runner.Failed == 0 ? 0 : 1;

static async Task SettingsRoundTripAsync(string root)
{
    var path = TestPath(root, "settings-roundtrip", "settings.json");
    var store = new JsonSettingsStore(path);
    var original = new AppSettings { StartWithWindows = false };
    original.Programs.Add(new ProgramDefinition { Name = "worker", ExecutablePath = "worker.exe", Arguments = "--quiet" });
    await store.SaveAsync(original);
    var actual = await store.LoadAsync();
    Check(!actual.StartWithWindows && actual.Programs.Count == 1, "settings fields were not preserved");
    Check(actual.Programs[0].Arguments == "--quiet" && actual.SchemaVersion == 1, "program data was not normalized");
}

static async Task CorruptSettingsAsync(string root)
{
    var path = TestPath(root, "settings-corrupt", "settings.json");
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    await File.WriteAllTextAsync(path, "{ broken json");
    var loaded = await new JsonSettingsStore(path).LoadAsync();
    Check(loaded.Programs.Count == 0, "corrupt settings should fall back to defaults");
    Check(Directory.GetFiles(Path.GetDirectoryName(path)!, "settings.json.corrupt-*").Length == 1,
        "corrupt original settings were not backed up");
}

static async Task LoggerRotationAsync(string root)
{
    var directory = TestPath(root, "logs", "data");
    var logger = new AppLogger(directory, maxFileBytes: 220, archiveCount: 2);
    for (var i = 0; i < 8; i++) await logger.WriteAsync(LogSeverity.Information, "test", $"entry-{i}-" + new string('x', 50));
    Check(File.Exists(Path.Combine(directory, "process-guardian.log.1")), "log rotation did not create an archive");
    var reloaded = new AppLogger(directory, maxFileBytes: 220, archiveCount: 2);
    Check(reloaded.RecentEntries.Any(entry => entry.Message.Contains("entry-7", StringComparison.Ordinal)),
        "recent entries were not loaded from disk");
    Check(reloaded.RecentEntries.Count <= 150, "recent-entry cache exceeded its bound");
}

static async Task CrashRecoveryAsync(string root)
{
    await using var fixture = await SupervisorFixture.CreateAsync(root, "crash", ChildKind.Sleep,
        Options(failureLimit: 4, retryMs: 80, stableMs: 5000));
    var originalPid = await fixture.WaitForRunningPidAsync();
    Process.GetProcessById(originalPid).Kill(entireProcessTree: true);
    var restartedPid = await fixture.WaitForRunningPidAsync(previousPid: originalPid);
    Check(restartedPid != originalPid, "supervisor did not replace crashed process");
    Check(fixture.Supervisor.Statuses.Single().RestartCount >= 1, "restart count was not recorded");
}

static async Task FailureCircuitBreakerAsync(string root)
{
    await using var fixture = await SupervisorFixture.CreateAsync(root, "failure-limit", ChildKind.Exit,
        Options(failureLimit: 3, retryMs: 35, stableMs: 5000));
    var status = await WaitForStatusAsync(fixture.Supervisor, ProgramState.PausedAfterFailures, TimeSpan.FromSeconds(5));
    Check(status.ConsecutiveFailures == 3, "failure counter did not reach configured limit");
    Check(!fixture.Supervisor.Programs.Single().Enabled, "paused target remained enabled in saved definition");
    Check((await new JsonSettingsStore(fixture.SettingsPath).LoadAsync()).Programs.Single().Enabled == false,
        "paused state was not persisted");
}

static async Task StableRunResetAsync(string root)
{
    await using var fixture = await SupervisorFixture.CreateAsync(root, "stable-reset", ChildKind.Sleep,
        Options(failureLimit: 4, retryMs: 45, stableMs: 300));
    var pid = await fixture.WaitForRunningPidAsync();
    await Task.Delay(450);
    Process.GetProcessById(pid).Kill(entireProcessTree: true);
    var waiting = await WaitForStatusAsync(fixture.Supervisor, ProgramState.WaitingToRestart, TimeSpan.FromSeconds(3));
    Check(waiting.ConsecutiveFailures == 1, "stable runtime did not clear previous failure count before exit");
    Check(fixture.Logger.RecentEntries.Any(entry => entry.Message.Contains("连续失败计数已清零", StringComparison.Ordinal)),
        "stable-run reset was not logged");
}

static async Task ManualPauseAsync(string root)
{
    await using var fixture = await SupervisorFixture.CreateAsync(root, "manual-pause", ChildKind.Sleep,
        Options(failureLimit: 4, retryMs: 40, stableMs: 5000));
    var pid = await fixture.WaitForRunningPidAsync();
    await fixture.Supervisor.SetEnabledAsync(fixture.Id, enabled: false);
    await Task.Delay(150);
    Check(fixture.Supervisor.Statuses.Single().State == ProgramState.Disabled, "manual pause state was not applied");
    Check(!IsAlive(pid), "manual pause did not stop the managed process");
    Check(fixture.Supervisor.Statuses.Single().RestartCount == 0, "manual stop was counted as a crash");
}

static async Task DisposeLeavesProcessAsync(string root)
{
    var fixture = await SupervisorFixture.CreateAsync(root, "dispose-leaves-child", ChildKind.Sleep,
        Options(failureLimit: 4, retryMs: 40, stableMs: 5000));
    var pid = await fixture.WaitForRunningPidAsync();
    await fixture.Supervisor.DisposeAsync();
    try { Check(IsAlive(pid), $"child PID {pid} exited when only the supervisor was disposed"); }
    finally { await fixture.DisposeAsync(); }
}

static ProcessSupervisorOptions Options(int failureLimit, int retryMs, int stableMs) => new()
{
    ReconcileInterval = TimeSpan.FromMilliseconds(25),
    StableRunPeriod = TimeSpan.FromMilliseconds(stableMs),
    InitialRetryDelay = TimeSpan.FromMilliseconds(retryMs),
    MaximumRetryDelay = TimeSpan.FromMilliseconds(retryMs * 4),
    FailureLimit = failureLimit
};

static async Task<ProgramStatus> WaitForStatusAsync(ProcessSupervisor supervisor, ProgramState state, TimeSpan timeout)
{
    var timer = Stopwatch.StartNew();
    while (timer.Elapsed < timeout)
    {
        var current = supervisor.Statuses.Single();
        if (current.State == state) return current;
        await Task.Delay(15);
    }
    throw new InvalidOperationException($"Timed out waiting for {state}; last status: {supervisor.Statuses.Single()}");
}

static string TestPath(string root, string directory, string file) => Path.Combine(root, directory, file);

static bool IsAlive(int pid)
{
    try { return !Process.GetProcessById(pid).HasExited; }
    catch (ArgumentException) { return false; }
}

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

enum ChildKind { Sleep, Exit }

sealed class SupervisorFixture : IAsyncDisposable
{
    private readonly ChildKind _kind;
    private readonly ProcessSupervisorOptions _options;
    private readonly List<int> _processIds = [];
    private readonly string _directory;
    private readonly string _readyMarker;

    private SupervisorFixture(string directory, string name, ChildKind kind, ProcessSupervisorOptions options)
    {
        _directory = directory;
        _readyMarker = Path.Combine(directory, "child-ready.marker");
        _kind = kind;
        _options = options;
        Directory.CreateDirectory(directory);
        SettingsPath = Path.Combine(directory, "settings.json");
        Logger = new AppLogger(Path.Combine(directory, "logs"));
        var definition = CreateDefinition(name, kind);
        Id = definition.Id;
        Supervisor = new ProcessSupervisor(new AppSettings { Programs = [definition] },
            new JsonSettingsStore(SettingsPath), Logger, options);
    }

    public Guid Id { get; }
    public string SettingsPath { get; }
    public AppLogger Logger { get; }
    public ProcessSupervisor Supervisor { get; }

    public static async Task<SupervisorFixture> CreateAsync(string root, string name, ChildKind kind,
        ProcessSupervisorOptions options)
    {
        var fixture = new SupervisorFixture(Path.Combine(root, name), name, kind, options);
        await fixture.Supervisor.StartAsync();
        return fixture;
    }

    public async Task<int> WaitForRunningPidAsync(int? previousPid = null)
    {
        var timer = Stopwatch.StartNew();
        while (timer.Elapsed < TimeSpan.FromSeconds(5))
        {
            var status = Supervisor.Statuses.Single();
            if (status.State == ProgramState.Running && status.Detail.Contains("PID ", StringComparison.Ordinal))
            {
                var pid = int.Parse(status.Detail[(status.Detail.LastIndexOf("PID ", StringComparison.Ordinal) + 4)..]);
                if (previousPid is null || pid != previousPid)
                {
                    _processIds.Add(pid);
                    var readyTimer = Stopwatch.StartNew();
                    while (!File.Exists(_readyMarker) && readyTimer.Elapsed < TimeSpan.FromSeconds(2))
                        await Task.Delay(10);
                    if (_kind == ChildKind.Sleep && !File.Exists(_readyMarker))
                        throw new InvalidOperationException($"child PID {pid} never entered sleep mode");
                    return pid;
                }
            }
            await Task.Delay(15);
        }
        throw new InvalidOperationException($"Timed out waiting for child process; last status: {Supervisor.Statuses.Single()}");
    }

    public async ValueTask DisposeAsync()
    {
        await Supervisor.DisposeAsync();
        foreach (var pid in _processIds.Distinct())
        {
            try { Process.GetProcessById(pid).Kill(entireProcessTree: true); }
            catch (ArgumentException) { }
            catch (InvalidOperationException) { }
        }
    }

    private static ProgramDefinition CreateDefinition(string name, ChildKind kind)
    {
        var self = Environment.ProcessPath ?? throw new InvalidOperationException("Cannot locate test runner executable.");
        var isDotnetHost = Path.GetFileNameWithoutExtension(self).Equals("dotnet", StringComparison.OrdinalIgnoreCase);
        var assembly = Assembly.GetEntryAssembly()?.Location ?? throw new InvalidOperationException("Cannot locate test assembly.");
        var executable = isDotnetHost ? self : assembly;
        var childMode = $"--child-{(kind == ChildKind.Sleep ? "sleep" : "exit")}";
        var marker = Path.Combine(Path.GetDirectoryName(assembly)!, "..", "..", "..", "..", ".artifacts", name, "child-ready.marker");
        var arguments = isDotnetHost
            ? $"\"{assembly}\" {childMode} \"{Path.GetFullPath(marker)}\""
            : $"{childMode} \"{Path.GetFullPath(marker)}\"";
        if (!isDotnetHost) executable = self;
        return new ProgramDefinition
        {
            Id = Guid.NewGuid(), Name = name, ExecutablePath = executable,
            Arguments = arguments, WorkingDirectory = AppContext.BaseDirectory, Enabled = true
        };
    }
}

sealed class TestRunner
{
    public int Failed { get; private set; }
    private int _passed;

    public async Task RunAsync(string name, Func<Task> test)
    {
        try
        {
            await test();
            _passed++;
            Console.WriteLine($"PASS  {name}");
        }
        catch (Exception exception)
        {
            Failed++;
            Console.WriteLine($"FAIL  {name}: {exception.Message}");
        }
    }

    public int Passed => _passed;
}
