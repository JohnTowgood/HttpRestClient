using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;

namespace HttpRestClient.Generic
{
    public class RestApiClient : IDisposable
    {
        bool disposed = false;
        private Dictionary<string, string> _defaultHeaders = new Dictionary<string, string>();
        private static HttpClientHandler clientHandler = new HttpClientHandler();

        public RestApiClient(string baseUrl)
        {
            BaseUrl = baseUrl;            
        }

        public RestApiClient(int timeout)
        {
            TimeOut = timeout;
        }

        public int TimeOut { get; set; } = 60;
        public string BaseUrl { get; set; }

        protected void SetDefaultHeaders(Dictionary<string, string> headers)
        {
            foreach (KeyValuePair<string, string> header in headers)
            {
                if (_defaultHeaders.ContainsKey(header.Key))
                {
                    _defaultHeaders[header.Key] = header.Value;
                }
                else
                {
                    _defaultHeaders.Add(header.Key, header.Value);
                }
            }
        }

        protected void Reset()
        {
            _defaultHeaders = new Dictionary<string, string>();
            BaseUrl = null;
        }

        protected void SetHttpHandlerCredentials(NetworkCredential networkCredential)
        {
            clientHandler.Credentials = networkCredential;
        }

        protected virtual async Task<RestApiResponse<TResponse, TError>> GetAsync<TResponse, TError>(string url, Dictionary<string, string> headers = null)
        {
            RestApiResponse<TResponse, TError> restClientResponse = new RestApiResponse<TResponse, TError>();

            using (HttpClient httpClient = GetHttpClient(headers))
            {
                url = url ?? BaseUrl;
                HttpResponseMessage response = await HttpInvoker(() => httpClient.GetAsync(url));
                restClientResponse = ProcessResponse<TResponse, TError>(response);
            }

            return restClientResponse;
        }

        protected virtual async Task<RestApiResponse<TError>> DeleteAsync<TError>(string url, Dictionary<string, string> headers = null)
        {
            RestApiResponse<TError> restClientResponse = new RestApiResponse<TError>();

            using (HttpClient httpClient = GetHttpClient(headers))
            {
                url = url ?? BaseUrl;
                HttpResponseMessage response = await HttpInvoker(() => httpClient.DeleteAsync(url));
                restClientResponse = ProcessResponse<TError>(response);
            }

            return restClientResponse;
        }


        protected virtual async Task<RestApiResponse<TResponse, TError>> PutAsync<TRequest, TResponse, TError>(TRequest request, string url, Dictionary<string, string> headers = null)
        {
            return await GetResponseAsync<TRequest, TResponse, TError>(HttpMethod.Put.Method, request, url, headers);
        }

        protected virtual async Task<RestApiResponse<TResponse, TError>> PostAsync<TRequest, TResponse, TError>(TRequest request, string url, Dictionary<string, string> headers = null)
        {
            return await GetResponseAsync<TRequest, TResponse, TError>(HttpMethod.Post.Method, request, url, headers);
        }

        protected virtual async Task<RestApiResponse<TResponse, TError>> PatchAsync<TRequest, TResponse, TError>(TRequest request, string url, Dictionary<string, string> headers = null)
        {
            return await GetResponseAsync<TRequest, TResponse, TError>("PATCH", request, url, headers);
        }

        private async Task<RestApiResponse<TResponse, TError>> GetResponseAsync<TRequest, TResponse, TError>(string method, TRequest request, string url, Dictionary<string, string> headers)
        {
            RestApiResponse<TResponse, TError> restClientResponse = new RestApiResponse<TResponse, TError>();

            using (HttpClient httpClient = GetHttpClient(headers))
            {
                url = url ?? BaseUrl;
                HttpContent content = GetStringContent(request, headers);
                HttpRequestMessage requestMessage = new HttpRequestMessage(new HttpMethod(method), url) { Content = content };

                HttpResponseMessage response = await HttpInvoker(() => httpClient.SendAsync(requestMessage));
                restClientResponse = ProcessResponse<TResponse, TError>(response);
            }

            return restClientResponse;

        }

