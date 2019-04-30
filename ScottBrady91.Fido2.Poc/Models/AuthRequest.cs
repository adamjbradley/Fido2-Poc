using System;
using System.Collections.Generic;

namespace ScottBrady91.Fido2.Poc.Models
{
    public class AuthRequest
    {
        public string challenge { get; set; }
        public string rpId { get; set; }
        public string username { get; set; }
        public List<PublicKey> allowCredentials { get; set; }

        public AuthRequest()
        {
        }

        public AuthRequest(string challenge, string rpId, List<PublicKey> publicKeys)
        {
            if (publicKeys == null)
                this.allowCredentials = new List<PublicKey>();
        }
    }
}
