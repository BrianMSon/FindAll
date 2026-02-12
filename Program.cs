using Avalonia;
using Avalonia.ReactiveUI;

namespace FindAll;

internal sealed class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        if (args.Contains("--save-icon"))
        {
            var app = BuildAvaloniaApp().SetupWithoutStarting();
            var dir = AppContext.BaseDirectory;
            // Navigate to project root (bin/Debug/net8.0 -> project root)
            var projectRoot = Path.GetFullPath(Path.Combine(dir, "..", "..", ".."));
            var iconPath = Path.Combine(projectRoot, "icon.png");
            Helpers.IconGenerator.SaveToFile(iconPath, 256);
            Console.WriteLine($"Icon saved: {iconPath}");
            return;
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace()
            .UseReactiveUI();
}
