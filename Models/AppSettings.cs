namespace ProcessGuardian.Models;

public sealed class AppSettings
{
    public int SchemaVersion { get; set; } = 1;
    public bool StartWithWindows { get; set; } = true;
    public List<ProgramDefinition> Programs { get; set; } = [];
}

public sealed class ProgramDefinition
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string ExecutablePath { get; set; } = "";
    public string Arguments { get; set; } = "";
    public string WorkingDirectory { get; set; } = "";
    public bool Enabled { get; set; } = true;

    public ProgramDefinition Copy() => new()
    {
        Id = Id,
        Name = Name,
        ExecutablePath = ExecutablePath,
        Arguments = Arguments,
        WorkingDirectory = WorkingDirectory,
        Enabled = Enabled
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
