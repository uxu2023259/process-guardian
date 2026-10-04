using System.Text.Json;
using ProcessGuardian.Models;
using ProcessGuardian.Services;

if (args.Length == 0)
{
    PrintUsage();
    return 2;
}

ProcessGuardianControlRequest request;
try
{
    request = args[0].ToLowerInvariant() switch
    {
        "minimize" when args.Length == 1 => new ProcessGuardianControlRequest("minimize"),
        "list" when args.Length == 1 => new ProcessGuardianControlRequest("list"),
        "add" => ParseAdd(args[1..]),
        _ => throw new ArgumentException("未知命令或参数不正确。")
    };
}
catch (ArgumentException exception)
{
    Console.Error.WriteLine(exception.Message);
    PrintUsage();
    return 2;
}

try
{
    var response = await ProcessGuardianControlClient.SendAsync(request);
    Console.WriteLine(response.Message);
    if (response.Programs is not null)
        Console.WriteLine(JsonSerializer.Serialize(response.Programs, new JsonSerializerOptions { WriteIndented = true }));
    return response.Success ? 0 : 1;
}
catch (Exception exception) when (exception is IOException or TimeoutException or OperationCanceledException)
{
    Console.Error.WriteLine($"无法连接正在运行的进程守护器：{exception.Message}");
    return 1;
}

static ProcessGuardianControlRequest ParseAdd(string[] values)
{
    var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    var attachExisting = false;
    for (var index = 0; index < values.Length; index++)
    {
        var key = values[index];
        if (key == "--attach-existing")
        {
            attachExisting = true;
            continue;
        }
        if (key is not ("--name" or "--path" or "--arguments" or "--working-directory") || index + 1 >= values.Length)
            throw new ArgumentException($"无效选项：{key}");
        options[key] = values[++index];
    }

    if (!options.TryGetValue("--name", out var name) || string.IsNullOrWhiteSpace(name))
        throw new ArgumentException("add 命令必须提供 --name。");
    if (!options.TryGetValue("--path", out var path) || !Path.IsPathFullyQualified(path))
        throw new ArgumentException("add 命令必须通过 --path 提供绝对程序路径。");

    return new ProcessGuardianControlRequest("add", new ProgramDefinition
    {
        Name = name.Trim(),
        ExecutablePath = path.Trim(),
        Arguments = options.GetValueOrDefault("--arguments", ""),
        WorkingDirectory = options.GetValueOrDefault("--working-directory", ""),
        Enabled = true,
        AttachExisting = attachExisting
    });
}

static void PrintUsage()
{
    Console.Error.WriteLine("用法：");
    Console.Error.WriteLine("  ProcessGuardian.Control.exe list");
    Console.Error.WriteLine("  ProcessGuardian.Control.exe minimize");
    Console.Error.WriteLine("  ProcessGuardian.Control.exe add --name 名称 --path 程序绝对路径 [--arguments 参数] [--working-directory 目录] [--attach-existing]");
}
