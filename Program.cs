using Avalonia;
using System;

namespace TagForge;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        // Set up global exception handlers
        AppDomain.CurrentDomain.UnhandledException += (sender, error) =>
        {
            if (error.ExceptionObject is Exception ex)
            {
                LogCrash(ex, "AppDomain.UnhandledException");
            }
        };

        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (sender, error) =>
        {
            LogCrash(error.Exception, "TaskScheduler.UnobservedTaskException");
            error.SetObserved();
        };

        try
        {
            BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            LogCrash(ex, "Main Loop Exception");
            // You might want to rethrow if you want the OS to still see it as a crash, 
            // but usually logging and exiting is what's desired for a custom crash reporter.
        }
    }

    private static void LogCrash(Exception ex, string source)
    {
        try
        {
            string workingDirectory = AppDomain.CurrentDomain.BaseDirectory;
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            string fileName = System.IO.Path.Combine(workingDirectory, $"crash_log_{timestamp}.txt");

            string crashReport = $"""
                TagForge Crash Report
                =====================
                Timestamp: {DateTime.Now}
                Source: {source}
                
                Exception Message:
                {ex.Message}

                Stack Trace:
                {ex.StackTrace}

                Inner Exception:
                {ex.InnerException}
                """;

            System.IO.File.WriteAllText(fileName, crashReport);
        }
        catch
        {
            // If logging fails, there's not much we can do.
            // Using Console.Error might be a last resort if available.
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
