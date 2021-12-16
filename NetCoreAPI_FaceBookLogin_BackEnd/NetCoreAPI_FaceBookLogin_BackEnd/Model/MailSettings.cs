using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NetCoreAPI_FaceBookLogin_BackEnd.Model
{
    public class MailSettings
    {
        public string Mail { get; set; }
        public string MailCC { get; set; }
        public string MailBCC { get; set; }
        public string DisplayName { get; set; }
        public string Password { get; set; }
        public string Host { get; set; }
        public int Port { get; set; }
    }

}
