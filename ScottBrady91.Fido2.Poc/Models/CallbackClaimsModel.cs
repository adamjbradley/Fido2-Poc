using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ScottBrady91.Fido2.Poc.Models
{
    public class CallbackClaimsModel
    {
        public string callback { get; set; }
    
        public override string ToString()
        {
            return JsonConvert.SerializeObject(this);
        }

        public static CallbackClaimsModel Parse(string JSON)
        {
            return JsonConvert.DeserializeObject(JSON, typeof(CallbackClaimsModel)) as CallbackClaimsModel;
        }
    }
}


