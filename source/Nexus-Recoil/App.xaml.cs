using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Nexus_Recoil.Core;
using Nexus_Recoil.Models;

namespace Nexus_Recoil;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        System.Diagnostics.Process.GetCurrentProcess().PriorityClass =
            System.Diagnostics.ProcessPriorityClass.High;

        HotkeyManager.InstallHook();

        ToolTipService.ShowDurationProperty.OverrideMetadata(
            typeof(DependencyObject), new FrameworkPropertyMetadata(4000));
        ToolTipService.InitialShowDelayProperty.OverrideMetadata(
            typeof(DependencyObject), new FrameworkPropertyMetadata(1000));

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            var msg = args.ExceptionObject is Exception ex ? ex.Message : args.ExceptionObject?.ToString();
            MessageBox.Show($"Unhandled error: {msg}", "Nexus Recoil",
                MessageBoxButton.OK, MessageBoxImage.Error);
        };

        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show($"Unhandled error: {args.Exception.Message}", "Nexus Recoil",
                MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        var settings = AppSettings.Load();
        bool firstRun = !settings.DisclaimerShown;

        if (!settings.DisclaimerShown)
        {
            var result = MessageBox.Show(
                "Nexus Recoil simulates mouse input to assist with recoil control. " +
                "You are solely responsible for ensuring your use complies with the " +
                "terms of service of any game. Press OK to continue.",
                "Nexus Recoil",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Information);

            if (result != MessageBoxResult.OK)
            {
                Shutdown();
                return;
            }

            settings.DisclaimerShown = true;
            if (!settings.Save())
                MessageBox.Show("Settings could not be saved. Changes will only apply for this session.",
                    "Save Failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        IMouseOutput? mouse = null;
        if (e.Args.Contains("--kernel-mouse", StringComparer.OrdinalIgnoreCase))
        {
            try { mouse = new KernelMouseOutput(); }
            catch (System.ComponentModel.Win32Exception ex)
            {
                MessageBox.Show(ex.Message, "Mouse Driver Unavailable",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(1);
                return;
            }
        }

        MainWindow mainWindow;
        try { mainWindow = new MainWindow(settings, mouse); }
        catch
        {
            mouse?.Dispose();
            throw;
        }
        MainWindow = mainWindow;
        // EnsureHandle creates the HWND (registers hotkeys) without showing the window
        new System.Windows.Interop.WindowInteropHelper(mainWindow).EnsureHandle();

        if (firstRun)
        {
            mainWindow.Opacity = 1;
            mainWindow.Show();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        HotkeyManager.UninstallHook();
        base.OnExit(e);
    }
}
