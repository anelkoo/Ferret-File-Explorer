using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Avalonia;
using FileExplorer.Gui.Services;
using FileExplorer.Startup;

namespace FileExplorer.Gui;

class Program
{
    internal static ResidentServer? ResidentServer { get; private set; }
    internal static LaunchRequest? InitialRequest { get; private set; }

    [STAThread]
    public static void Main(string[] args)
    {
        if (args != null && (args.Contains("-h") || args.Contains("--help")))
        {
            Console.WriteLine(
                $"""
                
                Usage:
                 {Process.GetCurrentProcess().ProcessName} [OPTIONS] [PATH]
                
                Options:
                 --background                         Keep a prepared Home window in the background
                 --quit                               Quit the background application
                 --file-picker, --file-selection      Open as a file picker
                 --folder-picker, --directory         Open as a folder picker
                 --save-picker, --save                Open as a save dialog
                 --multiple, --multi                  Allow selecting multiple items
                 --title <TEXT>, -t <TEXT>            Set dialog title
                 --path <PATH>, -p <PATH>             Set initial directory
                 --name <NAME>                        Set the initial file name (save dialog)
                 --accept-label <TEXT>                Set the confirm button text
                 --filter "<NAME>|<PATTERNS>"         Add a file filter, e.g. "Images|*.png *.jpg"
                 -h, --help                           Show help message
                
                """);
            
            return;
        }

        args ??= Array.Empty<string>();
        if (OperatingSystem.IsLinux() && !LaunchRequest.IsPicker(args))
        {
            LaunchRequest request = new() { Args = args };
            if (request.TrySendAsync().GetAwaiter().GetResult()) return;
            if (args.Contains("--quit")) return;

            Stopwatch startup = Stopwatch.StartNew();
            while (startup.Elapsed < TimeSpan.FromSeconds(30))
            {
                ResidentServer = Services.ResidentServer.TryCreate();
                if (ResidentServer != null) break;
                if (args.Contains("--resident")) return;
                if (request.TrySendAsync(1000).GetAwaiter().GetResult()) return;
                Thread.Sleep(25);
            }

            if (ResidentServer == null)
            {
                Console.Error.WriteLine("Ferret could not connect to the running session.");
                Environment.ExitCode = 1;
                return;
            }

            InitialRequest = args.Contains("--resident") ? null : request;
        }

        using (ResidentServer)
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        AppBuilder builder = AppBuilder.Configure<App>()
                     .UsePlatformDetect()
                     .With(new X11PlatformOptions
                     {
                         WmClass = "fe-file-explorer",
                         EnableMultiTouch = true,
                     });

#if DEBUG
        builder.LogToTrace();
#endif

        return builder;
    }
}
