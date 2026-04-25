using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;

namespace LitchiOzonRecovery
{
    internal static class ConfigService
    {
        public static AppConfig Load(string path)
        {
            if (!File.Exists(path))
            {
                return AppConfig.CreateDefault();
            }

            string json = TextFileReader.ReadAllText(path);
            AppConfig config = JsonConvert.DeserializeObject<AppConfig>(json);
            return config ?? AppConfig.CreateDefault();
        }

        public static void Save(string path, AppConfig config)
        {
            string json = JsonConvert.SerializeObject(config, Formatting.Indented);
            File.WriteAllText(path, json, new UTF8Encoding(false));
        }
    }

    internal static class TextFileReader
    {
        public static string ReadAllText(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            return Decode(bytes);
        }

        public static string Decode(byte[] bytes)
        {
            Encoding[] encodings = new Encoding[]
            {
                new UTF8Encoding(false, true),
                Encoding.GetEncoding("GB18030"),
                Encoding.Default
            };

            int i;
            for (i = 0; i < encodings.Length; i++)
            {
                try
                {
                    return encodings[i].GetString(bytes);
                }
                catch
                {
                }
            }

            return Encoding.Default.GetString(bytes);
        }
    }

    internal static class AssetCatalogService
    {
        public static List<CategoryNode> LoadCategories(string path)
        {
            List<CategoryNode> list = new List<CategoryNode>();
            JObject root = JObject.Parse(TextFileReader.ReadAllText(path));
            JObject result = root["result"] as JObject;
            if (result == null)
            {
                return list;
            }

            foreach (JProperty property in result.Properties())
            {
                JObject nodeObject = property.Value as JObject;
                if (nodeObject != null)
                {
                    list.Add(ParseCategoryNode(nodeObject));
                }
            }

            return list;
        }

        public static List<FeeRule> LoadFeeRules(string path)
        {
            JArray array = JArray.Parse(TextFileReader.ReadAllText(path));
            List<FeeRule> rules = array.ToObject<List<FeeRule>>();
            return rules ?? new List<FeeRule>();
        }

        public static int CountCategories(IList<CategoryNode> nodes)
        {
            int count = 0;
            int i;
            for (i = 0; i < nodes.Count; i++)
            {
                count += 1;
                count += CountCategories(nodes[i].Children);
            }

            return count;
        }

        public static List<CategoryNode> FilterCategories(IList<CategoryNode> nodes, string keyword)
        {
            if (string.IsNullOrEmpty(keyword))
            {
                return new List<CategoryNode>(nodes);
            }

            List<CategoryNode> filtered = new List<CategoryNode>();
            string match = keyword.Trim().ToLowerInvariant();
            int i;
            for (i = 0; i < nodes.Count; i++)
            {
                CategoryNode clone = FilterCategoryNode(nodes[i], match);
                if (clone != null)
                {
                    filtered.Add(clone);
                }
            }

            return filtered;
        }

        public static List<FeeRule> FilterFeeRules(IList<FeeRule> rules, string keyword)
        {
            if (string.IsNullOrEmpty(keyword))
            {
                return new List<FeeRule>(rules);
            }

            string match = keyword.Trim().ToLowerInvariant();
            List<FeeRule> list = new List<FeeRule>();
            int i;
            for (i = 0; i < rules.Count; i++)
            {
                FeeRule rule = rules[i];
                if (Contains(rule.Category1, match) ||
                    Contains(rule.Category2, match) ||
                    rule.CategoryId1.ToString().Contains(match) ||
                    rule.CategoryId2.ToString().Contains(match))
                {
                    list.Add(rule);
                }
            }

            return list;
        }

        public static FeeRule FindBestFeeRule(IList<FeeRule> rules, long categoryId1, long categoryId2)
        {
            FeeRule byCategory2 = null;
            FeeRule byCategory1 = null;
            int i;
            for (i = 0; i < rules.Count; i++)
            {
                FeeRule rule = rules[i];
                if (categoryId2 > 0 && rule.CategoryId2 == categoryId2)
                {
                    byCategory2 = rule;
                    break;
                }

                if (categoryId1 > 0 && rule.CategoryId1 == categoryId1 && byCategory1 == null)
                {
                    byCategory1 = rule;
                }
            }

            return byCategory2 ?? byCategory1;
        }

