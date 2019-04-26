﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using IdentityModel;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Newtonsoft.Json;
using PeterO.Cbor;
using ScottBrady91.Fido2.Poc.Models;

using Microsoft.Extensions.Options;
using Microsoft.Extensions.Caching.Memory;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using System.Threading.Tasks;
using System.Net;

namespace ScottBrady91.Fido2.Poc.Controllers
{
    [Route("api/[controller]")]
    [Produces("application/json")]
    [ApiController]
    public class AccountAPIController : Controller
    {
        // aka RP ID
        private const string RelyingPartyId = "fidowebauthnpoc.azurewebsites.net";

        private static readonly List<User> Users = new List<User>();
        private readonly ITempDataProvider tempData;
        private readonly AppSettingsModel AppSettings;
        private IMemoryCache cache;

        // Demo: Inject an instance of an AppSettingsModel class into the constructor of the consuming class, 
        // and let dependency injection handle the rest
        public AccountAPIController(ITempDataProvider tempData, IOptions<AppSettingsModel> appSettings, IMemoryCache cache)
        {
            this.AppSettings = appSettings.Value;
            this.tempData = tempData ?? throw new ArgumentNullException(nameof(tempData));
            this.cache = cache;
        }

        [HttpPost("/AccountAPI/FidoRegister")]
        public async Task<ActionResult> FidoRegister()
        {
            string input = null;

            if (this.Request.Body == null)
            {
                return StatusCode((int)HttpStatusCode.Conflict, new B2CResponseModel("Request content is null", HttpStatusCode.Conflict));
            }

            // Read the input claims from the request body
            using (StreamReader reader = new StreamReader(Request.Body, Encoding.UTF8))
            {
                input = await reader.ReadToEndAsync();
            }

            // Check input content value
            if (string.IsNullOrEmpty(input))
            {
                return StatusCode((int)HttpStatusCode.Conflict, new B2CResponseModel("Request content is empty", HttpStatusCode.Conflict));
            }

            // Convert the input string into InputClaimsModel object
            UsernameModel inputClaims = UsernameModel.Parse(input);

            if (inputClaims == null)
            {
                return StatusCode((int)HttpStatusCode.Conflict, new B2CResponseModel("Can not deserialize input claims", HttpStatusCode.Conflict));
            }

            // generate challenge
            var challenge = CryptoRandom.CreateUniqueId(16);

            // store challenge for later use
            tempData.SaveTempData(HttpContext, new Dictionary<string, object> { { "challenge", challenge }, { "username", inputClaims.Username } });
            cache.Set(challenge, new Dictionary<string, object> { { "challenge", challenge }, { "username", inputClaims.Username } });

            B2CResponseFIDORegisterModel output = new B2CResponseFIDORegisterModel(string.Empty, string.Empty, string.Empty, HttpStatusCode.OK)
            {
                Username = inputClaims.Username,
                Challenge = challenge,
                RelyingPartyId = RelyingPartyId
            };

            return Ok(output);
        }

        [HttpPost("/AccountAPI/RegisterCallbackJSON")]
        public async Task<ActionResult> RegisterCallbackJSON()
        {
            string input = null;

            if (this.Request.Body == null)
            {
                return StatusCode((int)HttpStatusCode.Conflict, new B2CResponseModel("Request content is null", HttpStatusCode.Conflict));
            }

            // Read the input claims from the request body
            using (StreamReader reader = new StreamReader(Request.Body, Encoding.UTF8))
            {
                input = await reader.ReadToEndAsync();
            }

            // Check input content value
            if (string.IsNullOrEmpty(input))
            {
                return StatusCode((int)HttpStatusCode.Conflict, new B2CResponseModel("Request content is empty", HttpStatusCode.Conflict));
            }

            // Convert the input string into InputClaimsModel object
            CallbackClaimsModel inputClaims = CallbackClaimsModel.Parse(input);

            if (inputClaims == null)
            {
                return StatusCode((int)HttpStatusCode.Conflict, new B2CResponseModel("Can not deserialize input claims", HttpStatusCode.Conflict));
            }

            byte[] data = Convert.FromBase64String(inputClaims.callback);
            string decodedString = Encoding.UTF8.GetString(data);
            CredentialsModel cm = JsonConvert.DeserializeObject<CredentialsModel>(decodedString);

            B2CResponseModel b2crm = RegisterCallbackAPI(cm);
            return Ok(b2crm);
        }

