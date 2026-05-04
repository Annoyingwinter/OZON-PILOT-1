using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
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
        public List<long> OzonCategoryCandidateIds { get; set; }
        public decimal PriceMultiplier { get; set; }
        public decimal MinOzonPrice { get; set; }
        public string CurrencyCode { get; set; }
        public string Vat { get; set; }
        public AppConfig Config { get; set; }
        public List<FeeRule> FeeRules { get; set; }
        public string FulfillmentMode { get; set; }

        public SourcingOptions()
        {
            OzonCategoryCandidateIds = new List<long>();
            FeeRules = new List<FeeRule>();
        }
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
        public Dictionary<long, long> OzonAttributeDictionaryValueIds { get; set; }
        public string Keyword { get; set; }
        public decimal Score { get; set; }
        public string Decision { get; set; }
        public string Reason { get; set; }

        public SourceProduct()
        {
            Images = new List<string>();
            Attributes = new Dictionary<string, string>();
            OzonAttributes = new Dictionary<long, string>();
            OzonAttributeDictionaryValueIds = new Dictionary<long, long>();
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
        public List<string> AcceptedOfferIds { get; set; }

        public OzonImportResult()
        {
            AcceptedOfferIds = new List<string>();
        }
    }

    internal sealed class OzonSkuWaitResult
    {
        public bool Success { get; set; }
        public List<string> ReadyOfferIds { get; private set; }
        public string Summary { get; set; }

        public OzonSkuWaitResult()
        {
            ReadyOfferIds = new List<string>();
        }
    }

    internal sealed class ProductAutomationService
    {
        private const int MaxOzonImageCount = 5;
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

                JObject response = JObject.Parse(PostDeepSeekJson(request.ToString(Formatting.None), ResolveDeepSeekApiKey()));
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
            if (categoryAttributes == null || categoryAttributes.Count == 0)
            {
                return new OzonImportResult
                {
                    Success = false,
                    ErrorMessage = "Ozon category/type metadata is unavailable for category_id=" + options.OzonCategoryId +
                        " type_id=" + options.OzonTypeId + ". Please reselect the category and try again."
                };
            }

            for (int i = 0; i < products.Count; i++)
            {
                SourceProduct product = products[i];
                if (product == null)
                {
                    continue;
                }

                WriteLog(log, "[upload] prepare " + (i + 1) + "/" + products.Count + ": " + SafeText(product.OfferId));
                NormalizeProductImages(product);
                if (string.IsNullOrEmpty(product.MainImage))
                {
                    return new OzonImportResult
                    {
                        Success = false,
                        ErrorMessage = "Upload stopped: product " + SafeText(product.OfferId) + " has no valid image. Ozon requires product photos."
                    };
                }

                EnrichProductWithDeepSeek(product, options, categoryAttributes);
                _imageAdapter.AdaptPassedProductImage(product, log);
                try
                {
                    items.Add(BuildOzonImportItem(product, options, categoryAttributes, clientId, apiKey));
                }
                catch (Exception ex)
                {
                    return new OzonImportResult
                    {
                        Success = false,
                        ErrorMessage = "Upload item build failed for offer " + SafeText(product.OfferId) + ": " + ex.Message
                    };
                }
            }

            JObject payload = new JObject();
            payload["items"] = items;
            try
            {
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
            catch (Exception ex)
            {
                return new OzonImportResult
                {
                    Success = false,
                    ErrorMessage = "Ozon rejected the upload before listing creation: " + ex.Message
                };
            }
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

                List<long> candidateIds = new List<long>();
                AddCategoryCandidate(candidateIds, options.OzonCategoryId);
                for (int i = 0; options.OzonCategoryCandidateIds != null && i < options.OzonCategoryCandidateIds.Count; i++)
                {
                    AddCategoryCandidate(candidateIds, options.OzonCategoryCandidateIds[i]);
                }

                for (int i = 0; i < candidateIds.Count; i++)
                {
                    try
                    {
                        JObject payload = new JObject();
                        payload["description_category_id"] = candidateIds[i];
                        payload["type_id"] = options.OzonTypeId;
                        payload["language"] = "RU";
                        JObject root = JObject.Parse(PostOzonJson("/v1/description-category/attribute", payload.ToString(Formatting.None), clientId, apiKey));
                        JArray result = root["result"] as JArray;
                        if (result != null && result.Count > 0)
                        {
                            options.OzonCategoryId = candidateIds[i];
                            return result;
                        }
                    }
                    catch
                    {
                    }
                }

                return new JArray();
            }
            catch
            {
                return new JArray();
            }
        }

        private static void AddCategoryCandidate(List<long> candidates, long categoryId)
        {
            if (categoryId > 0 && !candidates.Contains(categoryId))
            {
                candidates.Add(categoryId);
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
                    result.AcceptedOfferIds = ExtractAcceptedOfferIds(lastResponse);
                    return result;
                }
            }

            result.Success = false;
            result.ImportInfoResponse = lastResponse;
            result.ImportSummary = "Ozon import task is still processing. task_id=" + taskId + Environment.NewLine +
                BuildOzonImportSummary(lastResponse);
            result.AcceptedOfferIds = ExtractAcceptedOfferIds(lastResponse);
            return result;
        }

        public string PrepareRetryForFailedImports(IList<SourceProduct> products, SourcingOptions options, string importInfoResponse, string clientId, string apiKey, out List<SourceProduct> retryProducts)
        {
            retryProducts = new List<SourceProduct>();
            if (products == null || products.Count == 0 || string.IsNullOrEmpty(importInfoResponse))
            {
                return string.Empty;
            }

            JArray categoryAttributes = LoadOzonCategoryAttributes(options, clientId, apiKey);
            if (categoryAttributes == null || categoryAttributes.Count == 0)
            {
                return string.Empty;
            }

            Dictionary<string, SourceProduct> byOfferId = new Dictionary<string, SourceProduct>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < products.Count; i++)
            {
                SourceProduct product = products[i];
                if (product == null)
                {
                    continue;
                }

                string ozonOfferId = BuildOzonOfferId(product);
                if (!string.IsNullOrEmpty(ozonOfferId) && !byOfferId.ContainsKey(ozonOfferId))
                {
                    byOfferId[ozonOfferId] = product;
                }
            }

            try
            {
                JObject root = JObject.Parse(importInfoResponse);
                JToken result = root["result"] ?? root;
                JArray items = FindArray(result, "items", "products", "errors");
                if (items == null || items.Count == 0)
                {
                    return string.Empty;
                }

                StringBuilder builder = new StringBuilder();
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
                    bool failed = HasBlockingImportErrors(errors) ||
                        status.IndexOf("fail", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        status.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0;
                    if (!failed || string.IsNullOrEmpty(offerId) || !byOfferId.ContainsKey(offerId))
                    {
                        continue;
                    }

                    SourceProduct product = byOfferId[offerId];
                    string note;
                    if (TryRepairFailedImportItem(product, options, categoryAttributes, errors, offerId, clientId, apiKey, out note))
                    {
                        retryProducts.Add(product);
                        if (!string.IsNullOrEmpty(note))
                        {
                            builder.AppendLine(offerId + ": " + note);
                        }
                    }
                }

                return builder.ToString().Trim();
            }
            catch
            {
                retryProducts.Clear();
                return string.Empty;
            }
        }

        public string SetOzonStockTo100(IList<string> offerIds, string clientId, string apiKey)
        {
            if (offerIds == null || offerIds.Count == 0)
            {
                return "No imported offer_id values for stock update.";
            }

            string warehouseSummary;
            long warehouseId = ResolveOzonWarehouseId(clientId, apiKey, out warehouseSummary);
            if (warehouseId <= 0)
            {
                return "Stock update skipped: no active Ozon warehouse found. " + warehouseSummary;
            }

            JArray stocks = new JArray();
            Dictionary<string, bool> unique = new Dictionary<string, bool>();
            for (int i = 0; i < offerIds.Count; i++)
            {
                string offerId = offerIds[i];
                if (string.IsNullOrEmpty(offerId) || unique.ContainsKey(offerId))
                {
                    continue;
                }

                unique[offerId] = true;
                JObject stock = new JObject();
                stock["offer_id"] = offerId;
                stock["stock"] = 100;
                stock["warehouse_id"] = warehouseId;
                stocks.Add(stock);
            }

            if (stocks.Count == 0)
            {
                return "Stock update skipped: no valid offer_id values.";
            }

            JObject payload = new JObject();
            payload["stocks"] = stocks;
            string json = payload.ToString(Formatting.None);
            string lastError = string.Empty;
            for (int attempt = 0; attempt < 10; attempt++)
            {
                try
                {
                    if (attempt > 0)
                    {
                        Thread.Sleep(10000);
                    }

                    return PostOzonJson("/v2/products/stocks", json, clientId, apiKey);
                }
                catch (Exception ex)
                {
                    lastError = ex.Message;
                    if (lastError.IndexOf("WAREHOUSE_NOT_FOUND", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        lastError.IndexOf("warehouse", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        break;
                    }
                }
            }

            return "Stock update failed after retries for warehouse " + warehouseId.ToString(CultureInfo.InvariantCulture) +
                ". Ozon may still be creating SKU/processing cards: " + lastError;
        }

        private bool TryRepairFailedImportItem(SourceProduct product, SourcingOptions options, JArray categoryAttributes, JArray errors, string offerId, string clientId, string apiKey, out string note)
        {
            note = string.Empty;
            if (product == null)
            {
                return false;
            }

            string title = !string.IsNullOrEmpty(product.RussianTitle) ? product.RussianTitle : product.Title;
            string description = !string.IsNullOrEmpty(product.RussianDescription)
                ? product.RussianDescription
                : CleanPublicText(product.Title) + "\n" + product.SourceUrl;

            bool changed = false;
            bool physicalAdjusted = false;
            List<string> fixes = new List<string>();
            for (int i = 0; errors != null && i < errors.Count; i++)
            {
                JObject error = errors[i] as JObject;
                if (error == null)
                {
                    continue;
                }

                string code = FirstTokenString(error, "code", "field", "attribute_id");
                string message = FirstTokenString(error, "description", "message", "text");
                long attributeId = FirstTokenLong(error, "attribute_id", "id");
                JObject categoryAttr = FindCategoryAttributeById(categoryAttributes, attributeId);

                if (IsVolumeWeightError(code, message))
                {
                    if (ApplyVolumeWeightRecoveryProfile(product, title))
                    {
                        changed = true;
                        physicalAdjusted = true;
                        AddRepairNote(fixes, "rebalanced dimensions and weight");
                    }
                }

                if (IsMinimumLimitError(code, message))
                {
                    string key = ResolvePhysicalAttributeKey(categoryAttr, message);
                    if (!string.IsNullOrEmpty(key) && RaisePhysicalMinimum(product, key, title))
                    {
                        changed = true;
                        physicalAdjusted = true;
                        AddRepairNote(fixes, "raised minimum " + key);
                    }
                }

                if (IsHazardAttribute(categoryAttr) || IsHazardError(code, message))
                {
                    if (TryPopulateSafeHazardAttribute(product, options, categoryAttributes, categoryAttr, clientId, apiKey))
                    {
                        changed = true;
                        AddRepairNote(fixes, "filled safe hazard class");
                    }
                }
            }

            if (!changed)
            {
                note = string.Empty;
                return false;
            }

            if (physicalAdjusted)
            {
                NormalizePhysicalDimensions(product, title);
                ApplyVolumeWeightRecoveryProfile(product, title);
            }

            EnsureOzonContentAttributes(product, categoryAttributes, offerId, title, description);
            EnsureRequiredOzonAttributes(product, options, categoryAttributes, offerId, title, description, clientId, apiKey);
            PopulateSupportedOzonAttributes(product, options, categoryAttributes, offerId, title, description, clientId, apiKey);
            note = string.Join("; ", fixes.ToArray());
            return true;
        }

        private static void AddRepairNote(IList<string> fixes, string value)
        {
            if (fixes == null || string.IsNullOrEmpty(value) || fixes.Contains(value))
            {
                return;
            }

            fixes.Add(value);
        }

        private static bool IsVolumeWeightError(string code, string message)
        {
            string text = (code ?? string.Empty) + " " + (message ?? string.Empty);
            return ContainsComparable(
                text,
                "ml_incorrect_volume_weight", "incorrect volume weight", "volume weight",
                "\u0433\u0430\u0431\u0430\u0440\u0438\u0442", "\u0432\u0435\u0441", "\u0440\u0430\u0437\u043c\u0435\u0440/\u0432\u0435\u0441",
                "\u5c3a\u5bf8", "\u91cd\u91cf", "\u4f53\u79ef");
        }

        private static bool IsMinimumLimitError(string code, string message)
        {
            string text = (code ?? string.Empty) + " " + (message ?? string.Empty);
            return ContainsComparable(
                text,
                "value_min_limit", "less than allowed", "below minimum",
                "\u043c\u0435\u043d\u044c\u0448\u0435 \u0434\u043e\u043f\u0443\u0441\u0442\u0438\u043c", "\u043c\u0438\u043d\u0438\u043c",
                "\u5c0f\u4e8e\u5141\u8bb8", "\u5c0f\u4e8e\u6700\u5c0f", "\u6700\u5c0f\u503c");
        }

        private static bool IsHazardError(string code, string message)
        {
            string text = (code ?? string.Empty) + " " + (message ?? string.Empty);
            return ContainsComparable(
                text,
                "hazard", "hazardous", "danger class", "dangerous goods",
                "\u5371\u9669", "\u5371\u9669\u7b49\u7ea7", "\u5371\u9669\u54c1",
                "\u043e\u043f\u0430\u0441\u043d", "\u043a\u043b\u0430\u0441\u0441 \u043e\u043f\u0430\u0441\u043d", "\u043e\u043f\u0430\u0441\u043d\u044b\u0439 \u0433\u0440\u0443\u0437");
        }

        private static string ResolvePhysicalAttributeKey(JObject categoryAttr, string message)
        {
            string text = FirstTokenString(categoryAttr, "name", "attribute_name") + " " + (message ?? string.Empty);
            if (ContainsComparable(text, "weight", "\u91cd\u91cf", "\u6bdb\u91cd", "\u51c0\u91cd", "\u0432\u0435\u0441"))
            {
                return "weight_g";
            }

            if (ContainsComparable(text, "width", "\u5bbd\u5ea6", "\u5bbd", "\u0448\u0438\u0440\u0438\u043d"))
            {
                return "width_mm";
            }

            if (ContainsComparable(text, "height", "\u9ad8\u5ea6", "\u9ad8", "\u0432\u044b\u0441\u043e\u0442"))
            {
                return "height_mm";
            }

            if (ContainsComparable(text, "depth", "length", "diameter", "\u6df1\u5ea6", "\u539a\u5ea6", "\u957f\u5ea6", "\u76f4\u5f84", "\u0434\u043b\u0438\u043d", "\u0433\u043b\u0443\u0431\u0438\u043d", "\u0434\u0438\u0430\u043c"))
            {
                return "depth_mm";
            }

            return string.Empty;
        }

        private static bool RaisePhysicalMinimum(SourceProduct product, string key, string title)
        {
            int current = ResolvePositiveIntAttribute(product, key, 0);
            int target = DeterminePhysicalMinimum(key, title);
            if (target <= 0 || current >= target)
            {
                return false;
            }

            SetPhysicalAttribute(product, key, target);
            return true;
        }

        private static int DeterminePhysicalMinimum(string key, string title)
        {
            string text = (title ?? string.Empty).ToLowerInvariant();
            if (key == "weight_g")
            {
                if (ContainsAny(text, "spray", "bottle", "cleaner", "\u6e05\u6d01", "\u6d17\u6da4", "\u6d88\u6bd2", "\u74f6", "\u0441\u0440\u0435\u0434\u0441\u0442\u0432", "\u043e\u0447\u0438\u0441\u0442"))
                {
                    return 150;
                }

                return 50;
            }

            if (ContainsAny(text, "plug", "cap", "stopper", "\u5b54\u585e", "\u585e\u5b50", "\u76d6", "\u0437\u0430\u0433\u043b\u0443\u0448", "\u043f\u0440\u043e\u0431\u043a"))
            {
                return 12;
            }

            if (ContainsAny(text, "spray", "bottle", "cleaner", "\u6e05\u6d01", "\u6d17\u6da4", "\u6d88\u6bd2", "\u74f6", "\u0441\u0440\u0435\u0434\u0441\u0442\u0432", "\u043e\u0447\u0438\u0441\u0442"))
            {
                return 40;
            }

            return 20;
        }

        private static bool ApplyVolumeWeightRecoveryProfile(SourceProduct product, string title)
        {
            if (product == null)
            {
                return false;
            }

            bool changed = false;
            int height = ResolvePositiveIntAttribute(product, "height_mm", 0);
            int width = ResolvePositiveIntAttribute(product, "width_mm", 0);
            int depth = ResolvePositiveIntAttribute(product, "depth_mm", 0);
            int weight = ResolvePositiveIntAttribute(product, "weight_g", 0);

            int minDimension = DeterminePhysicalMinimum("height_mm", title);
            if (height < minDimension)
            {
                SetPhysicalAttribute(product, "height_mm", minDimension);
                height = minDimension;
                changed = true;
            }

            if (width < minDimension)
            {
                SetPhysicalAttribute(product, "width_mm", minDimension);
                width = minDimension;
                changed = true;
            }

            if (depth < minDimension)
            {
                SetPhysicalAttribute(product, "depth_mm", minDimension);
                depth = minDimension;
                changed = true;
            }

            int longest = Math.Max(height, Math.Max(width, depth));
            if (longest < 60)
            {
                if (height >= width && height >= depth)
                {
                    SetPhysicalAttribute(product, "height_mm", 60);
                }
                else if (width >= depth)
                {
                    SetPhysicalAttribute(product, "width_mm", 60);
                }
                else
                {
                    SetPhysicalAttribute(product, "depth_mm", 60);
                }

                changed = true;
            }

            int minWeight = DeterminePhysicalMinimum("weight_g", title);
            decimal volumeMl = ExtractVolumeMl(BuildPhysicalSourceText(product, title));
            if (volumeMl > 0m)
            {
                minWeight = Math.Max(minWeight, (int)Math.Ceiling(volumeMl * 0.85m));
            }

            if (weight < minWeight)
            {
                SetPhysicalAttribute(product, "weight_g", minWeight);
                changed = true;
            }

            if (changed)
            {
                ApplyDensityGuardrail(product);
            }

            return changed;
        }

        private bool TryPopulateSafeHazardAttribute(SourceProduct product, SourcingOptions options, JArray categoryAttributes, JObject categoryAttr, string clientId, string apiKey)
        {
            if (product == null || categoryAttributes == null)
            {
                return false;
            }

            if (categoryAttr != null && IsHazardAttribute(categoryAttr))
            {
                return TrySetHazardAttributeValue(product, options, categoryAttr, clientId, apiKey);
            }

            for (int i = 0; i < categoryAttributes.Count; i++)
            {
                JObject attr = categoryAttributes[i] as JObject;
                if (attr == null || !IsHazardAttribute(attr))
                {
                    continue;
                }

                if (TrySetHazardAttributeValue(product, options, attr, clientId, apiKey))
                {
                    return true;
                }
            }

            return false;
        }

        private bool TrySetHazardAttributeValue(SourceProduct product, SourcingOptions options, JObject attr, string clientId, string apiKey)
        {
            long id = FirstTokenLong(attr, "id", "attribute_id");
            if (id <= 0)
            {
                return false;
            }

            string current = product.OzonAttributes.ContainsKey(id) ? product.OzonAttributes[id] : string.Empty;
            long currentDictionaryId = product.OzonAttributeDictionaryValueIds.ContainsKey(id) ? product.OzonAttributeDictionaryValueIds[id] : 0;
            long dictionaryValueId = 0;
            string value = HasDictionaryValues(attr)
                ? ResolveDictionaryAttributeValue(options, attr, id, ResolveSafeHazardText(), true, clientId, apiKey, out dictionaryValueId)
                : ResolveSafeHazardText();
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            bool changed = !string.Equals(current, value, StringComparison.OrdinalIgnoreCase);
            if (!changed && dictionaryValueId > 0 && currentDictionaryId != dictionaryValueId)
            {
                changed = true;
            }

            if (!changed)
            {
                return false;
            }

            product.OzonAttributes[id] = value;
            if (dictionaryValueId > 0)
            {
                product.OzonAttributeDictionaryValueIds[id] = dictionaryValueId;
            }

            return true;
        }

        private static long ResolveOzonWarehouseId(string clientId, string apiKey, out string summary)
        {
            summary = string.Empty;
            string cursor = string.Empty;
            for (int page = 0; page < 10; page++)
            {
                JObject payload = new JObject();
                payload["limit"] = 200;
                if (!string.IsNullOrEmpty(cursor))
                {
                    payload["cursor"] = cursor;
                }

                JObject root = JObject.Parse(PostOzonJson("/v2/warehouse/list", payload.ToString(Formatting.None), clientId, apiKey));
                JArray warehouses = root["warehouses"] as JArray;
                long warehouseId = PickPreferredWarehouseId(warehouses, out summary);
                if (warehouseId > 0)
                {
                    return warehouseId;
                }

                bool hasNext = false;
                JToken hasNextToken = root["has_next"];
                if (hasNextToken != null && hasNextToken.Type != JTokenType.Null)
                {
                    bool.TryParse(Convert.ToString(hasNextToken, CultureInfo.InvariantCulture), out hasNext);
                }

                cursor = Convert.ToString(root["cursor"] ?? string.Empty, CultureInfo.InvariantCulture);
                if (!hasNext || string.IsNullOrEmpty(cursor))
                {
                    break;
                }
            }

            if (string.IsNullOrEmpty(summary))
            {
                summary = "Ozon returned no active warehouses from /v2/warehouse/list.";
            }

            return 0;
        }

        private static long PickPreferredWarehouseId(JArray warehouses, out string summary)
        {
            summary = string.Empty;
            long bestId = 0;
            int bestScore = int.MinValue;
            for (int i = 0; warehouses != null && i < warehouses.Count; i++)
            {
                JObject warehouse = warehouses[i] as JObject;
                if (warehouse == null)
                {
                    continue;
                }

                long warehouseId = FirstTokenLong(warehouse, "warehouse_id", "id");
                if (warehouseId <= 0)
                {
                    continue;
                }

                string status = FirstTokenString(warehouse, "status");
                string name = FirstTokenString(warehouse, "name");
                string type = FirstTokenString(warehouse, "warehouse_type");
                bool isRfbs = false;
                JToken isRfbsToken = warehouse["is_rfbs"];
                if (isRfbsToken != null && isRfbsToken.Type != JTokenType.Null)
                {
                    bool.TryParse(Convert.ToString(isRfbsToken, CultureInfo.InvariantCulture), out isRfbs);
                }

                bool paused = warehouse["pause_at"] != null && warehouse["pause_at"].Type != JTokenType.Null;
                int score = 0;
                if (string.Equals(status, "created", StringComparison.OrdinalIgnoreCase))
                {
                    score += 100;
                }

                if (!paused)
                {
                    score += 20;
                }

                if (isRfbs || string.Equals(type, "rfbs", StringComparison.OrdinalIgnoreCase))
                {
                    score += 10;
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    bestId = warehouseId;
                    summary = "warehouse_id=" + warehouseId.ToString(CultureInfo.InvariantCulture) +
                        ", name=" + name +
                        ", type=" + type +
                        ", status=" + status;
                }
            }

            return bestId;
        }

        public OzonSkuWaitResult WaitForOzonSkuCreation(IList<string> offerIds, string clientId, string apiKey, int attempts, int delayMs)
        {
            OzonSkuWaitResult result = new OzonSkuWaitResult();
            if (offerIds == null || offerIds.Count == 0)
            {
                result.Success = false;
                result.Summary = "SKU wait skipped: no accepted offer_id values.";
                return result;
            }

            int maxAttempts = attempts <= 0 ? 20 : attempts;
            int wait = delayMs <= 0 ? 30000 : delayMs;
            StringBuilder summary = new StringBuilder();
            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                if (attempt > 0)
                {
                    Thread.Sleep(wait);
                }

                Dictionary<string, string> skuByOffer = GetOzonSkuMap(offerIds, clientId, apiKey);
                result.ReadyOfferIds.Clear();
                for (int i = 0; i < offerIds.Count; i++)
                {
                    string offerId = offerIds[i];
                    if (skuByOffer.ContainsKey(offerId) && IsCreatedSkuValue(skuByOffer[offerId]))
                    {
                        result.ReadyOfferIds.Add(offerId);
                    }
                }

                summary.AppendLine("SKU check " + (attempt + 1) + "/" + maxAttempts + ": " + result.ReadyOfferIds.Count + "/" + offerIds.Count + " ready.");
                foreach (KeyValuePair<string, string> pair in skuByOffer)
                {
                    summary.AppendLine("  " + pair.Key + " -> " + (string.IsNullOrEmpty(pair.Value) ? "SKU pending" : "SKU " + pair.Value));
                }

                if (result.ReadyOfferIds.Count >= offerIds.Count)
                {
                    result.Success = true;
                    result.Summary = summary.ToString().Trim();
                    return result;
                }
            }

            result.Success = result.ReadyOfferIds.Count > 0;
            result.Summary = summary.ToString().Trim();
            return result;
        }

        public Dictionary<string, string> GetOzonSkuMap(IList<string> offerIds, string clientId, string apiKey)
        {
            Dictionary<string, string> map = new Dictionary<string, string>();
            JArray offerArray = new JArray();
            Dictionary<string, bool> unique = new Dictionary<string, bool>();
            for (int i = 0; offerIds != null && i < offerIds.Count; i++)
            {
                string offerId = offerIds[i];
                if (!string.IsNullOrEmpty(offerId) && !unique.ContainsKey(offerId))
                {
                    unique[offerId] = true;
                    map[offerId] = string.Empty;
                    offerArray.Add(offerId);
                }
            }

            if (offerArray.Count == 0)
            {
                return map;
            }

            JObject payload = new JObject();
            payload["offer_id"] = offerArray;

            TryPopulateSkuMapFromInfoList(map, payload, "/v3/product/info/list", clientId, apiKey);
            if (!AllOfferIdsReady(map))
            {
                TryPopulateSkuMapFromInfoList(map, payload, "/v2/product/info/list", clientId, apiKey);
            }

            if (!AllOfferIdsReady(map))
            {
                TryPopulateSkuMapFromProductList(map, offerArray, clientId, apiKey);
            }

            return map;
        }

        private void TryPopulateSkuMapFromInfoList(Dictionary<string, string> map, JObject payload, string path, string clientId, string apiKey)
        {
            try
            {
                JObject root = JObject.Parse(PostOzonJson(path, payload.ToString(Formatting.None), clientId, apiKey));
                JArray items = FindArray(root["result"] ?? root, "items", "products");
                if (items == null)
                {
                    items = root.SelectToken("result.items") as JArray;
                }

                if (items == null)
                {
                    items = root["items"] as JArray;
                }

                PopulateSkuMapFromItems(map, items);
            }
            catch
            {
            }
        }

        private void TryPopulateSkuMapFromProductList(Dictionary<string, string> map, JArray offerArray, string clientId, string apiKey)
        {
            try
            {
                JObject payload = new JObject();
                JObject filter = new JObject();
                filter["offer_id"] = offerArray;
                payload["filter"] = filter;
                payload["last_id"] = string.Empty;
                payload["limit"] = Math.Max(offerArray.Count, 100);

                JObject root = JObject.Parse(PostOzonJson("/v3/product/list", payload.ToString(Formatting.None), clientId, apiKey));
                JArray items = FindArray(root["result"] ?? root, "items", "products");
                if (items == null)
                {
                    items = root.SelectToken("result.items") as JArray;
                }

                PopulateSkuMapFromItems(map, items);
            }
            catch
            {
            }
        }

        private void PopulateSkuMapFromItems(Dictionary<string, string> map, JArray items)
        {
            for (int i = 0; items != null && i < items.Count; i++)
            {
                JObject item = items[i] as JObject;
                if (item == null)
                {
                    continue;
                }

                string offerId = FirstTokenString(item, "offer_id", "offerId");
                string sku = FirstTokenString(item, "sku", "fbo_sku", "fbs_sku");
                if (!IsCreatedSkuValue(sku))
                {
                    sku = FirstTokenString(
                        item.SelectToken("sources[0]"),
                        "sku",
                        "sku_id",
                        "id");
                }

                if (!IsCreatedSkuValue(sku))
                {
                    sku = Convert.ToString(
                        item.SelectToken("sku_info.sku") ??
                        item.SelectToken("skuInfo.sku") ??
                        item.SelectToken("source.sku") ??
                        string.Empty);
                }

                if (!string.IsNullOrEmpty(offerId) && map.ContainsKey(offerId) && string.IsNullOrEmpty(map[offerId]))
                {
                    map[offerId] = IsCreatedSkuValue(sku) ? sku.Trim() : string.Empty;
                }
            }
        }

        private static bool AllOfferIdsReady(Dictionary<string, string> map)
        {
            foreach (KeyValuePair<string, string> pair in map)
            {
                if (!IsCreatedSkuValue(pair.Value))
                {
                    return false;
                }
            }

            return map.Count > 0;
        }

        private static bool IsCreatedSkuValue(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            long sku;
            return long.TryParse(value.Trim(), out sku) && sku > 0;
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
                    bool hasBlockingErrors = HasBlockingImportErrors(errors);
                    bool hasWarnings = HasWarningImportErrors(errors);
                    if (hasBlockingErrors ||
                        status.IndexOf("fail", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        status.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        failed += 1;
                        builder.AppendLine("failed: " + SafeText(offerId) + " " + SafeText(status));
                        AppendImportMessages(builder, errors, 8, false);
                    }
                    else
                    {
                        ok += 1;
                        builder.AppendLine((hasWarnings ? "accepted_with_warning: " : "accepted: ") + SafeText(offerId) + " " + SafeText(status));
                        if (hasWarnings)
                        {
                            AppendImportMessages(builder, errors, 8, true);
                        }
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

        private static List<string> ExtractAcceptedOfferIds(string response)
        {
            List<string> offerIds = new List<string>();
            if (string.IsNullOrEmpty(response))
            {
                return offerIds;
            }

            try
            {
                JObject root = JObject.Parse(response);
                JToken result = root["result"] ?? root;
                JArray items = FindArray(result, "items", "products", "errors");
                for (int i = 0; items != null && i < items.Count; i++)
                {
                    JObject item = items[i] as JObject;
                    if (item == null)
                    {
                        continue;
                    }

                    string offerId = FirstTokenString(item, "offer_id", "offerId", "article");
                    string status = FirstTokenString(item, "status", "state");
                    JArray errors = FindArray(item, "errors", "error");
                    bool failed = HasBlockingImportErrors(errors) ||
                        status.IndexOf("fail", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        status.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0;
                    if (!failed && !string.IsNullOrEmpty(offerId) && !offerIds.Contains(offerId))
                    {
                        offerIds.Add(offerId);
                    }
                }
            }
            catch
            {
            }

            return offerIds;
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

        private static bool HasBlockingImportErrors(JArray errors)
        {
            for (int i = 0; errors != null && i < errors.Count; i++)
            {
                JObject error = errors[i] as JObject;
                if (error == null)
                {
                    continue;
                }

                string level = FirstTokenString(error, "level", "severity", "state");
                if (string.IsNullOrEmpty(level) ||
                    (level.IndexOf("warning", StringComparison.OrdinalIgnoreCase) < 0 &&
                    level.IndexOf("info", StringComparison.OrdinalIgnoreCase) < 0))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasWarningImportErrors(JArray errors)
        {
            for (int i = 0; errors != null && i < errors.Count; i++)
            {
                JObject error = errors[i] as JObject;
                if (error == null)
                {
                    continue;
                }

                string level = FirstTokenString(error, "level", "severity");
                if (level.IndexOf("warning", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static void AppendImportMessages(StringBuilder builder, JArray errors, int maxCount, bool warningsOnly)
        {
            int appended = 0;
            for (int i = 0; errors != null && i < errors.Count && appended < maxCount; i++)
            {
                JObject error = errors[i] as JObject;
                if (error == null)
                {
                    continue;
                }

                string level = FirstTokenString(error, "level", "severity");
                bool isWarning = level.IndexOf("warning", StringComparison.OrdinalIgnoreCase) >= 0;
                if (warningsOnly && !isWarning)
                {
                    continue;
                }

                string code = FirstTokenString(error, "code", "field", "attribute_id");
                string message = FirstTokenString(error, "description", "message", "text");
                string attributeId = FirstTokenString(error, "attribute_id", "id");
                string value = FirstTokenString(error, "value", "field_value", "attribute_value");
                if (!string.IsNullOrEmpty(attributeId))
                {
                    code = string.IsNullOrEmpty(code) ? "attribute_id=" + attributeId : code + " / attribute_id=" + attributeId;
                }

                if (!string.IsNullOrEmpty(value))
                {
                    message = string.IsNullOrEmpty(message) ? ("value=" + value) : message + " [value=" + value + "]";
                }

                string prefix = isWarning ? "warning" : "error";
                if (!string.IsNullOrEmpty(code) || !string.IsNullOrEmpty(message))
                {
                    builder.AppendLine("  " + prefix + ": " + SafeText(code) + (string.IsNullOrEmpty(message) ? string.Empty : " - " + SafeText(message)));
                    appended++;
                }
            }
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

        private static long FirstTokenLong(JToken token, params string[] names)
        {
            string value = FirstTokenString(token, names);
            long number;
            return long.TryParse(value, out number) ? number : 0;
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

        private static void AppendErrorMessages(StringBuilder builder, JArray errors, int maxMessages)
        {
            if (builder == null || errors == null)
            {
                return;
            }

            Dictionary<string, int> seen = new Dictionary<string, int>();
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
                string attributeId = FirstTokenString(error, "attribute_id", "id");
                string value = FirstTokenString(error, "value", "field_value", "attribute_value");
                string path = FirstTokenString(error, "path", "parameter", "field");
                if (!string.IsNullOrEmpty(attributeId))
                {
                    code = string.IsNullOrEmpty(code) ? "attribute_id=" + attributeId : code + " / attribute_id=" + attributeId;
                }

                if (!string.IsNullOrEmpty(path))
                {
                    message = string.IsNullOrEmpty(message) ? "path=" + path : message + " [path=" + path + "]";
                }

                if (!string.IsNullOrEmpty(value))
                {
                    message = string.IsNullOrEmpty(message) ? "value=" + value : message + " [value=" + value + "]";
                }

                string key = (string.IsNullOrEmpty(code) ? string.Empty : code + " - ") + SafeText(message);
                if (!seen.ContainsKey(key))
                {
                    seen[key] = 0;
                }

                seen[key] += 1;
            }

            int written = 0;
            foreach (KeyValuePair<string, int> pair in seen)
            {
                if (written >= maxMessages)
                {
                    break;
                }

                builder.AppendLine("  error: " + pair.Key + (pair.Value > 1 ? " (x" + pair.Value + ")" : string.Empty));
                written += 1;
            }

            if (seen.Count > written)
            {
                builder.AppendLine("  ... " + (seen.Count - written) + " more unique errors hidden");
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

            string deepSeekApiKey = ResolveDeepSeekApiKey();
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
                    new JProperty("content", "You generate Ozon marketplace listings. Return strict JSON only. Russian title and description are mandatory. Search keyword must be precise English. Fill Ozon required attributes when a text value can be inferred; do not invent safety certifications. Never mention delivery, shipping speed, payment, warranty, returns, exchange, seller promises, contact info, or marketplace service terms in the description.")));
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
                product.RussianDescription = SanitizeOzonDescription(Convert.ToString(listing["description_ru"] ?? string.Empty), product.RussianTitle, product);

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

        private static string ResolveDeepSeekApiKey()
        {
            string value = Environment.GetEnvironmentVariable(DeepSeekApiKeyEnvVar);
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }

        private static void AddDimensionAttribute(SourceProduct product, string name, string value)
        {
            if (product == null || string.IsNullOrEmpty(value))
            {
                return;
            }

            product.Attributes[name] = value;
        }

        private static void NormalizePhysicalDimensions(SourceProduct product, string title)
        {
            if (product == null)
            {
                return;
            }

            string text = BuildPhysicalSourceText(product, title);
            int widthMm;
            int heightMm;
            int depthMm;
            if (TryExtractDimensionTriplet(text, out widthMm, out heightMm, out depthMm))
            {
                SetEstimatedDimension(product, "width_mm", widthMm, true);
                SetEstimatedDimension(product, "height_mm", heightMm, true);
                SetEstimatedDimension(product, "depth_mm", depthMm, true);
            }

            decimal explicitWeightGrams = ExtractWeightGrams(text);
            if (explicitWeightGrams > 0m)
            {
                SetEstimatedDimension(product, "weight_g", (int)Math.Ceiling(explicitWeightGrams), true);
            }

            ApplyKeywordPhysicalProfile(product, text);
            ApplyWeightBasedPhysicalProfile(product, text);
            ApplyGenericPhysicalProfile(product, text);
            ApplyDensityGuardrail(product);
        }

        private static string BuildPhysicalSourceText(SourceProduct product, string title)
        {
            StringBuilder builder = new StringBuilder();
            builder.Append(title ?? string.Empty).Append(' ')
                .Append(product == null ? string.Empty : product.Title ?? string.Empty).Append(' ')
                .Append(product == null ? string.Empty : product.Keyword ?? string.Empty);

            if (product != null && product.Attributes != null)
            {
                foreach (KeyValuePair<string, string> pair in product.Attributes)
                {
                    builder.Append(' ').Append(pair.Key).Append(' ').Append(pair.Value);
                }
            }

            return builder.ToString().ToLowerInvariant();
        }

        private static void ApplyKeywordPhysicalProfile(SourceProduct product, string text)
        {
            if (TryApplyTapePhysicalProfile(product, text))
            {
                return;
            }

            if (ContainsAny(text, "table", "desk", "\u684c", "\u8336\u51e0", "\u5496\u5561\u684c", "\u8fb9\u51e0", "\u0441\u0442\u043e\u043b"))
            {
                SetEstimatedDimension(product, "height_mm", 450, true);
                SetEstimatedDimension(product, "width_mm", 600, true);
                SetEstimatedDimension(product, "depth_mm", 600, true);
                SetEstimatedDimension(product, "weight_g", 8000, true);
                return;
            }

            if (ContainsAny(text, "chair", "\u6905", "\u51f3", "\u0441\u0442\u0443\u043b"))
            {
                SetEstimatedDimension(product, "height_mm", 800, true);
                SetEstimatedDimension(product, "width_mm", 450, true);
                SetEstimatedDimension(product, "depth_mm", 450, true);
                SetEstimatedDimension(product, "weight_g", 5000, true);
                return;
            }

            if (ContainsAny(text, "thread", "yarn", "spool", "\u7f1d\u7eab", "\u7ebf", "\u7eb1", "\u7f1d\u7eab\u7ebf", "\u7ebd\u7ebf", "\u043d\u0438\u0442", "\u043f\u0440\u044f\u0436"))
            {
                SetEstimatedDimension(product, "height_mm", 70, true);
                SetEstimatedDimension(product, "width_mm", 70, true);
                SetEstimatedDimension(product, "depth_mm", 40, true);
                SetEstimatedDimension(product, "weight_g", 80, true);
                return;
            }

            if (ContainsAny(text, "spray", "aerosol", "bottle", "can", "\u55b7\u96fe", "\u55b7\u5242", "\u6c14\u96fe\u5242", "\u74f6", "\u7f50", "\u0441\u043f\u0440\u0435\u0439", "\u0430\u044d\u0440\u043e\u0437\u043e\u043b", "\u0444\u043b\u0430\u043a\u043e\u043d"))
            {
                decimal volumeMl = ExtractVolumeMl(text);
                if (volumeMl >= 550m)
                {
                    SetEstimatedDimension(product, "height_mm", 300, true);
                    SetEstimatedDimension(product, "width_mm", 65, true);
                    SetEstimatedDimension(product, "depth_mm", 65, true);
                    SetEstimatedDimension(product, "weight_g", (int)Math.Ceiling(volumeMl * 1.15m), true);
                }
                else if (volumeMl >= 250m)
                {
                    SetEstimatedDimension(product, "height_mm", 220, true);
                    SetEstimatedDimension(product, "width_mm", 55, true);
                    SetEstimatedDimension(product, "depth_mm", 55, true);
                    SetEstimatedDimension(product, "weight_g", (int)Math.Ceiling(volumeMl * 1.10m), true);
                }
                else
                {
                    SetEstimatedDimension(product, "height_mm", 180, true);
                    SetEstimatedDimension(product, "width_mm", 50, true);
                    SetEstimatedDimension(product, "depth_mm", 50, true);
                }

                return;
            }

            if (ContainsAny(text, "adapter", "\u0430\u0434\u0430\u043f\u0442\u0435\u0440", "\u043d\u0430\u0441\u0430\u0434", "\u914d\u4ef6", "\u63a5\u5934", "\u9002\u914d\u5668"))
            {
                SetEstimatedDimension(product, "height_mm", 50, true);
                SetEstimatedDimension(product, "width_mm", 120, true);
                SetEstimatedDimension(product, "depth_mm", 80, true);
                SetEstimatedDimension(product, "weight_g", 200, true);
                return;
            }

            if (ContainsAny(text, "lamp", "light", "\u0444\u043e\u043d\u0430\u0440", "\u0441\u0432\u0435\u0442", "\u706f"))
            {
                SetEstimatedDimension(product, "height_mm", 180, true);
                SetEstimatedDimension(product, "width_mm", 80, true);
                SetEstimatedDimension(product, "depth_mm", 80, true);
                SetEstimatedDimension(product, "weight_g", 300, true);
            }
        }

        private static bool TryApplyTapePhysicalProfile(SourceProduct product, string text)
        {
            if (!ContainsAny(text, "tape", "adhesive", "duct tape", "masking tape", "washi", "ribbon", "\u80f6\u5e26", "\u80f6\u5e03", "\u7f8e\u7eb9", "\u7f20\u7ed5", "\u80f6\u6761", "\u043b\u0435\u043d\u0442\u0430", "\u0441\u043a\u043e\u0442\u0447"))
            {
                return false;
            }

            decimal lengthMm = ExtractLargestMetricLengthMm(text);
            decimal widthMm = ExtractTapeWidthMm(text);
            decimal thicknessMm = ExtractTapeThicknessMm(text);

            int rollDiameter = 80;
            int rollWidth = 25;
            if (lengthMm >= 5000m)
            {
                rollDiameter = 90;
                rollWidth = 30;
            }

            if (lengthMm >= 10000m || widthMm >= 40m)
            {
                rollDiameter = 105;
                rollWidth = 45;
            }
            else if (widthMm >= 20m)
            {
                rollWidth = 35;
            }

            if (thicknessMm >= 1m)
            {
                rollWidth = Math.Max(rollWidth, 35);
            }

            SetEstimatedDimension(product, "height_mm", rollWidth, true);
            SetEstimatedDimension(product, "width_mm", rollDiameter, true);
            SetEstimatedDimension(product, "depth_mm", rollDiameter, true);

            int estimatedWeight = EstimateTapeWeightGrams(lengthMm, widthMm, thicknessMm);
            if (estimatedWeight > 0)
            {
                SetEstimatedDimension(product, "weight_g", estimatedWeight, true);
            }
            else
            {
                SetEstimatedDimension(product, "weight_g", 40, true);
            }

            return true;
        }

        private static void ApplyWeightBasedPhysicalProfile(SourceProduct product, string text)
        {
            int weight = ResolvePositiveIntAttribute(product, "weight_g", 0);
            if (weight <= 0)
            {
                return;
            }

            if (weight >= 10000)
            {
                SetEstimatedDimension(product, "height_mm", 100, true);
                SetEstimatedDimension(product, "width_mm", 350, true);
                SetEstimatedDimension(product, "depth_mm", 500, true);
                return;
            }

            if (weight >= 3000)
            {
                SetEstimatedDimension(product, "height_mm", 120, true);
                SetEstimatedDimension(product, "width_mm", 250, true);
                SetEstimatedDimension(product, "depth_mm", 300, true);
                return;
            }

            if (weight >= 1000)
            {
                SetEstimatedDimension(product, "height_mm", 80, true);
                SetEstimatedDimension(product, "width_mm", 180, true);
                SetEstimatedDimension(product, "depth_mm", 220, true);
                return;
            }

            if (weight >= 300)
            {
                SetEstimatedDimension(product, "height_mm", 60, true);
                SetEstimatedDimension(product, "width_mm", 100, true);
                SetEstimatedDimension(product, "depth_mm", 160, true);
                return;
            }

            if (weight >= 80)
            {
                SetEstimatedDimension(product, "height_mm", 40, true);
                SetEstimatedDimension(product, "width_mm", 80, true);
                SetEstimatedDimension(product, "depth_mm", 120, true);
                return;
            }

            SetEstimatedDimension(product, "height_mm", 30, true);
            SetEstimatedDimension(product, "width_mm", 60, true);
            SetEstimatedDimension(product, "depth_mm", 90, true);
        }

        private static void ApplyGenericPhysicalProfile(SourceProduct product, string text)
        {
            SetEstimatedDimension(product, "height_mm", 40, true);
            SetEstimatedDimension(product, "width_mm", 80, true);
            SetEstimatedDimension(product, "depth_mm", 120, true);
            SetEstimatedDimension(product, "weight_g", 120, true);
        }

        private static void ApplyDensityGuardrail(SourceProduct product)
        {
            int height = ResolvePositiveIntAttribute(product, "height_mm", 0);
            int width = ResolvePositiveIntAttribute(product, "width_mm", 0);
            int depth = ResolvePositiveIntAttribute(product, "depth_mm", 0);
            int weight = ResolvePositiveIntAttribute(product, "weight_g", 0);
            if (height <= 0 || width <= 0 || depth <= 0 || weight <= 0)
            {
                return;
            }

            decimal volumeCm3 = (decimal)height * width * depth / 1000m;
            if (volumeCm3 <= 0m)
            {
                return;
            }

            decimal density = weight / volumeCm3;
            const decimal maxSafeDensity = 6.0m;
            if (density <= maxSafeDensity)
            {
                return;
            }

            decimal targetVolumeMm3 = (weight / maxSafeDensity) * 1000m;
            decimal currentVolumeMm3 = (decimal)height * width * depth;
            if (targetVolumeMm3 <= currentVolumeMm3)
            {
                return;
            }

            double scale = Math.Pow((double)(targetVolumeMm3 / currentVolumeMm3), 1d / 3d) * 1.08d;
            SetPhysicalAttribute(product, "height_mm", Math.Max(12, (int)Math.Ceiling(height * (decimal)scale)));
            SetPhysicalAttribute(product, "width_mm", Math.Max(12, (int)Math.Ceiling(width * (decimal)scale)));
            SetPhysicalAttribute(product, "depth_mm", Math.Max(12, (int)Math.Ceiling(depth * (decimal)scale)));
        }

        private static void SetEstimatedDimension(SourceProduct product, string key, int value, bool overwritePlaceholder)
        {
            if (product == null || value <= 0)
            {
                return;
            }

            int current = ResolvePositiveIntAttribute(product, key, 0);
            if (current <= 0 || IsSuspiciousPhysicalValue(key, current) || (overwritePlaceholder && IsPhysicalPlaceholder(key, current)))
            {
                SetPhysicalAttribute(product, key, value);
            }
        }

        private static void SetPhysicalAttribute(SourceProduct product, string key, int value)
        {
            if (product == null || string.IsNullOrEmpty(key) || value <= 0)
            {
                return;
            }

            product.Attributes[key] = value.ToString(CultureInfo.InvariantCulture);
        }

        private static bool IsPhysicalPlaceholder(string key, int value)
        {
            if (value <= 0)
            {
                return true;
            }

            if (key == "weight_g")
            {
                return value == 500 || value == 300 || value == 200 || value == 120;
            }

            return value == 100 || value == 120 || value == 80 || value == 60 || value == 40 || value == 30;
        }

        private static bool IsSuspiciousPhysicalValue(string key, int value)
        {
            if (value <= 0)
            {
                return true;
            }

            if (key == "weight_g")
            {
                return value < 5 || value > 50000;
            }

            return value < 8 || value > 2000;
        }

        private static bool TryExtractDimensionTriplet(string text, out int widthMm, out int heightMm, out int depthMm)
        {
            widthMm = 0;
            heightMm = 0;
            depthMm = 0;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            Match match = Regex.Match(text, @"(\d+(?:[.,]\d+)?)\s*[x×*]\s*(\d+(?:[.,]\d+)?)\s*[x×*]\s*(\d+(?:[.,]\d+)?)(?:\s*(mm|cm|м|m|\u6beb\u7c73|\u5398\u7c73))?", RegexOptions.IgnoreCase);
            if (!match.Success)
            {
                return false;
            }

            decimal a = ParseDecimal(match.Groups[1].Value);
            decimal b = ParseDecimal(match.Groups[2].Value);
            decimal c = ParseDecimal(match.Groups[3].Value);
            string unit = match.Groups[4].Value ?? string.Empty;
            decimal multiplier = 1m;
            if (IsCentimeterUnit(unit))
            {
                multiplier = 10m;
            }
            else if (IsMeterUnit(unit))
            {
                multiplier = 1000m;
            }

            widthMm = (int)Math.Ceiling(a * multiplier);
            heightMm = (int)Math.Ceiling(b * multiplier);
            depthMm = (int)Math.Ceiling(c * multiplier);
            return widthMm > 0 && heightMm > 0 && depthMm > 0;
        }

        private static decimal ExtractWeightGrams(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return 0m;
            }

            MatchCollection matches = Regex.Matches(text, @"(\d+(?:[.,]\d+)?)\s*(kg|g|gram|grams|\u516c\u65a4|\u5343\u514b|\u514b|\u043a\u0433|\u0433\u0440|\u0433)(?![a-z])", RegexOptions.IgnoreCase);
            decimal best = 0m;
            for (int i = 0; i < matches.Count; i++)
            {
                decimal value = ParseDecimal(matches[i].Groups[1].Value);
                string unit = matches[i].Groups[2].Value.ToLowerInvariant();
                if (value <= 0m)
                {
                    continue;
                }

                decimal grams = unit.IndexOf("kg", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    unit.IndexOf("\u516c\u65a4", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    unit.IndexOf("\u5343\u514b", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    unit.IndexOf("\u043a\u0433", StringComparison.OrdinalIgnoreCase) >= 0
                    ? value * 1000m
                    : value;
                if (grams > best)
                {
                    best = grams;
                }
            }

            return best;
        }

        private static decimal ExtractVolumeMl(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return 0m;
            }

            MatchCollection matches = Regex.Matches(text, @"(\d+(?:[.,]\d+)?)\s*(ml|l|\u6beb\u5347|\u5347|\u043c\u043b|\u043b)(?![a-z])", RegexOptions.IgnoreCase);
            decimal best = 0m;
            for (int i = 0; i < matches.Count; i++)
            {
                decimal value = ParseDecimal(matches[i].Groups[1].Value);
                string unit = matches[i].Groups[2].Value.ToLowerInvariant();
                if (value <= 0m)
                {
                    continue;
                }

                decimal ml = IsLiterUnit(unit)
                    ? value * 1000m
                    : value;
                if (ml > best)
                {
                    best = ml;
                }
            }

            return best;
        }

        private static decimal ExtractLargestMetricLengthMm(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return 0m;
            }

            MatchCollection matches = Regex.Matches(text, @"(\d+(?:[.,]\d+)?)\s*(mm|cm|m|\u6beb\u7c73|\u5398\u7c73|\u7c73|\u043c\u043c|\u0441\u043c|\u043c)(?![a-z\u0430-\u044f])", RegexOptions.IgnoreCase);
            decimal best = 0m;
            for (int i = 0; i < matches.Count; i++)
            {
                decimal mm = ConvertMetricMatchToMm(matches[i]);
                if (mm > best)
                {
                    best = mm;
                }
            }

            return best;
        }

        private static decimal ExtractTapeWidthMm(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return 0m;
            }

            MatchCollection matches = Regex.Matches(text, @"(\d+(?:[.,]\d+)?)\s*(mm|cm|m|\u6beb\u7c73|\u5398\u7c73|\u7c73|\u043c\u043c|\u0441\u043c|\u043c)(?![a-z\u0430-\u044f])", RegexOptions.IgnoreCase);
            decimal best = 0m;
            for (int i = 0; i < matches.Count; i++)
            {
                decimal mm = ConvertMetricMatchToMm(matches[i]);
                if (mm >= 4m && mm <= 120m && mm > best)
                {
                    best = mm;
                }
            }

            return best;
        }

        private static decimal ExtractTapeThicknessMm(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return 0m;
            }

            MatchCollection matches = Regex.Matches(text, @"(\d+(?:[.,]\d+)?)\s*(mm|\u6beb\u7c73|\u043c\u043c)(?![a-z\u0430-\u044f])", RegexOptions.IgnoreCase);
            decimal best = 0m;
            for (int i = 0; i < matches.Count; i++)
            {
                decimal mm = ConvertMetricMatchToMm(matches[i]);
                if (mm >= 0.05m && mm <= 5m && (best <= 0m || mm < best))
                {
                    best = mm;
                }
            }

            return best;
        }

        private static decimal ConvertMetricMatchToMm(Match match)
        {
            if (match == null || !match.Success)
            {
                return 0m;
            }

            decimal value = ParseDecimal(match.Groups[1].Value);
            if (value <= 0m)
            {
                return 0m;
            }

            string unit = (match.Groups[2].Value ?? string.Empty).ToLowerInvariant();
            if (IsCentimeterUnit(unit))
            {
                return value * 10m;
            }

            if (IsMeterUnit(unit))
            {
                return value * 1000m;
            }

            return value;
        }

        private static int EstimateTapeWeightGrams(decimal lengthMm, decimal widthMm, decimal thicknessMm)
        {
            if (lengthMm <= 0m || widthMm <= 0m)
            {
                return 0;
            }

            decimal effectiveThicknessMm = thicknessMm > 0m ? thicknessMm : 0.2m;
            decimal volumeCm3 = (lengthMm * widthMm * effectiveThicknessMm) / 1000m;
            decimal grams = (volumeCm3 * 1.05m) + 12m;
            if (grams <= 0m)
            {
                return 0;
            }

            if (grams > 1500m)
            {
                grams = 1500m;
            }

            return (int)Math.Ceiling(grams);
        }

        private static bool ContainsAny(string text, params string[] needles)
        {
            for (int i = 0; needles != null && i < needles.Length; i++)
            {
                if (!string.IsNullOrEmpty(needles[i]) && text.IndexOf(needles[i], StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static void EnsureOzonContentAttributes(SourceProduct product, JArray categoryAttributes, string offerId, string title, string description)
        {
            if (product == null)
            {
                return;
            }

            AddOzonAttributeIfSupported(product, categoryAttributes, 4191, SanitizeOzonDescription(description, title, product));
            AddOzonAttributeIfSupported(product, categoryAttributes, 9024, offerId);
            AddOzonAttributeIfSupported(product, categoryAttributes, 9048, BuildModelName(product, title));
            AddOzonAttributeIfSupported(product, categoryAttributes, 85, ResolveBrand(product));
            AddOzonAttributeIfSupported(product, categoryAttributes, 4383, ResolveNumericAttribute(FindCategoryAttributeById(categoryAttributes, 4383), product, "weight_g", "500"));
            AddOzonAttributeIfSupported(product, categoryAttributes, 4497, ResolveNumericAttribute(FindCategoryAttributeById(categoryAttributes, 4497), product, "weight_g", "500"));
        }

        private static void AddOzonAttributeIfSupported(SourceProduct product, JArray categoryAttributes, long id, string value)
        {
            if (product == null || id <= 0 || string.IsNullOrEmpty(value))
            {
                return;
            }

            if (categoryAttributes == null || categoryAttributes.Count == 0)
            {
                return;
            }

            if (!CategorySupportsAttribute(categoryAttributes, id))
            {
                return;
            }

            if (product.OzonAttributes.ContainsKey(id) && !string.IsNullOrEmpty(product.OzonAttributes[id]))
            {
                return;
            }

            product.OzonAttributes[id] = value;
        }

        private static bool CategorySupportsAttribute(JArray categoryAttributes, long id)
        {
            for (int i = 0; categoryAttributes != null && i < categoryAttributes.Count; i++)
            {
                JObject attr = categoryAttributes[i] as JObject;
                if (attr == null)
                {
                    continue;
                }

                long attrId = 0;
                long.TryParse(Convert.ToString(attr["id"] ?? string.Empty), out attrId);
                if (attrId == id)
                {
                    return true;
                }
            }

            return false;
        }

        private void EnsureRequiredOzonAttributes(SourceProduct product, SourcingOptions options, JArray categoryAttributes, string offerId, string title, string description, string clientId, string apiKey)
        {
            for (int i = 0; categoryAttributes != null && i < categoryAttributes.Count; i++)
            {
                JObject attr = categoryAttributes[i] as JObject;
                if (attr == null || !IsRequiredAttribute(attr))
                {
                    continue;
                }

                long id = FirstTokenLong(attr, "id", "attribute_id");
                if (id <= 0)
                {
                    continue;
                }

                bool hasDictionary = HasDictionaryValues(attr);
                bool hasExistingValue = product.OzonAttributes.ContainsKey(id) && !string.IsNullOrEmpty(product.OzonAttributes[id]);
                if (hasExistingValue && IsPackageContentsAttribute(attr) && !IsAcceptablePackageContentsValue(product.OzonAttributes[id]))
                {
                    hasExistingValue = false;
                    product.OzonAttributes.Remove(id);
                    product.OzonAttributeDictionaryValueIds.Remove(id);
                }
                bool hasDictionaryValueId = product.OzonAttributeDictionaryValueIds.ContainsKey(id) &&
                    product.OzonAttributeDictionaryValueIds[id] > 0;
                if ((!hasDictionary && hasExistingValue) || (hasDictionary && hasExistingValue && hasDictionaryValueId))
                {
                    continue;
                }

                string value = hasExistingValue ? product.OzonAttributes[id] : string.Empty;
                long dictionaryValueId = 0;
                string preferredValue = hasExistingValue ? value : InferOzonAttributeValue(attr, product, offerId, title, description, true);
                if (hasDictionary)
                {
                    value = ResolveDictionaryAttributeValue(options, attr, id, preferredValue, true, clientId, apiKey, out dictionaryValueId);
                }

                if (!hasDictionary && string.IsNullOrEmpty(value))
                {
                    value = preferredValue;
                }

                if (hasDictionary && dictionaryValueId <= 0)
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(value))
                {
                    product.OzonAttributes[id] = value;
                    if (dictionaryValueId > 0)
                    {
                        product.OzonAttributeDictionaryValueIds[id] = dictionaryValueId;
                    }
                }
            }
        }

        private void PopulateSupportedOzonAttributes(SourceProduct product, SourcingOptions options, JArray categoryAttributes, string offerId, string title, string description, string clientId, string apiKey)
        {
            for (int i = 0; categoryAttributes != null && i < categoryAttributes.Count; i++)
            {
                JObject attr = categoryAttributes[i] as JObject;
                if (attr == null || !CanAutoFillOptionalAttribute(attr))
                {
                    continue;
                }

                long id = FirstTokenLong(attr, "id", "attribute_id");
                if (id <= 0)
                {
                    continue;
                }

                bool hasDictionary = HasDictionaryValues(attr);
                bool hasExistingValue = product.OzonAttributes.ContainsKey(id) && !string.IsNullOrEmpty(product.OzonAttributes[id]);
                if (hasExistingValue && IsPackageContentsAttribute(attr) && !IsAcceptablePackageContentsValue(product.OzonAttributes[id]))
                {
                    hasExistingValue = false;
                    product.OzonAttributes.Remove(id);
                    product.OzonAttributeDictionaryValueIds.Remove(id);
                }
                bool hasDictionaryValueId = product.OzonAttributeDictionaryValueIds.ContainsKey(id) &&
                    product.OzonAttributeDictionaryValueIds[id] > 0;
                if ((!hasDictionary && hasExistingValue) || (hasDictionary && hasExistingValue && hasDictionaryValueId))
                {
                    continue;
                }

                string preferredValue = hasExistingValue
                    ? product.OzonAttributes[id]
                    : InferOzonAttributeValue(attr, product, offerId, title, description, false);
                if (string.IsNullOrEmpty(preferredValue))
                {
                    continue;
                }

                long dictionaryValueId = 0;
                string value = preferredValue;
                if (hasDictionary)
                {
                    value = ResolveDictionaryAttributeValue(options, attr, id, preferredValue, false, clientId, apiKey, out dictionaryValueId);
                    if (string.IsNullOrEmpty(value) || dictionaryValueId <= 0)
                    {
                        continue;
                    }
                }

                product.OzonAttributes[id] = value;
                if (dictionaryValueId > 0)
                {
                    product.OzonAttributeDictionaryValueIds[id] = dictionaryValueId;
                }
            }
        }

        private static bool CanAutoFillOptionalAttribute(JObject attr)
        {
            if (attr == null || IsRequiredAttribute(attr))
            {
                return false;
            }

            if (IsComplexChildAttribute(attr))
            {
                return false;
            }

            if (HasDictionaryValues(attr))
            {
                return false;
            }

            string type = FirstTokenString(attr, "type", "attribute_type", "value_type");
            if (ContainsComparable(type, "string", "text", "multiline", "url", "link", "href", "decimal", "number", "numeric", "int", "float"))
            {
                return true;
            }

            string name = FirstTokenString(attr, "name", "attribute_name");
            return ContainsComparable(name,
                "brand", "model", "name", "description", "color", "colour", "material", "country", "manufacturer",
                "weight", "width", "height", "depth", "length", "diameter", "volume", "capacity",
                "\u54c1\u724c", "\u578b\u53f7", "\u989c\u8272", "\u6750\u8d28", "\u4ea7\u5730", "\u5382\u5546",
                "\u91cd\u91cf", "\u5bbd\u5ea6", "\u9ad8\u5ea6", "\u6df1\u5ea6", "\u957f\u5ea6", "\u76f4\u5f84", "\u5bb9\u91cf",
                "\u0431\u0440\u0435\u043d\u0434", "\u043c\u043e\u0434\u0435\u043b\u044c", "\u0446\u0432\u0435\u0442", "\u043c\u0430\u0442\u0435\u0440\u0438\u0430\u043b", "\u0441\u0442\u0440\u0430\u043d\u0430", "\u043f\u0440\u043e\u0438\u0437\u0432\u043e\u0434");
        }

        private static bool IsRequiredAttribute(JObject attr)
        {
            if (attr == null)
            {
                return false;
            }

            string value = Convert.ToString(attr["is_required"] ?? attr["required"] ?? string.Empty);
            return string.Equals(value, "True", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "1", StringComparison.OrdinalIgnoreCase);
        }

        private static bool HasDictionaryValues(JObject attr)
        {
            if (attr == null)
            {
                return false;
            }

            long dictionaryId = FirstTokenLong(attr, "dictionary_id", "dictionaryId");
            string type = Convert.ToString(attr["type"] ?? string.Empty);
            return dictionaryId > 0 || type.IndexOf("Dictionary", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private string ResolveDictionaryAttributeValue(SourcingOptions options, JObject categoryAttribute, long attributeId, string preferredValue, bool allowFirstFallback, string clientId, string apiKey, out long dictionaryValueId)
        {
            dictionaryValueId = 0;
            try
            {
                if (!string.IsNullOrWhiteSpace(preferredValue))
                {
                    JObject searchPayload = new JObject();
                    searchPayload["description_category_id"] = options.OzonCategoryId;
                    searchPayload["type_id"] = options.OzonTypeId;
                    searchPayload["attribute_id"] = attributeId;
                    searchPayload["language"] = "RU";
                    searchPayload["limit"] = 20;
                    searchPayload["value"] = preferredValue;
                    JArray searchedValues = LoadDictionaryAttributeValues("/v1/description-category/attribute/values/search", searchPayload, clientId, apiKey);
                    string searchedValue = MatchDictionaryAttributeValue(searchedValues, preferredValue, false, IsHazardAttribute(categoryAttribute), out dictionaryValueId);
                    if (!string.IsNullOrEmpty(searchedValue) && dictionaryValueId > 0)
                    {
                        return searchedValue;
                    }
                }

                JObject payload = new JObject();
                payload["description_category_id"] = options.OzonCategoryId;
                payload["type_id"] = options.OzonTypeId;
                payload["attribute_id"] = attributeId;
                payload["language"] = "RU";
                payload["limit"] = 50;
                JArray values = LoadDictionaryAttributeValues("/v1/description-category/attribute/values", payload, clientId, apiKey);
                return MatchDictionaryAttributeValue(values, preferredValue, allowFirstFallback, IsHazardAttribute(categoryAttribute), out dictionaryValueId);
            }
            catch
            {
            }

            return string.Empty;
        }

        private static JArray LoadDictionaryAttributeValues(string path, JObject payload, string clientId, string apiKey)
        {
            JObject root = JObject.Parse(PostOzonJson(path, payload.ToString(Formatting.None), clientId, apiKey));
            JArray values = FindArray(root["result"] ?? root, "values", "items", "result");
            if (values == null)
            {
                values = root["result"] as JArray;
            }

            return values ?? new JArray();
        }

        private static string MatchDictionaryAttributeValue(JArray values, string preferredValue, bool allowFirstFallback, bool preferSafeNegativeValues, out long dictionaryValueId)
        {
            dictionaryValueId = 0;
            string normalizedPreferred = NormalizeComparableText(preferredValue);
            string firstValue = string.Empty;
            long firstId = 0;
            string safeValue = string.Empty;
            long safeId = 0;
            for (int i = 0; values != null && i < values.Count; i++)
            {
                JObject item = values[i] as JObject;
                if (item == null)
                {
                    continue;
                }

                string value = FirstTokenString(item, "value", "name");
                long id = FirstTokenLong(item, "id", "dictionary_value_id");
                if (string.IsNullOrEmpty(value) || id <= 0)
                {
                    continue;
                }

                if (firstId <= 0)
                {
                    firstId = id;
                    firstValue = value;
                }

                if (preferSafeNegativeValues && safeId <= 0 && IsSafeNegativeDictionaryValue(value))
                {
                    safeId = id;
                    safeValue = value;
                }

                if (!string.IsNullOrEmpty(normalizedPreferred))
                {
                    string normalizedValue = NormalizeComparableText(value);
                    if (normalizedValue == normalizedPreferred ||
                        normalizedValue.IndexOf(normalizedPreferred, StringComparison.OrdinalIgnoreCase) >= 0 ||
                        normalizedPreferred.IndexOf(normalizedValue, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        dictionaryValueId = id;
                        return value;
                    }
                }
            }

            if (allowFirstFallback && preferSafeNegativeValues && safeId > 0)
            {
                dictionaryValueId = safeId;
                return safeValue;
            }

            if (allowFirstFallback && firstId > 0)
            {
                dictionaryValueId = firstId;
                return firstValue;
            }

            return string.Empty;
        }

        private static string InferOzonAttributeValue(JObject attr, SourceProduct product, string offerId, string title, string description, bool requiredOnly)
        {
            if (IsComplexChildAttribute(attr))
            {
                return string.Empty;
            }

            string name = FirstTokenString(attr, "name", "attribute_name");
            if (string.IsNullOrEmpty(name))
            {
                return requiredOnly ? "\u0423\u043d\u0438\u0432\u0435\u0440\u0441\u0430\u043b\u044c\u043d\u044b\u0439" : string.Empty;
            }

            if (ContainsComparable(name, "brand", "\u54c1\u724c", "\u724c\u5b50", "\u0431\u0440\u0435\u043d\u0434", "\u043c\u0430\u0440\u043a\u0430"))
            {
                return ResolveBrand(product);
            }

            if (ContainsComparable(name, "model", "\u578b\u53f7", "\u8d27\u53f7", "\u6b3e\u53f7", "\u043c\u043e\u0434\u0435\u043b\u044c", "\u0430\u0440\u0442\u0438\u043a\u0443\u043b"))
            {
                return BuildModelName(product, title);
            }

            if (ContainsComparable(name, "name", "\u043d\u0430\u0437\u0432\u0430\u043d", "\u0438\u043c\u044f"))
            {
                return Truncate(CleanPublicText(title), 250);
            }

            if (ContainsComparable(name, "description", "\u043e\u043f\u0438\u0441\u0430\u043d"))
            {
                return Truncate(SanitizeOzonDescription(description, title, product), 900);
            }

            if (ContainsComparable(name, "color", "colour", "\u989c\u8272", "\u8272", "\u0446\u0432\u0435\u0442"))
            {
                string color = FirstAttributeValue(product, "\u989c\u8272", "\u8272", "color", "colour", "Color", "\u0446\u0432\u0435\u0442");
                return string.IsNullOrEmpty(color) ? (requiredOnly ? "\u0411\u0435\u043b\u044b\u0439" : string.Empty) : Truncate(CleanPublicText(color), 120);
            }

            if (ContainsComparable(name, "material", "\u6750\u8d28", "\u6750\u6599", "\u9762\u6599", "\u6750\u6599\u6210\u5206", "\u043c\u0430\u0442\u0435\u0440\u0438\u0430\u043b", "\u0441\u043e\u0441\u0442\u0430\u0432"))
            {
                string material = FirstAttributeValue(product, "\u6750\u8d28", "\u6750\u6599", "\u9762\u6599", "material", "fabric", "\u043c\u0430\u0442\u0435\u0440\u0438\u0430\u043b", "\u0441\u043e\u0441\u0442\u0430\u0432");
                return string.IsNullOrEmpty(material) ? (requiredOnly ? "\u041f\u043b\u0430\u0441\u0442\u0438\u043a" : string.Empty) : Truncate(CleanPublicText(material), 120);
            }

            if (ContainsComparable(name, "weight", "\u91cd\u91cf", "\u6bdb\u91cd", "\u51c0\u91cd", "\u0432\u0435\u0441"))
            {
                return ResolveNumericAttribute(attr, product, "weight_g", requiredOnly ? "500" : string.Empty);
            }

            if (ContainsComparable(name, "width", "\u5bbd\u5ea6", "\u5bbd", "\u0448\u0438\u0440\u0438\u043d"))
            {
                return ResolveNumericAttribute(attr, product, "width_mm", requiredOnly ? "100" : string.Empty);
            }

            if (ContainsComparable(name, "height", "\u9ad8\u5ea6", "\u9ad8", "\u0432\u044b\u0441\u043e\u0442"))
            {
                return ResolveNumericAttribute(attr, product, "height_mm", requiredOnly ? "100" : string.Empty);
            }

            if (ContainsComparable(name, "depth", "length", "\u6df1\u5ea6", "\u539a\u5ea6", "\u957f\u5ea6", "\u0433\u043b\u0443\u0431\u0438\u043d", "\u0434\u043b\u0438\u043d"))
            {
                return ResolveNumericAttribute(attr, product, "depth_mm", requiredOnly ? "100" : string.Empty);
            }

            if (ContainsComparable(name, "country", "origin", "made in", "\u4ea7\u5730", "\u4ea7\u56fd", "\u56fd\u5bb6", "\u0441\u0442\u0440\u0430\u043d\u0430", "\u043f\u0440\u043e\u0438\u0441\u0445\u043e\u0436\u0434"))
            {
                string country = FirstAttributeValue(product, "\u4ea7\u5730", "\u4ea7\u56fd", "\u56fd\u5bb6", "origin", "country", "made in", "\u0441\u0442\u0440\u0430\u043d\u0430");
                return string.IsNullOrEmpty(country) ? string.Empty : Truncate(CleanPublicText(country), 120);
            }

            if (ContainsComparable(name, "manufacturer", "\u5382\u5546", "\u751f\u4ea7\u5546", "\u5236\u9020\u5546", "\u043f\u0440\u043e\u0438\u0437\u0432\u043e\u0434", "\u0438\u0437\u0433\u043e\u0442\u043e\u0432"))
            {
                string manufacturer = FirstAttributeValue(product, "\u5382\u5546", "\u751f\u4ea7\u5546", "\u5236\u9020\u5546", "manufacturer", "\u043f\u0440\u043e\u0438\u0437\u0432\u043e\u0434\u0438\u0442\u0435\u043b\u044c");
                if (string.IsNullOrEmpty(manufacturer))
                {
                    manufacturer = product != null ? product.ShopName : string.Empty;
                }

                return string.IsNullOrEmpty(manufacturer) ? string.Empty : Truncate(CleanPublicText(manufacturer), 120);
            }

            if (IsPackageContentsAttribute(attr))
            {
                string packageContents = BuildPackageContentsValue(product, title);
                return string.IsNullOrEmpty(packageContents)
                    ? (requiredOnly ? "\u0412 \u043a\u043e\u043c\u043f\u043b\u0435\u043a\u0442\u0435 1 \u0442\u043e\u0432\u0430\u0440" : string.Empty)
                    : packageContents;
            }

            if (IsHazardAttribute(attr))
            {
                return ResolveSafeHazardText();
            }

            string mappedValue;
            if (TryMapSourceAttributeValue(product, name, out mappedValue))
            {
                return mappedValue;
            }

            return requiredOnly ? "\u0423\u043d\u0438\u0432\u0435\u0440\u0441\u0430\u043b\u044c\u043d\u044b\u0439" : string.Empty;
        }

        private static string ResolveBrand(SourceProduct product)
        {
            string value = FirstAttributeValue(product, "\u54c1\u724c", "\u724c\u5b50", "Brand", "brand", "\u043c\u0430\u0440\u043a\u0430", "\u0431\u0440\u0435\u043d\u0434");
            return string.IsNullOrEmpty(value) ? "\u0411\u0435\u0437 \u0431\u0440\u0435\u043d\u0434\u0430" : Truncate(CleanPublicText(value), 120);
        }

        private static bool IsPackageContentsAttribute(JObject attr)
        {
            if (attr == null)
            {
                return false;
            }

            string name = FirstTokenString(attr, "name", "attribute_name");
            string description = FirstTokenString(attr, "description", "hint", "tooltip");
            return ContainsComparable(
                name + " " + description,
                "package contents", "package content", "included", "in the box", "set includes", "bundle contents",
                "contents", "complectation", "komplekt", "\u043a\u043e\u043c\u043f\u043b\u0435\u043a\u0442", "\u043a\u043e\u043c\u043f\u043b\u0435\u043a\u0442\u0430\u0446", "\u0432 \u043a\u043e\u043c\u043f\u043b\u0435\u043a\u0442\u0435",
                "\u914d\u5957", "\u5957\u88c5", "\u5305\u88c5\u6e05\u5355", "\u5305\u88c5\u5185\u5bb9", "\u6e05\u5355", "\u5185\u542b", "\u5305\u542b");
        }

        private static string BuildPackageContentsValue(SourceProduct product, string title)
        {
            string sourceValue = FirstAttributeValue(
                product,
                "\u5305\u88c5\u6e05\u5355", "\u5305\u88c5\u5185\u5bb9", "\u5185\u542b", "\u5305\u542b", "\u5957\u88c5", "\u914d\u4ef6", "\u9644\u4ef6", "\u9644\u9001",
                "package content", "package contents", "packing list", "included", "accessories", "kit includes",
                "\u043a\u043e\u043c\u043f\u043b\u0435\u043a\u0442", "\u043a\u043e\u043c\u043f\u043b\u0435\u043a\u0442\u0430\u0446", "\u0432 \u043a\u043e\u043c\u043f\u043b\u0435\u043a\u0442\u0435", "\u0441\u043e\u0441\u0442\u0430\u0432 \u043a\u043e\u043c\u043f\u043b\u0435\u043a\u0442\u0430");
            if (!string.IsNullOrEmpty(sourceValue))
            {
                string cleanedSource = NormalizePackageContentsValue(sourceValue);
                if (IsAcceptablePackageContentsValue(cleanedSource))
                {
                    return cleanedSource;
                }
            }

            string cleanTitle = CleanPublicText(title);
            if (string.IsNullOrEmpty(cleanTitle))
            {
                cleanTitle = product == null ? string.Empty : CleanPublicText(product.Title);
            }

            if (string.IsNullOrEmpty(cleanTitle))
            {
                return string.Empty;
            }

            string generic = Truncate(cleanTitle, 120);
            if (!ContainsComparable(generic, "\u0447\u0435\u0445\u043e\u043b", "\u0441\u043c\u044b\u0447\u043e\u043a", "case", "bow", "\u5957", "\u9644"))
            {
                generic += ". \u0412 \u043a\u043e\u043c\u043f\u043b\u0435\u043a\u0442\u0435 1 \u0448\u0442.";
            }

            return NormalizePackageContentsValue(generic);
        }

        private static string NormalizePackageContentsValue(string value)
        {
            string cleaned = Truncate(CleanPublicText(value), 250);
            cleaned = Regex.Replace(cleaned, @"\s*[,;，；]+\s*", ", ");
            cleaned = Regex.Replace(cleaned, @"\s{2,}", " ").Trim(' ', ',', ';', '，', '；');
            return cleaned;
        }

        private static bool IsAcceptablePackageContentsValue(string value)
        {
            string cleaned = NormalizePackageContentsValue(value);
            if (string.IsNullOrEmpty(cleaned))
            {
                return false;
            }

            if (cleaned.Length < 3)
            {
                return false;
            }

            if (LooksLikeLocationValue(cleaned))
            {
                return false;
            }

            return !ContainsComparable(
                cleaned,
                "\u6cf0\u5dde", "\u6240\u5728\u5730", "\u4ea7\u5730", "\u57ce\u5e02", "\u7701\u4efd", "\u5730\u5740",
                "taizhou", "city", "province", "address", "location");
        }

        private static bool LooksLikeLocationValue(string value)
        {
            string cleaned = CleanPublicText(value);
            if (string.IsNullOrEmpty(cleaned))
            {
                return false;
            }

            if (Regex.IsMatch(cleaned, @"^[\p{L}\p{IsCJKUnifiedIdeographs}\s\-]{2,20}$"))
            {
                return ContainsComparable(
                    cleaned,
                    "\u5dde", "\u5e02", "\u7701", "\u533a", "\u53bf",
                    "city", "province", "district", "region",
                    "\u0433\u043e\u0440\u043e\u0434", "\u043e\u0431\u043b\u0430\u0441\u0442", "\u0440\u0430\u0439\u043e\u043d");
            }

            return false;
        }

        private static bool IsHazardAttribute(JObject attr)
        {
            if (attr == null)
            {
                return false;
            }

            string name = FirstTokenString(attr, "name", "attribute_name");
            string description = FirstTokenString(attr, "description", "hint", "tooltip");
            return ContainsComparable(
                name + " " + description,
                "hazard", "hazardous", "danger class", "dangerous goods", "safety class",
                "\u5371\u9669", "\u5371\u9669\u7b49\u7ea7", "\u5371\u9669\u54c1",
                "\u043e\u043f\u0430\u0441\u043d", "\u043a\u043b\u0430\u0441\u0441 \u043e\u043f\u0430\u0441\u043d", "\u043e\u043f\u0430\u0441\u043d\u044b\u0439 \u0433\u0440\u0443\u0437");
        }

        private static string ResolveSafeHazardText()
        {
            return "\u041d\u0435 \u043e\u043f\u0430\u0441\u0435\u043d";
        }

        private static bool IsSafeNegativeDictionaryValue(string value)
        {
            string normalized = NormalizeComparableText(value);
            if (string.IsNullOrEmpty(normalized))
            {
                return false;
            }

            return normalized.IndexOf("неопас", StringComparison.OrdinalIgnoreCase) >= 0 ||
                normalized.IndexOf("не опас", StringComparison.OrdinalIgnoreCase) >= 0 ||
                normalized.IndexOf("not dangerous", StringComparison.OrdinalIgnoreCase) >= 0 ||
                normalized.IndexOf("not hazardous", StringComparison.OrdinalIgnoreCase) >= 0 ||
                normalized.IndexOf("no hazard", StringComparison.OrdinalIgnoreCase) >= 0 ||
                normalized.IndexOf("безопас", StringComparison.OrdinalIgnoreCase) >= 0 ||
                normalized == "нет" ||
                normalized == "no" ||
                normalized == "none";
        }

        private static string BuildModelName(SourceProduct product, string title)
        {
            string model = FirstAttributeValue(product, "\u578b\u53f7", "\u8d27\u53f7", "\u6b3e\u53f7", "Model", "model", "\u0430\u0440\u0442\u0438\u043a\u0443\u043b", "\u043c\u043e\u0434\u0435\u043b\u044c");
            if (!string.IsNullOrEmpty(model))
            {
                return Truncate(CleanPublicText(model), 120);
            }

            string basis = product != null && !string.IsNullOrEmpty(product.OfferId) ? product.OfferId : title;
            return Truncate("LZ-" + SafeOfferId(basis), 120);
        }

        private static string ResolveNumericAttribute(JObject attr, SourceProduct product, string key, string fallback)
        {
            if (product != null && product.Attributes != null && product.Attributes.ContainsKey(key))
            {
                decimal value = ParseDecimal(product.Attributes[key]);
                if (value > 0m)
                {
                    return FormatOzonNumericValue(ConvertNumericValueForAttribute(attr, key, value));
                }
            }

            return fallback;
        }

        private static int ResolvePositiveIntAttribute(SourceProduct product, string key, int fallback)
        {
            if (product != null && product.Attributes != null && product.Attributes.ContainsKey(key))
            {
                int value = ParseInt(product.Attributes[key]);
                if (value > 0)
                {
                    return value;
                }
            }

            return fallback;
        }

        private static decimal ConvertNumericValueForAttribute(JObject attr, string key, decimal value)
        {
            string text = (FirstTokenString(attr, "name", "attribute_name") + " " +
                FirstTokenString(attr, "description", "hint", "tooltip")).ToLowerInvariant();

            if (key == "weight_g")
            {
                if (TextContainsMeasurementToken(text, "kg", "\u043a\u0433"))
                {
                    return value / 1000m;
                }

                return value;
            }

            if (key == "width_mm" || key == "height_mm" || key == "depth_mm")
            {
                if (TextContainsMeasurementToken(text, "cm", "\u0441\u043c") ||
                    text.IndexOf("\u5398\u7c73", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return value / 10m;
                }

                if (TextContainsMeasurementToken(text, "m", "\u043c") ||
                    text.IndexOf("\u7c73", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return value / 1000m;
                }
            }

            return value;
        }

        private static bool IsMillimeterUnit(string unit)
        {
            string normalized = NormalizeUnitToken(unit);
            return normalized == "mm" || normalized == "\u6beb\u7c73" || normalized == "\u043c\u043c";
        }

        private static bool IsCentimeterUnit(string unit)
        {
            string normalized = NormalizeUnitToken(unit);
            return normalized == "cm" || normalized == "\u5398\u7c73" || normalized == "\u0441\u043c";
        }

        private static bool IsMeterUnit(string unit)
        {
            string normalized = NormalizeUnitToken(unit);
            return normalized == "m" || normalized == "\u7c73" || normalized == "\u043c";
        }

        private static bool IsLiterUnit(string unit)
        {
            string normalized = NormalizeUnitToken(unit);
            return normalized == "l" || normalized == "\u5347" || normalized == "\u043b";
        }

        private static string NormalizeUnitToken(string unit)
        {
            return string.IsNullOrWhiteSpace(unit) ? string.Empty : unit.Trim().ToLowerInvariant();
        }

        private static bool TextContainsMeasurementToken(string text, params string[] tokens)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            for (int i = 0; tokens != null && i < tokens.Length; i++)
            {
                string token = tokens[i];
                if (string.IsNullOrWhiteSpace(token))
                {
                    continue;
                }

                if (token.Length == 1)
                {
                    if (Regex.IsMatch(text, @"(^|[^a-z\u0430-\u044f])" + Regex.Escape(token) + @"([^a-z\u0430-\u044f]|$)", RegexOptions.IgnoreCase))
                    {
                        return true;
                    }

                    continue;
                }

                if (text.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static string FormatOzonNumericValue(decimal value)
        {
            if (value <= 0m)
            {
                return string.Empty;
            }

            decimal rounded = Math.Round(value, 3, MidpointRounding.AwayFromZero);
            string text = rounded.ToString("0.###", CultureInfo.InvariantCulture);
            return text == "0" ? string.Empty : text;
        }

        private static decimal ParseDecimal(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return 0m;
            }

            string cleaned = ExtractDecimalValue(value);
            decimal number;
            return decimal.TryParse(cleaned, NumberStyles.Any, CultureInfo.InvariantCulture, out number) ? number : 0m;
        }

        private static string FirstAttributeValue(SourceProduct product, params string[] keys)
        {
            if (product == null || product.Attributes == null)
            {
                return string.Empty;
            }

            for (int i = 0; i < keys.Length; i++)
            {
                string normalizedKey = NormalizeComparableText(keys[i]);
                if (string.IsNullOrEmpty(normalizedKey))
                {
                    continue;
                }

                foreach (KeyValuePair<string, string> pair in product.Attributes)
                {
                    if (string.IsNullOrEmpty(pair.Value))
                    {
                        continue;
                    }

                    string normalizedSourceKey = NormalizeComparableText(pair.Key);
                    if (string.IsNullOrEmpty(normalizedSourceKey))
                    {
                        continue;
                    }

                    if (normalizedSourceKey == normalizedKey ||
                        normalizedSourceKey.IndexOf(normalizedKey, StringComparison.OrdinalIgnoreCase) >= 0 ||
                        normalizedKey.IndexOf(normalizedSourceKey, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return pair.Value;
                    }
                }
            }

            return string.Empty;
        }

        private static bool TryMapSourceAttributeValue(SourceProduct product, string attributeName, out string value)
        {
            value = string.Empty;
            if (product == null || product.Attributes == null || string.IsNullOrEmpty(attributeName))
            {
                return false;
            }

            foreach (KeyValuePair<string, string> pair in product.Attributes)
            {
                if (string.IsNullOrEmpty(pair.Value))
                {
                    continue;
                }

                if (MatchesAttributeGroup(attributeName, pair.Key, "color", "colour", "\u989c\u8272", "\u8272", "\u914d\u8272", "\u0446\u0432\u0435\u0442") ||
                    MatchesAttributeGroup(attributeName, pair.Key, "material", "fabric", "\u6750\u8d28", "\u6750\u6599", "\u9762\u6599", "\u6210\u5206", "\u043c\u0430\u0442\u0435\u0440\u0438\u0430\u043b", "\u0441\u043e\u0441\u0442\u0430\u0432") ||
                    MatchesAttributeGroup(attributeName, pair.Key, "size", "\u5c3a\u5bf8", "\u89c4\u683c", "\u53f7\u7801", "\u0440\u0430\u0437\u043c\u0435\u0440") ||
                    MatchesAttributeGroup(attributeName, pair.Key, "weight", "\u91cd\u91cf", "\u6bdb\u91cd", "\u51c0\u91cd", "\u0432\u0435\u0441") ||
                    MatchesAttributeGroup(attributeName, pair.Key, "length", "\u957f\u5ea6", "\u957f", "\u0434\u043b\u0438\u043d\u0430") ||
                    MatchesAttributeGroup(attributeName, pair.Key, "width", "\u5bbd\u5ea6", "\u5bbd", "\u0448\u0438\u0440\u0438\u043d\u0430") ||
                    MatchesAttributeGroup(attributeName, pair.Key, "height", "\u9ad8\u5ea6", "\u9ad8", "\u0432\u044b\u0441\u043e\u0442\u0430") ||
                    MatchesAttributeGroup(attributeName, pair.Key, "depth", "\u6df1\u5ea6", "\u539a\u5ea6", "\u0433\u043b\u0443\u0431\u0438\u043d\u0430") ||
                    MatchesAttributeGroup(attributeName, pair.Key, "pattern", "\u56fe\u6848", "\u82b1\u578b", "\u5370\u82b1", "\u0443\u0437\u043e\u0440", "\u043f\u0440\u0438\u043d\u0442") ||
                    MatchesAttributeGroup(attributeName, pair.Key, "style", "\u98ce\u683c", "\u6b3e\u5f0f", "\u0441\u0442\u0438\u043b\u044c") ||
                    MatchesAttributeGroup(attributeName, pair.Key, "shape", "\u5f62\u72b6", "\u5916\u5f62", "\u0444\u043e\u0440\u043c\u0430") ||
                    MatchesAttributeGroup(attributeName, pair.Key, "quantity", "qty", "pcs", "\u6570\u91cf", "\u4ef6\u6570", "\u5305\u88c5", "\u043a\u043e\u043b\u0438\u0447\u0435\u0441\u0442\u0432\u043e", "\u043a\u043e\u043c\u043f\u043b\u0435\u043a\u0442\u0430\u0446\u0438\u044f") ||
                    MatchesAttributeGroup(attributeName, pair.Key, "country", "origin", "made in", "\u4ea7\u5730", "\u4ea7\u56fd", "\u56fd\u5bb6", "\u0441\u0442\u0440\u0430\u043d\u0430") ||
                    MatchesAttributeGroup(attributeName, pair.Key, "voltage", "\u7535\u538b", "\u043d\u0430\u043f\u0440\u044f\u0436\u0435\u043d\u0438\u0435") ||
                    MatchesAttributeGroup(attributeName, pair.Key, "power", "\u529f\u7387", "\u043c\u043e\u0449\u043d\u043e\u0441\u0442\u044c"))
                {
                    value = Truncate(CleanPublicText(pair.Value), 250);
                    return !string.IsNullOrEmpty(value);
                }
            }

            return false;
        }

        private static bool MatchesAttributeGroup(string attributeName, string sourceKey, params string[] markers)
        {
            return ContainsComparable(attributeName, markers) && ContainsComparable(sourceKey, markers);
        }

        private static bool ContainsComparable(string text, params string[] needles)
        {
            string normalizedText = NormalizeComparableText(text);
            if (string.IsNullOrEmpty(normalizedText))
            {
                return false;
            }

            for (int i = 0; needles != null && i < needles.Length; i++)
            {
                string normalizedNeedle = NormalizeComparableText(needles[i]);
                if (!string.IsNullOrEmpty(normalizedNeedle) &&
                    normalizedText.IndexOf(normalizedNeedle, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static string NormalizeComparableText(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            StringBuilder builder = new StringBuilder(value.Length);
            string lowered = value.Trim().ToLowerInvariant();
            for (int i = 0; i < lowered.Length; i++)
            {
                char c = lowered[i];
                if (char.IsLetterOrDigit(c) || (c >= 0x4E00 && c <= 0x9FFF))
                {
                    builder.Append(c);
                }
            }

            return builder.ToString();
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
                AddImage(product, FirstString(item, "pic_url", "image", "img", "picUrl"));
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

        private static decimal CalculateOzonListingPrice(SourceProduct product, SourcingOptions options)
        {
            string currency = string.IsNullOrEmpty(options == null ? null : options.CurrencyCode) ? "CNY" : options.CurrencyCode;
            decimal sourcePrice = Math.Max(1m, product == null ? 0m : product.PriceCny);
            int weight = ResolvePositiveIntAttribute(product, "weight_g", 500);
            AppConfig config = options == null ? null : options.Config;

            ProfitInput input = new ProfitInput();
            input.CategoryId1 = options == null ? 0 : options.OzonCategoryId;
            input.CategoryId2 = 0;
            input.CategoryCandidateIds = BuildFeeRuleCategoryCandidates(options);
            input.SourcePrice = ConvertPriceCurrency(sourcePrice, currency, options == null ? 0m : options.RubPerCny);
            input.WeightGrams = weight;
            input.DeliveryFee = ConvertPriceCurrency(config == null ? 0m : config.DeliveryFee, currency, options == null ? 0m : options.RubPerCny);
            input.OtherCost = 0m;
            input.PlatformCommissionPercent = config == null ? 0m : config.PlatformCommissionPercent;
            input.PromotionExpensePercent = config == null ? 0m : config.PromotionExpensePercent;
            input.TargetProfitPercent = config == null ? 0m : config.TargetProfitPercent;
            input.FulfillmentMode = string.IsNullOrEmpty(options == null ? null : options.FulfillmentMode) ? "FBS" : options.FulfillmentMode;

            ProfitEstimate estimate = ProfitCalculatorService.Calculate(config, options == null ? null : options.FeeRules, input);
            if (estimate == null || estimate.MatchedRule == null)
            {
                throw new InvalidOperationException("没有匹配的国际物流/运费规则，无法按定价公式计算售价。请先补齐 fee.txt 或选择匹配类目。");
            }

            decimal formulaPrice = estimate != null && estimate.SuggestedSellingPrice > 0m ? estimate.SuggestedSellingPrice : sourcePrice;
            return RoundMoney(Math.Max(1m, formulaPrice));
        }

        private static List<long> BuildFeeRuleCategoryCandidates(SourcingOptions options)
        {
            List<long> candidates = new List<long>();
            AppendUniqueCategoryCandidate(candidates, options == null ? 0 : options.OzonCategoryId);

            for (int i = 0; options != null && options.OzonCategoryCandidateIds != null && i < options.OzonCategoryCandidateIds.Count; i++)
            {
                AppendUniqueCategoryCandidate(candidates, options.OzonCategoryCandidateIds[i]);
            }

            return candidates;
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

        private static decimal ConvertPriceCurrency(decimal value, string currency, decimal rubPerCny)
        {
            if (value <= 0m)
            {
                return 0m;
            }

            if (string.Equals(currency, "RUB", StringComparison.OrdinalIgnoreCase))
            {
                decimal rate = rubPerCny <= 0m ? 12.5m : rubPerCny;
                return value * rate;
            }

            return value;
        }

        private static decimal RoundMoney(decimal value)
        {
            return Math.Round(value, 2, MidpointRounding.AwayFromZero);
        }

        private JObject BuildOzonImportItem(SourceProduct product, SourcingOptions options, JArray categoryAttributes, string clientId, string apiKey)
        {
            string currency = string.IsNullOrEmpty(options.CurrencyCode) ? "CNY" : options.CurrencyCode;
            decimal price = CalculateOzonListingPrice(product, options);

            string offerId = BuildOzonOfferId(product);
            string primaryImage = !string.IsNullOrEmpty(product.MainImage) ? product.MainImage : (product.Images.Count > 0 ? product.Images[0] : string.Empty);
            string title = !string.IsNullOrEmpty(product.RussianTitle) ? product.RussianTitle : product.Title;
            string description = !string.IsNullOrEmpty(product.RussianDescription)
                ? product.RussianDescription
                : CleanPublicText(product.Title) + "\n袠褋褌芯褔薪懈泻: " + product.SourceUrl;
            NormalizePhysicalDimensions(product, title);
            int height = ResolvePositiveIntAttribute(product, "height_mm", 100);
            int width = ResolvePositiveIntAttribute(product, "width_mm", 100);
            int depth = ResolvePositiveIntAttribute(product, "depth_mm", 100);
            int weight = ResolvePositiveIntAttribute(product, "weight_g", 500);

            JObject item = new JObject();
            item["description_category_id"] = options.OzonCategoryId;
            item["type_id"] = options.OzonTypeId;
            item["name"] = Truncate(CleanPublicText(title), 500);
            item["offer_id"] = offerId;
            item["barcode"] = string.Empty;
            item["price"] = price.ToString("0.##", CultureInfo.InvariantCulture);
            item["currency_code"] = currency;
            item["vat"] = string.IsNullOrEmpty(options.Vat) ? "0" : options.Vat;
            item["height"] = height;
            item["depth"] = depth;
            item["width"] = width;
            item["dimension_unit"] = "mm";
            item["weight"] = weight;
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

            EnsureOzonContentAttributes(product, categoryAttributes, offerId, title, description);
            EnsureRequiredOzonAttributes(product, options, categoryAttributes, offerId, title, description, clientId, apiKey);
            PopulateSupportedOzonAttributes(product, options, categoryAttributes, offerId, title, description, clientId, apiKey);
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
                JObject categoryAttr = FindCategoryAttributeById(categoryAttributes, pair.Key);
                bool isDictionaryAttribute = IsDictionaryAttribute(categoryAttr);
                if (isDictionaryAttribute &&
                    (!product.OzonAttributeDictionaryValueIds.ContainsKey(pair.Key) ||
                    product.OzonAttributeDictionaryValueIds[pair.Key] <= 0))
                {
                    continue;
                }

                string sanitizedValue = SanitizeOzonAttributeValue(categoryAttr, pair.Value, product);
                if (string.IsNullOrEmpty(sanitizedValue))
                {
                    continue;
                }

                JObject value = new JObject(new JProperty("value", Truncate(CleanPublicText(sanitizedValue), 900)));
                if (product.OzonAttributeDictionaryValueIds.ContainsKey(pair.Key) &&
                    product.OzonAttributeDictionaryValueIds[pair.Key] > 0)
                {
                    value["dictionary_value_id"] = product.OzonAttributeDictionaryValueIds[pair.Key];
                }

                values.Add(value);
                attr["values"] = values;
                attributes.Add(attr);
            }

            item["attributes"] = attributes;
            item["description"] = Truncate(CleanPublicText(description), 3000);
            return item;
        }

        private static JObject FindCategoryAttributeById(JArray categoryAttributes, long attributeId)
        {
            for (int i = 0; categoryAttributes != null && i < categoryAttributes.Count; i++)
            {
                JObject attr = categoryAttributes[i] as JObject;
                if (attr != null && FirstTokenLong(attr, "id", "attribute_id") == attributeId)
                {
                    return attr;
                }
            }

            return null;
        }

        private static string SanitizeOzonAttributeValue(JObject categoryAttr, string value, SourceProduct product)
        {
            string cleaned = Truncate(CleanPublicText(value), 900);
            if (LooksLikeDocumentNameAttribute(categoryAttr))
            {
                return string.Empty;
            }

            if (IsUrlAttribute(categoryAttr))
            {
                return ResolveOzonUrlAttributeValue(categoryAttr, cleaned, product);
            }

            if (string.IsNullOrEmpty(cleaned))
            {
                return string.Empty;
            }

            if (IsDictionaryAttribute(categoryAttr))
            {
                return cleaned;
            }

            if (IsNumericAttribute(categoryAttr))
            {
                return ExtractDecimalValue(cleaned);
            }

            return cleaned;
        }

        private static string ResolveOzonUrlAttributeValue(JObject categoryAttr, string cleaned, SourceProduct product)
        {
            if (LooksLikeDocumentUrlAttribute(categoryAttr))
            {
                return IsDirectPdfUrl(cleaned) ? cleaned : string.Empty;
            }

            if (IsHttpUrl(cleaned))
            {
                if (LooksLikeImageUrlAttribute(categoryAttr))
                {
                    return ResolveOzonAcceptedImageUrl(cleaned);
                }

                return cleaned;
            }

            if (product != null && LooksLikeImageUrlAttribute(categoryAttr))
            {
                return ResolveOzonAcceptedImageUrl(product.MainImage);
            }

            return string.Empty;
        }

        private static bool IsDictionaryAttribute(JObject attr)
        {
            return HasDictionaryValues(attr);
        }

        private static bool IsNumericAttribute(JObject attr)
        {
            string type = FirstTokenString(attr, "type", "attribute_type", "value_type");
            string name = FirstTokenString(attr, "name", "attribute_name");
            return ContainsComparable(type, "decimal", "number", "numeric", "int", "float") ||
                ContainsComparable(name, "weight", "width", "height", "depth", "length", "diameter", "volume", "capacity",
                    "\u91cd\u91cf", "\u957f\u5ea6", "\u5bbd\u5ea6", "\u9ad8\u5ea6", "\u6df1\u5ea6", "\u76f4\u5f84", "\u5bb9\u91cf",
                    "\u0432\u0435\u0441", "\u0448\u0438\u0440\u0438\u043d", "\u0432\u044b\u0441\u043e\u0442", "\u0434\u043b\u0438\u043d", "\u0434\u0438\u0430\u043c", "\u043e\u0431\u044a\u0435\u043c");
        }

        private static bool IsUrlAttribute(JObject attr)
        {
            string type = FirstTokenString(attr, "type", "attribute_type", "value_type");
            string name = FirstTokenString(attr, "name", "attribute_name");
            return ContainsComparable(type, "url", "link", "href") ||
                ContainsComparable(name, "url", "link", "href", "\u0441\u0441\u044b\u043b", "\u0430\u0434\u0440\u0435\u0441", "\u94fe\u63a5", "\u7f51\u5740");
        }

        private static bool LooksLikeVideoOrMediaUrlAttribute(JObject attr)
        {
            string name = FirstTokenString(attr, "name", "attribute_name");
            return ContainsComparable(name, "video", "media", "photo", "image", "\u0432\u0438\u0434\u0435\u043e", "\u0444\u043e\u0442\u043e", "\u0438\u0437\u043e\u0431\u0440", "\u89c6\u9891", "\u56fe\u7247", "\u56fe\u50cf");
        }

        private static bool LooksLikeImageUrlAttribute(JObject attr)
        {
            string name = FirstTokenString(attr, "name", "attribute_name");
            return ContainsComparable(name, "photo", "image", "picture", "gallery", "\u0444\u043e\u0442\u043e", "\u0438\u0437\u043e\u0431\u0440", "\u043a\u0430\u0440\u0442\u0438\u043d", "\u56fe\u7247", "\u56fe\u50cf");
        }

        private static bool LooksLikeDocumentUrlAttribute(JObject attr)
        {
            string name = FirstTokenString(attr, "name", "attribute_name");
            return ContainsComparable(name,
                "pdf", "document", "manual", "instruction", "brochure", "catalog", "certificate", "passport", "datasheet",
                "\u043f\u0430\u0441\u043f\u043e\u0440\u0442", "\u0438\u043d\u0441\u0442\u0440\u0443\u043a", "\u0441\u0435\u0440\u0442\u0438\u0444", "\u0434\u043e\u043a\u0443\u043c", "\u043a\u0430\u0442\u0430\u043b\u043e\u0433",
                "\u8bf4\u660e\u4e66", "\u6587\u6863", "\u624b\u518c", "\u8bc1\u4e66", "\u8d44\u6599");
        }

        private static bool LooksLikeDocumentNameAttribute(JObject attr)
        {
            string name = FirstTokenString(attr, "name", "attribute_name");
            bool isDocument = ContainsComparable(name,
                "pdf", "document", "manual", "instruction", "brochure", "catalog", "certificate", "passport", "datasheet",
                "\u0434\u043e\u043a\u0443\u043c", "\u043f\u0430\u0441\u043f\u043e\u0440\u0442", "\u0438\u043d\u0441\u0442\u0440\u0443\u043a", "\u0441\u0435\u0440\u0442\u0438\u0444",
                "\u6587\u6863", "\u8bf4\u660e\u4e66", "\u624b\u518c", "\u8bc1\u4e66", "\u8d44\u6599");
            bool isName = ContainsComparable(name,
                "name", "file name", "pdf name",
                "\u043d\u0430\u0437\u0432\u0430\u043d", "\u0438\u043c\u044f \u0444\u0430\u0439\u043b",
                "\u6587\u4ef6\u540d");
            return isDocument && isName;
        }

        private static bool IsComplexChildAttribute(JObject attr)
        {
            return FirstTokenLong(attr, "attribute_complex_id", "complex_id", "parent_id") > 0;
        }

        private static bool IsHttpUrl(string value)
        {
            Uri uri;
            if (!Uri.TryCreate(value, UriKind.Absolute, out uri))
            {
                return false;
            }

            return string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsDirectPdfUrl(string value)
        {
            if (!IsHttpUrl(value))
            {
                return false;
            }

            string queryless = value;
            int query = queryless.IndexOf('?');
            if (query > 0)
            {
                queryless = queryless.Substring(0, query);
            }

            return queryless.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);
        }

        private static string ExtractDecimalValue(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            StringBuilder builder = new StringBuilder();
            bool seenSeparator = false;
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (char.IsDigit(c))
                {
                    builder.Append(c);
                }
                else if ((c == '.' || c == ',') && !seenSeparator)
                {
                    builder.Append('.');
                    seenSeparator = true;
                }
                else if (builder.Length > 0)
                {
                    break;
                }
            }

            decimal parsed;
            string candidate = builder.ToString().Trim('.');
            return decimal.TryParse(candidate, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out parsed)
                ? parsed.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)
                : string.Empty;
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
            image = NormalizeImageUrl(image);
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

        private static void NormalizeProductImages(SourceProduct product)
        {
            if (product == null)
            {
                return;
            }

            List<string> normalized = new List<string>();
            for (int i = 0; product.Images != null && i < product.Images.Count; i++)
            {
                string image = ResolveOzonAcceptedImageUrl(product.Images[i]);
                if (!string.IsNullOrEmpty(image) && !normalized.Contains(image))
                {
                    normalized.Add(image);
                    if (normalized.Count >= MaxOzonImageCount)
                    {
                        break;
                    }
                }
            }

            string main = ResolveOzonAcceptedImageUrl(product.MainImage);
            if (!string.IsNullOrEmpty(main) && !normalized.Contains(main))
            {
                normalized.Insert(0, main);
            }

            while (normalized.Count > MaxOzonImageCount)
            {
                normalized.RemoveAt(normalized.Count - 1);
            }

            product.Images = normalized;
            product.MainImage = normalized.Count > 0 ? normalized[0] : string.Empty;
        }

        private static string ResolveOzonAcceptedImageUrl(string image)
        {
            string normalized = NormalizeImageUrl(image);
            if (string.IsNullOrEmpty(normalized))
            {
                return string.Empty;
            }

            List<string> candidates = BuildOzonImageCandidates(normalized);
            for (int i = 0; i < candidates.Count; i++)
            {
                if (IsOzonImageResolutionAccepted(candidates[i]))
                {
                    return candidates[i];
                }
            }

            return string.Empty;
        }

        private static string NormalizeImageUrl(string image)
        {
            string value = string.IsNullOrEmpty(image) ? string.Empty : image.Trim();
            if (string.IsNullOrEmpty(value) || value.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            if (value.StartsWith("//", StringComparison.Ordinal))
            {
                value = "https:" + value;
            }
            else if (value.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            {
                value = "https://" + value.Substring("http://".Length);
            }

            if (!value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            int hash = value.IndexOf('#');
            if (hash > 0)
            {
                value = value.Substring(0, hash);
            }

            string lower = value.ToLowerInvariant();
            string queryless = value;
            int query = queryless.IndexOf('?');
            if (query > 0)
            {
                queryless = queryless.Substring(0, query);
            }

            string lowerQueryless = queryless.ToLowerInvariant();
            if (lowerQueryless.EndsWith(".svg") || LooksLikeBlockedImageUrl(lowerQueryless) || LooksLikeTinyImageUrl(lowerQueryless))
            {
                return string.Empty;
            }

            bool hasKnownExtension = lowerQueryless.EndsWith(".jpg") ||
                lowerQueryless.EndsWith(".jpeg") ||
                lowerQueryless.EndsWith(".png") ||
                lowerQueryless.EndsWith(".webp");
            bool knownImageHost = lower.IndexOf("alicdn", StringComparison.OrdinalIgnoreCase) >= 0 ||
                lower.IndexOf("1688", StringComparison.OrdinalIgnoreCase) >= 0 ||
                lower.IndexOf("tbcdn", StringComparison.OrdinalIgnoreCase) >= 0 ||
                lower.IndexOf("cbu01", StringComparison.OrdinalIgnoreCase) >= 0;

            if (!hasKnownExtension && !knownImageHost)
            {
                return string.Empty;
            }

            return value;
        }

        private static bool LooksLikeBlockedImageUrl(string value)
        {
            string[] blockedKeywords = new string[]
            {
                "logo", "icon", "avatar", "sprite", "placeholder", "lazyload", "thumbnail", "thumb", "sample",
                "qrcode", "qr-code", "qr_", "48x48", "60x60", "80x80", "96x96", "100x100", "120x120", "160x160"
            };

            for (int i = 0; i < blockedKeywords.Length; i++)
            {
                if (value.IndexOf(blockedKeywords[i], StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool LooksLikeTinyImageUrl(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            MatchCollection matches = Regex.Matches(value, @"(?<!\d)(\d{2,4})\s*[xX]\s*(\d{2,4})(?!\d)");
            for (int i = 0; i < matches.Count; i++)
            {
                int width;
                int height;
                if (!int.TryParse(matches[i].Groups[1].Value, out width) ||
                    !int.TryParse(matches[i].Groups[2].Value, out height))
                {
                    continue;
                }

                if (width > 0 && height > 0 && (width < 200 || height < 200))
                {
                    return true;
                }
            }

            return value.IndexOf("48-48", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("_48-48", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("_50x50", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("_60x60", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("_80x80", StringComparison.OrdinalIgnoreCase) >= 0 ||
                value.IndexOf("q90.jpg_.webp", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static List<string> BuildOzonImageCandidates(string value)
        {
            List<string> candidates = new List<string>();
            AddCandidate(candidates, value);
            string queryless = value;
            int query = queryless.IndexOf('?');
            if (query > 0)
            {
                queryless = queryless.Substring(0, query);
                AddCandidate(candidates, queryless);
            }

            string lower = queryless.ToLowerInvariant();
            if (lower.IndexOf("alicdn.com", StringComparison.OrdinalIgnoreCase) < 0 &&
                lower.IndexOf("1688.com", StringComparison.OrdinalIgnoreCase) < 0)
            {
                return candidates;
            }

            string[] extensions = new string[] { ".jpg", ".jpeg", ".png", ".webp" };
            int end = -1;
            for (int i = 0; i < extensions.Length; i++)
            {
                int index = lower.IndexOf(extensions[i], StringComparison.OrdinalIgnoreCase);
                if (index >= 0)
                {
                    end = index + extensions[i].Length;
                    break;
                }
            }

            if (end <= 0)
            {
                return candidates;
            }

            string baseUrl = queryless.Substring(0, end);
            AddCandidate(candidates, baseUrl);
            AddCandidate(candidates, baseUrl + "_800x800.jpg");
            AddCandidate(candidates, baseUrl + "_960x960.jpg");
            AddCandidate(candidates, baseUrl + "_1000x1000.jpg");
            AddCandidate(candidates, baseUrl + "_1200x1200.jpg");
            AddCandidate(candidates, baseUrl + "_1500x1500.jpg");
            return candidates;
        }

        private static void AddCandidate(List<string> candidates, string value)
        {
            if (!string.IsNullOrEmpty(value) && !candidates.Contains(value))
            {
                candidates.Add(value);
            }
        }

        private static bool IsOzonImageResolutionAccepted(string imageUrl)
        {
            try
            {
                EnsureModernTls();
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(imageUrl);
                request.Method = "GET";
                request.Accept = "image/*";
                request.UserAgent = "Mozilla/5.0 OZON-PILOT/1.0";
                request.Timeout = 20000;
                request.ReadWriteTimeout = 20000;

                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                using (Stream stream = response.GetResponseStream())
                using (Image image = Image.FromStream(stream, false, false))
                {
                    int width = image.Width;
                    int height = image.Height;
                    return width >= 200 &&
                        height >= 200 &&
                        width <= 7680 &&
                        height <= 4320;
                }
            }
            catch
            {
                return false;
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

        private static string BuildOzonOfferId(SourceProduct product)
        {
            return "LZ1688-" + SafeOfferId(product == null ? null : product.OfferId);
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

        private static string SanitizeOzonDescription(string value, string title, SourceProduct product)
        {
            string text = CleanPublicText(value);
            if (string.IsNullOrEmpty(text))
            {
                text = CleanPublicText(title);
            }

            if (string.IsNullOrEmpty(text) && product != null)
            {
                text = CleanPublicText(product.Title);
            }

            string[] blockedPatterns = new string[]
            {
                @"[^.!\u3002\uff01]*\b(delivery|shipping|returns?|refund|exchange|warranty|seller|contact|customer service)\b[^.!\u3002\uff01]*[.!\u3002\uff01]?",
                @"[^.!\u3002\uff01]*\b(\u0434\u043e\u0441\u0442\u0430\u0432\w*|\u0432\u043e\u0437\u0432\u0440\u0430\u0442\w*|\u043e\u0431\u043c\u0435\u043d\w*|\u0433\u0430\u0440\u0430\u043d\u0442\w*|\u043f\u0440\u043e\u0434\u0430\u0432\u0435\u0446|\u0441\u0432\u044f\u0436\u0438\u0442\u0435\u0441\u044c|\u0441\u0435\u0440\u0432\u0438\u0441)\b[^.!\u3002\uff01]*[.!\u3002\uff01]?",
                @"[^.!\u3002\uff01]*(\u5feb\u9012|\u7269\u6d41|\u53d1\u8d27|\u9001\u8d27|\u8fd0\u8d39|\u9000\u8d27|\u9000\u6b3e|\u552e\u540e|\u8054\u7cfb|\u5ba2\u670d)[^.!\u3002\uff01]*[.!\u3002\uff01]?"
            };

            for (int i = 0; i < blockedPatterns.Length; i++)
            {
                text = Regex.Replace(text, blockedPatterns[i], " ", RegexOptions.IgnoreCase);
            }

            text = Regex.Replace(text, @"\s{2,}", " ").Trim();
            text = text.Trim(' ', ',', ';', '，', '；', '.', '。');
            if (string.IsNullOrEmpty(text))
            {
                text = CleanPublicText(title);
            }

            if (string.IsNullOrEmpty(text) && product != null)
            {
                text = CleanPublicText(product.Title);
            }

            return Truncate(text, 900);
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

