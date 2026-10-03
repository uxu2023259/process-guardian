namespace ProcessGuardian.Services;

/// <summary>Timing and retry policy for a supervisor instance.</summary>
public sealed record ProcessSupervisorOptions
{
    public static ProcessSupervisorOptions Default { get; } = new();

    public TimeSpan ReconcileInterval { get; init; } = TimeSpan.FromSeconds(2);
    public TimeSpan StableRunPeriod { get; init; } = TimeSpan.FromMinutes(5);
    public TimeSpan InitialRetryDelay { get; init; } = TimeSpan.FromSeconds(2);
    public TimeSpan MaximumRetryDelay { get; init; } = TimeSpan.FromSeconds(60);
    public int FailureLimit { get; init; } = 5;

    public void Validate()
    {
        if (ReconcileInterval <= TimeSpan.Zero || StableRunPeriod <= TimeSpan.Zero ||
            InitialRetryDelay <= TimeSpan.Zero || MaximumRetryDelay < InitialRetryDelay || FailureLimit < 1)
            throw new ArgumentOutOfRangeException(nameof(ProcessSupervisorOptions), "守护策略参数无效。");
    }
}