        public B2CResponseModel RegisterCallbackAPI(CredentialsModel model)
        {
            // 1. Let JSONtext be the result of running UTF-8 decode on the value of response.clientDataJSON
            var jsonText = Encoding.UTF8.GetString(Base64Url.Decode(model.Response.ClientDataJson));

            // 2. Let C, the client data claimed as collected during the credential creation, be the result of running an implementation-specific JSON parser on JSONtext
            var c = JsonConvert.DeserializeObject<ClientData>(jsonText);

            // 3. Verify that the value of C.type is webauthn.create
            if (c.Type != "webauthn.create") throw new Exception("Incorrect client data type");

            // 4. Verify that the value of C.challenge matches the challenge that was sent to the authenticator in the create() call.
            //var data = tempData.LoadTempData(HttpContext);
            Dictionary<string, object> data = (Dictionary<string, object>)cache.Get(c.Challenge);

            var challenge = (string)data["challenge"];
            if (Base64Url.Decode(c.Challenge) == Convert.FromBase64String(challenge)) throw new Exception("Incorrect challenge");

            // 5. Verify that the value of C.origin matches the Relying Party's origin.
            //if (c.Origin != "http://localhost:5000") throw new Exception("Incorrect origin");

            // 6. Verify that the value of C.tokenBinding.status matches the state of Token Binding for the TLS connection over which the assertion was obtained.
            // If Token Binding was used on that TLS connection, also verify that C.tokenBinding.id matches the base64url encoding of the Token Binding ID for the connection.
            // TODO: Token binding (once out of draft)

            // 7. Compute the hash of response.clientDataJSON using SHA-256.
            var hasher = new SHA256Managed();
            var hashedClientDataJson = hasher.ComputeHash(Base64Url.Decode(model.Response.ClientDataJson)); // Why???

            // 8. Perform CBOR decoding on the attestationObject field of the AuthenticatorAttestationResponse structure
            // to obtain the attestation statement format fmt, the authenticator data authData, and the attestation statement attStmt.
            CBORObject cbor;
            using (var stream = new MemoryStream(Base64Url.Decode(model.Response.AttestationObject)))
                cbor = CBORObject.Read(stream);

            var authData = cbor["authData"].GetByteString();
            var fmt = cbor["fmt"].AsString();

            var span = authData.AsSpan();
            var rpIdHash = span.Slice(0, 32); span = span.Slice(32);

            var flags = new BitArray(span.Slice(0, 1).ToArray()); span = span.Slice(1);
            var userPresent = flags[0]; // (UP)
            // Bit 1 reserved for future use (RFU1)
            var userVerified = flags[2]; // (UV)
            // Bits 3-5 reserved for future use (RFU2)
            var attestedCredentialData = flags[6]; // (AT) "Indicates whether the authenticator added attested credential data"
            var extensionDataIncluded = flags[7]; // (ED)

            // Signature counter (4 bytes, big-endian unint32)
            var counterBuf = span.Slice(0, 4); span = span.Slice(4);
            var counter = BitConverter.ToUInt32(counterBuf); // https://www.w3.org/TR/webauthn/#signature-counter

            // Attested Credential Data
            // cred data - AAGUID (16 bytes)
            var aaguid = span.Slice(0, 16); span = span.Slice(16);

            // cred data - L (2 bytes, big-endian uint16)
            var credIdLenBuf = span.Slice(0, 2); span = span.Slice(2);
            credIdLenBuf.Reverse();
            var credentialIdLength = BitConverter.ToUInt16(credIdLenBuf);

            // cred data - Credential ID (L bytes)
            var credentialId = span.Slice(0, credentialIdLength); span = span.Slice(credentialIdLength);

            // 9. Verify that the RP ID hash in authData is indeed the SHA-256 hash of the RP ID expected by the RP.
            //var computedRpIdHash = hasher.ComputeHash(Encoding.UTF8.GetBytes(RelyingPartyId));
            //if (!rpIdHash.SequenceEqual(computedRpIdHash)) throw new Exception("Incorrect RP ID");

            // 10. If user verification is required for this registration, verify that the User Verified bit of the flags in authData is set.
            // TODO: Handle user verificaton required

            // 11. If user verification is not required for this registration, verify that the User Present bit of the flags in authData is set.
            if (userPresent == false) throw new Exception("User not present");

            // 12. Verify that the values of the client extension outputs in clientExtensionResults
            // TODO: Handle extension results

            // 13. Determine the attestation statement format by performing a USASCII case-sensitive match on fmt against the set of supported WebAuthn Attestation Statement Format Identifier values
            // TODO: Handle accepted fmt values

            // 14. Verify that attStmt is a correct attestation statement, conveying a valid attestation signature, by using the attestation statement format fmt’s verification procedure given attStmt, authData and the hash of the serialized client data computed in step 7.
            // TODO: Handle fmt specific attestation statement

            // 15. If validation is successful, obtain a list of acceptable trust anchors (attestation root certificates or ECDAA-Issuer public keys) for that attestation type and attestation statement format fmt, from a trusted source or from policy.
            // For example, the FIDO Metadata Service [FIDOMetadataService] provides one way to obtain such information, using the aaguid in the attestedCredentialData in authData.
            // 16. Assess the attestation trustworthiness using the outputs of the verification procedure in step 14
            // TODO: Use of FIDO metadata service

            // 17. Check that the credentialId is not yet registered to any other user & 
            var parsedCredentialId = Convert.ToBase64String(credentialId.ToArray());

            List<User> _Users = (List<User>)cache.Get("Users");
            if (_Users != null)
            {
                if (_Users.Any(x => x.CredentialId == parsedCredentialId))
                {
                    {
                        return new B2CResponseModel("User already registered", System.Net.HttpStatusCode.InternalServerError);
                    }
                }
            }

            // 18. If the attestation statement attStmt verified successfully and is found to be trustworthy, then register the new credential
            var coseStruct = CBORObject.DecodeFromBytes(span.ToArray());
            var key = JsonConvert.DeserializeObject<CredentialPublicKey>(coseStruct.ToJSONString());

            Models.User user = new User { Username = (string)data["username"], CredentialId = parsedCredentialId, PublicKey = key };
            Users.Add(user);
            cache.Set("Users", Users);

            return new B2CResponseModel("User registered successfully", System.Net.HttpStatusCode.OK);
        }

