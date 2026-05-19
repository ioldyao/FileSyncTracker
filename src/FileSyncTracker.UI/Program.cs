using Avalonia;
using System;
using System.IO;
using System.Threading.Tasks;

namespace FileSyncTracker.UI;

class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Capture unhandled exceptions
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            var logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "FileSyncTracker", "logs");
            Directory.CreateDirectory(logDir);
            File.AppendAllText(Path.Combine(logDir, "crash.txt"),
                $"\n[{DateTime.Now}] UnhandledException: {e.ExceptionObject}\n");
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            var logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "FileSyncTracker", "logs");
            Directory.CreateDirectory(logDir);
            File.AppendAllText(Path.Combine(logDir, "crash.txt"),
                $"\n[{DateTime.Now}] UnobservedTaskException: {e.Exception}\n");
            e.SetObserved();
        };

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
