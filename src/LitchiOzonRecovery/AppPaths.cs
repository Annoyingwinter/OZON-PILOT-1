using System;
using System.Diagnostics;
using System.IO;

namespace LitchiOzonRecovery
{
    internal sealed class AppPaths
    {
        private readonly string _root;
        private readonly string _baselineRoot;

        private AppPaths(string root, string baselineRoot)
        {
            _root = root;
            _baselineRoot = baselineRoot;
        }

        public string WorkRoot
        {
            get { return _root; }
        }

        public string BaselineRoot
        {
            get { return _baselineRoot; }
        }

        public string ConfigFile
        {
            get { return Path.Combine(BaselineRoot, "SettingConfig.txt"); }
        }

        public string CategoryFile
        {
            get { return Path.Combine(BaselineRoot, "category.txt"); }
        }

        public string FeeFile
        {
            get { return Path.Combine(BaselineRoot, "fee.txt"); }
        }

        public string Plugin1688Folder
        {
            get { return Path.Combine(BaselineRoot, "Plugins", "1688"); }
        }

        public string BrowserProfileFolder
        {
            get { return EnsureDirectory(Path.Combine(_root, "browser-profile")); }
        }

        public string OzonLabelDirectory
        {
            get { return EnsureDirectory(Path.Combine(_root, "ozon-labels")); }
        }

        public string UiStateFile
        {
            get { return Path.Combine(_root, "ozon-pilot-ui-state.json"); }
        }

        public string LegacyBrowserProfileFolder
        {
            get { return Path.Combine(_root, "Chrome", "EBWebView"); }
        }

        public string WebViewRuntimeInstaller
        {
            get { return Path.Combine(BaselineRoot, "Webview", "Setup.exe"); }
        }

        public string WebViewLoaderDirectory
        {
            get
            {
                string arch = Environment.Is64BitProcess ? "win-x64" : "win-x86";
                string[] candidates = new string[]
                {
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "runtimes", arch, "native"),
                    Path.Combine(_root, "runtimes", arch, "native"),
                    Path.Combine(BaselineRoot, "runtimes", arch, "native")
                };

                int i;
                for (i = 0; i < candidates.Length; i++)
                {
                    if (File.Exists(Path.Combine(candidates[i], "WebView2Loader.dll")))
                    {
                        return candidates[i];
                    }
                }

                return null;
            }
        }

        public static AppPaths Discover()
        {
            string current = AppDomain.CurrentDomain.BaseDirectory;

            while (!string.IsNullOrEmpty(current))
            {
                string nestedBaseline = Path.Combine(current, "baseline");
                if (IsBaselineDirectory(nestedBaseline))
                {
                    return new AppPaths(current, nestedBaseline);
                }

                if (IsBaselineDirectory(current))
                {
                    return new AppPaths(current, current);
                }

                string distOzonPilot = Path.Combine(current, "dist", "OZON-PILOT");
                if (IsBaselineDirectory(distOzonPilot))
                {
                    return new AppPaths(distOzonPilot, distOzonPilot);
                }

                DirectoryInfo parent = Directory.GetParent(current);
                current = parent == null ? null : parent.FullName;
            }

            throw new DirectoryNotFoundException("Unable to locate the baseline directory.");
        }

        private static bool IsBaselineDirectory(string path)
        {
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
            {
                return false;
            }

            return File.Exists(Path.Combine(path, "SettingConfig.txt")) &&
                   File.Exists(Path.Combine(path, "category.txt")) &&
                   File.Exists(Path.Combine(path, "fee.txt")) &&
                   Directory.Exists(Path.Combine(path, "Plugins", "1688"));
        }

        public string FindUpdaterExecutable()
        {
            string[] candidates = new string[]
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "OZON-PILOT-Updater.exe"),
                Path.Combine(_root, "src", "LitchiAutoUpdate", "bin", "Debug", "OZON-PILOT-Updater.exe"),
                Path.Combine(_root, "dist", "OZON-PILOT", "OZON-PILOT-Updater.exe"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "LitchiAutoUpdate.exe"),
                Path.Combine(_root, "dist", "LitchiOzonRecovery", "LitchiAutoUpdate.exe")
            };

            int i;
            for (i = 0; i < candidates.Length; i++)
            {
                if (File.Exists(candidates[i]))
                {
                    return candidates[i];
                }
            }

            return null;
        }

        public static string EnsureDirectory(string path)
        {
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }

            return path;
        }

        public void OpenPath(string path)
        {
            if (File.Exists(path) || Directory.Exists(path))
            {
                Process.Start(path);
            }
        }
    }
}