        [HttpPost("/AccountAPI/RegisterCallback")]
        public ActionResult<B2CResponseModel> RegisterCallback([FromBody] CredentialsModel model)
        {
            // 1. Let JSONtext be the result of running UTF-8 decode on the value of response.clientDataJSON
            var jsonText = Encoding.UTF8.GetString(Base64Url.Decode(model.Response.ClientDataJson));

            // 2. Let C, the client data claimed as collected during the credential creation, be the result of running an implementation-specific JSON parser on JSONtext
            var c = JsonConvert.DeserializeObject<ClientData>(jsonText);

            // 3. Verify that the value of C.type is webauthn.create
            if (c.Type != "webauthn.create") throw new Exception("Incorrect client data type");

            // 4. Verify that the value of C.challenge matches the challenge that was sent to the authenticator in the create() call.
            //var data = tempData.LoadTempData(HttpContext);
            Dictionary<string, object> data = (Dictionary<string, object>)cache.Get(c.Challenge);

            var challenge = (string)data["challenge"];
            if (Base64Url.Decode(c.Challenge) == Convert.FromBase64String(challenge)) throw new Exception("Incorrect challenge");

            // 5. Verify that the value of C.origin matches the Relying Party's origin.
            //if (c.Origin != "http://localhost:5000") throw new Exception("Incorrect origin");

            // 6. Verify that the value of C.tokenBinding.status matches the state of Token Binding for the TLS connection over which the assertion was obtained.
            // If Token Binding was used on that TLS connection, also verify that C.tokenBinding.id matches the base64url encoding of the Token Binding ID for the connection.
            // TODO: Token binding (once out of draft)

            // 7. Compute the hash of response.clientDataJSON using SHA-256.
            var hasher = new SHA256Managed();
            var hashedClientDataJson = hasher.ComputeHash(Base64Url.Decode(model.Response.ClientDataJson)); // Why???

            // 8. Perform CBOR decoding on the attestationObject field of the AuthenticatorAttestationResponse structure
            // to obtain the attestation statement format fmt, the authenticator data authData, and the attestation statement attStmt.
            CBORObject cbor;
            using (var stream = new MemoryStream(Base64Url.Decode(model.Response.AttestationObject)))
                cbor = CBORObject.Read(stream);

            var authData = cbor["authData"].GetByteString();
            var fmt = cbor["fmt"].AsString();

            var span = authData.AsSpan();
            var rpIdHash = span.Slice(0, 32); span = span.Slice(32);

            var flags = new BitArray(span.Slice(0, 1).ToArray()); span = span.Slice(1);
            var userPresent = flags[0]; // (UP)
            // Bit 1 reserved for future use (RFU1)
            var userVerified = flags[2]; // (UV)
            // Bits 3-5 reserved for future use (RFU2)
            var attestedCredentialData = flags[6]; // (AT) "Indicates whether the authenticator added attested credential data"
            var extensionDataIncluded = flags[7]; // (ED)

            // Signature counter (4 bytes, big-endian unint32)
            var counterBuf = span.Slice(0, 4); span = span.Slice(4);
            var counter = BitConverter.ToUInt32(counterBuf); // https://www.w3.org/TR/webauthn/#signature-counter

            // Attested Credential Data
            // cred data - AAGUID (16 bytes)
            var aaguid = span.Slice(0, 16); span = span.Slice(16);

            // cred data - L (2 bytes, big-endian uint16)
            var credIdLenBuf = span.Slice(0, 2); span = span.Slice(2);
            credIdLenBuf.Reverse();
            var credentialIdLength = BitConverter.ToUInt16(credIdLenBuf);

            // cred data - Credential ID (L bytes)
            var credentialId = span.Slice(0, credentialIdLength); span = span.Slice(credentialIdLength);

            // 9. Verify that the RP ID hash in authData is indeed the SHA-256 hash of the RP ID expected by the RP.
            //var computedRpIdHash = hasher.ComputeHash(Encoding.UTF8.GetBytes(RelyingPartyId));
            //if (!rpIdHash.SequenceEqual(computedRpIdHash)) throw new Exception("Incorrect RP ID");

            // 10. If user verification is required for this registration, verify that the User Verified bit of the flags in authData is set.
            // TODO: Handle user verificaton required

            // 11. If user verification is not required for this registration, verify that the User Present bit of the flags in authData is set.
            if (userPresent == false) throw new Exception("User not present");

            // 12. Verify that the values of the client extension outputs in clientExtensionResults
            // TODO: Handle extension results

            // 13. Determine the attestation statement format by performing a USASCII case-sensitive match on fmt against the set of supported WebAuthn Attestation Statement Format Identifier values
            // TODO: Handle accepted fmt values

            // 14. Verify that attStmt is a correct attestation statement, conveying a valid attestation signature, by using the attestation statement format fmt’s verification procedure given attStmt, authData and the hash of the serialized client data computed in step 7.
            // TODO: Handle fmt specific attestation statement

            // 15. If validation is successful, obtain a list of acceptable trust anchors (attestation root certificates or ECDAA-Issuer public keys) for that attestation type and attestation statement format fmt, from a trusted source or from policy.
            // For example, the FIDO Metadata Service [FIDOMetadataService] provides one way to obtain such information, using the aaguid in the attestedCredentialData in authData.
            // 16. Assess the attestation trustworthiness using the outputs of the verification procedure in step 14
            // TODO: Use of FIDO metadata service

            // 17. Check that the credentialId is not yet registered to any other user & 
            var parsedCredentialId = Convert.ToBase64String(credentialId.ToArray());

            List<User> _Users = (List<User>)cache.Get("Users");
            if (_Users != null) 
            {
                if (_Users.Any(x => x.CredentialId == parsedCredentialId))
                {
                    {
                        return new B2CResponseModel("User already registered", System.Net.HttpStatusCode.InternalServerError);
                    }
                }
            }

            // 18. If the attestation statement attStmt verified successfully and is found to be trustworthy, then register the new credential
            var coseStruct = CBORObject.DecodeFromBytes(span.ToArray());
            var key = JsonConvert.DeserializeObject<CredentialPublicKey>(coseStruct.ToJSONString());

            Models.User user = new User { Username = (string)data["username"], CredentialId = parsedCredentialId, PublicKey = key };
            Users.Add(user);
            cache.Set("Users", Users);

            return new B2CResponseModel("User registered successfully", System.Net.HttpStatusCode.OK);
        }

