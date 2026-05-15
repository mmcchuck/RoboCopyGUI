using System;
using System.Threading;
using System.Windows.Forms;

namespace RoboCopyGUI
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            bool minimized = false;
            foreach (var a in args)
            {
                if (a == null) continue;
                if (a.Equals("--minimized", StringComparison.OrdinalIgnoreCase) ||
                    a.Equals("/minimized", StringComparison.OrdinalIgnoreCase) ||
                    a.Equals("-m", StringComparison.OrdinalIgnoreCase))
                    minimized = true;
            }

            bool createdNew;
            using (var mtx = new Mutex(true, "Local\\RoboCopyGUI.SingleInstance", out createdNew))
            {
                if (!createdNew)
                {
                    // Already running — silently exit. The existing instance owns the tray.
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                Application.ThreadException += (s, e) =>
                {
                    Logger.Log("UI exception: " + e.Exception);
                };
                AppDomain.CurrentDomain.UnhandledException += (s, e) =>
                {
                    Logger.Log("Unhandled exception: " + e.ExceptionObject);
                };

                Logger.Log("starting (minimized=" + minimized + ")");
                Application.Run(new TrayApp(minimized));
                Logger.Log("exiting");
                GC.KeepAlive(mtx);
            }
        }
    }
}
