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
            Require(actor.X >= controller.World.Bounds.X && actor.Y >= controller.World.Bounds.Y, "Dragging remains in work area", checks);
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
