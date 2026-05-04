using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace LitchiOzonRecovery
{
    internal sealed class OzonFbsPosting
    {
        public string PostingNumber { get; set; }
        public string Status { get; set; }
        public string ShipmentDate { get; set; }
        public string WarehouseName { get; set; }
    }

    internal sealed class OzonLabelDownloadResult
    {
        public List<string> Files { get; private set; }
        public List<string> Logs { get; private set; }
        public string SummaryFile { get; set; }
        public string DayIndexFile { get; set; }
        public string BatchId { get; set; }

        public OzonLabelDownloadResult()
        {
            Files = new List<string>();
            Logs = new List<string>();
        }
    }

    internal sealed class OzonFulfillmentLabelService
    {
        private const string OzonSellerApiBaseUrl = "https://api-seller.ozon.ru";
        private const int MaxPackageLabelsPerRequest = 20;
        private static bool _tlsInitialized;

        public string CheckCredentials(string clientId, string apiKey)
        {
            if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(apiKey))
            {
                throw new InvalidOperationException("Ozon Client-Id and Api-Key are required.");
            }

            JObject payload = new JObject();
            JObject root = JObject.Parse(PostOzonJson("/v1/warehouse/list", payload.ToString(Formatting.None), clientId, apiKey));
            JArray warehouses = root.SelectToken("result") as JArray;
            if (warehouses == null)
            {
                warehouses = root.SelectToken("result.warehouses") as JArray;
            }

            int count = warehouses == null ? 0 : warehouses.Count;
            return "Ozon API verified. Warehouses: " + count;
        }

        public List<OzonFbsPosting> ListFbsPostings(string clientId, string apiKey, string status, int daysBack, int limit)
        {
            if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(apiKey))
            {
                throw new InvalidOperationException("Ozon Client-Id and Api-Key are required.");
            }

            JObject payload = new JObject();
            payload["dir"] = "ASC";
            payload["filter"] = new JObject(
                new JProperty("since", DateTime.UtcNow.AddDays(-Math.Max(1, daysBack)).ToString("yyyy-MM-ddTHH:mm:ss.fffZ")),
                new JProperty("to", DateTime.UtcNow.AddDays(1).ToString("yyyy-MM-ddTHH:mm:ss.fffZ")),
                new JProperty("status", string.IsNullOrWhiteSpace(status) ? "awaiting_deliver" : status.Trim()));
            payload["limit"] = Math.Min(Math.Max(1, limit), 100);
            payload["offset"] = 0;
            payload["with"] = new JObject(new JProperty("analytics_data", false), new JProperty("barcodes", true));

            JObject root = JObject.Parse(PostOzonJson("/v3/posting/fbs/list", payload.ToString(Formatting.None), clientId, apiKey));
            JArray postings = root.SelectToken("result.postings") as JArray;
            if (postings == null)
            {
                postings = root.SelectToken("postings") as JArray;
            }

            List<OzonFbsPosting> result = new List<OzonFbsPosting>();
            for (int i = 0; postings != null && i < postings.Count; i++)
            {
                JObject item = postings[i] as JObject;
                if (item == null)
                {
                    continue;
                }

                string postingNumber = Convert.ToString(item["posting_number"] ?? string.Empty).Trim();
                if (string.IsNullOrEmpty(postingNumber))
                {
                    continue;
                }

                result.Add(new OzonFbsPosting
                {
                    PostingNumber = postingNumber,
                    Status = Convert.ToString(item["status"] ?? string.Empty),
                    ShipmentDate = Convert.ToString(item["shipment_date"] ?? item["in_process_at"] ?? string.Empty),
                    WarehouseName = Convert.ToString(item.SelectToken("delivery_method.warehouse") ?? item.SelectToken("warehouse.name") ?? string.Empty)
                });
            }

            return result;
        }

        public OzonLabelDownloadResult DownloadPackageLabels(IList<string> postingNumbers, string clientId, string apiKey, string outputDirectory)
        {
            OzonLabelDownloadResult result = new OzonLabelDownloadResult();
            List<string> normalized = NormalizePostingNumbers(postingNumbers);
            if (normalized.Count == 0)
            {
                throw new InvalidOperationException("At least one posting_number is required.");
            }

            if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(apiKey))
            {
                throw new InvalidOperationException("Ozon Client-Id and Api-Key are required.");
            }

            string dayDirectory = AppPaths.EnsureDirectory(Path.Combine(outputDirectory, DateTime.Now.ToString("yyyyMMdd")));
            string batchId = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            string dayIndex = Path.Combine(dayDirectory, "label-downloads.csv");
            string summaryFile = Path.Combine(dayDirectory, "label-batch-" + batchId + ".txt");
            result.BatchId = batchId;
            result.DayIndexFile = dayIndex;
            result.SummaryFile = summaryFile;

            EnsureDailyIndexHeader(dayIndex);
            List<string> summary = new List<string>();
            summary.Add("Ozon FBS label download batch");
            summary.Add("Batch: " + batchId);
            summary.Add("Created: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            summary.Add("Posting count: " + normalized.Count);
            summary.Add(string.Empty);

            for (int offset = 0; offset < normalized.Count; offset += MaxPackageLabelsPerRequest)
            {
                List<string> batch = normalized.GetRange(offset, Math.Min(MaxPackageLabelsPerRequest, normalized.Count - offset));
                JObject payload = new JObject();
                JArray postingArray = new JArray();
                for (int i = 0; i < batch.Count; i++)
                {
                    postingArray.Add(batch[i]);
                }

                payload["posting_number"] = postingArray;
                byte[] pdf = PostOzonBinary("/v2/posting/fbs/package-label", payload.ToString(Formatting.None), clientId, apiKey);
                string fileName = batch.Count == 1
                    ? SanitizeFileName(batch[0]) + ".pdf"
                    : "ozon-labels-" + batchId + "-" + offset + ".pdf";
                string path = Path.Combine(dayDirectory, fileName);
                File.WriteAllBytes(path, pdf);
                result.Files.Add(path);
                result.Logs.Add("Downloaded labels: " + string.Join(", ", batch.ToArray()) + " -> " + path);
                AppendDailyIndex(dayIndex, batchId, batch, path);
                summary.Add("PDF: " + path);
                summary.Add("Postings: " + string.Join(", ", batch.ToArray()));
                summary.Add(string.Empty);
            }

            File.WriteAllLines(summaryFile, summary.ToArray(), Encoding.UTF8);
            result.Logs.Add("Label batch summary: " + summaryFile);
            result.Logs.Add("Daily label index: " + dayIndex);
            return result;
        }

        public static List<string> ParsePostingNumbers(string text)
        {
            return NormalizePostingNumbers((text ?? string.Empty).Split(new char[] { '\r', '\n', ',', ';', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries));
        }

        private static List<string> NormalizePostingNumbers(IEnumerable<string> values)
        {
            List<string> result = new List<string>();
            Dictionary<string, bool> seen = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            foreach (string raw in values ?? new string[0])
            {
                string value = string.IsNullOrWhiteSpace(raw) ? string.Empty : raw.Trim();
                if (value.Length == 0 || seen.ContainsKey(value))
                {
                    continue;
                }

                seen[value] = true;
                result.Add(value);
            }

            return result;
        }

        private static string PostOzonJson(string path, string json, string clientId, string apiKey)
        {
            HttpWebRequest request = CreateOzonRequest(path, clientId, apiKey);
            byte[] body = Encoding.UTF8.GetBytes(json);
            request.ContentLength = body.Length;
            using (Stream stream = request.GetRequestStream())
            {
                stream.Write(body, 0, body.Length);
            }

            using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
            using (Stream stream = response.GetResponseStream())
            using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
            {
                return reader.ReadToEnd();
            }
        }

        private static byte[] PostOzonBinary(string path, string json, string clientId, string apiKey)
        {
            HttpWebRequest request = CreateOzonRequest(path, clientId, apiKey);
            request.Accept = "application/pdf";
            byte[] body = Encoding.UTF8.GetBytes(json);
            request.ContentLength = body.Length;
            using (Stream stream = request.GetRequestStream())
            {
                stream.Write(body, 0, body.Length);
            }

            try
            {
                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                using (Stream stream = response.GetResponseStream())
                using (MemoryStream memory = new MemoryStream())
                {
                    stream.CopyTo(memory);
                    return memory.ToArray();
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

        private static HttpWebRequest CreateOzonRequest(string path, string clientId, string apiKey)
        {
            EnsureModernTls();
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(OzonSellerApiBaseUrl + path);
            request.Method = "POST";
            request.ContentType = "application/json";
            request.Accept = "application/json";
            request.Timeout = 90000;
            request.Headers["Client-Id"] = clientId;
            request.Headers["Api-Key"] = apiKey;
            return request;
        }

        private static void EnsureModernTls()
        {
            if (_tlsInitialized)
            {
                return;
            }

            const int tls12 = 3072;
            ServicePointManager.SecurityProtocol = (SecurityProtocolType)tls12;
            ServicePointManager.Expect100Continue = false;
            _tlsInitialized = true;
        }

        private static string SanitizeFileName(string value)
        {
            char[] invalid = Path.GetInvalidFileNameChars();
            StringBuilder builder = new StringBuilder();
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                builder.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
            }

            return builder.Length == 0 ? "ozon-label" : builder.ToString();
        }

        private static void EnsureDailyIndexHeader(string path)
        {
            if (File.Exists(path))
            {
                return;
            }

            File.WriteAllText(path, "downloaded_at,batch_id,posting_numbers,pdf_path" + Environment.NewLine, Encoding.UTF8);
        }

        private static void AppendDailyIndex(string path, string batchId, IList<string> postingNumbers, string pdfPath)
        {
            string line =
                Csv(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")) + "," +
                Csv(batchId) + "," +
                Csv(string.Join(" ", postingNumbers == null ? new string[0] : ToArray(postingNumbers))) + "," +
                Csv(pdfPath) +
                Environment.NewLine;
            File.AppendAllText(path, line, Encoding.UTF8);
        }

        private static string[] ToArray(IList<string> values)
        {
            string[] result = new string[values.Count];
            for (int i = 0; i < values.Count; i++)
            {
                result[i] = values[i];
            }

            return result;
        }

        private static string Csv(string value)
        {
            string safe = value ?? string.Empty;
            return "\"" + safe.Replace("\"", "\"\"") + "\"";
        }
    }
}
