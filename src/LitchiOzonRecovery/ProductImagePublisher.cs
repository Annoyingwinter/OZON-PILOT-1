using System;
using System.IO;
using System.Net;
using System.Text;
using Newtonsoft.Json.Linq;

namespace LitchiOzonRecovery
{
    public interface IProductImagePublisher
    {
        string Publish(string localImagePath);
    }

    public sealed class HttpProductImagePublisherOptions
    {
        public string UploadUrl { get; set; }
        public string PublicBaseUrl { get; set; }
        public string ApiToken { get; set; }
        public int TimeoutMs { get; set; }

        public static HttpProductImagePublisherOptions FromEnvironment()
        {
            return new HttpProductImagePublisherOptions
            {
                UploadUrl = FirstNonEmpty(
                    Environment.GetEnvironmentVariable("OZON_IMAGE_UPLOAD_URL"),
                    "http://47.76.248.181/ozon-upload"),
                PublicBaseUrl = FirstNonEmpty(
                    Environment.GetEnvironmentVariable("OZON_IMAGE_PUBLIC_BASE_URL"),
                    "http://47.76.248.181/ozon-images/"),
                ApiToken = Environment.GetEnvironmentVariable("OZON_IMAGE_UPLOAD_TOKEN"),
                TimeoutMs = 120000
            };
        }

        private static string FirstNonEmpty(params string[] values)
        {
            for (int i = 0; i < values.Length; i++)
            {
                if (!string.IsNullOrWhiteSpace(values[i]))
                {
                    return values[i].Trim();
                }
            }

            return string.Empty;
        }
    }

    public sealed class HttpProductImagePublisher : IProductImagePublisher
    {
        private readonly HttpProductImagePublisherOptions _options;

        public HttpProductImagePublisher(HttpProductImagePublisherOptions options)
        {
            _options = options ?? HttpProductImagePublisherOptions.FromEnvironment();
        }

        public string Publish(string localImagePath)
        {
            if (string.IsNullOrWhiteSpace(localImagePath) || !File.Exists(localImagePath))
            {
                throw new FileNotFoundException("Image file does not exist.", localImagePath);
            }

            string response = PostMultipart(localImagePath);
            JObject root = JObject.Parse(response);
            string url = Convert.ToString(root["url"] ?? root["public_url"] ?? string.Empty);
            if (!string.IsNullOrWhiteSpace(url))
            {
                return url;
            }

            string fileName = Convert.ToString(root["file"] ?? root["filename"] ?? string.Empty);
            if (!string.IsNullOrWhiteSpace(fileName) && !string.IsNullOrWhiteSpace(_options.PublicBaseUrl))
            {
                return _options.PublicBaseUrl.TrimEnd('/') + "/" + Uri.EscapeDataString(fileName);
            }

            throw new InvalidOperationException("Upload response did not include url or file name: " + response);
        }

        private string PostMultipart(string localImagePath)
        {
            string boundary = "----OzonPilotUploadBoundary" + Guid.NewGuid().ToString("N");
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(_options.UploadUrl);
            request.Method = "POST";
            request.Accept = "application/json";
            request.ContentType = "multipart/form-data; boundary=" + boundary;
            request.Timeout = _options.TimeoutMs <= 0 ? 120000 : _options.TimeoutMs;
            if (!string.IsNullOrWhiteSpace(_options.ApiToken))
            {
                request.Headers["X-Upload-Token"] = _options.ApiToken;
            }

            using (Stream stream = request.GetRequestStream())
            {
                WriteString(stream, "--" + boundary + "\r\n");
                WriteString(stream, "Content-Disposition: form-data; name=\"file\"; filename=\"" + Path.GetFileName(localImagePath) + "\"\r\n");
                WriteString(stream, "Content-Type: " + DetectImageContentType(localImagePath) + "\r\n\r\n");
                byte[] fileBytes = File.ReadAllBytes(localImagePath);
                stream.Write(fileBytes, 0, fileBytes.Length);
                WriteString(stream, "\r\n--" + boundary + "--\r\n");
            }

            return ReadResponse(request);
        }

        private static void WriteString(Stream stream, string value)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value);
            stream.Write(bytes, 0, bytes.Length);
        }

        private static string DetectImageContentType(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".jpg" || ext == ".jpeg")
            {
                return "image/jpeg";
            }

            if (ext == ".webp")
            {
                return "image/webp";
            }

            return "image/png";
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
    }
}
