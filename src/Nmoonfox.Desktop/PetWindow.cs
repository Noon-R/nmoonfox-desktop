using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Nmoonfox.Core;

namespace Nmoonfox.Desktop;

public sealed class PetWindow : Window
{
    private readonly DesktopController owner;
    private readonly PetActor actor;
    private readonly PetVisual visual;
    private Point dragOffset;
    private bool dragging;
    private HwndSource? source;
    private bool? appliedClickThrough;
    public int PetId => actor.Id;

    public PetWindow(DesktopController owner, PetActor actor)
    {
        this.owner = owner; this.actor = actor;
        Title = "nmoonfox"; WindowStyle = WindowStyle.None; AllowsTransparency = true;
        Background = Brushes.Transparent; ShowInTaskbar = false; ShowActivated = false;
        ResizeMode = ResizeMode.NoResize; Focusable = false;
        visual = new PetVisual(owner.Sprites, actor); Content = visual;
        SourceInitialized += (_, _) =>
        {
            source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            source?.AddHook(MessageHook); UpdateInputStyle(); Refresh();
        };
        MouseEnter += (_, _) => { if (!owner.World.Settings.ClickThrough) owner.World.Pet(actor.Id); };
        MouseLeftButtonDown += (_, e) =>
        {
            if (owner.World.Settings.ClickThrough) return;
            var point = e.GetPosition(this);
            dragOffset = new Point(point.X / ActualWidth, point.Y / ActualHeight); dragging = true;
            owner.World.BeginDrag(actor.Id); CaptureMouse(); e.Handled = true;
        };
        MouseMove += (_, _) =>
        {
            if (!dragging) return;
            var point = owner.CursorPosition();
            owner.World.DragPointer(actor.Id, point.X, point.Y, dragOffset.X, dragOffset.Y);
            Refresh();
        };
        MouseLeftButtonUp += (_, e) => { FinishDrag(); e.Handled = true; };
        LostMouseCapture += (_, _) => FinishDrag();
        ContextMenu = BuildMenu();
        Refresh();
    }

    private ContextMenu BuildMenu()
    {
        var menu = new ContextMenu();
        void Add(string label, Action action)
        {
            var item = new MenuItem { Header = label };
            item.Click += (_, _) => action(); menu.Items.Add(item);
        }
        Add("なでる", () => owner.World.Pet(actor.Id));
        Add("設定を開く", owner.ShowSettings);
        Add("この画面に集める", owner.GatherHere);
        Add("各画面に分ける", owner.Spread);
        Add("この子をおやすみさせる（表示から外す）", () => owner.Remove(actor.Id));
        return menu;
    }
    private void FinishDrag()
    {
        if (!dragging) return;
        dragging = false; ReleaseMouseCapture(); owner.World.EndDrag(actor.Id);
    }
    public void Refresh()
    {
        var settings = owner.World.Settings;
        var world = owner.World;
        var display = world.DisplayFor(actor);
        Width = world.WidthFor(actor) / display.Scale; Height = world.HeightFor(actor) / display.Scale;
        Topmost = settings.AlwaysOnTop;
        visual.LogicalSize = settings.Size;
        if (source != null)
        {
            // HWND coordinates are physical pixels. Never assign virtual-desktop pixels to WPF Left/Top (DIPs).
            var width = Math.Max(1, (int)Math.Floor(world.WidthFor(actor)));
            var height = Math.Max(1, (int)Math.Floor(world.HeightFor(actor)));
            var x = (int)Math.Clamp(Math.Round(actor.X), display.WorkArea.X, display.WorkArea.X + display.WorkArea.Width - width);
            var y = (int)Math.Clamp(Math.Round(actor.Y), display.WorkArea.Y, display.WorkArea.Y + display.WorkArea.Height - height);
            if (!Native.GetWindowRect(source.Handle, out var rect) || rect.Left != x || rect.Top != y ||
                rect.Right - rect.Left != width || rect.Bottom - rect.Top != height)
                Native.SetWindowPos(source.Handle, IntPtr.Zero, x, y, width, height, 0x14);
        }
        var pointer = owner.CursorPosition();
        var dx = pointer.X - actor.X - world.WidthFor(actor) / 2;
        var dy = pointer.Y - actor.Y - world.HeightFor(actor) / 2;
        var watchDistance = 320 * world.ScaleFor(actor);
        visual.WatchCursor = dx * dx + dy * dy < watchDistance * watchDistance;
        UpdateInputStyle();
        visual.InvalidateVisual();
    }
    public void UpdateInputStyle()
    {
        if (source == null) return;
        var clickThrough = owner.World.Settings.ClickThrough || actor.IsYielding;
        if (appliedClickThrough == clickThrough) return;
        var style = Native.GetWindowLongPtr(source.Handle, Native.ExtendedStyle).ToInt64() | Native.NoActivate | Native.ToolWindow;
        style = clickThrough ? style | Native.Transparent : style & ~Native.Transparent;
        Native.SetWindowLongPtr(source.Handle, Native.ExtendedStyle, new IntPtr(style));
        appliedClickThrough = clickThrough;
    }
    private IntPtr MessageHook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == 0x21) { handled = true; return new IntPtr(3); } // MA_NOACTIVATE
        return IntPtr.Zero;
    }
    protected override void OnClosed(EventArgs e)
    {
        source?.RemoveHook(MessageHook); FinishDrag(); base.OnClosed(e);
    }
}

