using System.Windows;
using PowerLens.App.Interop;

namespace PowerLens.App;

public partial class App : Application
{
    private SingleInstance? _instance;

    protected override void OnStartup(StartupEventArgs e)
    {
        bool afterUpdate = e.Args.Contains(Updater.AfterUpdateArgument);
        _instance = new SingleInstance(waitForPrevious: afterUpdate);
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

        // Remove the previous version's files once it has fully exited.
        _ = Task.Delay(TimeSpan.FromSeconds(afterUpdate ? 5 : 30)).ContinueWith(_ => Updater.CleanUp(), TaskScheduler.Default);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _instance?.Dispose();
        base.OnExit(e);
    }
}
