using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TypeEasy.Windows;
using TypeEasy.Core;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Length != 1) throw new ArgumentException("Pass an output folder for the preview PNGs.");
        Directory.CreateDirectory(args[0]);
        var app = new Application();
        var count = 0;
        var minimumContrast = double.MaxValue;
        foreach (var mode in new[] { "dark", "light" })
        {
            var values = mode == "dark" ? new[] { "#192225", "#111A1D", "#E9F0EE", "#99ACA8", "#304044" }
                : new[] { "#FFFFFF", "#F3F6F3", "#182B26", "#5C7169", "#CCD9D2" };
            var names = new[] { "Panel", "Field", "Ink", "Muted", "Line" };
            for (var i = 0; i < names.Length; i++) app.Resources[names[i]] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(values[i]));
            foreach (var width in new[] { 800, 580 })
            foreach (var example in new[] { ('s', false, "home"), ('?', false, "shift"), ('+', true, "numpad"), ('q', false, "demo-q"), ('f', false, "no-hands") })
            {
                var control = new TeachingKeyboard { Width = width, ShowHands = example.Item3 != "no-hands" };
                control.SetTarget(example.Item1, example.Item2);
                if (example.Item3 == "demo-q")
                {
                    control.ToggleDemo();
                    if (!control.IsDemonstrating) throw new InvalidOperationException("Demo did not start.");
                }
                var panel = new Border { Background = (Brush)app.Resources["Panel"], Child = control, Padding = new Thickness(12), Width = width + 24 };
                // Let the control's width-driven height settle; never force an old fixed viewport.
                for (var pass = 0; pass < 3; pass++)
                {
                    panel.Measure(new Size(width + 24, double.PositiveInfinity));
                    panel.Arrange(new Rect(new Point(), panel.DesiredSize));
                    panel.UpdateLayout();
                }
                var desired = Math.Ceiling(control.GetHeightForWidth(width));
                if (Math.Abs(control.ActualHeight - desired) > 1)
                    throw new InvalidOperationException($"Responsive height: expected {desired}, got {control.ActualHeight}.");
                var bitmap = new RenderTargetBitmap((int)Math.Ceiling(panel.ActualWidth), (int)Math.Ceiling(panel.ActualHeight), 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(panel);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using (var output = File.Create(Path.Combine(args[0], $"{mode}-{width}-{example.Item3}.png"))) encoder.Save(output);
                var fill = typeof(TeachingKeyboard).GetMethod("ZoneFill", BindingFlags.NonPublic | BindingFlags.Instance)!;
                var ink = typeof(TeachingKeyboard).GetMethod("ContrastingInk", BindingFlags.NonPublic | BindingFlags.Static)!;
                foreach (var finger in Enum.GetValues<TypingFinger>())
                foreach (var active in new[] { false, true })
                {
                    var background = (Color)fill.Invoke(control, new object[] { finger, active, false })!;
                    var foreground = ((SolidColorBrush)ink.Invoke(null, new object[] { background })!).Color;
                    var ratio = (Math.Max(Luminance(background), Luminance(foreground)) + .05) / (Math.Min(Luminance(background), Luminance(foreground)) + .05);
                    minimumContrast = Math.Min(minimumContrast, ratio);
                    if (ratio < 4.5) throw new InvalidOperationException($"Insufficient {mode} {finger} key-label contrast: {ratio}.");
                }
                control.StopDemo();
                if (control.IsDemonstrating) throw new InvalidOperationException("Demo did not stop.");
                count++;
            }
        }
        Console.WriteLine($"Rendered {count} previews: dark/light × 800/580 wide × home/Shift/numpad/demo/hidden hands. Responsive heights passed. Minimum key-label contrast {minimumContrast:F2}:1.");
    }
    private static double Luminance(Color color)
    {
        static double C(byte value) { var c = value / 255.0; return c <= .04045 ? c / 12.92 : Math.Pow((c + .055) / 1.055, 2.4); }
        return .2126 * C(color.R) + .7152 * C(color.G) + .0722 * C(color.B);
    }
}
