using System;
using System.Drawing;
using System.IO;
using System.Reflection;

namespace RoboCopyGUI
{
    public static class AppIcon
    {
        const string ResourceName = "RoboCopyGUI.RoboCopyGUI.ico";

        public static Icon Load()
        {
            return LoadInternal(null);
        }

        public static Icon Load(Size size)
        {
            return LoadInternal(size);
        }

        static Icon LoadInternal(Size? size)
        {
            try
            {
                var asm = Assembly.GetExecutingAssembly();
                using (var stream = asm.GetManifestResourceStream(ResourceName))
                {
                    if (stream != null)
                    {
                        if (size.HasValue)
                            return new Icon(stream, size.Value);
                        return new Icon(stream);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log("load icon failed: " + ex.Message);
            }
            return SystemIcons.Application;
        }
    }
}
