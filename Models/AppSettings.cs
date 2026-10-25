namespace ProcessGuardian.Models;

public sealed class AppSettings
{
    public int SchemaVersion { get; set; } = 1;
    public bool StartWithWindows { get; set; } = true;
    public bool KeepAwakeEnabled { get; set; }
    public bool? ScreenSaverWasActive { get; set; }
    public List<ProgramDefinition> Programs { get; set; } = [];
}

public sealed class ProgramDefinition
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string ExecutablePath { get; set; } = "";
    public string Arguments { get; set; } = "";
    public string WorkingDirectory { get; set; } = "";
    public string PackageFamilyName { get; set; } = "";
    public string PackageArchitecture { get; set; } = "";
    public string PackageRelativeExecutablePath { get; set; } = "";
    public string PackageRelativeWorkingDirectory { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public bool AttachExisting { get; set; }

    public ProgramDefinition Copy() => new()
    {
        Id = Id,
        Name = Name,
        ExecutablePath = ExecutablePath,
        Arguments = Arguments,
        WorkingDirectory = WorkingDirectory,
        PackageFamilyName = PackageFamilyName,
        PackageArchitecture = PackageArchitecture,
        PackageRelativeExecutablePath = PackageRelativeExecutablePath,
        PackageRelativeWorkingDirectory = PackageRelativeWorkingDirectory,
        Enabled = Enabled,
        AttachExisting = AttachExisting
    };
}

public enum ProgramState
{
    Disabled,
    Starting,
    Running,
    WaitingToRestart,
    PausedAfterFailures,
    Error
}

public sealed record ProgramStatus(
    Guid Id,
    ProgramState State,
    int RestartCount,
    int ConsecutiveFailures,
    DateTimeOffset? NextAttempt,
    string Detail);

public enum LogSeverity { Information, Warning, Error }

public sealed record LogEntry(DateTimeOffset Timestamp, LogSeverity Severity, string ProgramName, string Message);
