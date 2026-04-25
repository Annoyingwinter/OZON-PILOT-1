using System;
using System.IO;
using System.Net;
using System.Text;

namespace LitchiAutoUpdate
{
    internal static class HttpHelper
    {
        public static string LoadUpdateManifest(string url)
        {
            try
            {
                return Post(url);
            }
            catch
            {
                return Get(url);
            }
        }

        private static string Post(string url)
        {
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = "POST";
            request.ContentType = "application/x-www-form-urlencoded";
            request.Proxy = null;

            byte[] body = Encoding.UTF8.GetBytes(string.Empty);
            request.ContentLength = body.Length;
            using (Stream requestStream = request.GetRequestStream())
            {
                requestStream.Write(body, 0, body.Length);
            }

            using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
            using (StreamReader reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
            {
                return reader.ReadToEnd();
            }
        }

        private static string Get(string url)
        {
            using (WebClient client = new WebClient())
            {
                client.Proxy = null;
                client.Encoding = Encoding.UTF8;
                return client.DownloadString(url);
            }
        }
    }
}
