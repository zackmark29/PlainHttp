﻿using System.Linq;
using System.Net.Http;

namespace PlainHttp
{
    /// <summary>
    /// Wraps the HTTP response information
    /// </summary>
    public class HttpResponse : IHttpResponse
    {
        public HttpResponseMessage Message { get; set; }

        public string Body { get; set; }

        public HttpRequest Request { get; set; }

        public bool Succeeded
        {
            get
            {
                return this.Message.IsSuccessStatusCode;
            }
        }

        public HttpResponse(HttpRequest request, HttpResponseMessage message)
        {
            this.Request = request;
            this.Message = message;
        }

        public HttpResponse(HttpRequest request, HttpResponseMessage message, string body)
            : this(request, message)
        {
            this.Request = request;
            this.Message = message;
            this.Body = body;
        }

        public string GetSingleHeader(string name)
        {
            if (this.Message.Headers.TryGetValues(name, out var values) ||
                this.Message.Content.Headers.TryGetValues(name, out values))
            {
                return values.FirstOrDefault();
            }
            else
            {
                return null;
            }
        }
    }
}
