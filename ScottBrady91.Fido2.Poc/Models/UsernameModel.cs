using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ScottBrady91.Fido2.Poc.Models
{
    public class UsernameModel
    {
        public string Username { get; set; }
        public string ReturnUrl { get; set; }

        public override string ToString()
        {
            return JsonConvert.SerializeObject(this);
        }

        public static UsernameModel Parse(string JSON)
        {
            return JsonConvert.DeserializeObject(JSON, typeof(UsernameModel)) as UsernameModel;
        }

    }
}