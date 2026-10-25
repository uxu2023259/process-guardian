using ProcessGuardian.Models;

namespace ProcessGuardian.Services;

public sealed class WindowsPackageResolver
{
    private readonly string _windowsAppsDirectory;

    public WindowsPackageResolver(string? windowsAppsDirectory = null)
    {
        _windowsAppsDirectory = windowsAppsDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WindowsApps");
    }

    public ProgramLaunchTarget Resolve(ProgramDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var changed = TrySetPackageReference(definition);
        if (string.IsNullOrWhiteSpace(definition.PackageFamilyName))
        {
            var executable = Environment.ExpandEnvironmentVariables(definition.ExecutablePath.Trim());
            var defaultWorkingDirectory = Environment.ExpandEnvironmentVariables(definition.WorkingDirectory.Trim());
            if (string.IsNullOrWhiteSpace(defaultWorkingDirectory))
                defaultWorkingDirectory = Path.GetDirectoryName(executable) ?? Environment.CurrentDirectory;
            return new ProgramLaunchTarget(false, true, executable, defaultWorkingDirectory, changed, null);
        }

        if (!TrySplitFamilyName(definition.PackageFamilyName, out var packageName, out var publisherId) ||
            !IsSafePackagePart(definition.PackageArchitecture) ||
            !IsSafeRelativePath(definition.PackageRelativeExecutablePath))
        {
            return Unresolved(definition, changed, "应用包标识无效");
        }

        var latest = FindLatestPackage(packageName, publisherId, definition);
        if (latest is null)
            return Unresolved(definition, changed, $"未找到应用包“{definition.PackageFamilyName}”的可执行文件");

        var workingDirectory = IsSafeRelativePath(definition.PackageRelativeWorkingDirectory)
            ? Path.GetFullPath(Path.Combine(latest.Root, definition.PackageRelativeWorkingDirectory))
            : Path.GetDirectoryName(latest.ExecutablePath)!;
        if (!IsInside(latest.Root, workingDirectory))
            workingDirectory = Path.GetDirectoryName(latest.ExecutablePath)!;

        if (!string.Equals(definition.ExecutablePath, latest.ExecutablePath, StringComparison.OrdinalIgnoreCase))
        {
            definition.ExecutablePath = latest.ExecutablePath;
            changed = true;
        }
        if (!string.Equals(definition.WorkingDirectory, workingDirectory, StringComparison.OrdinalIgnoreCase))
        {
            definition.WorkingDirectory = workingDirectory;
            changed = true;
        }

        return new ProgramLaunchTarget(true, true, latest.ExecutablePath, workingDirectory, changed, null);
    }