        [HttpPost("/AccountAPI/FidoLogin")]
        public async Task<ActionResult>FidoLogin()
        {
            string input = null;
            string replyUrl = null;

            if (this.Request.Body == null)
            {
                return StatusCode((int)HttpStatusCode.Conflict, new B2CResponseModel("Request content is null", HttpStatusCode.Conflict));
            }

            // Read the input claims from the request body
            using (StreamReader reader = new StreamReader(Request.Body, Encoding.UTF8))
            {
                input = await reader.ReadToEndAsync();
            }

            // Check input content value
            if (string.IsNullOrEmpty(input))
            {
                return StatusCode((int)HttpStatusCode.Conflict, new B2CResponseModel("Request content is empty", HttpStatusCode.Conflict));
            }

            // Convert the input string into InputClaimsModel object
            UsernameModel inputClaims = UsernameModel.Parse(input);

            if (inputClaims == null)
            {
                return StatusCode((int)HttpStatusCode.Conflict, new B2CResponseModel("Can not deserialize input claims", HttpStatusCode.Conflict));
            }

            // generate challenge
            var challenge = CryptoRandom.CreateUniqueId(16);

            List<User> _Users = (List<User>)cache.Get("Users");
            var user = Users.First(x => x.Username == inputClaims.Username);

            // store challenge & key ID for later use
            tempData.SaveTempData(HttpContext, new Dictionary<string, object> { { "challenge", challenge }, { "keyId", user.CredentialId }, { "returnUrl", inputClaims.ReturnUrl } });
            cache.Set(challenge, new Dictionary<string, object> { { "challenge", challenge }, { "keyId", user.CredentialId }, { "returnUrl", inputClaims.ReturnUrl } });

            B2CResponseFIDOLoginModel output = new B2CResponseFIDOLoginModel(string.Empty, string.Empty, string.Empty, HttpStatusCode.OK)
            {
                KeyId = user.CredentialId,
                Challenge = challenge,
                RelyingPartyId = RelyingPartyId
            };

            return Ok(output);
        }

