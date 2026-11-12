using System;
using System.IO;
using System.Windows;
using System.Windows.Markup;

namespace OpenCodeLauncher
{
    internal static class XamlLoader
    {
        public static object Load(string path)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                return XamlReader.Load(stream);
            }
        }

        public static ResourceDictionary LoadDictionary(string path)
        {
            return (ResourceDictionary)Load(path);
        }

        public static Window LoadWindow(string path)
        {
            return (Window)Load(path);
        }
    }
}
