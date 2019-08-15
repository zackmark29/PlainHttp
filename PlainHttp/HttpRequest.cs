﻿using Flurl.Util;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Serialization;

namespace PlainHttp
{
    /// <summary>
    /// A wrapper for making HTTP requests simpler.
    /// Handles serialization, proxy, timeout, file download
    /// </summary>
    public class HttpRequest : IHttpRequest
    {
        public HttpMethod Method { get; set; } = HttpMethod.Get;

        public Uri Uri { get; set; }

        public static IHttpClientFactory HttpClientFactory { get; set; }
            = new HttpClientFactory();

        public HttpRequestMessage Message { get; protected set; }

        public TimeSpan Timeout { get; set; }
            = TimeSpan.Zero;

        public Dictionary<string, string> Headers { get; set; }
            = new Dictionary<string, string>();

        public Uri Proxy { get; set; }

        public object Payload { get; set; }

        public ContentType ContentType { get; set; }

        public string DownloadFileName { get; set; }

        public Encoding ResponseEncoding { get; set; }

        private static AsyncLocal<TestingMode> testingMode
            = new AsyncLocal<TestingMode>();

        public HttpRequest()
        {
        }

        public HttpRequest(Uri uri)
        {
            this.Uri = uri;
        }

        public HttpRequest(string url)
        {
            this.Uri = new Uri(url);
        }

        public async Task<HttpResponse> SendAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            if (testingMode.Value != null)
            {
                return await MockedResponse();
            }

            HttpClient client;

            if (this.Proxy != null)
            {
                client = HttpClientFactory.GetProxiedClient(this.Proxy);
            }
            else
            {
                client = HttpClientFactory.GetClient(this.Uri);
            }

            HttpRequestMessage requestMessage = new HttpRequestMessage
            {
                Method = this.Method,
                RequestUri = this.Uri
            };

            // Add the headers to the request
            foreach (string headerName in this.Headers.Keys)
            {
                requestMessage.Headers.TryAddWithoutValidation(headerName, this.Headers[headerName]);
            }

            // Save the HttpRequestMessage
            this.Message = requestMessage;

            var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            // Enable timeout, if set
            if (this.Timeout != TimeSpan.Zero)
            {
                cts.CancelAfter(this.Timeout);
            }
            
            try
            {
                HttpResponseMessage responseMessage;

                // Serialize the payload
                if (this.Payload != null)
                {
                    if (this.ContentType == ContentType.Json)
                    {
                        SerializeToJson(requestMessage);
                    }
                    else if (this.ContentType == ContentType.Xml)
                    {
                        SerializeToXml(requestMessage);
                    }
                    else if (this.ContentType == ContentType.UrlEncoded)
                    {
                        SerializeToUrlEncoded(requestMessage);
                    }
                    // Raw
                    else
                    {
                        requestMessage.Content = new StringContent(this.Payload.ToString());
                    }
                }

                // Send the request
                responseMessage = await client.SendAsync(requestMessage, cts.Token);

                // Wrap the content into an HttpResponse instance,
                // also reading the body (string or file)
                return await CreateHttpResponse(responseMessage);
            }
            catch (Exception ex)
            {
                if (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested)
                {
                    throw new HttpRequestTimeoutException(this, ex);
                }

                throw new HttpRequestException(this, ex);
            }
        }

        private async Task<HttpResponse> CreateHttpResponse(HttpResponseMessage responseMessage)
        {
            // No file name, given read the body of the response as string
            if (this.DownloadFileName == null)
            {
                string body;

                if (this.ResponseEncoding != null)
                {
                    byte[] array = await responseMessage.Content.ReadAsByteArrayAsync();
                    body = this.ResponseEncoding.GetString(array);
                }
                else
                {
                    body = await responseMessage.Content.ReadAsStringAsync();
                }

                return new HttpResponse(this, responseMessage, body);
            }
            // Copy the response to a file
            else
            {
                using (Stream stream = await responseMessage.Content.ReadAsStreamAsync())
                using (FileStream fs = new FileStream(this.DownloadFileName, FileMode.Create, FileAccess.Write))
                {
                    await stream.CopyToAsync(fs);
                }

                return new HttpResponse(this, responseMessage);
            }
        }

        private async Task<HttpResponse> MockedResponse()
        {
            // Get the testing mode instance for this async context
            HttpResponseMessage message = testingMode.Value.RequestsQueue.Dequeue();

            return await CreateHttpResponse(message);
        }

        private void SerializeToUrlEncoded(HttpRequestMessage requestMessage)
        {
            // Already serialized
            if (this.Payload is string)
            {
                string serialized = this.Payload.ToString();

                requestMessage.Content = new StringContent(
                    content: this.Payload.ToString(),
                    encoding: Encoding.UTF8,
                    mediaType: "application/x-www-form-urlencoded"
                );
            }
            else
            {
                var qp = new Flurl.QueryParamCollection();

                foreach (KeyValuePair<string, object> pair in this.Payload.ToKeyValuePairs())
                {
                    qp.Merge(pair.Key, pair.Value, false, Flurl.NullValueHandling.Ignore);
                }

                string serialized = qp.ToString(true);

                requestMessage.Content = new StringContent(
                    content: serialized,
                    encoding: Encoding.UTF8,
                    mediaType: "application/x-www-form-urlencoded"
                );
            }
        }

        private void SerializeToXml(HttpRequestMessage requestMessage)
        {
            string serialized;

            // Already serialized
            if (this.Payload is string)
            {
                serialized = this.Payload.ToString();
            }
            else
            {
                XmlSerializer serializer = new XmlSerializer(this.Payload.GetType());
                StringBuilder result = new StringBuilder();

                using (var writer = XmlWriter.Create(result))
                {
                    serializer.Serialize(writer, this.Payload);
                }

                serialized = result.ToString();
            }

            requestMessage.Content = new StringContent(
                content: this.Payload.ToString(),
                encoding: Encoding.UTF8,
                mediaType: "text/xml"
            );
        }

        private void SerializeToJson(HttpRequestMessage requestMessage)
        {
            string serialized;

            // Already serialized
            if (this.Payload is string)
            {
                serialized = this.Payload.ToString();
            }
            else
            {
                serialized = JsonConvert.SerializeObject(this.Payload);
            }

            requestMessage.Content = new StringContent(
                content: serialized,
                encoding: Encoding.UTF8,
                mediaType: "application/json"
            );
        }

        public override string ToString()
        {
            return $"{this.Method} {this.Uri}";
        }

        public static void SetTestingMode(TestingMode t)
        {
            testingMode.Value = t;
        }
    }
}