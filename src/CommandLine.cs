using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace OpenCodeLauncher
{
    internal static class CommandLine
    {
        public static string Quote(string argument)
        {
            var value = argument ?? string.Empty;
            if (value.Length == 0) return "\"\"";
            if (value.IndexOfAny(new[] { ' ', '\t', '\n', '\v', '"' }) < 0) return value;

            var builder = new StringBuilder();
            builder.Append('"');
            var backslashes = 0;
            foreach (var character in value)
            {
                if (character == '\\')
                {
                    backslashes++;
                    continue;
                }
                if (character == '"')
                {
                    builder.Append('\\', backslashes * 2 + 1);
                    builder.Append('"');
                    backslashes = 0;
                    continue;
                }
                if (backslashes > 0)
                {
                    builder.Append('\\', backslashes);
                    backslashes = 0;
                }
                builder.Append(character);
            }
            if (backslashes > 0) builder.Append('\\', backslashes * 2);
            builder.Append('"');
            return builder.ToString();
        }

        public static string Join(IEnumerable<string> arguments)
        {
            var parts = new List<string>();
            foreach (var argument in arguments) parts.Add(Quote(argument));
            return string.Join(" ", parts.ToArray());
        }

        public static bool IsCmdUnsafe(string value)
        {
            return Regex.IsMatch(value ?? string.Empty, "[&|<>^%!\"()\\r\\n]");
        }

        public static string QuoteCmdArgument(string value)
        {
            if (IsCmdUnsafe(value))
            {
                throw new InvalidOperationException("批处理入口无法安全传递特殊字符，请选择 opencode.exe 或 opencode.ps1。");
            }
            if (string.IsNullOrEmpty(value)) return "\"\"";
            if (value.IndexOfAny(new[] { ' ', '\t' }) < 0) return value;
            return "\"" + value + "\"";
        }

        public static string BuildCmdCommand(string executable, IEnumerable<string> arguments)
        {
            var inner = new StringBuilder();
            inner.Append('"').Append(executable).Append('"');
            foreach (var argument in arguments)
            {
                inner.Append(' ').Append(QuoteCmdArgument(argument));
            }
            return "\"" + inner + "\"";
        }
    }
}
