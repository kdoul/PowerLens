using System.Windows;
using PowerLens.App.Interop;

namespace PowerLens.App;

public partial class App : Application
{
    private SingleInstance? _instance;

    protected override void OnStartup(StartupEventArgs e)
    {
        _instance = new SingleInstance();
        if (!_instance.IsFirst)
        {
            SingleInstance.ActivateExisting();
            Shutdown();
            return;
        }

        System.Windows.Forms.Application.EnableVisualStyles(); // Nicer tray menu.
        Theme.Apply(Resources, Theme.SystemUsesDarkMode(), acrylic: Backdrop.IsSupported);
        base.OnStartup(e);

        var settings = AppSettings.Load();
        bool startInTray = e.Args.Contains(StartupRegistration.TrayArgument) && settings.ShowTrayIcon;

        var window = new MainWindow(settings);
        MainWindow = window;
        if (!startInTray) window.ShowFromTray();

        _instance.ListenForActivation(() => Dispatcher.BeginInvoke(window.ShowFromTray));
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _instance?.Dispose();
        base.OnExit(e);
    }
}
