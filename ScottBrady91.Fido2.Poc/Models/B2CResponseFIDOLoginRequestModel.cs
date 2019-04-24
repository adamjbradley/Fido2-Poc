using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;

namespace ScottBrady91.Fido2.Poc.Models
{
    public class B2CResponseFidoLoginRequestModel
    {
        public string version { get; set; }
        public int status { get; set; }

        // Mandatory claims
        public string KeyId { get; set; }
        public string Challenge { get; set; }
        public string RelyingPartyId { get; set; }

        // Optional claims


        public B2CResponseFidoLoginRequestModel(string KeyId, string RelyingPartyId, string Challenge, HttpStatusCode status)
        {
            this.version = Assembly.GetExecutingAssembly().GetName().Version.ToString();
            this.status = (int)status;

            this.KeyId = KeyId;
            this.RelyingPartyId = RelyingPartyId;
            this.Challenge = Challenge;
        }
    }
}