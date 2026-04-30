using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using System.Linq;
using Avalonia.Markup.Xaml;
using TagForge.ViewModels;
using TagForge.Views;

using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace TagForge;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Set up global exception handling
            AppDomain.CurrentDomain.UnhandledException += (s, e) => LogFatalException(e.ExceptionObject as Exception, "AppDomain.UnhandledException");
            System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (s, e) => LogFatalException(e.Exception, "TaskScheduler.UnobservedTaskException");

            DisableAvaloniaDataAnnotationValidation();

            var mainVM = new MainViewModel();
            _ = mainVM.InitializeAsync(); // Run in background

            desktop.MainWindow = new MainWindow
            {
                DataContext = mainVM,
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void DisableAvaloniaDataAnnotationValidation()
    {
        // Get an array of plugins to remove
        var dataValidationPluginsToRemove =
            Avalonia.Data.Core.Plugins.BindingPlugins.DataValidators.OfType<DataAnnotationsValidationPlugin>().ToArray();

        // remove each entry found
        foreach (var plugin in dataValidationPluginsToRemove)
        {
            Avalonia.Data.Core.Plugins.BindingPlugins.DataValidators.Remove(plugin);
        }
    }

    private void LogFatalException(Exception? ex, string source)
    {
        if (ex == null) return;

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var appDirectory = Path.Combine(userProfile, ".tagforge");
        
        if (!Directory.Exists(appDirectory))
        {
            try { Directory.CreateDirectory(appDirectory); } catch { return; }
        }

        var logPath = Path.Combine(appDirectory, "crash.log");
        var sb = new StringBuilder();
        sb.AppendLine($"--- FATAL EXCEPTION ({DateTime.Now}) ---");
        sb.AppendLine($"Source: {source}");
        sb.AppendLine($"Message: {ex.Message}");
        sb.AppendLine($"Stack Trace: {ex.StackTrace}");
        if (ex.InnerException != null)
        {
            sb.AppendLine($"Inner Exception: {ex.InnerException.Message}");
            sb.AppendLine($"Inner Stack Trace: {ex.InnerException.StackTrace}");
        }
        sb.AppendLine("------------------------------------------");
        sb.AppendLine();

        try 
        {
            File.AppendAllText(logPath, sb.ToString());
        }
        catch { /* Cannot even log to file */ }
    }
}