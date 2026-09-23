local cosock = require "cosock"
local http = cosock.asyncify "socket.http"
local ltn12 = require "ltn12"
local json = require "st.json"
local M = {}
http.TIMEOUT = 25 -- Longer than the companion's 20-second event hold.
function M.request(config, path, body)
  local chunks, size = {}, 0
  local payload = body and json.encode(body)
  local headers = {Authorization = "Bearer " .. (config.token or ""), Connection = "close"}
  if payload then
    headers["Content-Type"] = "application/json"
    headers["Content-Length"] = tostring(#payload)
  end
  local ok, code = http.request({
    url = string.format("http://%s:%d%s", config.ip, config.port, path),
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
return M
