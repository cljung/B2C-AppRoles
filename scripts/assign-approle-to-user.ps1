#
# Connect-AzAccount -t "yourtenant.onmicrosoft.com"
#

# 1. get the user
$user = (Get-AzureADUser -SearchString "Max Peck")
 
# 2. create the group
$group = New-AzureADGroup -DisplayName "B2CTeamManagers" -MailEnabled $false -SecurityEnabled $true -MailNickName "NotSet"
 
# 3. add the user as a member of the group
Add-AzureADGroupMember -ObjectId $group.ObjectID -RefObjectId $user.ObjectID
 
# 4. get the app's service principal
$spo =(Get-AzureADServicePrincipal -Filter "DisplayName eq 'AspnetCoreMsal-Demo'")
 
# 5. find the role by name
$roleAppAdmin =($spo.AppRoles | Where {$_.DisplayName -eq "TeamManager"})
 
# 6. Assign the group to the AppRole
New-AzureADGroupAppRoleAssignment -ObjectId $group.ObjectId -PrincipalId $group.ObjectId -ResourceId $spo.ObjectId -Id $roleAppAdmin.id