        protected virtual Task<HttpResponseMessage> HttpInvoker(Func<Task<HttpResponseMessage>> action)
        {
            return Task.Run(action);
        }

        private HttpContent GetStringContent<TRequest>(TRequest request, Dictionary<string, string> headers)
        {
            string requestContent = request.ToString();
            string contentType = GetContentType(headers);

            switch (contentType)
            {
                case "application/xml":
                    requestContent = SerializeXml(request);
                    break;
                case "application/json":
                    requestContent = SerializeJson(request);
                    break;
            }

            return new ContentTypeSpecificStringContent(requestContent, Encoding.UTF8, contentType);
        }

        private string GetContentType(Dictionary<string, string> headers)
        {
            string contentType = "application/json";
            if (headers != null)
            {
                if (headers.ContainsKey("Content-Type"))
                {
                    contentType = headers["Content-Type"];
                }
                else if (headers.ContainsKey("content-type"))
                {
                    contentType = headers["content-type"];
                }
            }
            return contentType;
        }

        private HttpClient GetHttpClient(Dictionary<string, string> headers)
        {
            HttpClient httpClient = new HttpClient(clientHandler, false);
            if (!string.IsNullOrEmpty(BaseUrl)) { httpClient.BaseAddress = new Uri(BaseUrl); }
            httpClient.Timeout = TimeSpan.FromSeconds(TimeOut);
            httpClient.DefaultRequestHeaders.Clear();

            if (headers != null && headers.Count > 0)
            {
                foreach (KeyValuePair<string, string> header in headers)
                {
                    httpClient.DefaultRequestHeaders.TryAddWithoutValidation(header.Key, header.Value);
                }
            }

            if (_defaultHeaders != null && _defaultHeaders.Count > 0)
            {
                foreach (KeyValuePair<string, string> header in _defaultHeaders)
                {
                    httpClient.DefaultRequestHeaders.TryAddWithoutValidation(header.Key, header.Value);
                }
            }

            return httpClient;
        }

        private RestApiResponse<TError> ProcessResponse<TError>(HttpResponseMessage response)
        {
            RestApiResponse<TError> restClientResponse = new RestApiResponse<TError>();

            restClientResponse.IsSuccessStatusCode = response.IsSuccessStatusCode;

            int statusCode = (int)response.StatusCode;
            restClientResponse.StatusCode = statusCode;

            restClientResponse.Error = ProcessErrorResponse<TError>(response);

            return restClientResponse;
        }

        private RestApiResponse<TResponse, TError> ProcessResponse<TResponse, TError>(HttpResponseMessage response)
        {
            return new RestApiResponse<TResponse, TError>(ProcessResponse<TError>(response))
            {
                Data = ProcessSuccessResponse<TResponse>(response),
                Headers = ProcessHeaderResponse(response.Headers)
            };
        }

        private Dictionary<string, string> ProcessHeaderResponse(HttpResponseHeaders responseHeaders)
        {
            Dictionary<string, string> headers = new Dictionary<string, string>();
            foreach (var header in responseHeaders)
            {
                headers.Add(header.Key, string.Join(",", header.Value));
            }

            return headers;
        }

        private TResponse ProcessSuccessResponse<TResponse>(HttpResponseMessage response)
        {
            TResponse tResponse = default(TResponse);
            if (response.IsSuccessStatusCode)
            {
                tResponse = GetResponseContent<TResponse>(response);
            }

            return tResponse;
        }

        private TError ProcessErrorResponse<TError>(HttpResponseMessage response)
        {
            TError tError = default(TError);
            if (!response.IsSuccessStatusCode)
            {
                tError = GetResponseContent<TError>(response);
            }

            return tError;
        }

