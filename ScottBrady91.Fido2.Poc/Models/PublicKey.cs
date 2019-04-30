using System;
namespace ScottBrady91.Fido2.Poc.Models
{
    public class PublicKey
    {    
        public string type { get; set; }
        public string id { get; set; }

        public PublicKey(string id)
        {
            this.id = id;
            this.type = "public-key";
        }
    }
}
