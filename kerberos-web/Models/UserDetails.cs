using System;
namespace kerberos_web.Models
{
    public class UserDetails
    {
        public string Name { get; set; }
        public List<ClaimSummary> Claims { get; set; }
    }
}

