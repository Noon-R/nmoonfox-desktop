using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Nmoonfox.Core;

namespace Nmoonfox.Desktop;

internal static class SmokeTest
{
    internal static async Task Run(DesktopController controller, string output)
    {
        var checks = new List<string>();
        var code = 0;
        Directory.CreateDirectory(output);
        try
        {
            await Task.Delay(500);
            controller.World.Paused = true;
            Require(controller.Windows.Count == 3, "Default three pet windows", checks);
            Require(controller.TrayVisible, "Tray icon created", checks);
            File.WriteAllText(Path.Combine(output, "displays.json"), JsonSerializer.Serialize(controller.World.Displays));
            Require(controller.World.Displays.Count > 0 && controller.ControlWindow.DisplaysCheck.IsChecked == true,
                "Physical displays enumerated with all-displays default", checks);
            Capture(controller.ControlWindow, Path.Combine(output, "settings.png"));
            var foreground = Native.GetForegroundWindow();
            controller.ControlWindow.CountSlider.Value = 12;
            Require(controller.Windows.Count == 12, "Settings slider creates twelve windows", checks);
            Require(foreground == Native.GetForegroundWindow(), "Adding pets does not steal foreground focus", checks);
            controller.ControlWindow.CountSlider.Value = 0;
            Require(controller.Windows.Count == 0, "Zero pets keeps settings available", checks);
            controller.ControlWindow.CountSlider.Value = 3;
            controller.ChangeSettings(controller.World.Settings with { ClickThrough = true });
            foreach (var window in controller.Windows)
            {
                var style = Native.GetWindowLongPtr(new WindowInteropHelper(window).Handle, Native.ExtendedStyle).ToInt64();
                Require((style & Native.Transparent) != 0 && (style & Native.NoActivate) != 0, "Native transparent + non-activating style", checks);
            }
            controller.ChangeSettings(controller.World.Settings with { ClickThrough = false, Size = 192 });
            controller.World.SleepAll();
            foreach (var window in controller.Windows) window.Refresh();
            await Task.Delay(100);
            Capture(controller.Windows.First(), Path.Combine(output, "sleep.png"));
            controller.World.WakeAll();
            var actor = controller.World.Pets.First();
            controller.World.BeginDrag(actor.Id);
            controller.World.EndDrag(actor.Id);
            Require(actor.State == PetState.Petting, "Releasing a dragged pet enters grounded petting", checks);
            controller.World.Paused = false;
            await Task.Delay(600);
            controller.World.Paused = true;
            Require(actor.State == PetState.Petting && actor.Frame > 0, "Petting advances before capture", checks);
            var pettingWindow = controller.Windows.Single(window => window.PetId == actor.Id);
            pettingWindow.Refresh();
            await Task.Delay(60);
            Capture(pettingWindow, Path.Combine(output, "petting.png"));
            controller.World.BeginDrag(actor.Id);
            controller.World.DragTo(actor.Id, -100000, -100000);
            controller.World.EndDrag(actor.Id);
            var area = controller.World.DisplayFor(actor).WorkArea;
            Require(actor.X >= area.X && actor.Y >= area.Y, "Dragging remains in assigned work area", checks);
            foreach (var display in controller.World.Displays)
            {
                controller.World.BeginDrag(actor.Id);
                controller.World.DragPointer(actor.Id, display.WorkArea.X + display.WorkArea.Width / 2,
                    display.WorkArea.Y + display.WorkArea.Height / 2, .5, .5);
                controller.World.Tick(.2);
                pettingWindow.Refresh();
                await Task.Delay(100);
                pettingWindow.Refresh();
                var handle = new WindowInteropHelper(pettingWindow).Handle;
                Require(Native.GetWindowRect(handle, out var rect) &&
                    Math.Abs(rect.Left - actor.X) <= 1 && Math.Abs(rect.Top - actor.Y) <= 1 &&
                    Math.Abs(rect.Right - rect.Left - controller.World.WidthFor(actor)) <= 1 &&
                    Math.Abs(rect.Bottom - rect.Top - controller.World.HeightFor(actor)) <= 1,
                    "Native window placement and size on " + display.Id, checks);
                Require(Math.Abs(Native.GetDpiForWindow(handle) / 96.0 - display.Scale) < .01,
                    "Window DPI matches display on " + display.Id, checks);
                Require(actor.StateTime > 0 && actor.Frame > 0, "Held pet animates while roaming is paused", checks);
                Capture(pettingWindow, Path.Combine(output, "dragging-" + checks.Count + ".png"));
                controller.World.EndDrag(actor.Id);
            }
            controller.ChangeSettings(controller.World.Settings with { UseAllDisplays = false });
            Require(controller.World.Pets.All(p => p.DisplayId == controller.World.Displays[0].Id) &&
                controller.ControlWindow.DisplaysCheck.IsChecked == false, "Primary-only setting gathers pets", checks);
            controller.ChangeSettings(controller.World.Settings with { UseAllDisplays = true });
            controller.Spread();
            Require(controller.World.Pets.Select(p => p.DisplayId).Distinct().Count() ==
                Math.Min(controller.World.Pets.Count, controller.World.Displays.Count), "Spread uses available displays", checks);
            controller.World.Gather();
            controller.World.Paused = false;
            var shakeX = actor.X + controller.World.WidthFor(actor) / 2;
            var shakeY = actor.Y + controller.World.HeightFor(actor) / 2;
            var offsets = new double[] { -60, 0, 60, 0, -60, 0, 60 };
            var yielded = 0;
            for (var i = 0; i < offsets.Length; i++)
                yielded += controller.ProcessPointer(shakeX + offsets[i] * controller.World.ScaleFor(actor), shakeY, 1000 + i * .05, false);
            Require(yielded > 0 && actor.IsYielding, "Pointer shake through controller makes a nearby pet yield", checks);
            controller.World.Paused = true;
            pettingWindow.Refresh();
            var yieldingStyle = Native.GetWindowLongPtr(new WindowInteropHelper(pettingWindow).Handle, Native.ExtendedStyle).ToInt64();
            Require((yieldingStyle & Native.Transparent) != 0, "Yielding temporarily passes clicks through", checks);
            controller.ChangeSettings(controller.World.Settings with { YieldToMouse = false });
            Require(!actor.IsYielding && controller.ControlWindow.YieldCheck.IsChecked == false, "Disabling yielding restores normal state and control", checks);
            var restoredStyle = Native.GetWindowLongPtr(new WindowInteropHelper(pettingWindow).Handle, Native.ExtendedStyle).ToInt64();
            Require((restoredStyle & Native.Transparent) == 0, "Disabling yielding restores pet interaction", checks);
            controller.ControlWindow.Close();
            Require(!controller.ControlWindow.IsVisible && controller.Windows.Count == 3, "Closing settings leaves pets running", checks);
            controller.ShowSettings();
            Require(controller.ControlWindow.IsVisible, "Settings can reopen", checks);
            controller.Save();
            Require(new SettingsStore(Path.Combine(output, "settings.json")).Load() == controller.World.Settings, "GUI settings persist", checks);
            File.WriteAllText(Path.Combine(output, "smoke-result.json"), JsonSerializer.Serialize(new { passed = true, checks }, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            code = 1;
            File.WriteAllText(Path.Combine(output, "smoke-result.json"), JsonSerializer.Serialize(new { passed = false, checks, error = ex.ToString() }, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally { System.Windows.Application.Current.Shutdown(code); }
    }
    private static void Require(bool condition, string label, List<string> checks)
    {
        if (!condition) throw new InvalidOperationException(label);
        checks.Add(label);
    }
    private static void Capture(FrameworkElement visual, string path)
    {
        visual.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(visual.ActualWidth), (int)Math.Ceiling(visual.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); encoder.Save(stream);
    }
}
