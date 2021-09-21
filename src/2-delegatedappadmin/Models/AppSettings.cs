using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace AspnetCoreMsal_demo.Models
{
    public class AppSettings
    {
        public string Instance {get; set; }
        public string Domain {get;set; }
        public string TenantId {get;set; }
        public string ClientId { get; set; }

        public string GraphClientId { get; set; }
        public string GraphClientSecret { get; set; }
        public string TeamManagerAppRoleGroupId { get; set; }
    }
}
