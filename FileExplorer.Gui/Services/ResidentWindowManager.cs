using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using Avalonia.Threading;
using FileExplorer.Gui.ViewModels;
using FileExplorer.Startup;

namespace FileExplorer.Gui.Services;

internal sealed class ResidentWindowManager : IDisposable
{
    private readonly IClassicDesktopStyleApplicationLifetime _desktop;
    private readonly TrayIcon _tray;
    private readonly List<MainWindow> _windows = new();
    private MainWindow? _prepared;
    private MainWindow? _active;
    private bool _preparationScheduled;
    private bool _registryUpdateScheduled;
    private bool _stopping;
    private bool _disposed;

    public ResidentWindowManager(IClassicDesktopStyleApplicationLifetime desktop)
    {
        _desktop = desktop;
        NativeMenu menu = new();
        NativeMenuItem open = new(LocalizationService.Get("NewWindow"));
        open.Click += (_, _) => Open();
        NativeMenuItem quit = new(LocalizationService.Get("QuitFerret"));
        quit.Click += (_, _) => Quit();
        menu.Items.Add(open);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(quit);
        using var icon = AssetLoader.Open(new Uri("avares://FileExplorer.Gui/Assets/folder.ico"));
        _tray = new TrayIcon { Icon = new WindowIcon(icon), ToolTipText = "Ferret", Menu = menu };
        _tray.Clicked += (_, _) => Open();
        TrayIcon.SetIcons(Application.Current!, new TrayIcons { _tray });
        GuiSettings.TrayVisibilityChanged += UpdateTray;
        UpdateTray();
    }

    private void UpdateTray() => _tray.IsVisible = GuiSettings.ShowTrayIcon;

    public void Handle(LaunchRequest request)
    {
        if (request.Args.Contains("--quit"))
        {
            Dispatcher.UIThread.Post(Quit, DispatcherPriority.Background);
            return;
        }
        if (request.Args.Contains("--background") || request.Args.Contains("--resident")) return;
        Open(App.GetInitialPath(request.Args, request.WorkingDirectory), request.StartupId, request.ActivationToken);
    }

    public void Open(string? path = null, string? startupId = null, string? activationToken = null)
    {
        if (_stopping) return;
        MainWindow window = _prepared ?? CreateWindow(null);
        _prepared = null;
        if (path != null) ((MainWindowViewModel) window.DataContext!).NavigateTo(path);
        window.StartupId = startupId;
        window.WindowState = GuiSettings.WindowMaximized ? WindowState.Maximized : WindowState.Normal;
        string? previousToken = Environment.GetEnvironmentVariable("XDG_ACTIVATION_TOKEN");
        try
        {
            Environment.SetEnvironmentVariable("XDG_ACTIVATION_TOKEN", activationToken);
            window.Show();
        }
        finally
        {
            Environment.SetEnvironmentVariable("XDG_ACTIVATION_TOKEN", previousToken);
        }
        window.Activate();
        _active = window;
        UpdateRegistry();

        // Run after the shown window's layout/render work, never in the launch callback.
        window.RequestAnimationFrame(_ => SchedulePreparation());
    }

    public void SchedulePreparation()
    {
        if (_stopping || _prepared != null || _preparationScheduled) return;
        _preparationScheduled = true;
        Dispatcher.UIThread.Post(() =>
        {
            _preparationScheduled = false;
            if (_stopping || _prepared != null) return;
            _prepared = CreateWindow(null);
            _prepared.PrepareLayout();
        }, DispatcherPriority.ApplicationIdle);
    }

    private MainWindow CreateWindow(string? path)
    {
        MainWindow window = new(path);
        _windows.Add(window);
        window.Activated += (_, _) =>
        {
            _active = window;
            UpdateRegistry();
        };
        ((MainWindowViewModel) window.DataContext!).PropertyChanged += (_, e) =>
        {
            if (_active == window && e.PropertyName == nameof(MainWindowViewModel.CurrentPath)) UpdateRegistry();
        };
        window.Closed += (_, _) =>
        {
            _windows.Remove(window);
            if (_prepared == window) _prepared = null;
            if (_active == window) _active = _windows.LastOrDefault(w => w.IsVisible);
            UpdateRegistry();
        };
        return window;
    }

    private void UpdateRegistry()
    {
        if (_stopping || _registryUpdateScheduled) return;
        _registryUpdateScheduled = true;
        Dispatcher.UIThread.Post(() =>
        {
            _registryUpdateScheduled = false;
            if (_stopping) return;
            if (_active != null)
                GuiInstanceRegistry.Update(Environment.ProcessId, ((MainWindowViewModel) _active.DataContext!).CurrentPath);
            else
                GuiInstanceRegistry.Unregister(Environment.ProcessId);
        }, DispatcherPriority.Background);
    }

    public void Quit()
    {
        _stopping = true;
        _desktop.Shutdown();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _stopping = true;
        GuiSettings.TrayVisibilityChanged -= UpdateTray;
        _tray.Dispose();
        foreach (MainWindow window in _windows.ToArray()) window.Close();
        GuiInstanceRegistry.Unregister(Environment.ProcessId);
    }
}
