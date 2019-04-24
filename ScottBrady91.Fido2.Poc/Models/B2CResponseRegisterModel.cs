using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;

namespace ScottBrady91.Fido2.Poc.Models
{
    public class B2CResponseRegisterModel
    {
        public string version { get; set; }
        public int status { get; set; }

        // Mandatory claims
        public string Username { get; set; }
        public string Challenge { get; set; }
        public string RelyingPartyId { get; set; }

        // Optional claims


        public B2CResponseRegisterModel(string Username, string RelyingPartyId, string Challenge, HttpStatusCode status)
        {
            this.version = Assembly.GetExecutingAssembly().GetName().Version.ToString();
            this.status = (int)status;

            this.Username = Username;
            this.RelyingPartyId = RelyingPartyId;
            this.Challenge = Challenge;
        }
    }
}