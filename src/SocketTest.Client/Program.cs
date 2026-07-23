using Avalonia;
using ReactiveUI.Avalonia;

using System.IO;

namespace SocketTest.Client;

internal sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        Logger.Initialize(new LoggerOptions
        {
            MinimumLevel = LogType.Debug,
            EnableConsole = false,
            File = new FileLogOptions
            {
                DirectoryPath = Path.Combine(Environment.CurrentDirectory, "Log"),
                TimestampFormat = "yyyy-MM-dd HH:mm:ss.fff"
            }
        });

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            Logger.ShutdownAsync().GetAwaiter().GetResult();
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace()
            .UseReactiveUI(_ => { });
    }
}
