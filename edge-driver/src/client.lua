local cosock = require "cosock"
local http = cosock.asyncify "ssl.https"
local sha2 = require "sha2"
local ltn12 = require "ltn12"
local json = require "st.json"
local M = {}
http.TIMEOUT = 25 -- Longer than the companion's 20-second event hold.
local function request(config, path, body, bootstrap)
  if not bootstrap and (type(config.certificate) ~= "string" or not config.certificate:find("BEGIN CERTIFICATE", 1, true)) then
    return nil, "TLS trust is not provisioned"
  end
  local chunks, size = {}, 0
  local payload = body and json.encode(body)
  local headers = {Connection = "close"}
  if not bootstrap then headers.Authorization = "Bearer " .. (config.token or "") end
  if payload then
    headers["Content-Type"] = "application/json"
    headers["Content-Length"] = tostring(#payload)
  end
  local ok, code = http.request({
    url = string.format("https://%s:%d%s", config.ip, config.port, path),
    protocol = "any", verify = bootstrap and "none" or "peer", cafile = not bootstrap and config.certificate or nil,
    options = {"no_sslv2", "no_sslv3", "no_tlsv1", "no_tlsv1_1"},
    method = payload and "POST" or "GET", headers = headers,
    redirect = false, -- Never forward a pairing credential to a redirect target.
    source = payload and ltn12.source.string(payload) or nil,
    sink = function(chunk)
      if chunk then
        size = size + #chunk
        if size > 16384 then return nil, "response too large" end
        chunks[#chunks + 1] = chunk
      end
      return 1
    end,
  })
  if not ok then return nil, "network" end
  if tonumber(code) ~= 200 then return nil, "HTTP " .. tostring(code) end
  local decoded, result = pcall(json.decode, table.concat(chunks))
  if not decoded or type(result) ~= "table" then return nil, "invalid JSON" end
  return result
end
function M.request(config, path, body) return request(config, path, body, false) end

function M.fingerprint(pem)
  if type(pem) ~= "string" or #pem > 8192 then return nil end
  local body = pem:match("^%s*%-%-%-%-%-BEGIN CERTIFICATE%-%-%-%-%-%s*([A-Za-z0-9+/=%s]+)%s*%-%-%-%-%-END CERTIFICATE%-%-%-%-%-%s*$")
  if not body then return nil end
  body = body:gsub("%s", "")
  local hash = sha2.sha256(body):sub(1,32):upper()
  return hash:sub(1,8) .. " " .. hash:sub(9,16), hash:sub(17,24) .. " " .. hash:sub(25,32)
end

-- Public-certificate discovery only: no bearer, pairing code, or other secrets.
-- Its result is untrusted until the user compares the locally computed fingerprint.
function M.discover(config)
  local identity, err = request({ip=config.ip, port=config.port}, "/v1/identity", nil, true)
  if not identity or not M.fingerprint(identity.certificate) then return nil, err or "invalid certificate" end
  return identity.certificate
end
return M
