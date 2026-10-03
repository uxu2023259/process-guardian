using System.Globalization;
using ProcessGuardian.Models;

namespace ProcessGuardian.Services;

public sealed class AppLogger
{
    private const long MaxFileBytes = 2 * 1024 * 1024;
    private const int ArchiveCount = 5;
    private const int MemoryEntryCount = 150;
    private readonly string _directory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ProcessGuardian", "Logs");
    private readonly string _filePath;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Queue<LogEntry> _recent = new();

    public AppLogger()
    {
        Directory.CreateDirectory(_directory);
        _filePath = Path.Combine(_directory, "process-guardian.log");
        LoadRecentEntries();
    }

    public event EventHandler<LogEntry>? EntryWritten;
    public IReadOnlyList<LogEntry> RecentEntries => _recent.Reverse().ToArray();

    public async Task WriteAsync(LogSeverity severity, string programName, string message,
        CancellationToken cancellationToken = default)
    {
        var entry = new LogEntry(DateTimeOffset.Now, severity, programName, message);
        var line = string.Create(CultureInfo.InvariantCulture,
            $"{entry.Timestamp:O}\t{severity}\t{Clean(programName)}\t{Clean(message)}{Environment.NewLine}");

        await _gate.WaitAsync(cancellationToken);
        try
        {
            RotateIfNeeded(line.Length * 2L);
            await File.AppendAllTextAsync(_filePath, line, cancellationToken);
            _recent.Enqueue(entry);
            while (_recent.Count > MemoryEntryCount) _recent.Dequeue();
        }
        finally
        {
            _gate.Release();
        }
        EntryWritten?.Invoke(this, entry);
    }

    private void LoadRecentEntries()
    {
        var entries = new List<LogEntry>();
        foreach (var file in Directory.GetFiles(_directory, "process-guardian.log*")
                     .OrderByDescending(File.GetLastWriteTimeUtc))
        {
            try
            {
                foreach (var line in File.ReadLines(file).Reverse())
                {
                    var parts = line.Split('\t', 4);
                    if (parts.Length != 4 || !DateTimeOffset.TryParse(parts[0], CultureInfo.InvariantCulture,
                            DateTimeStyles.RoundtripKind, out var timestamp) ||
                        !Enum.TryParse<LogSeverity>(parts[1], out var severity)) continue;
                    entries.Add(new LogEntry(timestamp, severity, parts[2], parts[3]));
                    if (entries.Count == MemoryEntryCount) break;
                }
            }
            catch (IOException) { }
            if (entries.Count == MemoryEntryCount) break;
        }
        foreach (var entry in entries.OrderBy(entry => entry.Timestamp)) _recent.Enqueue(entry);
    }

    private void RotateIfNeeded(long incomingBytes)
    {
        if (!File.Exists(_filePath) || new FileInfo(_filePath).Length + incomingBytes <= MaxFileBytes) return;
        var oldest = _filePath + "." + ArchiveCount;
        if (File.Exists(oldest)) File.Delete(oldest);
        for (var index = ArchiveCount - 1; index >= 1; index--)
        {
            var source = _filePath + "." + index;
            if (File.Exists(source)) File.Move(source, _filePath + "." + (index + 1));
        }
        File.Move(_filePath, _filePath + ".1");
    }

    private static string Clean(string value) => value.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
}
