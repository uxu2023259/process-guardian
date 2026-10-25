using System.Text.Json;
using ProcessGuardian.Models;

namespace ProcessGuardian.Services;

public sealed class JsonSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public JsonSettingsStore(string? path = null)
    {
        _path = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ProcessGuardian", "settings.json");
    }

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_path)) return new AppSettings();

        try
        {
            await using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read,
                16 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            var settings = await JsonSerializer.DeserializeAsync<AppSettings>(stream, JsonOptions, cancellationToken);
            return Normalize(settings ?? new AppSettings());
        }
        catch (JsonException)
        {
            var backup = _path + ".corrupt-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
            File.Copy(_path, backup, overwrite: false);
            return new AppSettings();
        }
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var directory = Path.GetDirectoryName(_path)!;
            Directory.CreateDirectory(directory);
            var temporary = _path + ".tmp";
            await using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None,
                16 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, settings, JsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }
            File.Move(temporary, _path, overwrite: true);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static AppSettings Normalize(AppSettings settings)
    {
        settings.SchemaVersion = 1;
        settings.Programs ??= [];
        settings.Programs = settings.Programs
            .Where(program => program is not null && program.Id != Guid.Empty)
            .GroupBy(program => program.Id)
            .Select(group => group.First())
            .ToList();
        foreach (var program in settings.Programs)
        {
            program.Name ??= "";
            program.ExecutablePath ??= "";
            program.Arguments ??= "";
            program.WorkingDirectory ??= "";
            program.PackageFamilyName ??= "";
            program.PackageArchitecture ??= "";
            program.PackageRelativeExecutablePath ??= "";
            program.PackageRelativeWorkingDirectory ??= "";
        }
        return settings;
    }
}