        public static void ExportFeeRulesToExcel(IList<FeeRule> rules, string path)
        {
            XSSFWorkbook workbook = new XSSFWorkbook();
            ISheet sheet = workbook.CreateSheet("FeeRules");
            string[] headers = new string[]
            {
                "Id", "CategoryId1", "CategoryId2", "Category1", "Category2",
                "FBS", "FBS1500", "FBS5000", "FBP", "FBP1500", "FBP5000",
                "FBO", "FBO1500", "FBO5000"
            };

            IRow headerRow = sheet.CreateRow(0);
            int i;
            for (i = 0; i < headers.Length; i++)
            {
                headerRow.CreateCell(i).SetCellValue(headers[i]);
            }

            for (i = 0; i < rules.Count; i++)
            {
                FeeRule rule = rules[i];
                IRow row = sheet.CreateRow(i + 1);
                row.CreateCell(0).SetCellValue(rule.Id);
                row.CreateCell(1).SetCellValue(rule.CategoryId1.ToString());
                row.CreateCell(2).SetCellValue(rule.CategoryId2.ToString());
                row.CreateCell(3).SetCellValue(rule.Category1 ?? string.Empty);
                row.CreateCell(4).SetCellValue(rule.Category2 ?? string.Empty);
                row.CreateCell(5).SetCellValue((double)rule.FBS);
                row.CreateCell(6).SetCellValue((double)rule.FBS1500);
                row.CreateCell(7).SetCellValue((double)rule.FBS5000);
                row.CreateCell(8).SetCellValue((double)rule.FBP);
                row.CreateCell(9).SetCellValue((double)rule.FBP1500);
                row.CreateCell(10).SetCellValue((double)rule.FBP5000);
                row.CreateCell(11).SetCellValue((double)rule.FBO);
                row.CreateCell(12).SetCellValue((double)rule.FBO1500);
                row.CreateCell(13).SetCellValue((double)rule.FBO5000);
            }

            using (FileStream stream = File.Create(path))
            {
                workbook.Write(stream);
            }
        }

        private static CategoryNode ParseCategoryNode(JObject obj)
        {
            CategoryNode node = new CategoryNode();
            node.DescriptionCategoryId = GetString(obj, "descriptionCategoryId");
            node.DescriptionCategoryName = GetString(obj, "descriptionCategoryName");
            node.DescriptionTypeId = GetString(obj, "descriptionTypeId");
            node.DescriptionTypeName = GetString(obj, "descriptionTypeName");
            node.Disabled = GetBoolean(obj, "disabled");

            JObject childObject = obj["nodes"] as JObject;
            if (childObject != null)
            {
                foreach (JProperty property in childObject.Properties())
                {
                    JObject next = property.Value as JObject;
                    if (next != null)
                    {
                        node.Children.Add(ParseCategoryNode(next));
                    }
                }
            }

            return node;
        }

        private static CategoryNode FilterCategoryNode(CategoryNode node, string keyword)
        {
            bool selfMatch = Contains(node.DescriptionCategoryName, keyword) ||
                Contains(node.DescriptionCategoryId, keyword) ||
                Contains(node.DescriptionTypeName, keyword) ||
                Contains(node.DescriptionTypeId, keyword);

            CategoryNode clone = new CategoryNode();
            clone.DescriptionCategoryId = node.DescriptionCategoryId;
            clone.DescriptionCategoryName = node.DescriptionCategoryName;
            clone.DescriptionTypeId = node.DescriptionTypeId;
            clone.DescriptionTypeName = node.DescriptionTypeName;
            clone.Disabled = node.Disabled;

            int i;
            for (i = 0; i < node.Children.Count; i++)
            {
                CategoryNode child = FilterCategoryNode(node.Children[i], keyword);
                if (child != null)
                {
                    clone.Children.Add(child);
                }
            }

            if (selfMatch || clone.Children.Count > 0)
            {
                return clone;
            }

            return null;
        }

        private static bool Contains(string text, string keyword)
        {
            return !string.IsNullOrEmpty(text) && text.ToLowerInvariant().Contains(keyword);
        }

        private static string GetString(JObject obj, string name)
        {
            JToken token = obj[name];
            return token == null ? string.Empty : token.ToString();
        }

        private static bool GetBoolean(JObject obj, string name)
        {
            JToken token = obj[name];
            return token != null && token.Type == JTokenType.Boolean && token.Value<bool>();
        }
    }

