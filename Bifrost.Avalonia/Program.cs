using Avalonia;
using System;

namespace Bifrost.Avalonia
{
    internal class Program
    {
        // Initialization code. Don't use any Avalonia, third-party APIs or any
        // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
        // yet and stuff might break.
        /// <summary>Check for updates, install them after one confirmation and start the game without waiting for a click (Steam Deck Gaming Mode).</summary>
        internal static bool AutoPlay { get; private set; }

        [STAThread]
        public static void Main(string[] args)
        {
            AutoPlay = Array.Exists(args, arg => string.Equals(arg, "--autoplay", StringComparison.OrdinalIgnoreCase));
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }

        // Avalonia configuration, don't remove; also used by visual designer.
        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .WithInterFont()
                .LogToTrace();
    }
}
