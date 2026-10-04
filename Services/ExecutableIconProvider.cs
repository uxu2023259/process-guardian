using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace ProcessGuardian.Services;

public static class ExecutableIconProvider
{
    private static readonly ConcurrentDictionary<string, BitmapSource> Cache =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly BitmapSource DefaultIcon = CreateBitmapSource(System.Drawing.SystemIcons.Application);

    public static BitmapSource Get(string executablePath) => Cache.GetOrAdd(executablePath, LoadIcon);

    private static BitmapSource LoadIcon(string executablePath)
    {
        try
        {
            using var icon = System.Drawing.Icon.ExtractAssociatedIcon(executablePath);
            return icon is null ? DefaultIcon : CreateBitmapSource(icon);
        }
        catch (Exception)
        {
            return DefaultIcon;
        }
    }

    private static BitmapSource CreateBitmapSource(System.Drawing.Icon icon)
    {
        var bitmap = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty,
            BitmapSizeOptions.FromWidthAndHeight(32, 32));
        bitmap.Freeze();
        return bitmap;
    }
}
