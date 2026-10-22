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
    private readonly Stopwatch elapsed = new();
    private readonly bool smoke;
    private Forms.NotifyIcon? tray;
    private Drawing.Icon? trayIcon;
    private bool disposed;
    private double lastTime;
    private readonly MouseShakeDetector shakeDetector = new();

    public DesktopController(string dataDirectory, bool smoke)
    {
        this.smoke = smoke;
        store = new SettingsStore(Path.Combine(dataDirectory, "settings.json"));
        World = new PetWorld(CurrentBounds(), smoke ? new PetSettings() : store.Load(), Environment.TickCount);
        clock.Tick += (_, _) => Tick();
        saveTimer.Tick += (_, _) => { saveTimer.Stop(); Save(); };
        SystemParameters.StaticPropertyChanged += DisplayChanged;
    }
    public void Start()
    {
        ControlWindow = new SettingsWindow(this);
        CreateTray();
        Synchronize();
        ControlWindow.Show();
        elapsed.Start(); clock.Start();
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
        ControlWindow.Show(); ControlWindow.WindowState = WindowState.Normal; ControlWindow.Activate();
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
        return shakeDetector.Sample(x, y, timestamp) ? World.MakeSpace(x, y) : 0;
    }
    public Point CursorPosition()
    {
        if (!Native.GetCursorPos(out var p)) return new Point(-10000, -10000);
        var scale = Native.GetDpiForSystem() / 96.0;
        return new Point(p.X / scale, p.Y / scale);
    }
    private static DesktopBounds CurrentBounds()
    {
        var area = SystemParameters.WorkArea;
        return new DesktopBounds(area.X, area.Y, area.Width, area.Height);
    }
    private void DisplayChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (disposed) return;
        System.Windows.Application.Current.Dispatcher.BeginInvoke(() => World.ResizeBounds(CurrentBounds()));
    }
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
        menu.Items.Add("みんなを呼び戻す", null, (_, _) => World.Gather());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("終了", null, (_, _) => Exit());
        tray = new Forms.NotifyIcon { Icon = trayIcon, Text = "nmoonfox — 月のきつねたち", ContextMenuStrip = menu, Visible = true };
        tray.DoubleClick += (_, _) => ShowSettings();
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true; clock.Stop(); saveTimer.Stop();
        SystemParameters.StaticPropertyChanged -= DisplayChanged;
        if (tray != null) { tray.Visible = false; tray.ContextMenuStrip?.Dispose(); tray.Dispose(); }
        trayIcon?.Dispose();
        foreach (var window in windows.Values.ToArray()) window.Close();
        windows.Clear();
        ControlWindow?.AllowClose();
    }
}
