-- Exercise the real client; transport itself is verified on the real hub.
local requests, trust = {}, {}
local transport = {request=function(request)
  requests[#requests+1] = request
  request.sink('{}')
  return 1, 200
end}
package.loaded["cosock"] = {asyncify=function(name)
  assert(name == "ssl.https", "client must use HTTPS")
  return transport
end}
package.loaded["tls_trust"] = trust
package.loaded["ltn12"] = {source={string=function(value) return value end}}
package.loaded["st.json"] = {encode=function() return '{}' end, decode=function() return {} end}
package.loaded["client"] = nil
local client = require "client"
local config = {ip="192.168.1.10", port=8765, token="test-only"}
local result, err = client.request(config, "/v1/pair", {code="1234567890"})
assert(result == nil and err == "TLS trust is not provisioned" and #requests == 0)
trust.certificate = "-----BEGIN CERTIFICATE-----\ntest-only\n-----END CERTIFICATE-----"
assert(client.request(config, "/v1/state"))
local request = requests[1]
assert(request.url == "https://192.168.1.10:8765/v1/state")
assert(request.verify == "peer" and request.cafile == trust.certificate)
assert(request.redirect == false)
local options = {}
for _, value in ipairs(request.options) do options[value] = true end
assert(options.no_tlsv1 and options.no_tlsv1_1)
transport.request = function() return nil, "certificate verify failed" end
assert(client.request(config, "/v1/state") == nil)
assert(#requests == 1, "must not retry over plaintext")
print("PASS real Edge client requires provisioned trust, verified HTTPS and no plaintext fallback")
