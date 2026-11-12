using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace OpenCodeLauncher
{
    internal static class Json
    {
        private static readonly JavaScriptSerializer Serializer = new JavaScriptSerializer();

        public static Dictionary<string, object> ReadObject(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return new Dictionary<string, object>();
            var text = File.ReadAllText(path, Encoding.UTF8);
            var value = Serializer.DeserializeObject(text) as Dictionary<string, object>;
            return value ?? new Dictionary<string, object>();
        }

        public static void WriteObject(string path, object value)
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            var text = Serializer.Serialize(value);
            // 先写临时文件再替换，避免写入中断后留下半截 JSON。
            var temporary = path + ".tmp";
            try
            {
                File.WriteAllText(temporary, text, new UTF8Encoding(false));
                if (File.Exists(path))
                {
                    File.Replace(temporary, path, null);
                }
                else
                {
                    File.Move(temporary, path);
                }
            }
            finally
            {
                try
                {
                    if (File.Exists(temporary)) File.Delete(temporary);
                }
                catch
                {
                }
            }
        }

        public static object Get(Dictionary<string, object> source, string key)
        {
            if (source == null) return null;
            object value;
            return source.TryGetValue(key, out value) ? value : null;
        }

        public static string GetString(Dictionary<string, object> source, string key, string fallback)
        {
            var value = Get(source, key);
            return value == null ? fallback : Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        public static bool GetBool(Dictionary<string, object> source, string key, bool fallback)
        {
            var value = Get(source, key);
            if (value == null) return fallback;
            if (value is bool) return (bool)value;
            var text = Convert.ToString(value, CultureInfo.InvariantCulture);
            bool parsed;
            if (bool.TryParse(text, out parsed)) return parsed;
            return fallback;
        }

        public static double GetDouble(Dictionary<string, object> source, string key, double fallback)
        {
            var value = Get(source, key);
            if (value == null) return fallback;
            double parsed;
            if (value is double) return (double)value;
            if (double.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
            {
                return parsed;
            }
            return fallback;
        }

        public static Dictionary<string, object> GetObject(Dictionary<string, object> source, string key)
        {
            return Get(source, key) as Dictionary<string, object>;
        }

        public static object[] GetArray(Dictionary<string, object> source, string key)
        {
            return Get(source, key) as object[];
        }
    }
}
