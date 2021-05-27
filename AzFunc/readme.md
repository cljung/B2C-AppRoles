# B2C AppRoles Azure Function

This Azure Function can retrieve a users group memberships and the AppRoleAssignements. It is intended to be called from Azure AD B2C Custom Policies and you pass four parameters

- tenantId - the guid of your B2C directory (the Azure Function can serve multiple directories)
- clientId - the AppId that the user is signing in to
- objectId - the user's objectId
- scope - indication of what you want to retrieve: "roles", "roles groups" or "groups"

## How to deploy

Create an Azure Function app in the region where you have deployed your B2C instance. Create an HTTP Trigger function with the name `GetAppRoleAssignmentsMSGraph` and replace the code in `run.csx` with this code.

## Configuration

The Azure Function uses a service principle and client credentials in order to acquire an access_token do the Graph API queries.
You need to add the following configuration settings for the Azure Functions app.


- B2C_{TenantId}_ClientId - AppId/client_id for client credentials
- B2C_{TenantId}_ClientSecret - client_secret for client credentials

Note that {TenantId} shoud be replaced with your B2C directory's id (guid)
