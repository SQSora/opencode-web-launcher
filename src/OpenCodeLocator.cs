using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace OpenCodeLauncher
{
    internal sealed class LocateResult
    {
        public string Path;
        public bool IsTemporary;
    }

    internal sealed class OpenCodeLocator
    {
        private readonly List<string> _candidates = new List<string>();
        private readonly HashSet<string> _seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public LocateResult Find(string preferred)
        {
            _candidates.Clear();
            _seen.Clear();

            AddCandidates(preferred);
            AddPathCandidates();
            AddKnownLocations();

            string temporary = null;
            foreach (var candidate in _candidates)
            {
                if (!File.Exists(candidate)) continue;
                string full;
                try
                {
                    full = Path.GetFullPath(candidate);
                }
                catch
                {
                    continue;
                }

                if (IsTemporaryPath(full))
                {
                    if (temporary == null) temporary = full;
                    continue;
                }

                var result = new LocateResult();
                result.Path = full;
                result.IsTemporary = false;
                return result;
            }

            if (temporary != null)
            {
                var result = new LocateResult();
                result.Path = temporary;
                result.IsTemporary = true;
                return result;
            }

            return null;
        }

        private void AddPathCandidates()
        {
            var path = Environment.GetEnvironmentVariable("PATH");
            if (string.IsNullOrEmpty(path)) return;
            foreach (var directory in path.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (string.IsNullOrEmpty(directory)) continue;
                foreach (var name in new[] { "opencode.exe", "opencode.ps1", "opencode.cmd", "opencode" })
                {
                    try
                    {
                        var full = Path.Combine(directory.Trim(), name);
                        if (File.Exists(full)) AddCandidates(full);
                    }
                    catch
                    {
                    }
                }
            }
        }

        private void AddKnownLocations()
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

            AddCandidates(Path.Combine(local, "Programs", "opencode", "opencode.exe"));
            AddCandidates(Path.Combine(roaming, "npm", "opencode.cmd"));
            AddCandidates(Path.Combine(roaming, "npm", "node_modules", "@opencode", "cli", "bin", "opencode.exe"));
            AddCandidates(Path.Combine(profile, ".bun", "bin", "opencode.exe"));
            AddCandidates(Path.Combine(local, "Microsoft", "WinGet", "Links", "opencode.exe"));
            AddCandidates(Path.Combine(programFiles, "opencode", "opencode.exe"));

            AddFnmVersions(Path.Combine(local, "fnm", "node-versions"));
            AddFnmVersions(Path.Combine(roaming, "fnm", "node-versions"));
        }

        private void AddFnmVersions(string root)
        {
            try
            {
                if (!Directory.Exists(root)) return;
                foreach (var version in Directory.GetDirectories(root))
                {
                    var installation = Path.Combine(version, "installation");
                    AddCandidates(Path.Combine(installation, "opencode.cmd"));
                    AddCandidates(Path.Combine(installation, "node_modules", "@opencode", "cli", "bin", "opencode.exe"));
                }
            }
            catch
            {
            }
        }

        private void AddCandidates(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            string full;
            try
            {
                full = Path.GetFullPath(path);
            }
            catch
            {
                return;
            }

            var directory = Path.GetDirectoryName(full);
            var name = Path.GetFileNameWithoutExtension(full);
            if (!string.IsNullOrEmpty(directory) && !string.IsNullOrEmpty(name))
            {
                if (IsShim(full)) AddShimTargets(full);
                AddIfFile(Path.Combine(directory, name + ".exe"));
                AddIfFile(Path.Combine(directory, name + ".ps1"));
                AddIfFile(Path.Combine(directory, name + ".cmd"));
            }
            AddIfFile(full);
        }

        private void AddShimTargets(string shim)
        {
            var directory = Path.GetDirectoryName(shim);
            if (string.IsNullOrEmpty(directory)) return;
            AddIfFile(Path.Combine(directory, "node_modules", "@opencode", "cli", "bin", "opencode.exe"));
            AddIfFile(Path.Combine(directory, "node_modules", "opencode-ai", "bin", "opencode.exe"));
            AddIfFile(Path.Combine(directory, "node_modules", "opencode", "bin", "opencode.exe"));

            try
            {
                var text = File.ReadAllText(shim);
                var matches = Regex.Matches(text, "\"([^\"]+\\.exe)\"", RegexOptions.IgnoreCase);
                foreach (Match match in matches)
                {
                    AddIfFile(match.Groups[1].Value);
                }
            }
            catch
            {
            }
        }

        private void AddIfFile(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            string full;
            try
            {
                full = Path.GetFullPath(path);
            }
            catch
            {
                return;
            }
            if (!File.Exists(full)) return;
            if (_seen.Add(full)) _candidates.Add(full);
        }

        private static bool IsShim(string path)
        {
            var ext = Path.GetExtension(path);
            return string.Equals(ext, ".cmd", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(ext, ".bat", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(ext, ".ps1", StringComparison.OrdinalIgnoreCase);
        }

        private bool IsTemporaryPath(string path)
        {
            // fnm 的 multishell 路径是临时的，即使它被旧配置写成了 preferred 也不能直接采用。
            return path.IndexOf("\\fnm_multishells\\", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
