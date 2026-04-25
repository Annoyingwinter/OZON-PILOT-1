using System;
using System.Diagnostics;
using System.IO;

namespace LitchiOzonRecovery
{
    internal sealed class AppPaths
    {
        private readonly string _root;

        private AppPaths(string root)
        {
            _root = root;
        }

        public string WorkRoot
        {
            get { return _root; }
        }

        public string BaselineRoot
        {
            get { return Path.Combine(_root, "baseline"); }
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

        public string DatabaseFile
        {
            get { return Path.Combine(BaselineRoot, "Data", "SkuDb.db"); }
        }

        public string Plugin1688Folder
        {
            get { return Path.Combine(BaselineRoot, "Plugins", "1688"); }
        }

        public string OzonPlusRoot
        {
            get { return Path.Combine(_root, "ozon plus"); }
        }

        public string OzonPlusConfigFile
        {
            get { return Path.Combine(OzonPlusRoot, "config", "ozon-api.json"); }
        }

        public string OzonPlusOutputDirectory
        {
            get { return Path.Combine(OzonPlusRoot, "output"); }
        }

        public string OzonPlusKnowledgeBaseProductsDirectory
        {
            get { return Path.Combine(OzonPlusRoot, "knowledge-base", "products"); }
        }

        public string BrowserProfileFolder
        {
            get { return EnsureDirectory(Path.Combine(_root, "browser-profile")); }
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

        public string NativeInteropDirectory
        {
            get
            {
                string nativeFolder = Environment.Is64BitProcess ? "x64" : "x86";
                string[] candidates = new string[]
                {
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, nativeFolder),
                    Path.Combine(_root, nativeFolder),
                    Path.Combine(BaselineRoot, nativeFolder)
                };

                int i;
                for (i = 0; i < candidates.Length; i++)
                {
                    if (File.Exists(Path.Combine(candidates[i], "SQLite.Interop.dll")))
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
                if (Directory.Exists(Path.Combine(current, "baseline")))
                {
                    return new AppPaths(current);
                }

                DirectoryInfo parent = Directory.GetParent(current);
                current = parent == null ? null : parent.FullName;
            }

            throw new DirectoryNotFoundException("Unable to locate the baseline directory.");
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