        [HttpPost("/AccountAPI/LoginCallbackJSON")]
        public async Task<ActionResult> LoginCallbackJSON()
        {
            string input = null;

            if (this.Request.Body == null)
            {
                return StatusCode((int)HttpStatusCode.Conflict, new B2CResponseModel("Request content is null", HttpStatusCode.Conflict));
            }

            // Read the input claims from the request body
            using (StreamReader reader = new StreamReader(Request.Body, Encoding.UTF8))
            {
                input = await reader.ReadToEndAsync();
            }

            // Check input content value
            if (string.IsNullOrEmpty(input))
            {
                return StatusCode((int)HttpStatusCode.Conflict, new B2CResponseModel("Request content is empty", HttpStatusCode.Conflict));
            }

            // Convert the input string into InputClaimsModel object
            CallbackClaimsModel inputClaims = CallbackClaimsModel.Parse(input);

            if (inputClaims == null)
            {
                return StatusCode((int)HttpStatusCode.Conflict, new B2CResponseModel("Can not deserialize input claims", HttpStatusCode.Conflict));
            }

            byte[] data = Convert.FromBase64String(inputClaims.callback);
            string decodedString = Encoding.UTF8.GetString(data);
            CredentialsModel cm = JsonConvert.DeserializeObject<CredentialsModel>(decodedString);

            B2CResponseModel b2crm = LoginCallbackAPI(cm);
            return Ok(b2crm);
        }