    public bool IsFromSamePackage(string executablePath, ProgramDefinition definition)
    {
        if (string.IsNullOrWhiteSpace(definition.PackageFamilyName) ||
            !TryGetPackageLocation(executablePath, out var packageRoot, out var familyName, out var architecture, out _))
            return false;

        return IsInside(_windowsAppsDirectory, packageRoot) &&
               string.Equals(familyName, definition.PackageFamilyName, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(architecture, definition.PackageArchitecture, StringComparison.OrdinalIgnoreCase);
    }

    private bool TrySetPackageReference(ProgramDefinition definition)
    {
        if (!TryGetPackageLocation(definition.ExecutablePath, out var packageRoot, out var familyName,
                out var architecture, out var relativeExecutable) || !IsInside(_windowsAppsDirectory, packageRoot))
            return false;

        if (!string.IsNullOrWhiteSpace(definition.PackageFamilyName) &&
            !string.Equals(definition.PackageFamilyName, familyName, StringComparison.OrdinalIgnoreCase))
            return false;

        var changed = false;
        changed |= SetIfDifferent(definition.PackageFamilyName, familyName,
            value => definition.PackageFamilyName = value);
        changed |= SetIfDifferent(definition.PackageArchitecture, architecture,
            value => definition.PackageArchitecture = value);
        changed |= SetIfDifferent(definition.PackageRelativeExecutablePath, relativeExecutable,
            value => definition.PackageRelativeExecutablePath = value);

        var workingDirectory = string.IsNullOrWhiteSpace(definition.WorkingDirectory)
            ? Path.GetDirectoryName(relativeExecutable) ?? ""
            : GetRelativePathIfInside(packageRoot, definition.WorkingDirectory);
        if (workingDirectory is not null)
            changed |= SetIfDifferent(definition.PackageRelativeWorkingDirectory, workingDirectory,
                value => definition.PackageRelativeWorkingDirectory = value);
        else if (string.IsNullOrWhiteSpace(definition.PackageRelativeWorkingDirectory))
            changed |= SetIfDifferent(definition.PackageRelativeWorkingDirectory,
                Path.GetDirectoryName(relativeExecutable) ?? "",
                value => definition.PackageRelativeWorkingDirectory = value);

        return changed;
    }

    private InstalledPackage? FindLatestPackage(string packageName, string publisherId,
        ProgramDefinition definition)
    {
        if (!Directory.Exists(_windowsAppsDirectory)) return null;
        var pattern = $"{packageName}_*_{definition.PackageArchitecture}__{publisherId}";
        var packages = new List<InstalledPackage>();
        try
        {
            foreach (var root in Directory.EnumerateDirectories(_windowsAppsDirectory, pattern,
                         SearchOption.TopDirectoryOnly))
            {
                if (!TryParsePackageDirectory(Path.GetFileName(root), out var version, out var familyName,
                        out var architecture) ||
                    !string.Equals(familyName, definition.PackageFamilyName, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(architecture, definition.PackageArchitecture, StringComparison.OrdinalIgnoreCase))
                    continue;

                var executablePath = Path.GetFullPath(Path.Combine(root, definition.PackageRelativeExecutablePath));
                if (!IsInside(root, executablePath) || !File.Exists(executablePath)) continue;
                packages.Add(new InstalledPackage(version, root, executablePath));
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }

        return packages.OrderByDescending(package => package.Version)
            .ThenByDescending(package => package.Root, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private bool TryGetPackageLocation(string executablePath, out string packageRoot, out string familyName,
        out string architecture, out string relativeExecutable)
    {
        packageRoot = familyName = architecture = relativeExecutable = "";
        if (string.IsNullOrWhiteSpace(executablePath)) return false;

        try
        {
            var fullExecutablePath = Path.GetFullPath(executablePath.Trim());
            var appDirectory = Path.GetDirectoryName(fullExecutablePath);
            packageRoot = appDirectory is null ? "" : Directory.GetParent(appDirectory)?.FullName ?? "";
            if (string.IsNullOrWhiteSpace(packageRoot) ||
                !TryParsePackageDirectory(Path.GetFileName(packageRoot), out _, out familyName, out architecture))
                return false;

            relativeExecutable = Path.GetRelativePath(packageRoot, fullExecutablePath);
            return IsSafeRelativePath(relativeExecutable) && IsInside(packageRoot, fullExecutablePath);
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or NotSupportedException)
        {
            return false;
        }
    }

    private static bool TryParsePackageDirectory(string directoryName, out Version version,
        out string familyName, out string architecture)
    {
        version = new Version(0, 0);
        familyName = architecture = "";
        var publisherSeparator = directoryName.IndexOf("__", StringComparison.Ordinal);
        if (publisherSeparator <= 0 || publisherSeparator + 2 >= directoryName.Length) return false;

        var packageAndVersion = directoryName[..publisherSeparator];
        var publisherId = directoryName[(publisherSeparator + 2)..];
        var architectureSeparator = packageAndVersion.LastIndexOf('_');
        if (architectureSeparator <= 0 || architectureSeparator + 1 >= packageAndVersion.Length) return false;
        var versionSeparator = packageAndVersion.LastIndexOf('_', architectureSeparator - 1);
        if (versionSeparator <= 0) return false;

        var packageName = packageAndVersion[..versionSeparator];
        architecture = packageAndVersion[(architectureSeparator + 1)..];
        if (!Version.TryParse(packageAndVersion[(versionSeparator + 1)..architectureSeparator], out var parsedVersion) ||
            parsedVersion is null || !IsSafePackagePart(packageName) || !IsSafePackagePart(publisherId) ||
            !IsSafePackagePart(architecture))
            return false;

        version = parsedVersion;
        familyName = $"{packageName}_{publisherId}";
        return true;
    }

    private static bool TrySplitFamilyName(string familyName, out string packageName, out string publisherId)
    {
        packageName = publisherId = "";
        var separator = familyName.LastIndexOf('_');
        if (separator <= 0 || separator + 1 >= familyName.Length) return false;
        packageName = familyName[..separator];
        publisherId = familyName[(separator + 1)..];
        return IsSafePackagePart(packageName) && IsSafePackagePart(publisherId);
    }

    private static bool IsSafePackagePart(string value) =>
        !string.IsNullOrWhiteSpace(value) && value.All(character =>
            char.IsLetterOrDigit(character) || character is '.' or '_' or '-');

    private static bool IsSafeRelativePath(string path) =>
        !string.IsNullOrWhiteSpace(path) && !Path.IsPathRooted(path) &&
        path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .All(segment => segment.Length > 0 && segment != "." && segment != "..");

    private static string? GetRelativePathIfInside(string root, string path)
    {
        try
        {
            var fullPath = Path.GetFullPath(path.Trim());
            return IsInside(root, fullPath) ? Path.GetRelativePath(root, fullPath) : null;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or NotSupportedException)
        {
            return null;
        }
    }

    private static bool IsInside(string root, string path)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullPath = Path.GetFullPath(path);
        return fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static bool SetIfDifferent(string current, string value,
        Action<string> assign)
    {
        if (string.Equals(current, value, StringComparison.OrdinalIgnoreCase)) return false;
        assign(value);
        return true;
    }

    private static ProgramLaunchTarget Unresolved(ProgramDefinition definition, bool changed, string reason) =>
        new(true, false, definition.ExecutablePath, definition.WorkingDirectory, changed, reason);

    private sealed record InstalledPackage(Version Version, string Root, string ExecutablePath);
}

public sealed record ProgramLaunchTarget(
    bool IsPackageTarget,
    bool IsResolved,
    string ExecutablePath,
    string WorkingDirectory,
    bool DefinitionChanged,
    string? Error);
