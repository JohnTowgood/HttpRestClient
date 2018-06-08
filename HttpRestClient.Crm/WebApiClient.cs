using Newtonsoft.Json;
using Polly;
using Polly.Retry;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using HttpRestClient.Crm.Models;
using System.Text.RegularExpressions;
using HttpRestClient.Generic;
using Action = HttpRestClient.Crm.Models.Action;

namespace HttpRestClient.Crm
{
    public class WebApiClient : RestApiClient, IWebApiClient
    {
        private enum AuthenticationProviderType
        {
            None = 0,
            ActiveDirectory = 1,
            Federation = 2,
            LiveId = 3,
            OnlineFederation = 4,
            ClientCredentials = 5
        }

        private const int HTTPSTATUS_UNAUTHORIZED = (int)HttpStatusCode.Unauthorized;
        private const int DEFAULT_TIMEOUT = 60;//seconds
        private Dictionary<string, string> _entitySetNames = new Dictionary<string, string>();


        public WebApiClient(string resource, NetworkCredential networkCredential) : base(DEFAULT_TIMEOUT)
        {
            AuthenticatedCredentials = new AuthenticationCredentials
            {
                Resource = resource,
                NetworkCredential = networkCredential
            };
            Initialize();
        }

        public WebApiClient(string resource, NetworkCredential networkCredential, int retry) : base(DEFAULT_TIMEOUT)
        {
            AuthenticatedCredentials = new AuthenticationCredentials
            {
                Resource = resource,
                NetworkCredential = networkCredential
            };

            CreateRetryPolicy(retry);

            Initialize();
        }

        public WebApiClient(int timeout, string resource, NetworkCredential networkCredential) : base(timeout)
        {
            AuthenticatedCredentials = new AuthenticationCredentials
            {
                Resource = resource,
                NetworkCredential = networkCredential
            };
            Initialize();
        }

        public WebApiClient(string clientId, string resource, NetworkCredential networkCredential) : base(DEFAULT_TIMEOUT)
        {
            AuthenticatedCredentials = new AuthenticationCredentials
            {
                ClientId = clientId,
                Resource = resource,
                NetworkCredential = networkCredential
            };
            Initialize();
        }

        public WebApiClient(int timeout, string clientId, string resource, NetworkCredential networkCredential) : base(timeout)
        {
            AuthenticatedCredentials = new AuthenticationCredentials
            {
                ClientId = clientId,
                Resource = resource,
                NetworkCredential = networkCredential
            };
            Initialize();
        }

        public WebApiClient(string tenant, string clientId, string clientSecret, string resource) : base(DEFAULT_TIMEOUT)
        {
            AuthenticatedCredentials = new AuthenticationCredentials
            {
                Tenant = tenant,
                ClientId = clientId,
                ClientSecret = clientSecret,
                Resource = resource
            };
            Initialize();
        }

        public WebApiClient(int timeout, string tenant, string clientId, string clientSecret, string resource) : base(timeout)
        {
            AuthenticatedCredentials = new AuthenticationCredentials
            {
                Tenant = tenant,
                ClientId = clientId,
                ClientSecret = clientSecret,
                Resource = resource,
            };
            Initialize();
        }

        private AuthenticationCredentials AuthenticatedCredentials { get; set; }

        private AuthenticationProviderType AuthenticationType { get; set; }

        private RetryPolicy<HttpResponseMessage> RetryPolicy { get; set; }

        private void CreateRetryPolicy(int retry)
        {
            // Handle both exceptions and return values in one policy
            HttpStatusCode[] _httpStatusCodesWorthRetrying = { HttpStatusCode.RequestTimeout, HttpStatusCode.InternalServerError, HttpStatusCode.BadGateway,
                                                                HttpStatusCode.ServiceUnavailable, HttpStatusCode.GatewayTimeout };

            RetryPolicy = Policy.Handle<HttpRequestException>()
                                .OrResult<HttpResponseMessage>(r => _httpStatusCodesWorthRetrying.Contains(r.StatusCode))
                                .WaitAndRetryAsync(retry,
                                                    retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                                                    (exception, timeSpan, retryCount, context) =>
                                                    {
                                                        var msg = $"Retry {retryCount} implemented with Pollys RetryPolicy " +
                                                        $"of {context.PolicyKey} " +
                                                        $"due to: {exception}.";
                                                        Console.WriteLine(msg);
                                                    });
        }

