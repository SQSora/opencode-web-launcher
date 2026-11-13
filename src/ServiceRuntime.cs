using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Text.RegularExpressions;

namespace OpenCodeLauncher
{
    internal sealed class ServiceRuntimeState
    {
        public string Url;
        public int Pid;
    }

    internal static class ServiceRuntime
    {
        public static ServiceRuntimeState Read()
        {
            if (!File.Exists(AppPaths.ServiceRuntimeFile)) return null;
            try
            {
                var data = Json.ReadObject(AppPaths.ServiceRuntimeFile);
                var url = Json.GetString(data, "url", string.Empty);
                var pidText = Json.GetString(data, "pid", string.Empty);
                int pid;
                if (string.IsNullOrEmpty(url) || !int.TryParse(pidText, out pid)) return null;

                Process process;
                try
                {
                    process = Process.GetProcessById(pid);
                }
                catch
                {
                    return null;
                }

                if (process == null) return null;
                var name = process.ProcessName;
                if (!string.Equals(name, "opencode", StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.Equals(name, "node", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(name, "bun", StringComparison.OrdinalIgnoreCase))
                    {
                        return null;
                    }

                    try
                    {
                        var path = process.MainModule.FileName;
                        if (!string.IsNullOrEmpty(path) &&
                            path.IndexOf("opencode", StringComparison.OrdinalIgnoreCase) < 0)
                        {
                            return null;
                        }
                    }
                    catch
                    {
                    }
                }

                var state = new ServiceRuntimeState();
                state.Url = url;
                state.Pid = pid;
                return state;
            }
            catch
            {
                return null;
            }
        }
    }

    internal static class AccessUrlBuilder
    {
        public static string ConvertToLocal(string url)
        {
            if (string.IsNullOrEmpty(url)) return string.Empty;
            try
            {
                var uri = new Uri(url);
                var host = uri.DnsSafeHost;
                if (IsWildcard(host))
                {
                    var builder = new UriBuilder(uri);
                    builder.Host = "127.0.0.1";
                    return builder.Uri.AbsoluteUri.TrimEnd('/');
                }
            }
            catch
            {
            }

            var result = Regex.Replace(url, @"//0\.0\.0\.0:", "//127.0.0.1:");
            result = Regex.Replace(result, @"//\[::\]:", "//127.0.0.1:");
            result = Regex.Replace(result, @"//\*:", "//127.0.0.1:");
            return result;
        }

        public static List<string> GetAccessUrls(string rawUrl, string fallbackUrl)
        {
            var raw = string.IsNullOrEmpty(rawUrl) ? fallbackUrl : rawUrl;
            var urls = new List<string>();
            if (string.IsNullOrEmpty(raw)) return urls;

            Uri uri;
            try
            {
                uri = new Uri(raw);
            }
            catch
            {
                urls.Add(raw);
                return urls;
            }

            var scheme = uri.Scheme;
            var port = uri.Port > 0 ? uri.Port : 80;
            var host = uri.DnsSafeHost;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (IsWildcard(host))
            {
                var includeIpv6 = string.Equals(host, "::", StringComparison.OrdinalIgnoreCase) ||
                                  string.Equals(host, "[::]", StringComparison.OrdinalIgnoreCase) ||
                                  string.Equals(host, "*", StringComparison.OrdinalIgnoreCase);
                Add(urls, seen, scheme + "://127.0.0.1:" + port);
                if (includeIpv6) Add(urls, seen, scheme + "://[::1]:" + port);

                try
                {
                    foreach (var network in NetworkInterface.GetAllNetworkInterfaces())
                    {
                        if (network.OperationalStatus != OperationalStatus.Up) continue;
                        if (network.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                        var label = (network.Name ?? string.Empty) + " " + (network.Description ?? string.Empty);
                        if (Regex.IsMatch(label, "vEthernet|Loopback|Bluetooth|Docker|WSL|Hyper-V|VMware|VirtualBox", RegexOptions.IgnoreCase))
                        {
                            continue;
                        }

                        foreach (var unicast in network.GetIPProperties().UnicastAddresses)
                        {
                            var address = unicast.Address;
                            if (address == null) continue;
                            if (IPAddress.IsLoopback(address)) continue;

                            if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                            {
                                var text = address.ToString();
                                if (text.StartsWith("169.254.", StringComparison.Ordinal)) continue;
                                Add(urls, seen, scheme + "://" + text + ":" + port);
                            }
                            else if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
                            {
                                if (address.IsIPv6LinkLocal || address.IsIPv6SiteLocal) continue;
                                var text = address.ToString();
                                if (text == "::1") continue;
                                Add(urls, seen, scheme + "://[" + text + "]:" + port);
                            }
                        }
                    }
                }
                catch
                {
                }
            }
            else
            {
                Add(urls, seen, raw);
            }

            return urls;
        }

        public static string GetPairCode(string output)
        {
            if (string.IsNullOrEmpty(output)) return string.Empty;
            var match = Regex.Match(output, "/auth/connect/([A-Za-z0-9_-]+)", RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value : string.Empty;
        }

        public static string BuildPairUrl(string accessUrl, string code)
        {
            if (string.IsNullOrEmpty(accessUrl) || string.IsNullOrEmpty(code)) return accessUrl;
            try
            {
                var builder = new UriBuilder(accessUrl);
                builder.Path = "/auth/connect/" + code;
                builder.Query = string.Empty;
                builder.Fragment = string.Empty;
                return builder.Uri.AbsoluteUri;
            }
            catch
            {
                return accessUrl.TrimEnd('/') + "/auth/connect/" + code;
            }
        }

        private static bool IsWildcard(string host)
        {
            return string.Equals(host, "0.0.0.0", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(host, "::", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(host, "[::]", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(host, "*", StringComparison.OrdinalIgnoreCase);
        }

        private static void Add(List<string> urls, HashSet<string> seen, string url)
        {
            if (string.IsNullOrEmpty(url)) return;
            if (seen.Add(url)) urls.Add(url);
        }
    }
}
