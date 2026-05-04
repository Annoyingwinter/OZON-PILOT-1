using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
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
            return Normalize(config);
        }

        public static void Save(string path, AppConfig config)
        {
            string json = JsonConvert.SerializeObject(config, Formatting.Indented);
            File.WriteAllText(path, json, new UTF8Encoding(false));
        }

        private static AppConfig Normalize(AppConfig config)
        {
            AppConfig safe = config ?? AppConfig.CreateDefault();
            AppConfig defaults = AppConfig.CreateDefault();
            if (safe.PlatformCommissionPercent <= 0)
            {
                safe.PlatformCommissionPercent = defaults.PlatformCommissionPercent;
            }

            if (safe.PromotionExpensePercent <= 0)
            {
                safe.PromotionExpensePercent = defaults.PromotionExpensePercent;
            }

            if (safe.TargetProfitPercent <= 0)
            {
                safe.TargetProfitPercent = defaults.TargetProfitPercent;
            }

            if (safe.BlackShops == null)
            {
                safe.BlackShops = new List<string>();
            }

            if (safe.FilterCategoryIds == null)
            {
                safe.FilterCategoryIds = new List<long>();
            }

            return safe;
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

        public static string RepairMojibake(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return value ?? string.Empty;
            }

            string text = value.Trim();
            if (string.IsNullOrEmpty(text))
            {
                return value;
            }

            try
            {
                byte[] originalBytes = Encoding.GetEncoding("GB18030").GetBytes(text);
                string repaired = new UTF8Encoding(false, true).GetString(originalBytes);
                if (!string.IsNullOrEmpty(repaired) && MojibakeScore(repaired) < MojibakeScore(text))
                {
                    return repaired;
                }
            }
            catch
            {
            }

            return value;
        }

        private static int MojibakeScore(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return 0;
            }

            int score = 0;
            string markers = "锛鐨绫鏈鏂鏃鏉杩閫浠傜洰彔嶈�";
            for (int i = 0; i < text.Length; i++)
            {
                char ch = text[i];
                if (ch == '\uFFFD' || (ch >= '\uE000' && ch <= '\uF8FF'))
                {
                    score += 5;
                }

                if (markers.IndexOf(ch) >= 0)
                {
                    score += 2;
                }
            }

            return score;
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
                    CategoryNode node = ParseCategoryNode(nodeObject, string.Empty);
                    if (!IsBlockedCategory(node))
                    {
                        list.Add(node);
                    }
                }
            }

            return list;
        }

        public static List<FeeRule> LoadFeeRules(string path)
        {
            JArray array = JArray.Parse(TextFileReader.ReadAllText(path));
            List<FeeRule> rules = array.ToObject<List<FeeRule>>();
            if (rules != null)
            {
                for (int i = 0; i < rules.Count; i++)
                {
                    rules[i].Category1 = TextFileReader.RepairMojibake(rules[i].Category1);
                    rules[i].Category2 = TextFileReader.RepairMojibake(rules[i].Category2);
                }
            }

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
            return FindBestFeeRule(rules, null, categoryId1, categoryId2);
        }

        public static FeeRule FindBestFeeRule(IList<FeeRule> rules, IList<long> categoryCandidateIds, long categoryId1, long categoryId2)
        {
            if (rules == null || rules.Count == 0)
            {
                return null;
            }

            List<long> candidates = new List<long>();
            AppendUniqueCategoryCandidate(candidates, categoryId2);
            for (int i = 0; categoryCandidateIds != null && i < categoryCandidateIds.Count; i++)
            {
                AppendUniqueCategoryCandidate(candidates, categoryCandidateIds[i]);
            }

            AppendUniqueCategoryCandidate(candidates, categoryId1);

            for (int candidateIndex = 0; candidateIndex < candidates.Count; candidateIndex++)
            {
                long candidate = candidates[candidateIndex];
                for (int i = 0; i < rules.Count; i++)
                {
                    FeeRule rule = rules[i];
                    if (rule != null && candidate > 0 && rule.CategoryId2 == candidate)
                    {
                        return rule;
                    }
                }
            }

            for (int candidateIndex = 0; candidateIndex < candidates.Count; candidateIndex++)
            {
                long candidate = candidates[candidateIndex];
                for (int i = 0; i < rules.Count; i++)
                {
                    FeeRule rule = rules[i];
                    if (rule != null && candidate > 0 && rule.CategoryId1 == candidate)
                    {
                        return rule;
                    }
                }
            }

            return null;
        }

        private static void AppendUniqueCategoryCandidate(IList<long> values, long value)
        {
            if (values == null || value <= 0)
            {
                return;
            }

            for (int i = 0; i < values.Count; i++)
            {
                if (values[i] == value)
                {
                    return;
                }
            }

            values.Add(value);
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

        private static CategoryNode ParseCategoryNode(JObject obj, string parentCategoryId)
        {
            CategoryNode node = new CategoryNode();
            node.DescriptionCategoryId = GetString(obj, "descriptionCategoryId");
            string inheritedCategoryId = !string.IsNullOrEmpty(parentCategoryId) && parentCategoryId != "0"
                ? parentCategoryId
                : node.DescriptionCategoryId;
            node.DescriptionCategoryName = TextFileReader.RepairMojibake(GetString(obj, "descriptionCategoryName"));
            node.DescriptionTypeId = GetString(obj, "descriptionTypeId");
            node.DescriptionTypeName = TextFileReader.RepairMojibake(GetString(obj, "descriptionTypeName"));
            node.Disabled = GetBoolean(obj, "disabled");
            bool hasUploadType = !string.IsNullOrEmpty(node.DescriptionTypeId) && node.DescriptionTypeId != "0";
            node.UploadCategoryId = hasUploadType && !string.IsNullOrEmpty(parentCategoryId) && parentCategoryId != "0"
                ? parentCategoryId
                : node.DescriptionCategoryId;

            JObject childObject = obj["nodes"] as JObject;
            if (childObject != null)
            {
                foreach (JProperty property in childObject.Properties())
                {
                    JObject next = property.Value as JObject;
                    if (next != null)
                    {
                        CategoryNode child = ParseCategoryNode(next, inheritedCategoryId);
                        if (!IsBlockedCategory(child))
                        {
                            node.Children.Add(child);
                        }
                    }
                }
            }

            return node;
        }

        private static bool IsBlockedCategory(CategoryNode node)
        {
            if (node == null)
            {
                return false;
            }

            string text = ((node.DescriptionCategoryName ?? string.Empty) + " " + (node.DescriptionTypeName ?? string.Empty)).ToLowerInvariant();
            string[] complianceBlockedTerms = new string[]
            {
                "食品", "食物", "饮料", "酒", "药品", "药物", "医药", "医疗", "保健", "保健品",
                "康复", "护理", "轮椅", "助行", "拐杖", "病床", "矫形", "残疾", "残障",
                "古董", "收藏品", "food", "drink", "alcohol", "medicine", "medical", "pharmacy",
                "healthcare", "rehabilitation", "wheelchair", "walker", "crutch", "orthopedic",
                "disabled", "antique", "collectible", "антиквариат", "коллекция", "медицина"
            };

            for (int i = 0; i < complianceBlockedTerms.Length; i++)
            {
                if (text.IndexOf(complianceBlockedTerms[i], StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }
            return text.IndexOf("食品", StringComparison.OrdinalIgnoreCase) >= 0 ||
                text.IndexOf("食物", StringComparison.OrdinalIgnoreCase) >= 0 ||
                text.IndexOf("饮料", StringComparison.OrdinalIgnoreCase) >= 0 ||
                text.IndexOf("酒", StringComparison.OrdinalIgnoreCase) >= 0 ||
                text.IndexOf("药品", StringComparison.OrdinalIgnoreCase) >= 0 ||
                text.IndexOf("药物", StringComparison.OrdinalIgnoreCase) >= 0 ||
                text.IndexOf("医药", StringComparison.OrdinalIgnoreCase) >= 0 ||
                text.IndexOf("保健品", StringComparison.OrdinalIgnoreCase) >= 0 ||
                text.IndexOf("古董", StringComparison.OrdinalIgnoreCase) >= 0 ||
                text.IndexOf("收藏品", StringComparison.OrdinalIgnoreCase) >= 0 ||
                text.IndexOf("антиквариат", StringComparison.OrdinalIgnoreCase) >= 0 ||
                text.IndexOf("коллекцион", StringComparison.OrdinalIgnoreCase) >= 0 ||
                text.IndexOf("food", StringComparison.OrdinalIgnoreCase) >= 0 ||
                text.IndexOf("medicine", StringComparison.OrdinalIgnoreCase) >= 0 ||
                text.IndexOf("pharmacy", StringComparison.OrdinalIgnoreCase) >= 0 ||
                text.IndexOf("antique", StringComparison.OrdinalIgnoreCase) >= 0 ||
                text.IndexOf("collectible", StringComparison.OrdinalIgnoreCase) >= 0;
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

    internal static class ProfitCalculatorService
    {
        public static ProfitEstimate Calculate(AppConfig config, IList<FeeRule> rules, ProfitInput input)
        {
            ProfitEstimate result = new ProfitEstimate();
            FeeRule rule = AssetCatalogService.FindBestFeeRule(
                rules,
                input == null ? null : input.CategoryCandidateIds,
                input == null ? 0 : input.CategoryId1,
                input == null ? 0 : input.CategoryId2);
            result.MatchedRule = rule;

            AppConfig safeConfig = config ?? AppConfig.CreateDefault();
            decimal targetProfit = input.TargetProfitPercent > 0
                ? input.TargetProfitPercent
                : ResolveTargetProfitPercent(safeConfig);
            decimal deliveryFee = input.DeliveryFee > 0 ? input.DeliveryFee : safeConfig.DeliveryFee;
            decimal commissionPercent = input.PlatformCommissionPercent > 0
                ? input.PlatformCommissionPercent
                : ResolvePlatformCommissionPercent(safeConfig);
            decimal promotionPercent = input.PromotionExpensePercent > 0
                ? input.PromotionExpensePercent
                : ResolvePromotionExpensePercent(safeConfig);
            decimal logisticsFee = rule == null ? 0m : ResolveLogisticsFee(rule, input.FulfillmentMode, input.WeightGrams);
            decimal cost = input.SourcePrice + input.OtherCost + deliveryFee + logisticsFee;
            decimal denominator = 1m - (commissionPercent / 100m) - (promotionPercent / 100m) - (targetProfit / 100m);
            if (denominator <= 0m)
            {
                throw new InvalidOperationException("平台佣金比例、推广费用比例、目标利润率之和必须小于 100%。");
            }

            decimal suggested = RoundMoney(cost / denominator);
            decimal actualSellingPrice = input.ManualSellingPrice > 0 ? input.ManualSellingPrice : suggested;
            decimal netRevenue = actualSellingPrice * (1m - (commissionPercent / 100m) - (promotionPercent / 100m));
            decimal profitAmount = netRevenue - cost;
            decimal profitPercent = cost <= 0 ? 0m : RoundMoney((profitAmount / cost) * 100m);

            result.LogisticsFee = logisticsFee;
            result.EstimatedCost = RoundMoney(cost);
            result.PlatformCommissionPercent = commissionPercent;
            result.PromotionExpensePercent = promotionPercent;
            result.SuggestedSellingPrice = suggested;
            result.ActualSellingPrice = RoundMoney(actualSellingPrice);
            result.ProfitAmount = RoundMoney(profitAmount);
            result.ProfitPercent = profitPercent;
            result.MeetsPriceFilter = actualSellingPrice >= safeConfig.MinPirce && actualSellingPrice <= safeConfig.MaxPrice;
            result.MeetsWeightFilter = input.WeightGrams >= safeConfig.MinWeight && input.WeightGrams <= safeConfig.MaxWeight;
            result.MeetsProfitFilter = profitPercent >= safeConfig.MinProfitPer;
            result.Notes = BuildNotes(rule, input, targetProfit);
            return result;
        }

        private static decimal ResolveTargetProfitPercent(AppConfig config)
        {
            if (config == null)
            {
                return 30m;
            }

            if (config.TargetProfitPercent > 0)
            {
                return config.TargetProfitPercent;
            }

            if (config.MinProfitPer > 0)
            {
                return Math.Max(config.MinProfitPer, 30m);
            }

            return 30m;
        }

        private static decimal ResolvePlatformCommissionPercent(AppConfig config)
        {
            return config != null && config.PlatformCommissionPercent > 0 ? config.PlatformCommissionPercent : 10m;
        }

        private static decimal ResolvePromotionExpensePercent(AppConfig config)
        {
            return config != null && config.PromotionExpensePercent > 0 ? config.PromotionExpensePercent : 30m;
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

/*
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

*/
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

/*
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
*/
}