public sealed class PetVisual(SpriteLibrary library, PetActor actor) : FrameworkElement
{
    public bool WatchCursor { get; set; }
    public double LogicalSize { get; set; } = 144;
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (ActualWidth <= 0) return;
        var width = LogicalSize;
        var fit = ActualWidth / width;
        dc.PushTransform(new ScaleTransform(fit, fit));
        var height = width * 208 / 192;
        var asleep = actor.State == PetState.Sleep;
        var dragged = actor.State == PetState.Dragged;
        var walking = actor.State is PetState.WalkLeft or PetState.WalkRight;
        var breathing = Math.Sin(actor.StateTime * 2.2 + actor.Id) * (asleep ? .012 : .004);
        var bob = walking ? -Math.Abs(Math.Sin(actor.StateTime * 9 * Math.PI / 4)) * 1.1 : 0;
        if (dragged)
        {
            // Inset rotation keeps ears and crescent tail inside the transparent window.
            var centerY = 24 + height / 2;
            dc.PushTransform(new TranslateTransform(0, -6 - Math.Sin(actor.StateTime * 4) * 2));
            dc.PushTransform(new RotateTransform(actor.DragTilt + Math.Sin(actor.StateTime * 3.6) * 1.5, width / 2, centerY));
            dc.PushTransform(new ScaleTransform(.86, .86, width / 2, centerY));
        }
        dc.PushTransform(new ScaleTransform(1, 1 + breathing, width / 2, height + 24));
        var image = asleep ? library.Sleeping :
            actor.State == PetState.Idle && WatchCursor && actor.StateTime > 1.2
                ? library.Look(actor.LookDirection) : library.Frame(actor.State, actor.Frame);
        dc.DrawImage(image, new Rect(0, 24 + bob, width, height));
        dc.Pop();
        if (dragged) { dc.Pop(); dc.Pop(); dc.Pop(); }
        if (asleep)
        {
            Text(dc, "z z", width * .52, 9 + Math.Sin(actor.StateTime) * 2, 15, Color.FromRgb(142, 127, 186));
        }
        if (actor.Affection > 0)
        {
            var progress = 1 - actor.Affection / 2;
            dc.PushOpacity(Math.Min(1, actor.Affection * 1.5));
            Text(dc, "♥", width * .36, 18 - progress * 16, 18, Color.FromRgb(225, 133, 171));
            Text(dc, "♥", width * .61, 23 - progress * 13, 12, Color.FromRgb(240, 175, 188));
            dc.Pop();
        }
        dc.Pop();
    }
    private void Text(DrawingContext dc, string text, double x, double y, double size, Color color)
    {
        var formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI"), size, new SolidColorBrush(color), VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(formatted, new Point(x, y));
    }
}
