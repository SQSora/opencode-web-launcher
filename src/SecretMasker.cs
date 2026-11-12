using System;
using System.Text.RegularExpressions;

namespace OpenCodeLauncher
{
    internal static class SecretMasker
    {
        public static string CurrentPassword = string.Empty;

        public static string Mask(string text, params string[] extraSecrets)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            try
            {
                var result = Regex.Replace(
                    text,
                    "(\"password\"\\s*:\\s*\")[^\"]*(\")",
                    "$1***$2",
                    RegexOptions.IgnoreCase);
                result = Regex.Replace(
                    result,
                    "(password(?:\\s*[:=]\\s*|\\s+))\\S+",
                    "$1***",
                    RegexOptions.IgnoreCase);

                if (extraSecrets != null)
                {
                    foreach (var secret in extraSecrets)
                    {
                        if (!string.IsNullOrEmpty(secret))
                        {
                            result = result.Replace(secret, "***");
                        }
                    }
                }

                if (!string.IsNullOrEmpty(CurrentPassword) && CurrentPassword.Length >= 4)
                {
                    result = result.Replace(CurrentPassword, "***");
                }

                result = Regex.Replace(result, "/auth/connect/[^/?#]+", "/auth/connect/***");
                return result;
            }
            catch
            {
                return text;
            }
        }
    }
}