        protected override Task<HttpResponseMessage> HttpInvoker(Func<Task<HttpResponseMessage>> action)
        {
            if (RetryPolicy != null)
            {
                return RetryPolicy.ExecuteAsync(() => action());
            }
            else
            {
                return Task.Run(action);
            }
        }

        private void Initialize()
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;//Set to work with TLS 1.2, need for Dynamics 365

            AuthenticatedCredentials.UserRealm = GetUserRealm(AuthenticatedCredentials.NetworkCredential?.UserName);

            if (!string.IsNullOrEmpty(AuthenticatedCredentials.NetworkCredential?.Domain))
            {
                AuthenticationType = AuthenticationProviderType.ActiveDirectory;
            }
            else if (AuthenticatedCredentials.UserRealm != null && AuthenticatedCredentials.UserRealm.account_type.Equals("Federated", StringComparison.InvariantCultureIgnoreCase))
            {
                AuthenticationType = AuthenticationProviderType.OnlineFederation;
            }
            else if (AuthenticatedCredentials.UserRealm != null && AuthenticatedCredentials.UserRealm.account_type.Equals("Managed", StringComparison.InvariantCultureIgnoreCase))
            {
                AuthenticationType = AuthenticationProviderType.LiveId;
            }
            else if (!string.IsNullOrEmpty(AuthenticatedCredentials.ClientSecret))
            {
                AuthenticationType = AuthenticationProviderType.ClientCredentials;
            }
            else
            {
                AuthenticationType = AuthenticationProviderType.Federation;
            }

            Authenticate();
        }

        public void SetupEntitySetNames()
        {
            if (_entitySetNames.Count == 0)
            {
                string query = "EntityDefinitions?$select=LogicalName,EntitySetName&$filter=IsPrivate eq false";
                RestApiResponse<EntityCollection, Error> response = GetAsync<EntityCollection, Error>(query).Result;
                if (response.IsSuccessStatusCode)
                {
                    response.Data.Entities.ForEach(e =>
                    {
                        _entitySetNames.Add(e.Get<string>("LogicalName"), e.Get<string>("EntitySetName"));
                    });
                }
            }
        }

        private void Authenticate()
        {
            Reset();
            switch (AuthenticationType)
            {
                case AuthenticationProviderType.ActiveDirectory:
                    AuthenticateCredentials();
                    break;
                case AuthenticationProviderType.Federation:
                case AuthenticationProviderType.OnlineFederation:
                    AuthenticateFederatedRealmCredentials();
                    break;
                case AuthenticationProviderType.LiveId:
                    AuthenticateLiveIdCredentials();
                    break;
                case AuthenticationProviderType.ClientCredentials:
                    AuthenticateClientCredentials();
                    break;
                default:
                    throw new NotSupportedException(string.Format("{0} authentication type is not supported", AuthenticationType));
            }
        }

        private void AuthenticateCredentials()
        {
            SetHttpHandlerCredentials(AuthenticatedCredentials.NetworkCredential);
        }

        private void AuthenticateClientCredentials()
        {
            string request = string.Format("client_id={0}&resource={1}&client_secret={2}&grant_type=client_credentials",
                                            AuthenticatedCredentials.ClientId, Uri.EscapeDataString(AuthenticatedCredentials.Resource),
                                            Uri.EscapeDataString(AuthenticatedCredentials.ClientSecret));

            if (!string.IsNullOrEmpty(AuthenticatedCredentials?.SecurityToken?.refresh_token))
            {
                request = string.Format("client_id={0}&resource={1}&refresh_token={2}&grant_type=refresh_token",
                                       AuthenticatedCredentials.ClientId, Uri.EscapeDataString(AuthenticatedCredentials.Resource),
                                       AuthenticatedCredentials.SecurityToken.refresh_token);
            }

            string oauthUrl = string.Format("https://login.microsoftonline.com/{0}/oauth2/token", AuthenticatedCredentials.Tenant);

            SetSecurityToken(request, oauthUrl);
        }

