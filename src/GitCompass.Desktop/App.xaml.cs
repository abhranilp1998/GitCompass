using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GitCompass.Desktop;

public partial class App : System.Windows.Application
{
    public bool IsTestHost { get; set; }
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (IsTestHost) return;
        var smoke = e.Args.Contains("--smoke");
        var capture = Argument(e.Args, "--capture") ?? Path.Combine(Path.GetTempPath(), "GitCompass-smoke.png");
        DispatcherUnhandledException += (_, args) =>
        {
            if (smoke) { Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(capture))!); File.WriteAllText(Path.ChangeExtension(capture, ".error.txt"), args.Exception.ToString()); args.Handled = true; Shutdown(1); }
            else { MessageBox.Show(args.Exception.Message, "GitCompass — unexpected problem"); args.Handled = true; }
        };
        var window = new ShellWindow(Argument(e.Args, "--state-root"));
        if (smoke) { window.WindowStartupLocation = WindowStartupLocation.Manual; window.Left = -10000; window.Top = -10000; window.ShowActivated = false; window.ShowInTaskbar = false; }
        MainWindow = window;
        window.Show();
        await window.InitializeAsync();
        var project = Argument(e.Args, "--project");
        if (project is not null) await window.OpenProjectAsync(project);
        window.ConfigureView(Argument(e.Args, "--page"), Argument(e.Args, "--theme"));
        if (e.Args.Contains("--capture") || e.Args.Contains("--smoke"))
        {
            var path = capture;
            await Task.Delay(700);
            window.UpdateLayout();
            var surface = (FrameworkElement)window.Content;
            var bitmap = new RenderTargetBitmap((int)surface.ActualWidth, (int)surface.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            var visual = new DrawingVisual();
            using (var drawing = visual.RenderOpen())
            {
                var bounds = new Rect(0, 0, surface.ActualWidth, surface.ActualHeight);
                // A WPF bitmap cannot capture the desktop compositor's backdrop.
                drawing.DrawRectangle(window.TryFindResource("WindowBrush") as Brush ?? window.Background, null, bounds);
                drawing.DrawRectangle(new VisualBrush(surface) { ViewboxUnits = BrushMappingMode.Absolute, Viewbox = bounds, Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top }, null, bounds);
            }
            bitmap.Render(visual);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var stream = File.Create(path)) encoder.Save(stream);
            File.WriteAllText(Path.ChangeExtension(path, ".txt"), window.SmokeDescription);
            if (e.Args.Contains("--smoke")) window.Close();
        }
    }
    private static string? Argument(string[] arguments, string name)
    {
        var position = Array.IndexOf(arguments, name);
        return position >= 0 && position + 1 < arguments.Length && !arguments[position + 1].StartsWith("--") ? arguments[position + 1] : null;
    }
}
