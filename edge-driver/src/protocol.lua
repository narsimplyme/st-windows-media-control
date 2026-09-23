-- Pure validation and revision logic; also exercised without the Edge runtime.
local M = {}
function M.configuration(p, saved)
  local ip = p.pcAddress or ""
  local a, b, c, d = ip:match("^(%d+)%.(%d+)%.(%d+)%.(%d+)$")
  if not a then return nil end
  for _, octet in ipairs({a, b, c, d}) do
    if #octet > 3 or tonumber(octet) > 255 then return nil end
  end
  if tonumber(a) == 0 or tonumber(a) == 127 or tonumber(a) >= 224 then return nil end
  local port = tonumber(p.pcPort or 8765)
  if not port or port % 1 ~= 0 or port < 1024 or port > 65535 then return nil end
  local code = p.pairingCode
  -- Integer preferences request numeric input in the app. Normalize before
  -- comparing persisted string codes; never round fractions or use exponent notation.
  if type(code) == "number" then
    if code ~= code or code < 0 or code > 9999999999 or code % 1 ~= 0 then return nil end
    code = code == 0 and "" or string.format("%.0f", code)
  end
  if code == "" or code == "0000000000" then code = nil end
  if code ~= nil and (type(code) ~= "string" or #code ~= 10 or code:find("[^0-9]")) then return nil end
  local result = {ip=ip, port=port, code=code}
  if type(saved) == "table" and saved.ip == ip and saved.port == port and saved.code == code and M.credentials(saved) then
    result.id, result.token = saved.deviceId, saved.token
    return result
  end
  if code then return result end
  -- Upgrade compatibility: capture legacy preferences privately before changing
  -- to the short-code profile. New users never need to enter these fields.
  if M.credentials(p) then result.id, result.token = p.deviceId, p.token; return result end
  return nil
end
function M.credentials(p)
  return type(p) == "table" and type(p.token) == "string" and #p.token == 32
    and not p.token:find("[^%x]") and p.token ~= string.rep("0", 32)
    and type(p.deviceId) == "string"
    and p.deviceId:match("^%x%x%x%x%x%x%x%x%-%x%x%x%x%-%x%x%x%x%-%x%x%x%x%-%x%x%x%x%x%x%x%x%x%x%x%x$") ~= nil
    and p.deviceId ~= "00000000-0000-0000-0000-000000000000"

end
function M.valid(s, id)
  if type(s) ~= "table" or s.deviceId ~= id or type(s.epoch) ~= "string" or
      #s.epoch ~= 32 or s.epoch:find("[^%x]") or type(s.revision) ~= "number" or
      s.revision < 1 or s.revision % 1 ~= 0 then return false end
  local a, m = s.audio, s.media
  if type(a) ~= "table" or type(m) ~= "table" then return false end
  if m.album ~= nil and (type(m.album) ~= "string" or #m.album > 1024) then return false end
  if type(a.available) ~= "boolean" or type(a.volume) ~= "number" or a.volume < 0 or
      a.volume > 100 or a.volume % 1 ~= 0 or type(a.muted) ~= "boolean" then return false end
  if type(m.available) ~= "boolean" or not ({playing=true, paused=true, stopped=true})[m.playback] then return false end
  for _, key in ipairs({"title", "artist", "source"}) do
    if type(m[key]) ~= "string" or #m[key] > 1024 then return false end
  end
  for _, key in ipairs({"canPlay", "canPause", "canNext", "canPrevious", "canToggle"}) do
    if type(m[key]) ~= "boolean" then return false end
  end
  return true
end
function M.newer(previous, current)
  return not previous or previous.epoch ~= current.epoch or current.revision > previous.revision
end
return M