        private void AuthenticateFederatedRealmCredentials()
        {
            if (!string.IsNullOrEmpty(AuthenticatedCredentials?.SecurityToken?.refresh_token))
            {
                string request = string.Format("client_id={0}&resource={1}&refresh_token={2}&grant_type=refresh_token",
                                      AuthenticatedCredentials.ClientId,
                                      Uri.EscapeDataString(AuthenticatedCredentials.Resource),
                                      AuthenticatedCredentials.SecurityToken.refresh_token);

                SetSecurityToken(request);
            }
            else
            {
                string adfsUrl = AuthenticatedCredentials.UserRealm.federation_active_auth_url.Replace("2005", "13");

                StringBuilder sbSoapXml = new StringBuilder();
                sbSoapXml.Append("<s:Envelope xmlns:s='http://www.w3.org/2003/05/soap-envelope' xmlns:a='http://www.w3.org/2005/08/addressing' xmlns:u='http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-utility-1.0.xsd'>");
                sbSoapXml.Append("<s:Header>");
                sbSoapXml.Append("<a:Action s:mustUnderstand='1'>http://docs.oasis-open.org/ws-sx/ws-trust/200512/RST/Issue</a:Action>");
                sbSoapXml.Append("<a:messageID>urn:uuid:" + Guid.NewGuid().ToString() + "</a:messageID>");
                sbSoapXml.Append("<a:ReplyTo><a:Address>http://www.w3.org/2005/08/addressing/anonymous</a:Address></a:ReplyTo>");
                sbSoapXml.Append("<a:To s:mustUnderstand='1'>" + adfsUrl + "</a:To>");
                sbSoapXml.Append("<o:Security s:mustUnderstand='1' xmlns:o='http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-secext-1.0.xsd'>");
                sbSoapXml.Append("<u:Timestamp u:Id='_0'>");
                sbSoapXml.Append("<u:Created>" + DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ") + "</u:Created>");
                sbSoapXml.Append("<u:Expires>" + DateTime.UtcNow.AddMinutes(10).ToString("yyyy-MM-ddTHH:mm:ss.fffZ") + "</u:Expires>");
                sbSoapXml.Append("</u:Timestamp>");
                sbSoapXml.Append("<o:UsernameToken u:Id='uuid-" + Guid.NewGuid().ToString() + "'>");
                sbSoapXml.Append("<o:Username>" + AuthenticatedCredentials.NetworkCredential.UserName + "</o:Username>");
                sbSoapXml.Append("<o:Password>" + AuthenticatedCredentials.NetworkCredential.Password + "</o:Password>");
                sbSoapXml.Append("</o:UsernameToken>");
                sbSoapXml.Append("</o:Security>");
                sbSoapXml.Append("</s:Header>");
                sbSoapXml.Append("<s:Body>");
                sbSoapXml.Append("<trust:RequestSecurityToken xmlns:trust='http://docs.oasis-open.org/ws-sx/ws-trust/200512'>");
                sbSoapXml.Append("<wsp:AppliesTo xmlns:wsp='http://schemas.xmlsoap.org/ws/2004/09/policy'>");
                sbSoapXml.Append("<a:EndpointReference>");
                sbSoapXml.Append("<a:Address>" + AuthenticatedCredentials.UserRealm.cloud_audience_urn + "</a:Address>");
                sbSoapXml.Append("</a:EndpointReference>");
                sbSoapXml.Append("</wsp:AppliesTo>");
                sbSoapXml.Append("<trust:KeyType>http://docs.oasis-open.org/ws-sx/ws-trust/200512/Bearer</trust:KeyType>");
                sbSoapXml.Append("<trust:RequestType>http://docs.oasis-open.org/ws-sx/ws-trust/200512/Issue</trust:RequestType>");
                sbSoapXml.Append("</trust:RequestSecurityToken>");
                sbSoapXml.Append("</s:Body>");
                sbSoapXml.Append("</s:Envelope>");

                Dictionary<string, string> headers = new Dictionary<string, string> { { "Content-Type", "application/soap+xml" }, { "SOAPAction", "http://docs.oasis-open.org/ws-sx/ws-trust/200512/RST/Issue" } };
                RestApiResponse<string, Error> adfsResponse = base.PostAsync<string, string, Error>(sbSoapXml.ToString(), adfsUrl, headers).Result;

                if (adfsResponse.IsSuccessStatusCode)
                {
                    XDocument xDoc = XDocument.Load(new StringReader(adfsResponse.Data));

                    XNamespace xmlns1 = "http://www.w3.org/2003/05/soap-envelope";
                    XNamespace xmlns2 = "http://docs.oasis-open.org/ws-sx/ws-trust/200512";

                    string saml = xDoc.Descendants(xmlns1 + "Envelope")
                                    .Elements(xmlns1 + "Body")
                                    .Elements(xmlns2 + "RequestSecurityTokenResponseCollection")
                                    .Elements(xmlns2 + "RequestSecurityTokenResponse")
                                    .Elements(xmlns2 + "RequestedSecurityToken")
                                    .First().FirstNode.ToString(SaveOptions.DisableFormatting);//remove indent

                    byte[] samlBytes = Encoding.UTF8.GetBytes(saml);
                    string samlToken = Convert.ToBase64String(samlBytes);

                    string request = string.Format("client_id={0}&resource={1}&grant_type={2}&assertion={3}&scope=openid",
                                                AuthenticatedCredentials.ClientId,
                                                Uri.EscapeDataString(AuthenticatedCredentials.Resource),
                                                Uri.EscapeDataString("urn:ietf:params:oauth:grant-type:saml1_1-bearer"),
                                                Uri.EscapeDataString(samlToken));

                    SetSecurityToken(request);
                }
            }
        }

        private void AuthenticateLiveIdCredentials()
        {
            string request = string.Format("client_id={0}&resource={1}&username={2}&password={3}&grant_type=password",
                                            AuthenticatedCredentials.ClientId, Uri.EscapeDataString(AuthenticatedCredentials.Resource),
                                            Uri.EscapeDataString(AuthenticatedCredentials.NetworkCredential.UserName),
                                            Uri.EscapeDataString(AuthenticatedCredentials.NetworkCredential.Password));

            if (!string.IsNullOrEmpty(AuthenticatedCredentials?.SecurityToken?.refresh_token))
            {
                request = string.Format("client_id={0}&resource={1}&refresh_token={2}&grant_type=refresh_token",
                                       AuthenticatedCredentials.ClientId, Uri.EscapeDataString(AuthenticatedCredentials.Resource),
                                       AuthenticatedCredentials.SecurityToken.refresh_token);
            }

            SetSecurityToken(request);
        }

        private void SetSecurityToken(string request, string oauthUrl = null)
        {
            oauthUrl = oauthUrl ?? "https://login.microsoftonline.com/common/oauth2/token";

            Dictionary<string, string> headers = new Dictionary<string, string> { { "Accept", "application/json" }, { "Content-Type", "application/x-www-form-urlencoded" } };

            RestApiResponse<SecurityToken, Error> response = base.PostAsync<string, SecurityToken, Error>(request, oauthUrl, headers).Result;

            AuthenticatedCredentials.SecurityToken = response.Data;

            string token = string.Format("{0} {1}", AuthenticatedCredentials.SecurityToken.token_type, AuthenticatedCredentials.SecurityToken.access_token);
            headers = new Dictionary<string, string> { { "Authorization", token }, { "OData-MaxVersion", "4.0" }, { "OData-Version", "4.0" } };

            SetDefaultHeaders(headers);
        }

        private void RefreshSecurityToken()
        {
            string tempBaseUrl = BaseUrl;
            Authenticate();
            BaseUrl = tempBaseUrl;
        }

        private UserRealm GetUserRealm(string username)
        {
            UserRealm userRealm = null;

            if (!string.IsNullOrEmpty(username))
            {
                string userRealmUrl = string.Format("https://login.windows.net/common/UserRealm/{0}?api-version={1}", username, "1.0");

                Dictionary<string, string> headers = new Dictionary<string, string> { { "Accept", "application/json" } };

                RestApiResponse<UserRealm, Error> response = base.GetAsync<UserRealm, Error>(userRealmUrl, headers).Result;

                if (response.IsSuccessStatusCode)
                {
                    userRealm = response.Data;
                }
            }

            return userRealm;
        }

        public async Task<EntityCollection> RetrieveMultipleAsync(string query)
        {
            RestApiResponse<EntityCollection, Exceptions> response = await GetAsync<EntityCollection, Exceptions>(query);

            return new EntityCollection { Entities = response.Data?.Entities, Exceptions = response.Error, IsSuccessStatusCode = response.IsSuccessStatusCode };
        }

        private string GetEntitySetName(string entityName)
        {
            return _entitySetNames.ContainsKey(entityName) ? _entitySetNames[entityName] : entityName;
        }

        public async Task<Entity> RetrieveAsync(string entityName, Guid id, ColumnSet columnSet)
        {
            
            string query = $"{GetEntitySetName(entityName)}({id})?$select={string.Join(",", columnSet.Columns)}";

            RestApiResponse<Entity, Exceptions> response = await GetAsync<Entity, Exceptions>(query);

            Entity returnEntity = response.Data ?? new Entity();
            returnEntity.LogicalName = entityName;
            returnEntity.Exceptions = response.Error;
            returnEntity.IsSuccessStatusCode = response.IsSuccessStatusCode;

            return returnEntity;
        }

        public Guid Create(Entity entity)
        {
            string query = $"{GetEntitySetName(entity.LogicalName)}";
            RestApiResponse<Guid, Exceptions> response = PostAsync<Entity, Guid, Exceptions>(FormatEntity(entity), query).Result;
            Guid id = Guid.Empty;

            if (response.Headers.ContainsKey("OData-EntityId"))
            {
                string entityIdHeaderValue = response.Headers["OData-EntityId"];
                MatchCollection matches = Regex.Matches(entityIdHeaderValue, @"(\{){0,1}[0-9a-fA-F]{8}\-[0-9a-fA-F]{4}\-[0-9a-fA-F]{4}\-[0-9a-fA-F]{4}\-[0-9a-fA-F]{12}(\}){0,1}");
                if (matches.Count == 1)
                {
                    id = new Guid(matches[0].Value);
                }
            }
            return id;
        }

        private Entity FormatEntity(Entity originalEntity)
        {
            Entity formattedEntity = new Entity(originalEntity.LogicalName);
            foreach (var attribute in originalEntity)
            {
                var obj = attribute.Value;
                if (obj != null)
                {
                    Type objType = obj.GetType();

                    if (objType == typeof(EntityReference))
                    {
                        EntityReference eRef = obj as EntityReference;
                        formattedEntity[$"{attribute.Key}@odata.bind"] = $"/{GetEntitySetName(eRef.LogicalName)}({eRef.Id})";
                    }
                    else
                    {
                        formattedEntity[attribute.Key] = attribute.Value;
                    }
                }
                else
                {
                    formattedEntity[attribute.Key] = attribute.Value;
                }
            }
            return formattedEntity;
        }

        private Action FormatAction(Action originalAction)
        {
            
            Action formattedAction = new Action(originalAction.ActionName);
            foreach (var attribute in originalAction)
            {
                var obj = attribute.Value;
                if (obj != null)
                {
                    Type objType = obj.GetType();

                    if (objType == typeof(EntityReference))
                    {
                        EntityReference eRef = obj as EntityReference;
                        string idname = eRef.LogicalName == "phonetocaseprocess" ? "businessprocessflowinstanceid" : $"{eRef.LogicalName}id";

                        formattedAction[attribute.Key] = new Dictionary<string, string> { { idname, eRef.Id.ToString() },{ "@odata.type", $"Microsoft.Dynamics.CRM.{eRef.LogicalName}" } };
                    }
                    else
                    {
                        formattedAction[attribute.Key] = attribute.Value;
                    }
                }
                else
                {
                    formattedAction[attribute.Key] = attribute.Value;
                }
            }
            return formattedAction;
        }

        public Guid Upsert(Entity entity)
        {
            string query = $"{GetEntitySetName(entity.LogicalName)}({entity.Id})";
            RestApiResponse<Guid, Exceptions> response = PatchAsync<Entity, Guid, Exceptions>(entity, query).Result;

            return response.Data;
        }

        public Entity Upsert(Entity entity, ColumnSet columnSet)
        {
            string query = $"{GetEntitySetName(entity.LogicalName)}({entity.Id})?$select={string.Join(",", columnSet.Columns)}";
            Dictionary<string, string> headers = new Dictionary<string, string> { { "Prefer", "return=representation" } };

            RestApiResponse<Entity, Exceptions> response = PatchAsync<Entity, Entity, Exceptions>(entity, query, headers).Result;

            Entity returnEntity = response.Data ?? new Entity();
            returnEntity.LogicalName = entity.LogicalName;
            returnEntity.Exceptions = response.Error;
            returnEntity.IsSuccessStatusCode = response.IsSuccessStatusCode;

            return returnEntity;
        }

        public void Update(Entity entity)
        {
            string query = $"{GetEntitySetName(entity.LogicalName)}({entity.Id})";
            RestApiResponse<Guid, Exceptions> response = PatchAsync<Entity, Guid, Exceptions>(FormatEntity(entity), query).Result;
        }

        public Entity Update(Entity entity, ColumnSet columnSet)
        {
            string query = $"{GetEntitySetName(entity.LogicalName)}({entity.Id})?$select={string.Join(",", columnSet.Columns)}";
            Dictionary<string, string> headers = new Dictionary<string, string> { { "Prefer", "return=representation" } };

            RestApiResponse<Entity, Exceptions> response = PatchAsync<Entity, Entity, Exceptions>(FormatEntity(entity), query, headers).Result;

            Entity returnEntity = response.Data ?? new Entity();
            returnEntity.LogicalName = entity.LogicalName;
            returnEntity.Exceptions = response.Error;
            returnEntity.IsSuccessStatusCode = response.IsSuccessStatusCode;

            return returnEntity;
        }

        public void Delete(string entityName, Guid id)
        {
            string query = $"{GetEntitySetName(entityName)}({id})";
            RestApiResponse<Exceptions> response = DeleteAsync<Exceptions>(query).Result;
        }

        public void Execute(Action action)
        {
            RestApiResponse<Entity, Exceptions> response = PostAsync<Action, Entity, Exceptions>(FormatAction(action), action.ActionName).Result;
        }

        protected override async Task<RestApiResponse<TResponse, TError>> GetAsync<TResponse, TError>(string url, Dictionary<string, string> headers = null)
        {
            var response = await base.GetAsync<TResponse, TError>(url, headers);
            if (!response.IsSuccessStatusCode && response.StatusCode == HTTPSTATUS_UNAUTHORIZED)
            {
                RefreshSecurityToken();
                response = await base.GetAsync<TResponse, TError>(url, headers);
            }
            return response;
        }

        protected override async Task<RestApiResponse<TError>> DeleteAsync<TError>(string url, Dictionary<string, string> headers = null)
        {
            var response = await base.DeleteAsync<TError>(url, headers);
            if (!response.IsSuccessStatusCode && response.StatusCode == HTTPSTATUS_UNAUTHORIZED)
            {
                RefreshSecurityToken();
                response = await base.DeleteAsync<TError>(url, headers);
            }
            return response;
        }

        protected override async Task<RestApiResponse<TResponse, TError>> PutAsync<TRequest, TResponse, TError>(TRequest request, string url, Dictionary<string, string> headers = null)
        {
            var response = await base.PutAsync<TRequest, TResponse, TError>(request, url, headers);
            if (!response.IsSuccessStatusCode && response.StatusCode == HTTPSTATUS_UNAUTHORIZED)
            {
                RefreshSecurityToken();
                response = await base.PutAsync<TRequest, TResponse, TError>(request, url, headers);
            }
            return response;
        }

        protected override async Task<RestApiResponse<TResponse, TError>> PostAsync<TRequest, TResponse, TError>(TRequest request, string url, Dictionary<string, string> headers = null)
        {
            var response = await base.PostAsync<TRequest, TResponse, TError>(request, url, headers);
            if (!response.IsSuccessStatusCode && response.StatusCode == HTTPSTATUS_UNAUTHORIZED)
            {
                RefreshSecurityToken();
                response = await base.PostAsync<TRequest, TResponse, TError>(request, url, headers);
            }
            return response;
        }

        protected override async Task<RestApiResponse<TResponse, TError>> PatchAsync<TRequest, TResponse, TError>(TRequest request, string url, Dictionary<string, string> headers = null)
        {
            var response = await base.PatchAsync<TRequest, TResponse, TError>(request, url, headers);
            if (!response.IsSuccessStatusCode && response.StatusCode == HTTPSTATUS_UNAUTHORIZED)
            {
                RefreshSecurityToken();
                response = await base.PatchAsync<TRequest, TResponse, TError>(request, url, headers);
            }
            return response;
        }

        protected override T DeserializeJson<T>(string jsonString)
        {
            return JsonConvert.DeserializeObject<T>(jsonString);
        }

        protected override string SerializeJson<T>(T obj)
        {
            return JsonConvert.SerializeObject(obj);
        }

        public void Execute(Function function)
        {
            throw new NotImplementedException();
        }

        public Task<Entity> RetrieveAsync(string entityName, AlternateKeys keys, ColumnSet columnSet)
        {
            throw new NotImplementedException();
        }

        public void Associate(EntityReference from, string relationship, EntityReference to)
        {
            throw new NotImplementedException();
        }

        public void Disassociate(EntityReference from, string relationship, EntityReference to)
        {
            throw new NotImplementedException();
        }
    }