        private TContent GetResponseContent<TContent>(HttpResponseMessage response)
        {
            TContent tContent = default(TContent);
            string content = response.Content.ReadAsStringAsync().Result;
            if (!string.IsNullOrEmpty(content))
            {
                if (response.Content.Headers.ContentType.MediaType == "application/json")
                {
                    tContent = DeserializeJson<TContent>(content);
                }
                else if (response.Content.Headers.ContentType.MediaType == "application/xml")
                {
                    tContent = DeserializeXml<TContent>(content);
                }
                else
                {
                    tContent = (TContent)Convert.ChangeType(content, typeof(TContent)); ;
                }
            }
            return tContent;
        }

        protected virtual T DeserializeXml<T>(string xmlString)
        {
            if (string.IsNullOrEmpty(xmlString))
            {
                throw new ArgumentNullException("xmlString");
            }

            return DeserializeXml<T>(new MemoryStream(Encoding.UTF8.GetBytes(xmlString)));
        }

        private T DeserializeXml<T>(Stream xmlStream)
        {
            var xmlSerializer = new XmlSerializer(typeof(T));

            if (xmlStream == null)
            {
                throw new ArgumentNullException("xmlStream");
            }

            return (T)xmlSerializer.Deserialize(xmlStream);
        }

        protected virtual string SerializeXml<T>(T obj)
        {
            string xml = string.Empty;
            XmlSerializer xs = new XmlSerializer(obj.GetType());
            using (MemoryStream buffer = new MemoryStream())
            {
                xs.Serialize(buffer, obj);
                xml = Encoding.UTF8.GetString(buffer.ToArray());
            }
            return xml;
        }

        protected virtual T DeserializeJson<T>(string jsonString)
        {
            T serializedObject;

            if (string.IsNullOrEmpty(jsonString))
            {
                throw new ArgumentNullException("jsonString");
            }

            using (MemoryStream ms = new MemoryStream())
            {
                //initialize DataContractJsonSerializer object and pass Student class type to it
                DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(T));

                //user stream writer to write JSON string data to memory stream
                StreamWriter sw = new StreamWriter(ms);
                sw.Write(jsonString);
                sw.Flush();

                ms.Position = 0;
                //get the Desrialized data in object of type Student
                serializedObject = (T)serializer.ReadObject(ms);
            }

            return serializedObject;
        }

        protected virtual string SerializeJson<T>(T obj)
        {
            string json = string.Empty;
            using (MemoryStream SerializememoryStream = new MemoryStream())
            {
                //initialize DataContractJsonSerializer object and pass Student class type to it
                DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(T));
                //write newly created object(NewStudent) into memory stream
                serializer.WriteObject(SerializememoryStream, obj);

                json = Encoding.Default.GetString(SerializememoryStream.ToArray());
            }
            return json;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        // Protected implementation of Dispose pattern.
        protected virtual void Dispose(bool disposing)
        {
            if (disposed)
                return;

            if (disposing)
            {
                clientHandler.Dispose();
                _defaultHeaders = null;
            }

            disposed = true;
        }

    }

    internal class ContentTypeSpecificStringContent : StringContent
    {
        /// <summary>
        /// Ensure content type is reset after base class mucks it up.
        /// </summary>
        /// <param name="content">Content to send</param>
        /// <param name="encoding">Encoding to use</param>
        /// <param name="contentType">Content type to use</param>
        public ContentTypeSpecificStringContent(string content, Encoding encoding, string contentType) : base(content, encoding, contentType)
        {
            Headers.ContentType = new MediaTypeHeaderValue(contentType);
        }
    }

    public class RestApiResponse<TError>
    {
        public bool IsSuccessStatusCode { get; set; }

        public int StatusCode { get; set; }

        public TError Error { get; set; }

        public string OtherError { get; set; }
    }

    public class RestApiResponse<TResponse, TError> : RestApiResponse<TError>
    {
        public RestApiResponse() { }

        public RestApiResponse(RestApiResponse<TError> response)
        {
            IsSuccessStatusCode = response.IsSuccessStatusCode;
            StatusCode = response.StatusCode;
            Error = response.Error;
            OtherError = response.OtherError;
        }

        public TResponse Data { get; set; }

        public Dictionary<string, string> Headers { get; set; }
    }
}