    internal sealed class DatabaseService
    {
        private readonly string _dbPath;

        public DatabaseService(string dbPath)
        {
            _dbPath = dbPath;
        }

        public Dictionary<string, long> GetTableCounts()
        {
            Dictionary<string, long> result = new Dictionary<string, long>();
            string[] names = new string[] { "SkuTable", "ShopTable", "tb_catch_shop" };
            int i;
            for (i = 0; i < names.Length; i++)
            {
                result[names[i]] = ExecuteCount(names[i]);
            }

            return result;
        }

        public DataTable GetPreview(string tableName, int limit)
        {
            using (SQLiteConnection connection = OpenConnection())
            using (SQLiteCommand command = connection.CreateCommand())
            {
                command.CommandText = "select * from " + tableName + " order by Id desc limit " + limit;
                using (SQLiteDataAdapter adapter = new SQLiteDataAdapter(command))
                {
                    DataTable table = new DataTable();
                    adapter.Fill(table);
                    return table;
                }
            }
        }

        public List<string> GetAllIds(string tableName)
        {
            List<string> ids = new List<string>();
            string column = GetIdColumnName(tableName);

            using (SQLiteConnection connection = OpenConnection())
            using (SQLiteCommand command = connection.CreateCommand())
            {
                command.CommandText = "select " + column + " from " + tableName + " order by Id";
                using (SQLiteDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        ids.Add(Convert.ToString(reader[0]));
                    }
                }
            }

            return ids;
        }

        public int AppendIds(string tableName, IList<string> ids)
        {
            return SaveIds(tableName, ids, false);
        }

        public int ReplaceIds(string tableName, IList<string> ids)
        {
            return SaveIds(tableName, ids, true);
        }

        private int SaveIds(string tableName, IList<string> ids, bool replaceAll)
        {
            string column = GetIdColumnName(tableName);
            int inserted = 0;

            using (SQLiteConnection connection = OpenConnection())
            using (SQLiteTransaction transaction = connection.BeginTransaction())
            {
                if (replaceAll)
                {
                    using (SQLiteCommand delete = connection.CreateCommand())
                    {
                        delete.CommandText = "delete from " + tableName;
                        delete.Transaction = transaction;
                        delete.ExecuteNonQuery();
                    }
                }

                int i;
                for (i = 0; i < ids.Count; i++)
                {
                    string value = NormalizeId(ids[i]);
                    if (string.IsNullOrEmpty(value))
                    {
                        continue;
                    }

                    using (SQLiteCommand insert = connection.CreateCommand())
                    {
                        insert.Transaction = transaction;
                        insert.CommandText = "insert or ignore into " + tableName + " (" + column + ") values (@value)";
                        insert.Parameters.AddWithValue("@value", value);
                        inserted += insert.ExecuteNonQuery();
                    }
                }

                transaction.Commit();
            }

            return inserted;
        }

        private long ExecuteCount(string tableName)
        {
            using (SQLiteConnection connection = OpenConnection())
            using (SQLiteCommand command = connection.CreateCommand())
            {
                command.CommandText = "select count(1) from " + tableName;
                object value = command.ExecuteScalar();
                return value == null ? 0L : Convert.ToInt64(value);
            }
        }

        private string GetIdColumnName(string tableName)
        {
            return string.Equals(tableName, "SkuTable", StringComparison.OrdinalIgnoreCase) ? "SkuId" : "ShopId";
        }

        private string NormalizeId(string value)
        {
            return string.IsNullOrEmpty(value) ? string.Empty : value.Trim();
        }

