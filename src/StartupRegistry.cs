using System;
using System.Windows.Forms;
using Microsoft.Win32;

namespace RoboCopyGUI
{
    public static class StartupRegistry
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string ValueName = "RoboCopyGUI";

        public static bool IsEnabled()
        {
            using (var key = Registry.CurrentUser.OpenSubKey(RunKey, false))
            {
                return key != null && key.GetValue(ValueName) != null;
            }
        }

        public static void Set(bool enabled)
        {
            using (var key = Registry.CurrentUser.OpenSubKey(RunKey, true))
            {
                if (key == null) return;
                if (enabled)
                {
                    string exe = Application.ExecutablePath;
                    key.SetValue(ValueName, "\"" + exe + "\" --minimized");
                }
                else
                {
                    try { key.DeleteValue(ValueName, false); } catch { }
                }
            }
        }
    }
}
