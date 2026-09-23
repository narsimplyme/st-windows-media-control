local p = require "protocol"
local config = {pcAddress="192.168.1.20", pcPort=8765, token=string.rep("a",32), deviceId="12345678-1234-1234-1234-123456789abc"}
assert(p.configuration(config))
for _, invalid in ipairs({string.rep("a",31), string.rep("a",33), string.rep("a",36), string.rep("a",64), string.rep("0",32), string.rep("g",32)}) do
  config.token=invalid
  assert(not p.configuration(config))
end
config.token=string.rep("a",32)
config.deviceId="00000000-0000-0000-0000-000000000000"
assert(not p.configuration(config))
config.deviceId="12345678-1234-1234-1234-123456789abc"
config.pcAddress="192.168.1.20/path"
assert(not p.configuration(config))
config.pcAddress="999.168.1.20"
assert(not p.configuration(config))
config.pcAddress="192.168.1.20"
config.token="secret\r\nInjected: yes"
assert(not p.configuration(config))
local s = {deviceId=config.deviceId, epoch=string.rep("a",32), revision=1,
  audio={available=true, volume=0, muted=false}, media={available=false, playback="stopped", title="", artist="", source="",
    canPlay=false, canPause=false, canNext=false, canPrevious=false, canToggle=false}}
assert(p.valid(s,config.deviceId))
assert(not p.valid(s,"another-pc"))
assert(p.newer(nil,s))
assert(not p.newer(s,s))
assert(not p.newer({epoch=s.epoch,revision=2},s))
assert(p.newer({epoch=string.rep("b",32),revision=999},s))
s.audio.volume=101
assert(not p.valid(s,config.deviceId))
s.audio.volume=12.5
assert(not p.valid(s,config.deviceId))
s.audio.volume=100
s.media.canPlay=nil
assert(not p.valid(s,config.deviceId))
print("PASS Edge protocol validation and restart/revision ordering")

local short = {pcAddress="192.168.1.20", pcPort=8765, pairingCode="1234567890"}
local pending = p.configuration(short)
assert(pending and pending.code == short.pairingCode and not pending.token)
for _, invalid in ipairs({"123456789", "12345678901", "12345abcde", "１２３４５６７８９０", "0000000000"}) do
  short.pairingCode = invalid
  assert(not p.configuration(short))
end
short.pairingCode = "1234567890"
local saved = {ip=short.pcAddress, port=8765, code=short.pairingCode, deviceId=config.deviceId, token=string.rep("b",32)}
assert(p.configuration(short,saved).token == saved.token, "cached credentials survive code expiry")
short.pairingCode = "9876543210"
assert(not p.configuration(short,saved).token, "changing code forces a new exchange")
short.pairingCode = saved.code
short.pcAddress = "192.168.1.21"
assert(not p.configuration(short,saved).token, "cached credentials must not be sent to another PC")
print("PASS numeric code validation and credential binding")

short.pcAddress = saved.ip
short.pairingCode = 1234567890
assert(p.configuration(short,saved).token == saved.token, "numeric input retains existing string-code pairing")
for _, value in ipairs({1000000000, 2147483648, 9999999999}) do
  short.pairingCode = value
  assert(p.configuration(short).code == string.format("%.0f",value), "all ten-digit values preserve precision")
end
for _, value in ipairs({0, -1, 999999999, 10000000000, 1234567890.5, math.huge, 0/0}) do
  short.pairingCode = value
  assert(not p.configuration(short), "invalid numeric code rejected")
end
print("PASS numeric keyboard preference normalization, bounds and saved-pairing compatibility")
