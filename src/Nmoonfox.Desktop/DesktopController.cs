using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using Nmoonfox.Core;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace Nmoonfox.Desktop;

public sealed class DesktopController : IDisposable
{
    public PetWorld World { get; }
    public SpriteLibrary Sprites { get; } = new();
    public SettingsWindow ControlWindow { get; private set; } = null!;
    public IReadOnlyCollection<PetWindow> Windows => windows.Values;
    public bool TrayVisible => tray?.Visible == true;
    public string Status { get; private set; } = "設定は自動で保存されます";
    public event Action? Changed;
    private readonly Dictionary<int, PetWindow> windows = new();
    private readonly SettingsStore store;
    private readonly DispatcherTimer clock = new() { Interval = TimeSpan.FromMilliseconds(1000.0 / 30) };
    private readonly DispatcherTimer saveTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly DispatcherTimer displayTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly Stopwatch elapsed = new();
    private readonly bool smoke;
    private Forms.NotifyIcon? tray;
    private Drawing.Icon? trayIcon;
    private bool disposed;
    private double lastTime;
    private readonly MouseShakeDetector shakeDetector = new();
    private string? pointerDisplay;
    private double pointerScale;

    public DesktopController(string dataDirectory, bool smoke)
    {
        this.smoke = smoke;
        store = new SettingsStore(Path.Combine(dataDirectory, "settings.json"));
        World = new PetWorld(DisplayCatalog.Read(), smoke ? new PetSettings() : store.Load(), Environment.TickCount);
        clock.Tick += (_, _) => Tick();
        saveTimer.Tick += (_, _) => { saveTimer.Stop(); Save(); };
        SystemParameters.StaticPropertyChanged += DisplayChanged;
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += DisplaysChanged;
        displayTimer.Tick += (_, _) => RefreshDisplays();
    }
    public void Start()
    {
        ControlWindow = new SettingsWindow(this);
        ControlWindow.DpiChanged += (_, _) => ControlWindow.Dispatcher.BeginInvoke(KeepSettingsVisible);
        CreateTray();
        Synchronize();
        ControlWindow.Show();
        elapsed.Start(); clock.Start(); displayTimer.Start();
    }
    public void ChangeSettings(PetSettings settings)
    {
        World.ApplySettings(settings); Synchronize();
        foreach (var window in windows.Values) { window.UpdateInputStyle(); window.Refresh(); }
        saveTimer.Stop(); saveTimer.Start(); Changed?.Invoke();
    }
    public void TogglePause() { World.Paused = !World.Paused; Changed?.Invoke(); }
    public void Remove(int id)
    {
        World.Remove(id); ChangeSettings(World.Settings);
    }
    public void ShowSettings()
    {
        ControlWindow.Show(); ControlWindow.WindowState = WindowState.Normal; KeepSettingsVisible(); ControlWindow.Activate();
    }
    public void GatherHere()
    {
        var cursor = CursorPosition(); World.Gather(World.DisplayAt(cursor.X, cursor.Y).Id);
        foreach (var window in windows.Values) window.Refresh();
    }
    public void Spread()
    {
        World.Spread(); foreach (var window in windows.Values) window.Refresh();
    }
    public void Exit() { Save(); System.Windows.Application.Current.Shutdown(); }
    public void Save()
    {
        try { store.Save(World.Settings); Status = "設定を保存しました"; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { Status = "設定を保存できませんでした。今回の変更は起動中のみ有効です。"; }
        Changed?.Invoke();
    }
    private void Synchronize()
    {
        var ids = World.Pets.Select(p => p.Id).ToHashSet();
        foreach (var id in windows.Keys.Where(id => !ids.Contains(id)).ToArray())
        { windows[id].Close(); windows.Remove(id); }
        foreach (var actor in World.Pets)
        {
            if (windows.ContainsKey(actor.Id)) continue;
            var window = new PetWindow(this, actor); windows.Add(actor.Id, window); window.Show();
        }
    }
    private void Tick()
    {
        var now = elapsed.Elapsed.TotalSeconds;
        World.Tick(now - lastTime); lastTime = now;
        var cursor = CursorPosition(); World.ObservePointer(cursor.X, cursor.Y);
        ProcessPointer(cursor.X, cursor.Y, now, Native.PointerButtonDown() || ControlWindow.IsMouseOver);
        foreach (var window in windows.Values) window.Refresh();
    }
    internal int ProcessPointer(double x, double y, double timestamp, bool busy)
    {
        if (busy || World.Paused || !World.Settings.YieldToMouse || World.Pets.Any(p => p.State == PetState.Dragged))
        { shakeDetector.Reset(); return 0; }
        var display = World.DisplayAt(x, y);
        if (pointerDisplay != display.Id || pointerScale != display.Scale)
        { shakeDetector.Reset(); pointerDisplay = display.Id; pointerScale = display.Scale; }
        return shakeDetector.Sample((x - display.WorkArea.X) / display.Scale, (y - display.WorkArea.Y) / display.Scale, timestamp)
            ? World.MakeSpace(x, y) : 0;
    }
    public Point CursorPosition()
    {
        if (!Native.GetCursorPos(out var p)) return new Point(-10000, -10000);
        return new Point(p.X, p.Y);
    }
    internal void RefreshDisplays()
    {
        if (disposed) return;
        IReadOnlyList<DesktopDisplay> displays;
        try { displays = DisplayCatalog.Read(); }
        catch (InvalidOperationException) { return; } // Retain last known topology during a transient switch.
        if (displays.Count == World.Displays.Count && displays.All(d => World.Displays.Contains(d))) return;
        World.ResizeDisplays(displays); shakeDetector.Reset(); pointerDisplay = null;
        foreach (var window in windows.Values) window.Refresh();
        KeepSettingsVisible(); Changed?.Invoke();
    }
    private void KeepSettingsVisible()
    {
        if (ControlWindow == null || !ControlWindow.IsVisible) return;
        var handle = new System.Windows.Interop.WindowInteropHelper(ControlWindow).Handle;
        if (!Native.GetWindowRect(handle, out var rect)) return;
        // Keep the title bar reachable even after removing a monitor. Include displays outside pet scope.
        var display = World.Displays.MinBy(d =>
        {
            var b = d.WorkArea;
            var dx = Math.Max(b.X - rect.Left, Math.Max(0, rect.Left - b.X - b.Width));
            var dy = Math.Max(b.Y - rect.Top, Math.Max(0, rect.Top - b.Y - b.Height));
            return dx * dx + dy * dy;
        })!;
        var area = display.WorkArea;
        ControlWindow.MinWidth = Math.Min(490, area.Width / display.Scale);
        ControlWindow.MinHeight = Math.Min(620, area.Height / display.Scale);
        ControlWindow.Width = Math.Min(ControlWindow.Width, area.Width / display.Scale);
        ControlWindow.Height = Math.Min(ControlWindow.Height, area.Height / display.Scale);
        var width = Math.Min(rect.Right - rect.Left, area.Width);
        var height = Math.Min(rect.Bottom - rect.Top, area.Height);
        Native.SetWindowPos(handle, IntPtr.Zero,
            (int)Math.Clamp(rect.Left, area.X, area.X + Math.Max(0, area.Width - width)),
            (int)Math.Clamp(rect.Top, area.Y, area.Y + Math.Max(0, area.Height - height)),
            (int)width, (int)height, 0x14); // NOZORDER | NOACTIVATE
    }
    private void DisplayChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (disposed) return;
        System.Windows.Application.Current.Dispatcher.BeginInvoke(RefreshDisplays);
    }
    private void DisplaysChanged(object? sender, EventArgs e)
    { if (!disposed) System.Windows.Application.Current.Dispatcher.BeginInvoke(RefreshDisplays); }
    private void CreateTray()
    {
        using var bitmap = new Drawing.Bitmap(32, 32);
        using (var graphics = Drawing.Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var purple = new Drawing.SolidBrush(Drawing.Color.FromArgb(117, 97, 181));
            using var gold = new Drawing.SolidBrush(Drawing.Color.FromArgb(255, 220, 115));
            graphics.FillEllipse(purple, 0, 0, 31, 31);
            graphics.FillEllipse(gold, 6, 4, 21, 23);
            graphics.FillEllipse(purple, 2, 1, 19, 20);
        }
        var handle = bitmap.GetHicon();
        using (var icon = Drawing.Icon.FromHandle(handle)) trayIcon = (Drawing.Icon)icon.Clone();
        Native.DestroyIcon(handle);
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("設定を開く", null, (_, _) => ShowSettings());
        menu.Items.Add("休憩 / 再開", null, (_, _) => TogglePause());
        var through = new Forms.ToolStripMenuItem("クリック透過") { Checked = World.Settings.ClickThrough, CheckOnClick = true };
        through.Click += (_, _) => ChangeSettings(World.Settings with { ClickThrough = through.Checked });
        Changed += () => through.Checked = World.Settings.ClickThrough;
        menu.Items.Add(through);
        menu.Items.Add("この画面に集める", null, (_, _) => GatherHere());
        menu.Items.Add("各画面に分ける", null, (_, _) => Spread());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("終了", null, (_, _) => Exit());
        tray = new Forms.NotifyIcon { Icon = trayIcon, Text = "nmoonfox — 月のきつねたち", ContextMenuStrip = menu, Visible = true };
        tray.DoubleClick += (_, _) => ShowSettings();
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true; clock.Stop(); saveTimer.Stop(); displayTimer.Stop();
        SystemParameters.StaticPropertyChanged -= DisplayChanged;
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= DisplaysChanged;
        if (tray != null) { tray.Visible = false; tray.ContextMenuStrip?.Dispose(); tray.Dispose(); }
        trayIcon?.Dispose();
        foreach (var window in windows.Values.ToArray()) window.Close();
        windows.Clear();
        ControlWindow?.AllowClose();
    }
}