        [HttpPost("/AccountAPI/LoginCallback")]
        public ActionResult<B2CResponseModel> LoginCallback([FromBody] CredentialsModel model)
        {
            // 1. If the allowCredentials option was given when this authentication ceremony was initiated, verify that credential.id identifies one of the public key credentials that were listed in allowCredentials.
            //var data = tempData.LoadTempData(HttpContext);

            // 2. If credential.response.userHandle is present, verify that the user identified by this value is the owner of the public key credential identified by credential.id.
            // 3. Using credential’s id attribute (or the corresponding rawId, if base64url encoding is inappropriate for your use case), look up the corresponding credential public key.

            // 4. Let cData, aData and sig denote the value of credential’s response's clientDataJSON, authenticatorData, and signature respectively.
            var cData = model.Response.ClientDataJson;
            var aData = model.Response.AuthenticatorData;
            var sig = model.Response.Signature;

            // 5. Let JSONtext be the result of running UTF-8 decode on the value of cData.
            var jsonText = Encoding.UTF8.GetString(Base64Url.Decode(cData));

            // 6. Let C, the client data claimed as used for the signature, be the result of running an implementation-specific JSON parser on JSONtext.
            var c = JsonConvert.DeserializeObject<ClientData>(jsonText);

            Dictionary<string, object> data = (Dictionary<string, object>)cache.Get(c.Challenge);
            if ((string)data["keyId"] != model.RawId) throw new Exception("Incorrect key used");

            List<User> _Users = (List<User>)cache.Get("Users");
            var user = Users.First(x => x.CredentialId == model.RawId);
            if (!string.IsNullOrEmpty(model.Response.UserHandle) && model.Response.UserHandle != user.Username) throw new Exception("Incorrect user handle returned");

            // 7. Verify that the value of C.type is the string webauthn.get.
            if (c.Type != "webauthn.get") throw new Exception("Incorrect client data type");

            // 8. Verify that the value of C.challenge matches the challenge that was sent to the authenticator in the PublicKeyCredentialRequestOptions passed to the get() call.
            var challenge = (string)data["challenge"];
            if (Base64Url.Decode(c.Challenge) == Convert.FromBase64String(challenge)) throw new Exception("Incorrect challenge");

            // 9. Verify that the value of C.origin matches the Relying Party's origin.
            //if (c.Origin != "http://localhost:5000") throw new Exception("Incorrect origin");

            // 10. Verify that the value of C.tokenBinding.status matches the state of Token Binding for the TLS connection over which the attestation was obtained.
            // If Token Binding was used on that TLS connection, also verify that C.tokenBinding.id matches the base64url encoding of the Token Binding ID for the connection.
            // TODO: Token binding once out of draft

            // 11. Verify that the rpIdHash in aData is the SHA-256 hash of the RP ID expected by the Relying Party.
            var hasher = new SHA256Managed();

            var span = Base64Url.Decode(aData).AsSpan();
            // RP ID Hash (32 bytes)
            var rpIdHash = span.Slice(0, 32); span = span.Slice(32);

            // Flags (1 byte)
            var flagsBuf = span.Slice(0, 1).ToArray();
            var flags = new BitArray(flagsBuf); span = span.Slice(1);
            var userPresent = flags[0]; // (UP)
            // Bit 1 reserved for future use (RFU1)
            var userVerified = flags[2]; // (UV)
            // Bits 3-5 reserved for future use (RFU2)
            var attestedCredentialData = flags[6]; // (AT) "Indicates whether the authenticator added attested credential data"
            var extensionDataIncluded = flags[7]; // (ED)

            // Signature counter (4 bytes, big-endian unint32)
            var counterBuf = span.Slice(0, 4); span = span.Slice(4);
            var counter = BitConverter.ToUInt32(counterBuf); // https://www.w3.org/TR/webauthn/#signature-counter

            //var computedRpIdHash = hasher.ComputeHash(Encoding.UTF8.GetBytes(RelyingPartyId));
            //if (!rpIdHash.SequenceEqual(computedRpIdHash)) throw new Exception("Incorrect RP ID");

            // 12. If user verification is required for this assertion, verify that the User Verified bit of the flags in aData is set.
            // TODO: Handle user verificaton required

            // 13. If user verification is not required for this assertion, verify that the User Present bit of the flags in aData is set.
            if (userPresent == false) throw new Exception("User not present");

            //14. Verify that the values of the client extension outputs in clientExtensionResults
            // TODO: Handle extension results

            // 15. Let hash be the result of computing a hash over the cData using SHA-256.
            var hash = hasher.ComputeHash(Base64Url.Decode(cData)); // 32 bytes

            // 16. Using the credential public key looked up in step 3, verify that sig is a valid signature over the binary concatenation of aData and hash.
            var a = Base64Url.Decode(model.Response.AuthenticatorData);
            var sigBase = new byte[a.Length + hash.Length];
            a.CopyTo(sigBase, 0);
            hash.CopyTo(sigBase, a.Length);

            var ecDsa = ECDsa.Create(new ECParameters
            {
                Curve = ECCurve.NamedCurves.nistP256,
                Q = new ECPoint
                {
                    X = Base64Url.Decode(user.PublicKey.X),
                    Y = Base64Url.Decode(user.PublicKey.Y)
                }
            });

            var isValid = ecDsa.VerifyData(sigBase, DeserializeSignature(sig), HashAlgorithmName.SHA256);

            RequestResult rr = new RequestResult();
            if (isValid)
            {
                // 17. the signature counter value adata.signCount is nonzero or the value stored in conjunction with credential’s id attribute is nonzero
                if (user.Counter < counter)
                {
                    user.Counter = Convert.ToInt32(counter);
                    return new B2CResponseModel(JsonConvert.SerializeObject(rr), System.Net.HttpStatusCode.OK);
                }

                throw new Exception("Possible cloned authenticator");
            }

            throw new Exception("Invalid Signature");
        }

