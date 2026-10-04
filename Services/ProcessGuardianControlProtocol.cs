using ProcessGuardian.Models;

namespace ProcessGuardian.Services;

public static class ProcessGuardianControlProtocol
{
    public const string PipeName = "ProcessGuardian.Control.v1";
}

public sealed record ProcessGuardianControlRequest(
    string Command,
    ProgramDefinition? Program = null);

public sealed record ProcessGuardianControlProgram(
    Guid Id,
    string Name,
    string ExecutablePath,
    bool Enabled,
    ProgramState State,
    int RestartCount,
    string Detail);

public sealed record ProcessGuardianControlResponse(
    bool Success,
    string Message,
    IReadOnlyList<ProcessGuardianControlProgram>? Programs = null);
