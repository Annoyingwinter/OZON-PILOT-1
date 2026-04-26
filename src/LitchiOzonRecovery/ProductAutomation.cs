using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace LitchiOzonRecovery
{
    internal sealed class SourcingSeed
    {
        public string Keyword { get; set; }
        public string Category { get; set; }
        public string Why { get; set; }
    }

    internal sealed class SourcingOptions
    {
        public string Provider { get; set; }
        public string ApiKey { get; set; }
        public string ApiSecret { get; set; }
        public int PerKeywordLimit { get; set; }
        public int DetailLimit { get; set; }
        public decimal RubPerCny { get; set; }
        public long OzonCategoryId { get; set; }
        public long OzonTypeId { get; set; }
        public decimal PriceMultiplier { get; set; }
        public string CurrencyCode { get; set; }
        public string Vat { get; set; }
    }

    internal sealed class SourceProduct
    {
        public string OfferId { get; set; }
        public string Title { get; set; }
        public string RussianTitle { get; set; }
        public string RussianDescription { get; set; }
        public string EnglishKeyword { get; set; }
        public string SourceUrl { get; set; }
        public decimal PriceCny { get; set; }
        public string PriceText { get; set; }
        public int SalesCount { get; set; }
        public string ShopName { get; set; }
        public string ShopUrl { get; set; }
        public string MainImage { get; set; }
        public List<string> Images { get; set; }
        public Dictionary<string, string> Attributes { get; set; }
        public Dictionary<long, string> OzonAttributes { get; set; }
        public string Keyword { get; set; }
        public decimal Score { get; set; }
        public string Decision { get; set; }
        public string Reason { get; set; }

        public SourceProduct()
        {
            Images = new List<string>();
            Attributes = new Dictionary<string, string>();
            OzonAttributes = new Dictionary<long, string>();
        }
    }

    internal sealed class SourcingResult
    {
        public List<SourceProduct> Products { get; private set; }
        public List<string> Logs { get; private set; }

        public SourcingResult()
        {
            Products = new List<SourceProduct>();
            Logs = new List<string>();
        }
    }

    internal sealed class OzonImportResult
    {
        public bool Success { get; set; }
        public string TaskId { get; set; }
        public string RawResponse { get; set; }
        public string ErrorMessage { get; set; }
        public string ImportInfoResponse { get; set; }
        public string ImportSummary { get; set; }
    }

    internal sealed class ProductAutomationService
    {
        private const string OneboundSearchEndpoint = "https://api-gw.onebound.cn/1688/item_search/";
        private const string OneboundDetailEndpoint = "https://api-gw.onebound.cn/1688/item_get/";
        private const string DingdanxiaSearchEndpoint = "https://api.dingdanxia.com/1688/item_search";
        private const string DingdanxiaDetailEndpoint = "https://api.dingdanxia.com/1688/item_get";
        private const string OzonSellerApiBaseUrl = "https://api-seller.ozon.ru";
        private const string DeepSeekChatEndpoint = "https://api.deepseek.com/chat/completions";
        private const string DeepSeekApiKeyEnvVar = "DEEPSEEK_API_KEY";
        private static bool _tlsInitialized;
        private readonly OzonProductImageAdaptationModule _imageAdapter = new OzonProductImageAdaptationModule();

        public SourcingResult Collect1688Candidates(IList<SourcingSeed> seeds, AppConfig config, SourcingOptions options)
        {
            SourcingResult result = new SourcingResult();
            if (seeds == null || seeds.Count == 0)
            {
                result.Logs.Add("No keyword seeds.");
                return result;
            }

            Validate1688Options(options);
            int perKeywordLimit = options.PerKeywordLimit <= 0 ? 5 : options.PerKeywordLimit;
            int detailLimit = options.DetailLimit <= 0 ? 12 : options.DetailLimit;
            Dictionary<string, SourceProduct> byOfferId = new Dictionary<string, SourceProduct>();

            for (int i = 0; i < seeds.Count; i++)
            {
                SourcingSeed seed = seeds[i];
                if (seed == null || string.IsNullOrEmpty(seed.Keyword))
                {
                    continue;
                }

                result.Logs.Add("Search 1688: " + seed.Keyword);
                List<SourceProduct> found = Search1688(seed.Keyword, options, perKeywordLimit);
                result.Logs.Add("  found " + found.Count + " cards");

                for (int j = 0; j < found.Count; j++)
                {
                    SourceProduct product = found[j];
                    product.Keyword = seed.Keyword;
                    if (string.IsNullOrEmpty(product.OfferId) || byOfferId.ContainsKey(product.OfferId))
                    {
                        continue;
                    }

                    byOfferId[product.OfferId] = product;
                }
            }

            List<SourceProduct> candidates = new List<SourceProduct>(byOfferId.Values);
            candidates.Sort(delegate(SourceProduct left, SourceProduct right)
            {
                return right.SalesCount.CompareTo(left.SalesCount);
            });

            int enrichCount = Math.Min(detailLimit, candidates.Count);
            for (int i = 0; i < enrichCount; i++)
            {
                try
                {
                    SourceProduct detail = Get1688Detail(candidates[i].OfferId, options);
                    MergeProduct(candidates[i], detail);
                }
                catch (Exception ex)
                {
                    result.Logs.Add("  detail failed " + candidates[i].OfferId + ": " + ex.Message);
                }
            }

            for (int i = 0; i < candidates.Count; i++)
            {
                ScoreCandidate(candidates[i], config);
            }

            candidates.Sort(delegate(SourceProduct left, SourceProduct right)
            {
                int decision = DecisionRank(right.Decision).CompareTo(DecisionRank(left.Decision));
                if (decision != 0) return decision;
                return right.Score.CompareTo(left.Score);
            });

            result.Products.AddRange(candidates);
            result.Logs.Add("Final candidates: " + result.Products.Count);
            return result;
        }

        public string GenerateEnglishCategoryKeyword(string categoryText)
        {
            if (string.IsNullOrEmpty(categoryText))
            {
                return string.Empty;
            }

            try
            {
                JObject request = new JObject();
                request["model"] = "deepseek-chat";
                request["temperature"] = 0.1;
                request["response_format"] = new JObject(new JProperty("type", "json_object"));

                JArray messages = new JArray();
                messages.Add(new JObject(
                    new JProperty("role", "system"),
                    new JProperty("content", "Return strict JSON only. Convert marketplace category names into one precise English 1688 search keyword. Do not return generic words such as product, item, general merchandise, toy unless the category is actually toys. Keep it 2-5 words.")));
                messages.Add(new JObject(
                    new JProperty("role", "user"),
                    new JProperty("content", "Category: " + categoryText + "\nSchema: {\"keyword\":\"precise english keyword\"}")));
                request["messages"] = messages;

                JObject response = JObject.Parse(PostDeepSeekJson(request.ToString(Formatting.None)));
                string content = Convert.ToString(response.SelectToken("choices[0].message.content") ?? string.Empty);
                JObject json = JObject.Parse(ExtractJsonObject(content));
                return CleanPublicText(Convert.ToString(json["keyword"] ?? string.Empty));
            }
            catch
            {
                return string.Empty;
            }
        }

        public OzonImportResult UploadToOzon(IList<SourceProduct> products, SourcingOptions options, string clientId, string apiKey)
        {
            return UploadToOzon(products, options, clientId, apiKey, null);
        }

        public OzonImportResult UploadToOzon(IList<SourceProduct> products, SourcingOptions options, string clientId, string apiKey, Action<string> log)
        {
            if (products == null || products.Count == 0)
            {
                return new OzonImportResult { Success = false, ErrorMessage = "No products selected." };
            }

            if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(apiKey))
            {
                return new OzonImportResult { Success = false, ErrorMessage = "Ozon Client-Id and Api-Key are required." };
            }

            if (options.OzonCategoryId <= 0 || options.OzonTypeId <= 0)
            {
                return new OzonImportResult { Success = false, ErrorMessage = "Ozon category id and type id are required." };
            }

            JArray items = new JArray();
            JArray categoryAttributes = LoadOzonCategoryAttributes(options, clientId, apiKey);
            WriteLog(log, "[upload] selected products after filtering: " + products.Count + ".");
            for (int i = 0; i < products.Count; i++)
            {
                SourceProduct product = products[i];
                if (product == null)
                {
                    continue;
                }

                WriteLog(log, "[upload] prepare " + (i + 1) + "/" + products.Count + ": " + SafeOfferId(product.OfferId));
                EnrichProductWithDeepSeek(product, options, categoryAttributes);
                _imageAdapter.AdaptPassedProductImage(product, log);
                items.Add(BuildOzonImportItem(product, options));
            }

            JObject payload = new JObject();
            payload["items"] = items;
            string response = PostOzonJson("/v3/product/import", payload.ToString(Formatting.None), clientId, apiKey);
            JObject parsed = JObject.Parse(response);
            string taskId = Convert.ToString(parsed.SelectToken("result.task_id") ?? parsed.SelectToken("task_id") ?? string.Empty);
            return new OzonImportResult
            {
                Success = true,
                TaskId = taskId,
                RawResponse = response
            };
        }

        public string GetOzonImportInfo(string taskId, string clientId, string apiKey)
        {
            JObject payload = new JObject();
            payload["task_id"] = taskId;
            return PostOzonJson("/v1/product/import/info", payload.ToString(Formatting.None), clientId, apiKey);
        }

        private JArray LoadOzonCategoryAttributes(SourcingOptions options, string clientId, string apiKey)
        {
            try
            {
                if (options == null || options.OzonCategoryId <= 0 || options.OzonTypeId <= 0)
                {
                    return new JArray();
                }

                JObject payload = new JObject();
                payload["description_category_id"] = options.OzonCategoryId;
                payload["type_id"] = options.OzonTypeId;
                payload["language"] = "RU";
                JObject root = JObject.Parse(PostOzonJson("/v1/description-category/attribute", payload.ToString(Formatting.None), clientId, apiKey));
                JArray result = root["result"] as JArray;
                return result ?? new JArray();
            }
            catch
            {
                return new JArray();
            }
        }

        public OzonImportResult WaitForOzonImportInfo(string taskId, string clientId, string apiKey, int attempts, int delayMs)
        {
            OzonImportResult result = new OzonImportResult();
            result.TaskId = taskId;
            if (string.IsNullOrEmpty(taskId))
            {
                result.Success = false;
                result.ErrorMessage = "Ozon returned no task_id.";
                return result;
            }

            int count = attempts <= 0 ? 6 : attempts;
            int wait = delayMs <= 0 ? 5000 : delayMs;
            string lastResponse = string.Empty;
            for (int i = 0; i < count; i++)
            {
                if (i > 0)
                {
                    Thread.Sleep(wait);
                }

                lastResponse = GetOzonImportInfo(taskId, clientId, apiKey);
                string summary = BuildOzonImportSummary(lastResponse);
                if (!IsImportStillProcessing(summary))
                {
                    result.Success = !HasImportErrors(summary);
                    result.ImportInfoResponse = lastResponse;
                    result.ImportSummary = summary;
                    return result;
                }
            }

            result.Success = false;
            result.ImportInfoResponse = lastResponse;
            result.ImportSummary = "Ozon import task is still processing. task_id=" + taskId + Environment.NewLine +
                BuildOzonImportSummary(lastResponse);
            return result;
        }

        public string BuildOzonImportSummary(string response)
        {
            if (string.IsNullOrEmpty(response))
            {
                return "Empty Ozon import info response.";
            }

            try
            {
                JObject root = JObject.Parse(response);
                StringBuilder builder = new StringBuilder();
                JToken result = root["result"] ?? root;
                string taskStatus = FirstTokenString(result, "status", "state", "task_status");
                if (!string.IsNullOrEmpty(taskStatus))
                {
                    builder.AppendLine("task status: " + taskStatus);
                }

                JArray items = FindArray(result, "items", "products", "errors");
                if (items == null || items.Count == 0)
                {
                    string message = FirstTokenString(root, "message", "error");
                    if (!string.IsNullOrEmpty(message))
                    {
                        builder.AppendLine("message: " + message);
                    }

                    if (builder.Length == 0)
                    {
                        builder.AppendLine("No item-level result yet; Ozon may still be processing.");
                    }

                    return builder.ToString().Trim();
                }

                int ok = 0;
                int failed = 0;
                for (int i = 0; i < items.Count; i++)
                {
                    JObject item = items[i] as JObject;
                    if (item == null)
                    {
                        continue;
                    }

                    string offerId = FirstTokenString(item, "offer_id", "offerId", "article");
                    string status = FirstTokenString(item, "status", "state");
                    JArray errors = FindArray(item, "errors", "error");
                    bool hasErrors = errors != null && errors.Count > 0;
                    if (hasErrors || status.IndexOf("fail", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        status.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        failed += 1;
                        builder.AppendLine("failed: " + SafeText(offerId) + " " + SafeText(status));
                        AppendErrorMessages(builder, errors);
                    }
                    else
                    {
                        ok += 1;
                        builder.AppendLine("accepted: " + SafeText(offerId) + " " + SafeText(status));
                    }
                }

                builder.Insert(0, "items accepted=" + ok + ", failed=" + failed + Environment.NewLine);
                return builder.ToString().Trim();
            }
            catch (Exception ex)
            {
                return "Could not parse Ozon import info: " + ex.Message + Environment.NewLine + response;
            }
        }

        private static bool HasImportErrors(string summary)
        {
            if (string.IsNullOrEmpty(summary))
            {
                return true;
            }

            string lower = summary.ToLowerInvariant();
            if (lower.IndexOf("failed=0", StringComparison.OrdinalIgnoreCase) >= 0 &&
                lower.IndexOf("failed:", StringComparison.OrdinalIgnoreCase) < 0 &&
                lower.IndexOf("error:", StringComparison.OrdinalIgnoreCase) < 0)
            {
                return false;
            }

            return lower.IndexOf("failed:", StringComparison.OrdinalIgnoreCase) >= 0 ||
                lower.IndexOf("error:", StringComparison.OrdinalIgnoreCase) >= 0 ||
                lower.IndexOf("could not parse", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsImportStillProcessing(string summary)
        {
            if (string.IsNullOrEmpty(summary))
            {
                return true;
            }

            string lower = summary.ToLowerInvariant();
            return lower.IndexOf("processing", StringComparison.OrdinalIgnoreCase) >= 0 ||
                lower.IndexOf("pending", StringComparison.OrdinalIgnoreCase) >= 0 ||
                lower.IndexOf("no item-level result yet", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public void ExportCandidates(string path, IList<SourceProduct> products)
        {
            JArray array = JArray.FromObject(products ?? new List<SourceProduct>());
            File.WriteAllText(path, array.ToString(Formatting.Indented), new UTF8Encoding(false));
        }

        private static string FirstTokenString(JToken token, params string[] names)
        {
            if (token == null)
            {
                return string.Empty;
            }

            for (int i = 0; i < names.Length; i++)
            {
                JToken child = token[names[i]];
                if (child != null && child.Type != JTokenType.Null)
                {
                    string text = Convert.ToString(child);
                    if (!string.IsNullOrEmpty(text))
                    {
                        return text.Trim();
                    }
                }
            }

            return string.Empty;
        }

        private static JArray FindArray(JToken token, params string[] names)
        {
            if (token == null)
            {
                return null;
            }

            for (int i = 0; i < names.Length; i++)
            {
                JToken direct = token[names[i]];
                if (direct is JArray)
                {
                    return (JArray)direct;
                }

                if (direct != null)
                {
                    JArray nested = FindArray(direct, names);
                    if (nested != null)
                    {
                        return nested;
                    }
                }
            }

            JObject obj = token as JObject;
            if (obj != null)
            {
                foreach (JProperty property in obj.Properties())
                {
                    if (property.Value is JArray)
                    {
                        for (int i = 0; i < names.Length; i++)
                        {
                            if (property.Name.IndexOf(names[i], StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                return (JArray)property.Value;
                            }
                        }
                    }
                }
            }

            return null;
        }

        private static void AppendErrorMessages(StringBuilder builder, JArray errors)
        {
            if (builder == null || errors == null)
            {
                return;
            }

            for (int i = 0; i < errors.Count; i++)
            {
                JObject error = errors[i] as JObject;
                if (error == null)
                {
                    builder.AppendLine("  error: " + Convert.ToString(errors[i]));
                    continue;
                }

                string code = FirstTokenString(error, "code", "field", "attribute_id");
                string message = FirstTokenString(error, "message", "error", "text", "description");
                builder.AppendLine("  error: " + (string.IsNullOrEmpty(code) ? string.Empty : code + " - ") + SafeText(message));
            }
        }

        private static string SafeText(string value)
        {
            return string.IsNullOrEmpty(value) ? "(none)" : value;
        }

        private static void Validate1688Options(SourcingOptions options)
        {
            if (options == null)
            {
                throw new ArgumentNullException("options");
            }

            if (string.IsNullOrEmpty(options.ApiKey))
            {
                throw new InvalidOperationException("1688 API key is required.");
            }
        }

        private static int DecisionRank(string decision)
        {
            if (string.Equals(decision, "Go", StringComparison.OrdinalIgnoreCase)) return 3;
            if (string.Equals(decision, "Watch", StringComparison.OrdinalIgnoreCase)) return 2;
            return 1;
        }

        private static void ScoreCandidate(SourceProduct product, AppConfig config)
        {
            decimal score = 0m;
            List<string> reasons = new List<string>();

            if (product.PriceCny > 0)
            {
                score += 20m;
                if (config != null && config.IsFilterGmPrice && product.PriceCny < config.MinGmPirce)
                {
                    reasons.Add("source price below configured minimum");
                }
            }
            else
            {
                reasons.Add("missing source price");
            }

            if (product.SalesCount > 0)
            {
                score += Math.Min(25m, product.SalesCount / 20m);
            }
            else
            {
                reasons.Add("missing sales signal");
            }

            if (!string.IsNullOrEmpty(product.MainImage) || product.Images.Count > 0)
            {
                score += 20m;
            }
            else
            {
                reasons.Add("missing product image");
            }

            if (product.Attributes.Count > 0)
            {
                score += Math.Min(15m, product.Attributes.Count * 2m);
            }

            if (!string.IsNullOrEmpty(product.ShopName))
            {
                score += 10m;
            }

            if (config != null && config.MinSaleNum > 0 && product.SalesCount > 0 && product.SalesCount < config.MinSaleNum)
            {
                reasons.Add("sales below configured minimum");
            }

            product.Score = Math.Round(score, 2);
            product.Decision = reasons.Count == 0 && score >= 45m ? "Go" : (score >= 30m ? "Watch" : "No-Go");
            product.Reason = reasons.Count == 0 ? "meets basic source, sales and content checks" : string.Join("; ", reasons.ToArray());
        }

        private static void MergeProduct(SourceProduct target, SourceProduct detail)
        {
            if (target == null || detail == null)
            {
                return;
            }

            if (!string.IsNullOrEmpty(detail.Title)) target.Title = detail.Title;
            if (!string.IsNullOrEmpty(detail.SourceUrl)) target.SourceUrl = detail.SourceUrl;
            if (detail.PriceCny > 0) target.PriceCny = detail.PriceCny;
            if (!string.IsNullOrEmpty(detail.PriceText)) target.PriceText = detail.PriceText;
            if (detail.SalesCount > 0) target.SalesCount = detail.SalesCount;
            if (!string.IsNullOrEmpty(detail.ShopName)) target.ShopName = detail.ShopName;
            if (!string.IsNullOrEmpty(detail.ShopUrl)) target.ShopUrl = detail.ShopUrl;
            if (!string.IsNullOrEmpty(detail.MainImage)) target.MainImage = detail.MainImage;
            if (detail.Images.Count > 0) target.Images = detail.Images;

            foreach (KeyValuePair<string, string> pair in detail.Attributes)
            {
                if (!target.Attributes.ContainsKey(pair.Key))
                {
                    target.Attributes[pair.Key] = pair.Value;
                }
            }
        }

        private static void EnrichProductWithDeepSeek(SourceProduct product, SourcingOptions options, JArray categoryAttributes)
        {
            if (product == null)
            {
                return;
            }

            string deepSeekApiKey = Environment.GetEnvironmentVariable(DeepSeekApiKeyEnvVar);
            if (string.IsNullOrWhiteSpace(deepSeekApiKey))
            {
                if (string.IsNullOrEmpty(product.RussianTitle))
                {
                    product.RussianTitle = Truncate(CleanPublicText(product.Title), 120);
                }

                return;
            }

            try
            {
                JObject source = new JObject();
                source["title_cn"] = product.Title ?? string.Empty;
                source["price_cny"] = product.PriceCny;
                source["source_url"] = product.SourceUrl ?? string.Empty;
                source["keyword"] = product.Keyword ?? product.EnglishKeyword ?? string.Empty;
                source["attributes_cn"] = JObject.FromObject(product.Attributes ?? new Dictionary<string, string>());
                source["ozon_required_attributes"] = BuildDeepSeekAttributeHints(categoryAttributes);

                JObject request = new JObject();
                request["model"] = "deepseek-chat";
                request["temperature"] = 0.2;
                request["response_format"] = new JObject(new JProperty("type", "json_object"));
                JArray messages = new JArray();
                messages.Add(new JObject(
                    new JProperty("role", "system"),
                    new JProperty("content", "You generate Ozon marketplace listings. Return strict JSON only. Russian title and description are mandatory. Search keyword must be precise English. Fill Ozon required attributes when a text value can be inferred; do not invent safety certifications.")));
                messages.Add(new JObject(
                    new JProperty("role", "user"),
                    new JProperty("content",
                        "Create listing JSON for this 1688 product. Required schema: {\"search_keyword_en\":\"...\",\"title_ru\":\"...\",\"description_ru\":\"...\",\"ozon_attributes\":[{\"id\":123,\"value\":\"...\"}],\"dimensions\":{\"height_mm\":100,\"width_mm\":100,\"depth_mm\":100,\"weight_g\":500}}. Product data: " +
                        source.ToString(Formatting.None))));
                request["messages"] = messages;

                JObject response = JObject.Parse(PostDeepSeekJson(request.ToString(Formatting.None), deepSeekApiKey));
                string content = Convert.ToString(response.SelectToken("choices[0].message.content") ?? string.Empty);
                JObject listing = JObject.Parse(ExtractJsonObject(content));
                product.EnglishKeyword = CleanPublicText(Convert.ToString(listing["search_keyword_en"] ?? string.Empty));
                product.RussianTitle = CleanPublicText(Convert.ToString(listing["title_ru"] ?? string.Empty));
                product.RussianDescription = CleanPublicText(Convert.ToString(listing["description_ru"] ?? string.Empty));

                JArray attrs = listing["ozon_attributes"] as JArray;
                for (int i = 0; attrs != null && i < attrs.Count; i++)
                {
                    JObject attr = attrs[i] as JObject;
                    if (attr == null)
                    {
                        continue;
                    }

                    long id = 0;
                    long.TryParse(Convert.ToString(attr["id"] ?? string.Empty), out id);
                    string value = CleanPublicText(Convert.ToString(attr["value"] ?? string.Empty));
                    if (id > 0 && !string.IsNullOrEmpty(value) && !product.OzonAttributes.ContainsKey(id))
                    {
                        product.OzonAttributes[id] = value;
                    }
                }

                JObject dimensions = listing["dimensions"] as JObject;
                if (dimensions != null)
                {
                    AddDimensionAttribute(product, "height_mm", Convert.ToString(dimensions["height_mm"] ?? string.Empty));
                    AddDimensionAttribute(product, "width_mm", Convert.ToString(dimensions["width_mm"] ?? string.Empty));
                    AddDimensionAttribute(product, "depth_mm", Convert.ToString(dimensions["depth_mm"] ?? string.Empty));
                    AddDimensionAttribute(product, "weight_g", Convert.ToString(dimensions["weight_g"] ?? string.Empty));
                }
            }
            catch
            {
                if (string.IsNullOrEmpty(product.RussianTitle))
                {
                    product.RussianTitle = Truncate(CleanPublicText(product.Title), 120);
                }
            }
        }

        private static JArray BuildDeepSeekAttributeHints(JArray attributes)
        {
            JArray hints = new JArray();
            for (int i = 0; attributes != null && i < attributes.Count && hints.Count < 30; i++)
            {
                JObject attr = attributes[i] as JObject;
                if (attr == null)
                {
                    continue;
                }

                bool required = string.Equals(Convert.ToString(attr["is_required"] ?? string.Empty), "True", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(Convert.ToString(attr["required"] ?? string.Empty), "True", StringComparison.OrdinalIgnoreCase);
                if (!required)
                {
                    continue;
                }

                JObject hint = new JObject();
                hint["id"] = attr["id"];
                hint["name"] = Convert.ToString(attr["name"] ?? attr["attribute_name"] ?? string.Empty);
                hint["type"] = Convert.ToString(attr["type"] ?? string.Empty);
                hint["dictionary_id"] = attr["dictionary_id"];
                hints.Add(hint);
            }

            return hints;
        }

        private static void AddDimensionAttribute(SourceProduct product, string name, string value)
        {
            if (product == null || string.IsNullOrEmpty(value))
            {
                return;
            }

            product.Attributes[name] = value;
        }

        private static List<SourceProduct> Search1688(string keyword, SourcingOptions options, int limit)
        {
            string provider = NormalizeProvider(options.Provider);
            string url = provider == "dingdanxia"
                ? DingdanxiaSearchEndpoint + "?apikey=" + Uri.EscapeDataString(options.ApiKey) + "&q=" + Uri.EscapeDataString(keyword) + "&page=1"
                : OneboundSearchEndpoint + "?key=" + Uri.EscapeDataString(options.ApiKey) + "&secret=" + Uri.EscapeDataString(options.ApiSecret ?? string.Empty) + "&q=" + Uri.EscapeDataString(keyword) + "&page=1&sort=default&page_size=" + limit;
            JObject json = JObject.Parse(GetJson(url));
            JArray items = FindFirstArray(json, new string[] { "items.item", "result", "data", "items" });
            List<SourceProduct> products = new List<SourceProduct>();

            if (items == null)
            {
                return products;
            }

            for (int i = 0; i < items.Count && products.Count < limit; i++)
            {
                JObject item = items[i] as JObject;
                if (item == null)
                {
                    continue;
                }

                string offerId = FirstString(item, "num_iid", "id", "offerId", "offer_id");
                if (string.IsNullOrEmpty(offerId))
                {
                    continue;
                }

                SourceProduct product = new SourceProduct();
                product.OfferId = offerId;
                product.Title = FirstString(item, "title", "name");
                product.SourceUrl = "https://detail.1688.com/offer/" + offerId + ".html";
                product.PriceText = FirstString(item, "price", "salePrice", "priceText");
                product.PriceCny = ParseMoney(product.PriceText);
                product.MainImage = FirstString(item, "pic_url", "image", "img", "picUrl");
                if (!string.IsNullOrEmpty(product.MainImage)) product.Images.Add(product.MainImage);
                product.ShopName = FirstString(item, "nick", "shop_name", "seller_nick");
                product.ShopUrl = FirstString(item, "seller_url", "shop_url");
                product.SalesCount = ParseInt(FirstString(item, "sales", "volume", "sold", "sale_count"));
                products.Add(product);
            }

            return products;
        }

        private static SourceProduct Get1688Detail(string offerId, SourcingOptions options)
        {
            string provider = NormalizeProvider(options.Provider);
            string url = provider == "dingdanxia"
                ? DingdanxiaDetailEndpoint + "?apikey=" + Uri.EscapeDataString(options.ApiKey) + "&num_iid=" + Uri.EscapeDataString(offerId)
                : OneboundDetailEndpoint + "?key=" + Uri.EscapeDataString(options.ApiKey) + "&secret=" + Uri.EscapeDataString(options.ApiSecret ?? string.Empty) + "&num_iid=" + Uri.EscapeDataString(offerId) + "&lang=zh-CN";
            JObject json = JObject.Parse(GetJson(url));
            JObject item = FirstObject(json, "item", "result", "data");
            if (item == null)
            {
                return null;
            }

            SourceProduct product = new SourceProduct();
            product.OfferId = offerId;
            product.Title = FirstString(item, "title", "name");
            product.SourceUrl = "https://detail.1688.com/offer/" + offerId + ".html";
            product.PriceText = FirstString(item, "price", "orginal_price", "salePrice");
            product.PriceCny = ParseMoney(product.PriceText);
            product.ShopName = FirstString(item, "nick", "seller_nick", "shop_name");
            product.ShopUrl = FirstString(item, "seller_url", "shop_url");
            product.SalesCount = ParseInt(FirstString(item, "sales", "volume", "sold", "sale_count"));

            AddImage(product, FirstString(item, "pic_url", "image", "main_image"));
            JArray itemImages = FindFirstArray(item, new string[] { "item_imgs.item_img", "images", "image_urls" });
            if (itemImages != null)
            {
                for (int i = 0; i < itemImages.Count; i++)
                {
                    JObject imageObject = itemImages[i] as JObject;
                    string image = imageObject == null ? Convert.ToString(itemImages[i]) : FirstString(imageObject, "url", "image", "img");
                    AddImage(product, image);
                }
            }

            JObject props = item["props_list"] as JObject;
            if (props != null)
            {
                foreach (JProperty property in props.Properties())
                {
                    string key = property.Name;
                    int colon = key.IndexOf(':');
                    if (colon >= 0 && colon + 1 < key.Length)
                    {
                        key = key.Substring(colon + 1);
                    }

                    if (!product.Attributes.ContainsKey(key))
                    {
                        product.Attributes[key] = Convert.ToString(property.Value);
                    }
                }
            }

            return product;
        }

        private static JObject BuildOzonImportItem(SourceProduct product, SourcingOptions options)
        {
            decimal multiplier = options.PriceMultiplier <= 0 ? 2.2m : options.PriceMultiplier;
            string currency = string.IsNullOrEmpty(options.CurrencyCode) ? "CNY" : options.CurrencyCode;
            decimal price = string.Equals(currency, "RUB", StringComparison.OrdinalIgnoreCase)
                ? Math.Ceiling(Math.Max(1m, product.PriceCny) * (options.RubPerCny <= 0 ? 12.5m : options.RubPerCny) * multiplier)
                : Math.Ceiling(Math.Max(1m, product.PriceCny) * multiplier);
            string offerId = "LZ1688-" + SafeOfferId(product.OfferId);
            string primaryImage = !string.IsNullOrEmpty(product.MainImage) ? product.MainImage : (product.Images.Count > 0 ? product.Images[0] : string.Empty);
            string title = !string.IsNullOrEmpty(product.RussianTitle) ? product.RussianTitle : product.Title;
            string description = !string.IsNullOrEmpty(product.RussianDescription)
                ? product.RussianDescription
                : CleanPublicText(product.Title) + "\nИсточник: " + product.SourceUrl;

            JObject item = new JObject();
            item["description_category_id"] = options.OzonCategoryId;
            item["type_id"] = options.OzonTypeId;
            item["name"] = Truncate(CleanPublicText(title), 500);
            item["offer_id"] = offerId;
            item["barcode"] = string.Empty;
            item["price"] = price.ToString("0");
            item["old_price"] = Math.Ceiling(price * 1.25m).ToString("0");
            item["currency_code"] = currency;
            item["vat"] = string.IsNullOrEmpty(options.Vat) ? "0" : options.Vat;
            item["height"] = 100;
            item["depth"] = 100;
            item["width"] = 100;
            item["dimension_unit"] = "mm";
            item["weight"] = 500;
            item["weight_unit"] = "g";
            item["primary_image"] = primaryImage;

            JArray images = new JArray();
            for (int i = 0; i < product.Images.Count; i++)
            {
                if (product.Images[i] != primaryImage)
                {
                    images.Add(product.Images[i]);
                }
            }
            item["images"] = images;

            JArray attributes = new JArray();
            foreach (KeyValuePair<long, string> pair in product.OzonAttributes)
            {
                if (pair.Key <= 0 || string.IsNullOrEmpty(pair.Value))
                {
                    continue;
                }

                JObject attr = new JObject();
                attr["id"] = pair.Key;
                JArray values = new JArray();
                values.Add(new JObject(new JProperty("value", Truncate(CleanPublicText(pair.Value), 900))));
                attr["values"] = values;
                attributes.Add(attr);
            }

            item["attributes"] = attributes;
            item["description"] = Truncate(CleanPublicText(description), 3000);
            return item;
        }

        private static string PostOzonJson(string path, string json, string clientId, string apiKey)
        {
            EnsureModernTls();
            string url = OzonSellerApiBaseUrl + path;
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = "POST";
            request.ContentType = "application/json";
            request.Accept = "application/json";
            request.Timeout = 60000;
            request.Headers["Client-Id"] = clientId;
            request.Headers["Api-Key"] = apiKey;

            byte[] body = Encoding.UTF8.GetBytes(json);
            request.ContentLength = body.Length;
            using (Stream requestStream = request.GetRequestStream())
            {
                requestStream.Write(body, 0, body.Length);
            }

            return ReadResponse(request);
        }

        private static string PostDeepSeekJson(string json, string apiKey)
        {
            EnsureModernTls();
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(DeepSeekChatEndpoint);
            request.Method = "POST";
            request.ContentType = "application/json";
            request.Accept = "application/json";
            request.Timeout = 45000;
            request.Headers["Authorization"] = "Bearer " + apiKey;

            byte[] body = Encoding.UTF8.GetBytes(json);
            request.ContentLength = body.Length;
            using (Stream requestStream = request.GetRequestStream())
            {
                requestStream.Write(body, 0, body.Length);
            }

            return ReadResponse(request);
        }

        private static string GetJson(string url)
        {
            EnsureModernTls();
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = "GET";
            request.Accept = "application/json";
            request.UserAgent = "OZON-PILOT/1.0";
            request.Timeout = 45000;
            return ReadResponse(request);
        }

        private static void EnsureModernTls()
        {
            if (_tlsInitialized)
            {
                return;
            }

            // TLS 1.2 is required by Ozon Seller API. .NET 4.0 may not expose the enum names,
            // so use numeric values while still allowing older endpoints used by legacy suppliers.
            const int tls10 = 192;
            const int tls11 = 768;
            const int tls12 = 3072;
            ServicePointManager.SecurityProtocol =
                (SecurityProtocolType)(tls10 | tls11 | tls12);
            ServicePointManager.Expect100Continue = false;
            ServicePointManager.ServerCertificateValidationCallback =
                delegate { return true; };
            _tlsInitialized = true;
        }

        private static string ReadResponse(HttpWebRequest request)
        {
            try
            {
                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                using (Stream stream = response.GetResponseStream())
                using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
                {
                    return reader.ReadToEnd();
                }
            }
            catch (WebException ex)
            {
                string detail = ex.Message;
                if (ex.Response != null)
                {
                    using (Stream stream = ex.Response.GetResponseStream())
                    using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
                    {
                        detail = reader.ReadToEnd();
                    }
                }

                throw new InvalidOperationException(detail, ex);
            }
        }

        private static string NormalizeProvider(string value)
        {
            string provider = string.IsNullOrEmpty(value) ? "onebound" : value.Trim().ToLowerInvariant();
            return provider == "dingdanxia" ? "dingdanxia" : "onebound";
        }

        private static void AddImage(SourceProduct product, string image)
        {
            if (string.IsNullOrEmpty(image))
            {
                return;
            }

            if (string.IsNullOrEmpty(product.MainImage))
            {
                product.MainImage = image;
            }

            if (!product.Images.Contains(image))
            {
                product.Images.Add(image);
            }
        }

        private static JArray FindFirstArray(JObject obj, string[] paths)
        {
            for (int i = 0; i < paths.Length; i++)
            {
                JToken token = obj.SelectToken(paths[i]);
                if (token is JArray)
                {
                    return (JArray)token;
                }
            }

            return null;
        }

        private static JObject FirstObject(JObject obj, params string[] names)
        {
            for (int i = 0; i < names.Length; i++)
            {
                JObject child = obj[names[i]] as JObject;
                if (child != null)
                {
                    return child;
                }
            }

            return null;
        }

        private static string FirstString(JObject obj, params string[] names)
        {
            for (int i = 0; i < names.Length; i++)
            {
                JToken token = obj[names[i]];
                if (token != null && token.Type != JTokenType.Null)
                {
                    string text = Convert.ToString(token);
                    if (!string.IsNullOrEmpty(text))
                    {
                        return text.Trim();
                    }
                }
            }

            return string.Empty;
        }

        private static decimal ParseMoney(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return 0m;
            }

            StringBuilder builder = new StringBuilder();
            bool dot = false;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (char.IsDigit(c))
                {
                    builder.Append(c);
                }
                else if (c == '.' && !dot)
                {
                    builder.Append(c);
                    dot = true;
                }
                else if (builder.Length > 0)
                {
                    break;
                }
            }

            decimal value;
            return decimal.TryParse(builder.ToString(), out value) ? value : 0m;
        }

        private static int ParseInt(string text)
        {
            int value;
            return int.TryParse(text, out value) ? value : 0;
        }

        private static string SafeOfferId(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return Guid.NewGuid().ToString("N").Substring(0, 12);
            }

            StringBuilder builder = new StringBuilder();
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (char.IsLetterOrDigit(c) || c == '-' || c == '_')
                {
                    builder.Append(c);
                }
            }

            return builder.Length == 0 ? Guid.NewGuid().ToString("N").Substring(0, 12) : builder.ToString();
        }

        private static void WriteLog(Action<string> log, string message)
        {
            if (log != null)
            {
                log(message);
            }
        }

        private static string CleanPublicText(string value)
        {
            string text = value ?? string.Empty;
            text = text.Replace("Ozon hot", string.Empty).Replace("1688 hot", string.Empty);
            return WebUtility.HtmlDecode(text).Replace("\r", " ").Replace("\n", " ").Replace("\t", " ").Trim();
        }

        private static string ExtractJsonObject(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "{}";
            }

            int start = value.IndexOf('{');
            int end = value.LastIndexOf('}');
            if (start >= 0 && end > start)
            {
                return value.Substring(start, end - start + 1);
            }

            return value;
        }

        private static string Truncate(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
            {
                return value ?? string.Empty;
            }

            return value.Substring(0, maxLength);
        }
    }
}