        public B2CResponseModel LoginCallbackAPI(CredentialsModel model)
        {
            // 1. If the allowCredentials option was given when this authentication ceremony was initiated, verify that credential.id identifies one of the public key credentials that were listed in allowCredentials.
            //var data = tempData.LoadTempData(HttpContext);

            // 2. If credential.response.userHandle is present, verify that the user identified by this value is the owner of the public key credential identified by credential.id.
            // 3. Using credential’s id attribute (or the corresponding rawId, if base64url encoding is inappropriate for your use case), look up the corresponding credential public key.

            // 4. Let cData, aData and sig denote the value of credential’s response's clientDataJSON, authenticatorData, and signature respectively.
            var cData = model.Response.ClientDataJson;
            var aData = model.Response.AuthenticatorData;
            var sig = model.Response.Signature;

            // 5. Let JSONtext be the result of running UTF-8 decode on the value of cData.
            var jsonText = Encoding.UTF8.GetString(Base64Url.Decode(cData));

            // 6. Let C, the client data claimed as used for the signature, be the result of running an implementation-specific JSON parser on JSONtext.
            var c = JsonConvert.DeserializeObject<ClientData>(jsonText);

            Dictionary<string, object> data = (Dictionary<string, object>)cache.Get(c.Challenge);
            if ((string)data["keyId"] != model.RawId) throw new Exception("Incorrect key used");

            List<User> _Users = (List<User>)cache.Get("Users");
            var user = Users.First(x => x.CredentialId == model.RawId);
            if (!string.IsNullOrEmpty(model.Response.UserHandle) && model.Response.UserHandle != user.Username) throw new Exception("Incorrect user handle returned");

            // 7. Verify that the value of C.type is the string webauthn.get.
            if (c.Type != "webauthn.get") throw new Exception("Incorrect client data type");

            // 8. Verify that the value of C.challenge matches the challenge that was sent to the authenticator in the PublicKeyCredentialRequestOptions passed to the get() call.
            var challenge = (string)data["challenge"];
            if (Base64Url.Decode(c.Challenge) == Convert.FromBase64String(challenge)) throw new Exception("Incorrect challenge");

            // 9. Verify that the value of C.origin matches the Relying Party's origin.
            //if (c.Origin != "http://localhost:5000") throw new Exception("Incorrect origin");

            // 10. Verify that the value of C.tokenBinding.status matches the state of Token Binding for the TLS connection over which the attestation was obtained.
            // If Token Binding was used on that TLS connection, also verify that C.tokenBinding.id matches the base64url encoding of the Token Binding ID for the connection.
            // TODO: Token binding once out of draft

            // 11. Verify that the rpIdHash in aData is the SHA-256 hash of the RP ID expected by the Relying Party.
            var hasher = new SHA256Managed();

            var span = Base64Url.Decode(aData).AsSpan();
            // RP ID Hash (32 bytes)
            var rpIdHash = span.Slice(0, 32); span = span.Slice(32);

            // Flags (1 byte)
            var flagsBuf = span.Slice(0, 1).ToArray();
            var flags = new BitArray(flagsBuf); span = span.Slice(1);
            var userPresent = flags[0]; // (UP)
            // Bit 1 reserved for future use (RFU1)
            var userVerified = flags[2]; // (UV)
            // Bits 3-5 reserved for future use (RFU2)
            var attestedCredentialData = flags[6]; // (AT) "Indicates whether the authenticator added attested credential data"
            var extensionDataIncluded = flags[7]; // (ED)

            // Signature counter (4 bytes, big-endian unint32)
            var counterBuf = span.Slice(0, 4); span = span.Slice(4);
            var counter = BitConverter.ToUInt32(counterBuf); // https://www.w3.org/TR/webauthn/#signature-counter

            //var computedRpIdHash = hasher.ComputeHash(Encoding.UTF8.GetBytes(RelyingPartyId));
            //if (!rpIdHash.SequenceEqual(computedRpIdHash)) throw new Exception("Incorrect RP ID");

            // 12. If user verification is required for this assertion, verify that the User Verified bit of the flags in aData is set.
            // TODO: Handle user verificaton required

            // 13. If user verification is not required for this assertion, verify that the User Present bit of the flags in aData is set.
            if (userPresent == false) throw new Exception("User not present");

            //14. Verify that the values of the client extension outputs in clientExtensionResults
            // TODO: Handle extension results

            // 15. Let hash be the result of computing a hash over the cData using SHA-256.
            var hash = hasher.ComputeHash(Base64Url.Decode(cData)); // 32 bytes

            // 16. Using the credential public key looked up in step 3, verify that sig is a valid signature over the binary concatenation of aData and hash.
            var a = Base64Url.Decode(model.Response.AuthenticatorData);
            var sigBase = new byte[a.Length + hash.Length];
            a.CopyTo(sigBase, 0);
            hash.CopyTo(sigBase, a.Length);

            var ecDsa = ECDsa.Create(new ECParameters
            {
                Curve = ECCurve.NamedCurves.nistP256,
                Q = new ECPoint
                {
                    X = Base64Url.Decode(user.PublicKey.X),
                    Y = Base64Url.Decode(user.PublicKey.Y)
                }
            });

            var isValid = ecDsa.VerifyData(sigBase, DeserializeSignature(sig), HashAlgorithmName.SHA256);

            RequestResult rr = new RequestResult();
            if (isValid)
            {
                // 17. the signature counter value adata.signCount is nonzero or the value stored in conjunction with credential’s id attribute is nonzero
                if (user.Counter < counter)
                {
                    user.Counter = Convert.ToInt32(counter);
                    return new B2CResponseModel(JsonConvert.SerializeObject(rr), System.Net.HttpStatusCode.OK);
                }

                throw new Exception("Possible cloned authenticator");
            }

            throw new Exception("Invalid Signature");
        }

