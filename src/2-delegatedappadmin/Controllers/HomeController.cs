using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using AspnetCoreMsal_demo.Models;
using AspnetCoreMsal_demo.Helpers;

namespace AspnetCoreMsal_demo.Controllers
{
    // [Authorize]
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly AppSettings _appSettings;

        public HomeController(ILogger<HomeController> logger, IOptions<AppSettings> appSettings)
        {
            _logger = logger;
            _appSettings = appSettings.Value;
        }

        [AllowAnonymous]
        public IActionResult Index()
        {
            List<string> teams = new List<string>();
            if (User.Identity.IsAuthenticated ) {
                foreach (var group in User.Claims.Where(c => c.Type == "groups")) {
                    teams.Add( group.Value );
                }
            }
            ViewBag.Teams = teams;
            return View();
        }

        [AllowAnonymous]
        public IActionResult Privacy()
        {
            return View();
        }

        [Authorize(Policy = "AppAdmin")]
        public IActionResult AppAdmin() {
            return View();
        }
        [Authorize(Policy = "TeamManager")]
        public IActionResult TeamManager() {
            string userObjectId = User.Claims.Where(c => c.Type == "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier").Select(c => c.Value).SingleOrDefault();
            var ownedObjects = GraphHelper.GetGraphClient(_appSettings).Users[userObjectId].OwnedObjects.Request().GetAsync().Result;
            Dictionary<string, string> dict = new Dictionary<string, string>();
            foreach (var group in ownedObjects) {
                if ( group is Microsoft.Graph.Group && ((Microsoft.Graph.Group)group).DisplayName.StartsWith("Team_") ) {
                    dict.Add(((Microsoft.Graph.Group)group).Id, ((Microsoft.Graph.Group)group).Description);
                }
            }
            ViewBag.Groups = dict;
            return View();
        }

        [AllowAnonymous]
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }

        [HttpPost]
        [Authorize(Policy = "AppAdmin")]
        public async Task<IActionResult> CreateTeam(string teamName, string managerEmail) {
            string returnView = "~/Views/Home/AppAdmin.cshtml";
            // Add 'Team_' as prefix to distinguish from other groups
            string groupName = "Team_" + teamName.Replace(" ", "").Replace("'", "").Replace("\"", "");
            try {
                var graphClient = GraphHelper.GetGraphClient(_appSettings);
                // check to see that the group name doesn't exist already 
                var groups = graphClient.Groups.Request()
                                                .Select(m => new { m.Id, m.DisplayName })
                                                .Filter($"displayName eq '{groupName}'")
                                                .GetAsync().Result;
                if (groups.Count > 0) {
                    ViewData["Message"] = "A team with that name already exists. Please choose another one";
                    return View(returnView);
                }
                // get the manager (assuming a Local Account)
                var users = graphClient.Users.Request()
                                            .Select(m => new { m.Id, m.DisplayName })
                                            .Filter($"identities/any(c:c/issuerAssignedId eq '{managerEmail}' and c/issuer eq '{_appSettings.Domain}')")
                                            .GetAsync().Result;
                if (users.Count == 0) {
                    ViewData["Message"] = $"Cannot find manager with email {managerEmail}";
                    return View(returnView);
                }
                var group = new Microsoft.Graph.Group {
                    DisplayName = groupName,
                    Description = teamName,
                    SecurityEnabled = true,
                    MailEnabled = false,
                    MailNickname = groupName
                };
                // create the group
                var newGroup = graphClient.Groups.Request().AddAsync(group).Result;
                // add the manager as the owner
                await graphClient.Groups[ newGroup.Id ].Owners.References.Request().AddAsync( users[0] );
                // add the manager as a member of the group
                await graphClient.Groups[ newGroup.Id ].Members.References.Request().AddAsync( users[0] );
                // add the manager as a member of the AppRole group for general Team Managers (could already be a member of another team)
                var alreadyManager = graphClient.Groups[_appSettings.TeamManagerAppRoleGroupId].Members.Request()
                                .Select(m => new { m.Id })
                                .Filter($"id eq '{users[0].Id}'")
                                .GetAsync().Result;
                if ( alreadyManager.Count == 0 ) {
                    await graphClient.Groups[_appSettings.TeamManagerAppRoleGroupId].Members.References.Request().AddAsync(users[0]);
                }
                ViewData["Message"] = $"A team with name {teamName} was created with {users[0].DisplayName} ({managerEmail}) as manager";
            } catch (Exception ex) {
                ViewData["Message"] = $"Technical error - {ex.Message}";
            }
            return View(returnView);
        }
        [HttpPost]
        [Authorize(Policy = "TeamManager")]
        public async Task<IActionResult> AddUserToTeam(string team, string role, string email ) {
            string returnView = "~/Views/Home/TeamManager.cshtml";
            try {
                var graphClient = GraphHelper.GetGraphClient(_appSettings);
                // make sure the group still exists
                var groups = graphClient.Groups.Request()
                                                .Select(m => new { m.Id, m.DisplayName, m.Description })
                                                .Filter($"id eq '{team}'")
                                                .GetAsync().Result;
                if (groups.Count == 0) {
                    ViewData["Message"] = "The team does not exist. Please choose another one";
                    return View(returnView);
                }
                // get the user (assuming a Local Account)
                var users = graphClient.Users.Request()
                                            .Select(m => new { m.Id, m.DisplayName })
                                            .Filter($"identities/any(c:c/issuerAssignedId eq '{email}' and c/issuer eq '{_appSettings.Domain}')")
                                            .GetAsync().Result;
                if (users.Count == 0) {
                    ViewData["Message"] = $"Cannot find user with email {email}";
                    return View(returnView);
                }
                // add the user as a member of the group
                await graphClient.Groups[team].Members.References.Request().AddAsync(users[0]);
                // if role is manager, make owner and add to general group
                if ( role.ToLowerInvariant() == "manager" ) {
                    await graphClient.Groups[ team ].Owners.References.Request().AddAsync(users[0]);
                    // add the manager as a member of the AppRole group for general Team Managers (could already be a member of another team)
                    var alreadyManager = graphClient.Groups[_appSettings.TeamManagerAppRoleGroupId].Members.Request()
                                    .Select(m => new { m.Id })
                                    .Filter($"id eq '{users[0].Id}'")
                                    .GetAsync().Result;
                    if (alreadyManager.Count == 0) {
                        await graphClient.Groups[_appSettings.TeamManagerAppRoleGroupId].Members.References.Request().AddAsync(users[0]);
                    }
                }
                ViewData["Message"] = $"User {users[0].DisplayName} ({email}) was added as {role} to team {groups[0].Description}";
            } catch (Exception ex) {
                ViewData["Message"] = $"Technical error - {ex.Message}";
            }
            return View(returnView);
        }
    } // cls
} // ns
