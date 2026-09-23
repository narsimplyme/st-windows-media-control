-- Run inside an Edge test driver with a separately verified PC certificate PEM.
-- No pairing code/token is sent. Do not obtain trusted_pem from this connection.
local cosock = require "cosock"
local socket = cosock.socket
local ssl = cosock.ssl or require "cosock.ssl"
return function(ip, port, trusted_pem)
  assert(type(trusted_pem) == "string" and trusted_pem:find("BEGIN CERTIFICATE", 1, true))
  local tcp = assert(socket.tcp())
  tcp:settimeout(5)
  local ok, err = tcp:connect(ip, port)
  if not ok then tcp:close(); return nil, err end
  local tls
  tls, err = ssl.wrap(tcp, {
    mode = "client", protocol = "any", verify = "peer", cafile = trusted_pem,
    options = {"no_sslv2", "no_sslv3", "no_tlsv1", "no_tlsv1_1"},
  })
  if not tls then tcp:close(); return nil, err end
  tls:settimeout(5)
  ok, err = tls:dohandshake()
  if ok then
    ok, err = tls:send("GET /probe HTTP/1.1\r\nHost: " .. ip .. "\r\nConnection: close\r\n\r\n")
    if ok then
      local status
      status, err = tls:receive("*l")
      ok = status and status:match("^HTTP/1%.[01] 200 ") ~= nil
      if not ok then err = err or "unexpected HTTP status" end
    end
  end
  tls:close()
  return ok, err
end