        [HttpGet("GetUser/{username}")]
        public ActionResult<User> GetUser([FromQuery] string Username)
        {
            /*
            var data = tempData.LoadTempData(HttpContext);
            if (Users.Any(x => x.Username == Username))
            {
                return Users.Find(x => x.Username == Username);
            }
            */

            List<User> _Users = (List<User>)cache.Get("Users");
            if (Users.Any(x => x.Username == Username))
            {
                return _Users.Find(x => x.Username == Username);
            }

            return null;
        }

        private byte[] DeserializeSignature(string signature)
        {
            // Thanks to: https://crypto.stackexchange.com/questions/1795/how-can-i-convert-a-der-ecdsa-signature-to-asn-1
            var s = Base64Url.Decode(signature);

            using (var ms = new MemoryStream(s))
            {
                var header = ms.ReadByte();
                var b1 = ms.ReadByte();

                var markerR = ms.ReadByte();
                var b2 = ms.ReadByte();
                var vr = new byte[b2];
                ms.Read(vr, 0, vr.Length);
                vr = RemoveAnyNegativeFlag(vr);

                var markerS = ms.ReadByte();
                var b3 = ms.ReadByte();
                var vs = new byte[b3];
                ms.Read(vs, 0, vs.Length);
                vs = RemoveAnyNegativeFlag(vs);

                var parsedSignature = new byte[vr.Length + vs.Length];
                vr.CopyTo(parsedSignature, 0);
                vs.CopyTo(parsedSignature, vr.Length);

                return parsedSignature;
            }
        }

        private byte[] RemoveAnyNegativeFlag(byte[] input)
        {
            if (input[0] != 0) return input;

            var output = new byte[input.Length - 1];
            Array.Copy(input, 1, output, 0, output.Length);
            return output;
        }

   }
}