using Microsoft.Graph;
using Azure.Identity;
using AspnetCoreMsal_demo.Models;

namespace AspnetCoreMsal_demo.Helpers
{
    public class GraphHelper
    {
        public static GraphServiceClient GetGraphClient(AppSettings appSettings) {
            var clientSecretCredential = new ClientSecretCredential(
                    appSettings.Domain, appSettings.GraphClientId, appSettings.GraphClientSecret
                    , new TokenCredentialOptions { AuthorityHost = AzureAuthorityHosts.AzurePublicCloud }
                );
            var graphClient = new GraphServiceClient(clientSecretCredential, new[] { "https://graph.microsoft.com/.default" });
            return graphClient;
        }

    }
}
