using System.IO;
using System.IO.Compression;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TypeEasy.Windows;

/// <summary>One indexed archive per dictionary window; bounded decoded-image cache.</summary>
public sealed class DictionaryGlyphStore : IDisposable
{
    private readonly string directory;
    private ZipArchive? archive;
    private bool opened;
    private readonly object gate = new();
    private readonly Dictionary<string, BitmapSource> cache = new();
    private readonly Queue<string> order = new();
    private bool disposed;
    public DictionaryGlyphStore(string directory) => this.directory = directory;
    public BitmapSource? Load(string key, Color background, Color foreground, bool dark)
    {
        if (key.Length != 1 || char.IsControl(key[0]) || "\\/:*?\"<>|".Contains(key)) return null;
        lock (gate)
        {
            if (disposed) return null;
            var id = key + background + foreground + dark;
            if (cache.TryGetValue(id, out var cached)) return cached;
            if (!opened)
            {
                var pack = Path.Combine(directory, "Glyphs.zip");
                if (File.Exists(pack)) archive = ZipFile.OpenRead(pack);
                opened = true;
            }
            using var stream = archive is not null ? archive.GetEntry(key + ".png")?.Open()
                : File.Exists(Path.Combine(directory, "Glyphs", key + ".png")) ? File.OpenRead(Path.Combine(directory, "Glyphs", key + ".png")) : null;
            if (stream is null) return null;
            using var encoded = new MemoryStream();
            stream.CopyTo(encoded); encoded.Position = 0;
            var source = BitmapFrame.Create(encoded, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            BitmapSource image = dark ? Recolor(source, background, foreground) : source;
            image.Freeze();
            while (order.Count >= 48) cache.Remove(order.Dequeue());
            cache.Add(id, image); order.Enqueue(id);
            return image;
        }
    }
    public static BitmapSource Recolor(BitmapSource source, Color background, Color foreground)
    {
        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        int stride = converted.PixelWidth * 4;
        var pixels = new byte[stride * converted.PixelHeight]; converted.CopyPixels(pixels, stride, 0);
        for (int i = 0; i < pixels.Length; i += 4)
        {
            double a = pixels[i + 3] / 255.0;
            double ink = (1 - (0.2126 * pixels[i + 2] + 0.7152 * pixels[i + 1] + 0.0722 * pixels[i]) / 255.0) * a;
            pixels[i] = (byte)Math.Round(background.B + (foreground.B - background.B) * ink);
            pixels[i + 1] = (byte)Math.Round(background.G + (foreground.G - background.G) * ink);
            pixels[i + 2] = (byte)Math.Round(background.R + (foreground.R - background.R) * ink);
            pixels[i + 3] = 255;
        }
        var result = BitmapSource.Create(converted.PixelWidth, converted.PixelHeight, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
        result.Freeze(); return result;
    }
    public void Dispose() { lock(gate) { disposed = true; archive?.Dispose(); cache.Clear(); order.Clear(); } }
}