        private SQLiteConnection OpenConnection()
        {
            SQLiteConnection connection = new SQLiteConnection("Data Source=" + _dbPath + ";Version=3;");
            connection.Open();
            return connection;
        }
    }

    internal static class TableFileService
    {
        public static List<string> ReadIds(string path)
        {
            string extension = Path.GetExtension(path).ToLowerInvariant();
            if (extension == ".xlsx")
            {
                return ReadIdsFromXlsx(path);
            }

            return ReadIdsFromText(path);
        }

        public static void WriteIds(string path, IList<string> ids)
        {
            string extension = Path.GetExtension(path).ToLowerInvariant();
            if (extension == ".xlsx")
            {
                WriteIdsToXlsx(path, ids);
                return;
            }

            StringBuilder builder = new StringBuilder();
            int i;
            for (i = 0; i < ids.Count; i++)
            {
                builder.AppendLine(ids[i]);
            }

            File.WriteAllText(path, builder.ToString(), new UTF8Encoding(false));
        }

        private static List<string> ReadIdsFromText(string path)
        {
            string content = TextFileReader.ReadAllText(path);
            string[] lines = content.Replace("\r", "\n").Split(new char[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
            List<string> ids = new List<string>();
            int i;
            for (i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (string.IsNullOrEmpty(line))
                {
                    continue;
                }

                string[] parts = line.Split(new char[] { ',', ';', '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length > 0)
                {
                    ids.Add(parts[0]);
                }
            }

            return ids;
        }

        private static List<string> ReadIdsFromXlsx(string path)
        {
            List<string> ids = new List<string>();
            using (FileStream stream = File.OpenRead(path))
            {
                XSSFWorkbook workbook = new XSSFWorkbook(stream);
                ISheet sheet = workbook.GetSheetAt(0);
                int i;
                for (i = sheet.FirstRowNum; i <= sheet.LastRowNum; i++)
                {
                    IRow row = sheet.GetRow(i);
                    if (row == null)
                    {
                        continue;
                    }

                    ICell cell = row.GetCell(0);
                    if (cell == null)
                    {
                        continue;
                    }

                    string value = cell.ToString();
                    if (!string.IsNullOrEmpty(value))
                    {
                        ids.Add(value.Trim());
                    }
                }
            }

            return ids;
        }

        private static void WriteIdsToXlsx(string path, IList<string> ids)
        {
            XSSFWorkbook workbook = new XSSFWorkbook();
            ISheet sheet = workbook.CreateSheet("Ids");
            IRow header = sheet.CreateRow(0);
            header.CreateCell(0).SetCellValue("Id");

            int i;
            for (i = 0; i < ids.Count; i++)
            {
                IRow row = sheet.CreateRow(i + 1);
                row.CreateCell(0).SetCellValue(ids[i]);
            }

            using (FileStream stream = File.Create(path))
            {
                workbook.Write(stream);
            }
        }
    }

    internal static class ProfitCalculatorService
    {
        public static ProfitEstimate Calculate(AppConfig config, IList<FeeRule> rules, ProfitInput input)
        {
            ProfitEstimate result = new ProfitEstimate();
            FeeRule rule = AssetCatalogService.FindBestFeeRule(rules, input.CategoryId1, input.CategoryId2);
            result.MatchedRule = rule;

            decimal targetProfit = input.TargetProfitPercent > 0 ? input.TargetProfitPercent : config.MinProfitPer;
            decimal deliveryFee = input.DeliveryFee > 0 ? input.DeliveryFee : config.DeliveryFee;
            decimal logisticsFee = rule == null ? 0m : ResolveLogisticsFee(rule, input.FulfillmentMode, input.WeightGrams);
            decimal cost = input.SourcePrice + input.OtherCost + deliveryFee + logisticsFee;
            decimal suggested = RoundMoney(cost * (1m + (targetProfit / 100m)));
            decimal actualSellingPrice = input.ManualSellingPrice > 0 ? input.ManualSellingPrice : suggested;
            decimal profitAmount = actualSellingPrice - cost;
            decimal profitPercent = cost <= 0 ? 0m : RoundMoney((profitAmount / cost) * 100m);

            result.LogisticsFee = logisticsFee;
            result.EstimatedCost = RoundMoney(cost);
            result.SuggestedSellingPrice = suggested;
            result.ActualSellingPrice = RoundMoney(actualSellingPrice);
            result.ProfitAmount = RoundMoney(profitAmount);
            result.ProfitPercent = profitPercent;
            result.MeetsPriceFilter = actualSellingPrice >= config.MinPirce && actualSellingPrice <= config.MaxPrice;
            result.MeetsWeightFilter = input.WeightGrams >= config.MinWeight && input.WeightGrams <= config.MaxWeight;
            result.MeetsProfitFilter = profitPercent >= config.MinProfitPer;
            result.Notes = BuildNotes(rule, input, targetProfit);
            return result;
        }

        private static decimal ResolveLogisticsFee(FeeRule rule, string mode, decimal weightGrams)
        {
            string normalized = string.IsNullOrEmpty(mode) ? "FBS" : mode.ToUpperInvariant();

            if (normalized == "FBO")
            {
                if (weightGrams <= 500m)
                {
                    return rule.FBO;
                }

                if (weightGrams <= 1500m)
                {
                    return rule.FBO1500;
                }

                return rule.FBO5000;
            }

            if (normalized == "FBP")
            {
                if (weightGrams <= 500m)
                {
                    return rule.FBP;
                }

                if (weightGrams <= 1500m)
                {
                    return rule.FBP1500;
                }

                return rule.FBP5000;
            }

            if (weightGrams <= 500m)
            {
                return rule.FBS;
            }

            if (weightGrams <= 1500m)
            {
                return rule.FBS1500;
            }

            return rule.FBS5000;
        }

        private static decimal RoundMoney(decimal value)
        {
            return Math.Round(value, 2, MidpointRounding.AwayFromZero);
        }

        private static string BuildNotes(FeeRule rule, ProfitInput input, decimal targetProfit)
        {
            StringBuilder builder = new StringBuilder();
            if (rule == null)
            {
                builder.AppendLine("没有找到匹配的运费规则，本次按 0 运费估算。");
            }
            else
            {
                builder.AppendLine("匹配到的运费规则：" + rule);
            }

            builder.AppendLine("履约模式：" + input.FulfillmentMode);
            builder.AppendLine("目标利润率：" + targetProfit + "%");
            builder.AppendLine("重量(g)：" + input.WeightGrams);
            return builder.ToString().Trim();
        }
    }

    internal sealed class UpdaterService
    {
        private readonly AppPaths _paths;

        public UpdaterService(AppPaths paths)
        {
            _paths = paths;
        }

        public void Launch(string updateApiUrl)
        {
            string updaterExe = _paths.FindUpdaterExecutable();
            if (string.IsNullOrEmpty(updaterExe) || !File.Exists(updaterExe))
            {
                throw new FileNotFoundException("未找到更新器程序。");
            }

            ProcessStartInfo info = new ProcessStartInfo(updaterExe, "\"" + updateApiUrl + "\"");
            info.UseShellExecute = false;
            Process.Start(info);
        }
    }

    internal static class BrowserBootstrap
    {
        public static CoreWebView2Environment CreateEnvironment(AppPaths paths)
        {
            if (!Directory.Exists(paths.Plugin1688Folder))
            {
                return null;
            }

            CoreWebView2EnvironmentOptions options = new CoreWebView2EnvironmentOptions();
            options.AreBrowserExtensionsEnabled = true;

            string userDataFolder = Directory.Exists(paths.LegacyBrowserProfileFolder)
                ? paths.LegacyBrowserProfileFolder
                : paths.BrowserProfileFolder;

            var task = CoreWebView2Environment.CreateAsync(null, userDataFolder, options);
            task.Wait();
            return task.Result;
        }
    }

    internal static class PromptDialog
    {
        public static string Show(IWin32Window owner, string title, string message, string initialValue)
        {
            Form form = new Form();
            form.Text = title;
            form.Width = 620;
            form.Height = 160;
            form.StartPosition = FormStartPosition.CenterParent;
            form.FormBorderStyle = FormBorderStyle.FixedDialog;
            form.MaximizeBox = false;
            form.MinimizeBox = false;

            Label label = new Label();
            label.Text = message;
            label.Left = 12;
            label.Top = 12;
            label.Width = 580;

            TextBox text = new TextBox();
            text.Left = 12;
            text.Top = 36;
            text.Width = 580;
            text.Text = initialValue ?? string.Empty;

            Button ok = new Button();
            ok.Text = "确定";
            ok.Left = 436;
            ok.Top = 72;
            ok.Width = 75;
            ok.DialogResult = DialogResult.OK;

            Button cancel = new Button();
            cancel.Text = "取消";
            cancel.Left = 517;
            cancel.Top = 72;
            cancel.Width = 75;
            cancel.DialogResult = DialogResult.Cancel;

            form.Controls.Add(label);
            form.Controls.Add(text);
            form.Controls.Add(ok);
            form.Controls.Add(cancel);
            form.AcceptButton = ok;
            form.CancelButton = cancel;

            DialogResult result = form.ShowDialog(owner);
            return result == DialogResult.OK ? text.Text.Trim() : null;
        }
    }
}
