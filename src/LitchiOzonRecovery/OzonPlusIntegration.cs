using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace LitchiOzonRecovery
{
    internal sealed class OzonPlusConfig
    {
        public string ClientId { get; set; }
        public string ApiKey { get; set; }
        public string Currency { get; set; }
        public long WarehouseId { get; set; }
        public string WarehouseName { get; set; }

        public static OzonPlusConfig CreateDefault()
        {
            return new OzonPlusConfig
            {
                Currency = "CNY",
                WarehouseName = string.Empty
            };
        }
    }

    internal sealed class OzonPlusEnvironmentStatus
    {
        public string RootPath { get; set; }
        public string NodeExecutable { get; set; }
        public bool RootExists { get; set; }
        public bool HasNode { get; set; }
        public bool HasNodeModules { get; set; }
        public bool HasConfigFile { get; set; }
        public string ConfigFile { get; set; }
        public string OutputDirectory { get; set; }
        public string KnowledgeBaseDirectory { get; set; }
    }

    internal sealed class OzonPlusRunOptions
    {
        public IList<SourcingSeed> Seeds { get; set; }
        public string Provider { get; set; }
        public bool UseApi { get; set; }
        public string ApiProvider { get; set; }
        public string ApiKey { get; set; }
        public string ApiSecret { get; set; }
        public int Limit { get; set; }
        public int DetailLimit { get; set; }
        public string ListingMode { get; set; }
        public bool DryRun { get; set; }
    }

    internal sealed class OzonPlusExecutionResult
    {
        public bool Success { get; set; }
        public int ExitCode { get; set; }
        public string OutputText { get; set; }
        public string SeedFilePath { get; set; }
        public string AnalysisPath { get; set; }
        public List<AutomationGridRow> Rows { get; private set; }

        public OzonPlusExecutionResult()
        {
            Rows = new List<AutomationGridRow>();
        }
    }

    internal sealed class AutomationGridRow
    {
        [DisplayName("关键词")]
        public string Keyword { get; set; }

        [DisplayName("商品名")]
        public string ProductName { get; set; }

        [DisplayName("决策")]
        public string Decision { get; set; }

        [DisplayName("分数")]
        public decimal Score { get; set; }

        [DisplayName("供应价(CNY)")]
        public decimal SupplyPriceCny { get; set; }

        [DisplayName("目标价(RUB)")]
        public decimal TargetPriceRub { get; set; }

        [DisplayName("上架状态")]
        public string Status { get; set; }

        [DisplayName("Offer ID")]
        public string OfferId { get; set; }

        [DisplayName("Product ID")]
        public string ProductId { get; set; }

        [DisplayName("来源链接")]
        public string SourceUrl { get; set; }

        [Browsable(false)]
        public string FolderPath { get; set; }
    }

    internal sealed class OzonPlusService
    {
        private readonly AppPaths _paths;

        public OzonPlusService(AppPaths paths)
        {
            _paths = paths;
        }

        public OzonPlusEnvironmentStatus GetEnvironmentStatus()
        {
            string root = _paths.OzonPlusRoot;
            OzonPlusEnvironmentStatus status = new OzonPlusEnvironmentStatus();
            status.RootPath = root;
            status.RootExists = !string.IsNullOrEmpty(root) && Directory.Exists(root);
            status.NodeExecutable = ResolveNodeExecutable(root);
            status.HasNode = !string.IsNullOrEmpty(status.NodeExecutable);
            status.HasNodeModules = status.RootExists && Directory.Exists(Path.Combine(root, "node_modules"));
            status.ConfigFile = Path.Combine(root ?? string.Empty, "config", "ozon-api.json");
            status.HasConfigFile = File.Exists(status.ConfigFile);
            status.OutputDirectory = Path.Combine(root ?? string.Empty, "output");
            status.KnowledgeBaseDirectory = Path.Combine(root ?? string.Empty, "knowledge-base", "products");
            return status;
        }

        public OzonPlusConfig LoadOzonApiConfig()
        {
            string path = _paths.OzonPlusConfigFile;
            if (!File.Exists(path))
            {
                return OzonPlusConfig.CreateDefault();
            }

            string json = File.ReadAllText(path, new UTF8Encoding(false));
            OzonPlusConfig config = JsonConvert.DeserializeObject<OzonPlusConfig>(json);
            return config ?? OzonPlusConfig.CreateDefault();
        }

        public void SaveOzonApiConfig(OzonPlusConfig config)
        {
            EnsureOzonPlusReady(true);
            string path = _paths.OzonPlusConfigFile;
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string json = JsonConvert.SerializeObject(config ?? OzonPlusConfig.CreateDefault(), Formatting.Indented);
            File.WriteAllText(path, json, new UTF8Encoding(false));
        }

        public OzonPlusExecutionResult RunSelection(OzonPlusRunOptions options)
        {
            EnsureOzonPlusReady(true);
            ValidateSeeds(options);

            string seedFile = CreateManualSeedFile(options.Seeds);
            List<string> arguments = new List<string>();
            arguments.Add("scripts/select-1688-for-ozon.js");
            arguments.Add("--seed-file");
            arguments.Add(seedFile);
            arguments.Add("--provider");
            arguments.Add(string.IsNullOrEmpty(options.Provider) ? "1688" : options.Provider);
            arguments.Add("--limit");
            arguments.Add(Math.Max(1, options.Limit).ToString());
            arguments.Add("--detail-limit");
            arguments.Add(Math.Max(1, options.DetailLimit).ToString());
            arguments.Add("--output-dir");
            arguments.Add(_paths.OzonPlusOutputDirectory);
            arguments.Add("--headless");

            if (options.UseApi)
            {
                arguments.Add("--use-api");
                if (!string.IsNullOrEmpty(options.ApiProvider))
                {
                    arguments.Add("--api-provider");
                    arguments.Add(options.ApiProvider);
                }
            }

            OzonPlusExecutionResult result = ExecuteNode(arguments, BuildEnvironment(options));
            result.SeedFilePath = seedFile;
            result.AnalysisPath = ParseAnalysisPath(result.OutputText);
            if (!string.IsNullOrEmpty(result.AnalysisPath))
            {
                result.Rows.AddRange(LoadSelectionRows(result.AnalysisPath));
            }

            return result;
        }

        public OzonPlusExecutionResult RunPipeline(OzonPlusRunOptions options)
        {
            EnsureOzonPlusReady(true);
            ValidateSeeds(options);

            string seedFile = CreateManualSeedFile(options.Seeds);
            List<string> arguments = new List<string>();
            arguments.Add("scripts/run-pipeline.js");
            arguments.Add("--input");
            arguments.Add(seedFile);
            arguments.Add("--provider");
            arguments.Add(string.IsNullOrEmpty(options.Provider) ? "1688" : options.Provider);
            arguments.Add("--limit");
            arguments.Add(Math.Max(1, options.Limit).ToString());
            arguments.Add("--count");
            arguments.Add(Math.Max(1, options.DetailLimit).ToString());
            arguments.Add("--listing-mode");
            arguments.Add(string.IsNullOrEmpty(options.ListingMode) ? "full_auto" : options.ListingMode);
            arguments.Add("--headless");
            if (options.DryRun)
            {
                arguments.Add("--dry-run");
            }

            OzonPlusExecutionResult result = ExecuteNode(arguments, BuildEnvironment(options));
            result.SeedFilePath = seedFile;
            result.Rows.AddRange(LoadKnowledgeBaseRows(120));
            return result;
        }

        public List<AutomationGridRow> LoadLatestSelectionRows()
        {
            string path = FindLatestFile(_paths.OzonPlusOutputDirectory, "*-ozon-selector-*.analysis.json");
            return string.IsNullOrEmpty(path) ? new List<AutomationGridRow>() : LoadSelectionRows(path);
        }

        public List<AutomationGridRow> LoadKnowledgeBaseRows(int maxCount)
        {
            List<AutomationGridRow> rows = new List<AutomationGridRow>();
            string productsRoot = _paths.OzonPlusKnowledgeBaseProductsDirectory;
            if (!Directory.Exists(productsRoot))
            {
                return rows;
            }

            DirectoryInfo[] directories = new DirectoryInfo(productsRoot).GetDirectories();
            Array.Sort(directories, delegate(DirectoryInfo left, DirectoryInfo right)
            {
                return right.LastWriteTime.CompareTo(left.LastWriteTime);
            });

            int added = 0;
            for (int i = 0; i < directories.Length; i++)
            {
                if (added >= maxCount)
                {
                    break;
                }

                string folder = directories[i].FullName;
                string productPath = Path.Combine(folder, "product.json");
                if (!File.Exists(productPath))
                {
                    continue;
                }

                JObject product = ReadJsonObject(productPath);
                JObject listing = ReadJsonObject(Path.Combine(folder, "listing.json"));
                JObject mapping = ReadJsonObject(Path.Combine(folder, "ozon-import-mapping.json"));
                JObject execution = ReadJsonObject(Path.Combine(folder, "execution_outcome.json"));

                AutomationGridRow row = new AutomationGridRow();
                row.Keyword = ReadString(product, "keyword");
                row.ProductName = ReadString(product, "product.name");
                if (string.IsNullOrEmpty(row.ProductName))
                {
                    row.ProductName = ReadString(listing, "title_ru");
                }
                if (string.IsNullOrEmpty(row.ProductName))
                {
                    row.ProductName = directories[i].Name;
                }

                row.Decision = ReadString(product, "product.final_decision");
                if (string.IsNullOrEmpty(row.Decision))
                {
                    row.Decision = ReadString(product, "product.go_or_no_go");
                }

                row.Score = ReadDecimal(product, "research.opportunity_score");
                row.SupplyPriceCny = ReadDecimal(product, "product.supply_price_cny");
                row.TargetPriceRub = ReadDecimal(product, "product.target_price_rub");
                row.Status = ReadString(mapping, "status");
                if (string.IsNullOrEmpty(row.Status))
                {
                    row.Status = ReadString(execution, "status");
                }
                if (string.IsNullOrEmpty(row.Status))
                {
                    row.Status = File.Exists(Path.Combine(folder, "listing.json")) ? "已生成草稿" : "已入知识库";
                }

                row.OfferId = ReadString(mapping, "offer_id");
                row.ProductId = ReadString(mapping, "ozon_product_id");
                if (string.IsNullOrEmpty(row.ProductId))
                {
                    row.ProductId = ReadString(execution, "product_id");
                }
                row.SourceUrl = ReadString(product, "source.detail_url");
                if (string.IsNullOrEmpty(row.SourceUrl))
                {
                    row.SourceUrl = ReadString(product, "candidates[0].source_url");
                }
                row.FolderPath = folder;
                rows.Add(row);
                added += 1;
            }

            return rows;
        }

        private static void ValidateSeeds(OzonPlusRunOptions options)
        {
            if (options == null || options.Seeds == null || options.Seeds.Count == 0)
            {
                throw new InvalidOperationException("请至少输入一个关键词，每行一个，支持“关键词|类目”格式。");
            }
        }

        private void EnsureOzonPlusReady(bool requireNodeModules)
        {
            OzonPlusEnvironmentStatus status = GetEnvironmentStatus();
            if (!status.RootExists)
            {
                throw new DirectoryNotFoundException("未找到 ozon plus 目录，无法接入 Node 自动化链路。");
            }

            if (!status.HasNode)
            {
                throw new FileNotFoundException("未找到 Node.js，可执行文件缺失或不可用。");
            }

            if (requireNodeModules && !status.HasNodeModules)
            {
                throw new DirectoryNotFoundException("ozon plus/node_modules 不存在，请先安装依赖。");
            }

            if (!Directory.Exists(status.OutputDirectory))
            {
                Directory.CreateDirectory(status.OutputDirectory);
            }

            if (!Directory.Exists(status.KnowledgeBaseDirectory))
            {
                Directory.CreateDirectory(status.KnowledgeBaseDirectory);
            }
        }

        private string CreateManualSeedFile(IList<SourcingSeed> seeds)
        {
            string outputDirectory = _paths.OzonPlusOutputDirectory;
            if (!Directory.Exists(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);
            }

            JArray items = new JArray();
            for (int i = 0; i < seeds.Count; i++)
            {
                SourcingSeed seed = seeds[i];
                JObject entry = new JObject();
                entry["keyword"] = seed.Keyword ?? string.Empty;
                entry["category"] = seed.Category ?? string.Empty;
                entry["source"] = "manual";
                entry["score"] = 100 - i;
                entry["rank"] = i + 1;
                items.Add(entry);
            }

            JObject payload = new JObject();
            payload["generated_at"] = DateTime.Now.ToString("s");
            payload["generated_by"] = "OZON-PILOT";
            payload["seeds"] = items;

            string path = Path.Combine(outputDirectory, "manual-seeds-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json");
            File.WriteAllText(path, payload.ToString(Formatting.Indented), new UTF8Encoding(false));
            return path;
        }

        private IDictionary<string, string> BuildEnvironment(OzonPlusRunOptions options)
        {
            Dictionary<string, string> env = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (options == null)
            {
                return env;
            }

            if (!string.IsNullOrEmpty(options.ApiKey))
            {
                string provider = string.IsNullOrEmpty(options.ApiProvider) ? "onebound" : options.ApiProvider.ToLowerInvariant();
                if (provider == "dingdanxia")
                {
                    env["DINGDANXIA_API_KEY"] = options.ApiKey;
                }
                else
                {
                    env["ONEBOUND_API_KEY"] = options.ApiKey;
                    if (!string.IsNullOrEmpty(options.ApiSecret))
                    {
                        env["ONEBOUND_SECRET"] = options.ApiSecret;
                    }
                }
            }

            return env;
        }

        private OzonPlusExecutionResult ExecuteNode(IList<string> arguments, IDictionary<string, string> env)
        {
            string root = _paths.OzonPlusRoot;
            string nodeExecutable = ResolveNodeExecutable(root);
            if (string.IsNullOrEmpty(nodeExecutable))
            {
                throw new FileNotFoundException("Node.js 不可用。");
            }

            ProcessStartInfo startInfo = new ProcessStartInfo();
            startInfo.FileName = nodeExecutable;
            startInfo.Arguments = BuildArguments(arguments);
            startInfo.WorkingDirectory = root;
            startInfo.UseShellExecute = false;
            startInfo.RedirectStandardOutput = true;
            startInfo.RedirectStandardError = true;
            startInfo.CreateNoWindow = true;
            startInfo.StandardOutputEncoding = Encoding.UTF8;
            startInfo.StandardErrorEncoding = Encoding.UTF8;

            foreach (KeyValuePair<string, string> pair in env)
            {
                startInfo.EnvironmentVariables[pair.Key] = pair.Value;
            }

            StringBuilder output = new StringBuilder();
            using (Process process = new Process())
            {
                process.StartInfo = startInfo;
                process.OutputDataReceived += delegate(object sender, DataReceivedEventArgs e)
                {
                    if (e.Data != null)
                    {
                        output.AppendLine(e.Data);
                    }
                };
                process.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e)
                {
                    if (e.Data != null)
                    {
                        output.AppendLine(e.Data);
                    }
                };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                process.WaitForExit();

                OzonPlusExecutionResult result = new OzonPlusExecutionResult();
                result.Success = process.ExitCode == 0;
                result.ExitCode = process.ExitCode;
                result.OutputText = output.ToString().Trim();
                return result;
            }
        }

        private static string BuildArguments(IList<string> arguments)
        {
            StringBuilder builder = new StringBuilder();
            for (int i = 0; i < arguments.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(" ");
                }

                builder.Append(QuoteArgument(arguments[i]));
            }

            return builder.ToString();
        }

        private static string QuoteArgument(string value)
        {
            string safe = value ?? string.Empty;
            if (safe.IndexOf(' ') < 0 && safe.IndexOf('"') < 0)
            {
                return safe;
            }

            return "\"" + safe.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }

        private static string ParseAnalysisPath(string outputText)
        {
            if (string.IsNullOrEmpty(outputText))
            {
                return string.Empty;
            }

            Match match = Regex.Match(outputText, "\\[(?:保存|淇濆瓨)\\]\\s*(.+)", RegexOptions.IgnoreCase);
            if (!match.Success)
            {
                return string.Empty;
            }

            return match.Groups[1].Value.Trim();
        }

        private List<AutomationGridRow> LoadSelectionRows(string analysisPath)
        {
            List<AutomationGridRow> rows = new List<AutomationGridRow>();
            if (!File.Exists(analysisPath))
            {
                return rows;
            }

            JObject root = ReadJsonObject(analysisPath);
            JArray products = root["products"] as JArray;
            if (products == null)
            {
                return rows;
            }

            for (int i = 0; i < products.Count; i++)
            {
                JObject item = products[i] as JObject;
                if (item == null)
                {
                    continue;
                }

                AutomationGridRow row = new AutomationGridRow();
                row.Keyword = ReadString(item, "keyword");
                row.ProductName = ReadString(item, "name");
                row.Decision = ReadString(item, "final_decision");
                row.Score = ReadDecimal(item, "opportunity_score");
                row.SupplyPriceCny = ReadDecimal(item, "supply_price_cny");
                row.TargetPriceRub = ReadDecimal(item, "target_price_rub");
                row.Status = "已完成选品评估";
                row.SourceUrl = ReadString(item, "source_url");
                row.FolderPath = Path.GetDirectoryName(analysisPath);
                rows.Add(row);
            }

            return rows;
        }

        private static JObject ReadJsonObject(string path)
        {
            if (!File.Exists(path))
            {
                return new JObject();
            }

            return JObject.Parse(File.ReadAllText(path, new UTF8Encoding(false)));
        }

        private static string ReadString(JObject root, string path)
        {
            JToken token = root.SelectToken(path);
            return token == null ? string.Empty : Convert.ToString(token);
        }

        private static decimal ReadDecimal(JObject root, string path)
        {
            JToken token = root.SelectToken(path);
            if (token == null)
            {
                return 0m;
            }

            decimal value;
            return decimal.TryParse(Convert.ToString(token), out value) ? value : 0m;
        }

        private static string FindLatestFile(string directory, string pattern)
        {
            if (!Directory.Exists(directory))
            {
                return string.Empty;
            }

            string[] files = Directory.GetFiles(directory, pattern, SearchOption.TopDirectoryOnly);
            Array.Sort(files, delegate(string left, string right)
            {
                return File.GetLastWriteTime(right).CompareTo(File.GetLastWriteTime(left));
            });

            return files.Length == 0 ? string.Empty : files[0];
        }

        private static string ResolveNodeExecutable(string root)
        {
            List<string> candidates = new List<string>();
            string env = Environment.GetEnvironmentVariable("OZON_PILOT_NODE_BIN");
            if (!string.IsNullOrEmpty(env))
            {
                candidates.Add(env);
            }

            if (!string.IsNullOrEmpty(root))
            {
                candidates.Add(Path.Combine(root, "vendor", "node", "node.exe"));
            }

            candidates.Add("node");
            for (int i = 0; i < candidates.Count; i++)
            {
                if (IsNodeExecutableAvailable(candidates[i]))
                {
                    return candidates[i];
                }
            }

            return string.Empty;
        }

        private static bool IsNodeExecutableAvailable(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            if (File.Exists(value))
            {
                return true;
            }

            if (!string.Equals(value, "node", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            try
            {
                ProcessStartInfo info = new ProcessStartInfo();
                info.FileName = "cmd.exe";
                info.Arguments = "/c node -v";
                info.UseShellExecute = false;
                info.CreateNoWindow = true;
                info.RedirectStandardOutput = true;
                info.RedirectStandardError = true;
                using (Process process = Process.Start(info))
                {
                    process.WaitForExit();
                    return process.ExitCode == 0;
                }
            }
            catch
            {
                return false;
            }
        }
    }
}
