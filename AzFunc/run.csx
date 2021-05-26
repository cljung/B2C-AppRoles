#r "Newtonsoft.Json"

using System.Net;
using System.Text;
using Newtonsoft.Json;
using  Newtonsoft.Json.Linq;

public static async Task<HttpResponseMessage> Run(HttpRequest req, ILogger log)
{
    log.LogInformation("C# HTTP trigger function processed a request.");

    string objectId = req.Query["objectId"]; // User
    string clientId = req.Query["clientId"]; // App
    string tenantId = req.Query["tenantId"]; // Tenant
    string scope = req.Query["scope"];       // "roles", "roles groups" or "groups"

    string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
    dynamic data = JsonConvert.DeserializeObject(requestBody);
    objectId = objectId ?? data?.objectId;
    clientId = clientId ?? data?.clientId;
    tenantId = tenantId ?? data?.tenantId;
    scope = scope ?? data?.scope;
    // default just roles
    if ( string.IsNullOrWhiteSpace(scope) ) {
        scope = "roles";
    } else {
        scope = scope.ToLowerInvariant();
    }
    log.LogInformation($"Params: objectId={objectId}, clientId={clientId}, tenantId={tenantId}, scope={scope}" );

    //////////////////////////////////////////////////////////////////////////////////////////////////////
    // If you use Cert based auth, you will receive a client cert
    //////////////////////////////////////////////////////////////////////////////////////////////////////
    var cert = req.HttpContext.Connection.ClientCertificate;
    log.LogInformation($"Incoming cert: {cert}");
    if(cert != null ) { 
        var b2cCertSubject = System.Environment.GetEnvironmentVariable( $"B2C_{tenantId}_CertSubject"); //
        var b2cCertThumbprint = System.Environment.GetEnvironmentVariable($"B2C_{tenantId}_CertThumbprint");
        if ( !( cert.Subject.Equals(b2cCertSubject) && cert.Thumbprint.Equals(b2cCertThumbprint) ) ) {
            var respContent = new { version = "1.0.0", status = (int)HttpStatusCode.BadRequest, 
                                    userMessage = "Technical error - cert..."};
            var json = JsonConvert.SerializeObject(respContent);
            log.LogInformation(json);
            return new HttpResponseMessage(HttpStatusCode.Conflict) {
                            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
            };
        }
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////
    // get the B2C client credentials for this tenant
    //////////////////////////////////////////////////////////////////////////////////////////////////////
    var b2cClientId = System.Environment.GetEnvironmentVariable($"B2C_{tenantId}_ClientId"); //
    var b2cClientSecret = System.Environment.GetEnvironmentVariable($"B2C_{tenantId}_ClientSecret");
    
    //////////////////////////////////////////////////////////////////////////////////////////////////////
    // Authenticate via the Client Credentials flow
    //////////////////////////////////////////////////////////////////////////////////////////////////////
    string accessToken = GetCachedAccessToken( tenantId );
    if ( null == accessToken ) 
    {
        HttpClient client = new HttpClient();
        var dict= new Dictionary<string, string>();
        dict.Add("grant_type", "client_credentials");
        dict.Add("client_id", b2cClientId);
        dict.Add("client_secret", b2cClientSecret);
        dict.Add("resource", "https://graph.microsoft.com");
        dict.Add("scope", "User.Read.All AppRoleAssignment.Read.All");

        var urlTokenEndpoint = $"https://login.microsoftonline.com/{tenantId}/oauth2/token?api-version=1.0";
        log.LogInformation(urlTokenEndpoint);

        HttpResponseMessage resp = client.PostAsync( urlTokenEndpoint, new FormUrlEncodedContent(dict)).Result;
        var contents = await resp.Content.ReadAsStringAsync();
        client.Dispose();
        log.LogInformation("HttpStatusCode=" + resp.StatusCode.ToString() + " - " + contents );

        // If the client creds failed, return error
        if ( resp.StatusCode != HttpStatusCode.OK ) {
            var respContent = new { version = "1.0.0", status = (int)HttpStatusCode.BadRequest, userMessage = "Technical error..."};
            var json = JsonConvert.SerializeObject(respContent);
            log.LogInformation(json);
            return new HttpResponseMessage(HttpStatusCode.Conflict) {
                            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
            };
        }
        accessToken = JObject.Parse(contents)["access_token"].ToString();
        CacheAccessToken( tenantId, accessToken );
    }
    log.LogInformation(accessToken);

    //////////////////////////////////////////////////////////////////////////////////////////////////////
    // GraphAPI query for user's group membership
    //////////////////////////////////////////////////////////////////////////////////////////////////////
    var groupsList = new List<string>();
    if ( scope.Contains("groups") ) {
        HttpClient httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken );
        var url = $"https://graph.microsoft.com/v1.0/users/{objectId}/memberOf?$select=id,displayName";
        log.LogInformation(url);
        var res = await httpClient.GetAsync(url);
        log.LogInformation("HttpStatusCode=" + res.StatusCode.ToString());
        if(res.IsSuccessStatusCode) {
            var respData = await res.Content.ReadAsStringAsync();
            var groupArray = (JArray)JObject.Parse(respData)["value"];
            foreach (JObject g in groupArray) {
                var name = g["displayName"].Value<string>();
                groupsList.Add(name);
            }    
        }
        httpClient.Dispose();
    }

    //////////////////////////////////////////////////////////////////////////////////////////////////////
    // GraphAPI query for user AppRoleAssignments
    //////////////////////////////////////////////////////////////////////////////////////////////////////
    JObject userData = null;
    bool hasAssignments = false;
    var roleNames = new List<string>();

    if ( scope.Contains("roles") ) {
        HttpClient httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken );
        var url = $"https://graph.microsoft.com/beta/users/{objectId}/appRoleAssignments?$select=appRoleId,resourceId,resourceDisplayName";
        log.LogInformation(url);
        var res = await httpClient.GetAsync(url);
        log.LogInformation("HttpStatusCode=" + res.StatusCode.ToString());
        if(res.IsSuccessStatusCode) {
            var respData = await res.Content.ReadAsStringAsync();
            log.LogInformation(respData);
            userData = JObject.Parse(respData);
            foreach( var item in userData["value"] ) {
                hasAssignments = true;
                break;
            }
        }
        httpClient.Dispose();
    }

    if ( hasAssignments ) {
        HttpClient httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        var url = $"https://graph.microsoft.com/beta/servicePrincipals?$filter=appId eq '{clientId}'&$select=id,appRoles";
        log.LogInformation(url);
        var res = httpClient.GetAsync(url).Result;
        JArray appRolesArray = null;
        string spId = "";
        if (res.IsSuccessStatusCode) {
            var respData = res.Content.ReadAsStringAsync().Result;
            var spoData = JObject.Parse(respData);
            foreach (var item in spoData["value"]) {
                spId = item["id"].ToString();
                appRolesArray = (JArray)item["appRoles"];
                foreach (var itemUser in userData["value"]) {
                    if (spId == itemUser["resourceId"].ToString()) {
                        var appRoleId = itemUser["appRoleId"].ToString();
                        foreach (var role in appRolesArray) {
                            if (appRoleId == role["id"].ToString()) {
                                roleNames.Add(role["value"].ToString());
                            }
                        }
                    }
                }
            }
        }
        httpClient.Dispose();
    }

    var jsonToReturn = JsonConvert.SerializeObject( new { roles = roleNames, groups = groupsList } );
    log.LogInformation(jsonToReturn);
    return new HttpResponseMessage(HttpStatusCode.OK) {
            Content = new StringContent(jsonToReturn, System.Text.Encoding.UTF8, "application/json")
        };
}

public static string GetCachedAccessToken( string tenantId, int secondsRemaining = 60 ) // access_token needs to be valid for N seconds more
{
    string accessToken = Environment.GetEnvironmentVariable($"B2C_{tenantId}_AppRoles_AccessToken");
    if (accessToken != null)
    {
        DateTime epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        string b64 = accessToken.Split(".")[1];
        while ((b64.Length % 4) != 0)
            b64 += "=";
        JObject jwtClaims = JObject.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(b64)));
        DateTime expiryTime = epoch.AddSeconds(int.Parse(jwtClaims["exp"].ToString()));
        if (DateTime.UtcNow >= expiryTime.AddSeconds(-secondsRemaining))
            accessToken = null; // invalidate
    }
    return accessToken;
}
public static void CacheAccessToken( string tenantId, string accesToken )
{
    Environment.SetEnvironmentVariable($"B2C_{tenantId}_AppRoles_AccessToken", accesToken);
}