    internal class AuthenticationCredentials
    {
        public string Tenant { get; set; }

        public string ClientId { get; set; } = "2ad88395-b77d-4561-9441-d0e40824f9bc";
        public string ClientSecret { get; set; }

        public string Resource { get; set; }

        public NetworkCredential NetworkCredential { get; set; }

        public UserRealm UserRealm { get; set; }

        public SecurityToken SecurityToken { get; set; }

    }

    [DataContract]
    internal class UserRealm
    {
        [DataMember]
        public string ver { get; set; }

        [DataMember]
        public string account_type { get; set; }

        [DataMember]
        public string domain_name { get; set; }

        [DataMember]
        public string federation_protocol { get; set; }

        [DataMember]
        public string federation_metadata_url { get; set; }

        [DataMember]
        public string federation_active_auth_url { get; set; }

        [DataMember]
        public string cloud_instance_name { get; set; }

        [DataMember]
        public string cloud_audience_urn { get; set; }
    }

    [DataContract]
    internal class SecurityToken
    {
        [DataMember]
        public string token_type { get; set; }

        [DataMember]
        public string scope { get; set; }

        [DataMember]
        public string expires_in { get; set; }

        [DataMember]
        public string ext_expires_in { get; set; }

        [DataMember]
        public string expires_on { get; set; }

        [DataMember]
        public string not_before { get; set; }

        [DataMember]
        public string resource { get; set; }

        [DataMember]
        public string access_token { get; set; }

        [DataMember]
        public string refresh_token { get; set; }
    }

    [DataContract]
    internal class Error
    {
        [DataMember]
        public string error { get; set; }

        [DataMember]
        public string error_description { get; set; }

        [DataMember]
        public int[] error_codes { get; set; }

        [DataMember]
        public string timestamp { get; set; }

        [DataMember]
        public string trace_id { get; set; }

        [DataMember]
        public string correlation_id { get; set; }
    }
}