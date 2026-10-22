using System;
using System.IO;
using System.Threading;
using System.Windows;

namespace Nmoonfox.Desktop;

public partial class App : System.Windows.Application
{
    private Mutex? mutex;
    private bool ownsMutex;
    private DesktopController? controller;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var smokeIndex = Array.IndexOf(e.Args, "--smoke-test");
        var smoke = smokeIndex >= 0;
        var data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "nmoonfox-desktop");
        if (smoke)
        {
            if (smokeIndex + 1 >= e.Args.Length) { Shutdown(2); return; }
            data = Path.GetFullPath(e.Args[smokeIndex + 1]);
        }
        else
        {
            mutex = new Mutex(true, @"Local\nmoonfox-desktop", out ownsMutex);
            if (!ownsMutex)
            {
                System.Windows.MessageBox.Show("nmoonfox はすでに起動しています。タスクトレイの月のアイコンから設定を開けます。", "nmoonfox");
                Shutdown(); return;
            }
        }
        try
        {
            controller = new DesktopController(data, smoke);
            controller.Start();
            if (smoke) _ = SmokeTest.Run(controller, data);
        }
        catch (Exception ex)
        {
            Directory.CreateDirectory(data);
            File.WriteAllText(Path.Combine(data, "startup-error.txt"), ex.ToString());
            if (!smoke) System.Windows.MessageBox.Show("起動できませんでした。\n" + ex.Message, "nmoonfox");
            Shutdown(1);
        }
    }
    protected override void OnExit(ExitEventArgs e)
    {
        controller?.Dispose();
        if (ownsMutex) mutex?.ReleaseMutex();
        mutex?.Dispose();
        base.OnExit(e);
    }
}
