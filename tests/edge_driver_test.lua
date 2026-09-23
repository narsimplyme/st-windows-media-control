-- Behavioral harness: real init.lua, fake SmartThings API and cooperative transport.
-- Complements, but does not replace, an actual hub test.
local captured, spawned, posts = nil, {}, {}
local caps = {}
for _, name in ipairs({"audioVolume", "audioMute", "mediaPlayback", "mediaTrackControl", "audioTrackData", "refresh"}) do
  local id = name
  caps[id] = setmetatable({ID=id}, {__index=function(_, attribute)
    return function(value) return {capability=id, attribute=attribute, value=value} end
  end})
end
package.loaded["st.capabilities"] = caps
package.loaded["log"] = {info=function() end, warn=function() end}
package.loaded["cosock"] = {spawn=function(fn) spawned[#spawned+1] = coroutine.create(fn) end}
package.loaded["cosock.socket"] = {sleep=function() coroutine.yield("sleep") end}
package.loaded["client"] = {request=function(config, path, body)
  if path == "/v1/pair" then return coroutine.yield("pair", body.code) end
  if body then posts[#posts+1]=body; return {accepted=true} end
  return coroutine.yield("request", path)
end}
package.loaded["st.driver"] = function(_, definition)
  captured = definition
  return {run=function() end}
end
local preferences = {pcAddress="192.168.1.20", pcPort=8765, token=string.rep("a",32), deviceId="12345678-1234-1234-1234-123456789abc"}
local device = {id="test", preferences=preferences, events={}, device_network_id="st-mediabridge-manual-v1"}
device.fields = {tlsTrust={ip=preferences.pcAddress, port=preferences.pcPort, certificate="test-certificate"}}
device.metadataRequests = 0
function device:get_field(key) return self.fields[key] end
function device:set_field(key, value) self.fields[key] = value end
function device:try_update_metadata(metadata) self.metadata = metadata; self.metadataRequests = self.metadataRequests + 1 end
function device:emit_event(event)
  if event.capability == "audioTrackData" then
    assert(event.value.albumArtUrl == nil or event.value.albumArtUrl:match("^https?://"),
      "optional artwork URI must not be an empty string, including certificate prompts")
  end
  self.events[#self.events+1]=event
end
function device:online() self.connected=true end
function device:offline() self.connected=false end
local driver = {devices={}, creates=0}
function driver:get_devices() return self.devices end
function driver:try_create_device(_) self.creates=self.creates+1 end
function driver:call_with_delay(_, fn) self.delayed=fn end
local function step(co, value)
  local ok, kind, path = coroutine.resume(co, value)
  assert(ok,kind)
  return kind,path
end
local function snapshot(revision, volume, epoch)
  return {deviceId=preferences.deviceId, epoch=epoch or string.rep("a",32), revision=revision,
    audio={available=true,volume=volume,muted=false},
    media={available=false,playback="stopped",title="",artist="",source="",
      canPlay=false,canPause=false,canNext=false,canPrevious=false,canToggle=false}}
end
dofile(TEST_ROOT .. "/edge-driver/src/init.lua")
-- A single untouched sentinel must prevent both state requests and commands.
local pairedToken = preferences.token
preferences.token = string.rep("0",32)
captured.lifecycle_handlers.init(driver,device)
captured.capability_handlers.audioVolume.setVolume(driver,device,{args={volume=25}})
assert(#spawned==0 and #posts==0 and not device.connected, "unpaired driver must not communicate")
preferences.token = pairedToken
captured.discovery(driver)
captured.discovery(driver)
assert(driver.creates==1, "pending discovery must not duplicate")
driver.devices={device}
captured.lifecycle_handlers.init(driver,device)
captured.discovery(driver)
assert(driver.creates==1, "saved device must not duplicate")
local worker = spawned[#spawned]
assert(step(worker)=="request")
assert(step(worker,snapshot(1,0))=="sleep")
assert(device.connected)
local count=#device.events
assert(count>0)
step(worker)
step(worker,snapshot(1,0))
assert(#device.events==count, "duplicate snapshot must not emit")
step(worker)
step(worker,snapshot(2,67))
assert(device.events[#device.events].attribute=="volume" and device.events[#device.events].value==67)
count=#device.events
step(worker)
step(worker,snapshot(1,10))
assert(#device.events==count, "old revision must not roll back state")
step(worker)
step(worker,snapshot(1,12,string.rep("b",32)))
assert(device.events[#device.events].value==12, "restart epoch must resync")
captured.capability_handlers.audioVolume.setVolume(driver,device,{args={volume=0}})
captured.capability_handlers.audioMute.unmute(driver,device)
captured.capability_handlers.mediaTrackControl.nextTrack(driver,device)
assert(posts[1].command=="setVolume" and posts[1].value==0)
assert(posts[2].command=="setMute" and posts[2].value==false)
assert(posts[3].command=="next")
captured.capability_handlers.mediaPlayback.stop(driver,device,{command="stop"})
captured.capability_handlers.mediaPlayback.stop(driver,device,{command="stop"})
captured.capability_handlers.mediaPlayback.pause(driver,device,{command="pause"})
captured.capability_handlers.mediaPlayback.play(driver,device,{command="play"})
assert(posts[4].command=="pause" and posts[5].command=="pause", "stop must pause, never toggle or resume")
assert(posts[6].command=="pause" and posts[7].command=="play", "explicit play/pause commands preserved")
local advertised
for _, event in ipairs(device.events) do
  if event.attribute=="supportedPlaybackCommands" then advertised=event.value end
end
assert(#advertised==2 and advertised[1]=="play" and advertised[2]=="pause", "advertise stable play/pause even without a session")
assert(not captured.capability_handlers.switch)
step(worker)
local withArt=snapshot(2,12,string.rep("b",32))
withArt.media.album="Album"
withArt.media.albumArtUrl="http://192.168.1.20:8765/v1/artwork/" .. string.rep("c",32)
step(worker,withArt)
assert(device.events[#device.events].value.albumArtUrl==nil, "old companion artwork must not be forwarded")
step(worker)
step(worker,snapshot(3,12,string.rep("b",32)))
assert(device.events[#device.events].value.albumArtUrl==nil, "absent artwork must omit the optional URI field")
step(worker) -- old worker is now waiting on a request
captured.lifecycle_handlers.infoChanged(driver,device)
count=#device.events
step(worker,snapshot(100,99))
assert(coroutine.status(worker)=="dead" and #device.events==count, "old pairing response must be discarded")
worker=spawned[#spawned]
step(worker)
captured.lifecycle_handlers.removed(driver,device)
step(worker,snapshot(200,99))
assert(coroutine.status(worker)=="dead" and #device.events==count, "removed device must stop")
print("PASS Edge discovery, commands, event mapping, ordering and lifecycle invalidation")

preferences.deviceIcon = "speaker"
captured.lifecycle_handlers.infoChanged(driver,device)
assert(device.metadata.profile == "media-bridge-speaker")
local requests = device.metadataRequests
captured.lifecycle_handlers.infoChanged(driver,device)
assert(device.metadataRequests == requests, "metadata events must not loop")
preferences.deviceIcon = "invalid"
captured.lifecycle_handlers.infoChanged(driver,device)
assert(device.metadata.profile == "media-bridge", "unknown icon falls back to monitor")
assert(preferences.token == pairedToken, "icon selection must preserve credentials")
print("PASS icon profile selection and metadata loop prevention")

preferences.pairingCode = "1234567890"
preferences.deviceId, preferences.token = nil, nil
captured.lifecycle_handlers.infoChanged(driver,device)
local pairingWorker = spawned[#spawned]
local kind, code = step(pairingWorker)
assert(kind == "pair" and code == preferences.pairingCode)
assert(step(pairingWorker, {deviceId="12345678-1234-1234-1234-123456789abc",token=string.rep("b",32)}) == "request")
local saved = device:get_field("pairingCredentials")
assert(saved.token == string.rep("b",32) and saved.code == code)
captured.capability_handlers.audioVolume.setVolume(driver,device,{args={volume=20}})
assert(posts[#posts].value == 20, "commands use saved internal credential")
captured.lifecycle_handlers.init(driver,device)
assert(step(spawned[#spawned]) == "request", "restart reuses saved credentials without pairing again")
preferences.pairingCode = "9876543210"
captured.lifecycle_handlers.infoChanged(driver,device)
local stale = spawned[#spawned]
assert(step(stale) == "pair")
preferences.pairingCode = "1111111111"
captured.lifecycle_handlers.infoChanged(driver,device)
step(stale,{deviceId="12345678-1234-1234-1234-123456789abc",token=string.rep("c",32)})
assert(device:get_field("pairingCredentials").token == string.rep("b",32), "stale pairing response cannot overwrite current credentials")
local failing = spawned[#spawned]
assert(step(failing) == "pair")
assert(step(failing,{token="invalid"}) == "sleep", "invalid exchange response must not be persisted")
print("PASS short-code exchange, persistence, command use and stale-response rejection")

-- An untrusted certificate must never receive a pairing code or bearer token.
device.fields.tlsTrust = nil
package.loaded["client"].discover = function() return coroutine.yield("discover") end
package.loaded["client"].fingerprint = function() return "11111111 22222222", "33333333 44444444" end
captured.lifecycle_handlers.infoChanged(driver,device)
local enrollment = spawned[#spawned]
assert(step(enrollment) == "discover")
assert(step(enrollment,"candidate-certificate") == "sleep")
assert(device:get_field("tlsTrust") == nil, "candidate must not be persisted")
assert(step(enrollment) == "sleep", "pairing must wait for comparison approval")
captured.capability_handlers.mediaPlayback.pause(driver,device,{command="pause"})
assert(step(enrollment) == "sleep", "pause cannot approve trust")
captured.capability_handlers.mediaPlayback.play(driver,device,{command="play"})
assert(step(enrollment) == "sleep", "play/automation cannot approve certificate trust")
preferences.approveCertificate = true
captured.lifecycle_handlers.infoChanged(driver,device)
enrollment = spawned[#spawned]
assert(device:get_field("tlsTrust").certificate == "candidate-certificate", "explicit comparison approval persists the certificate independently of pairing")
assert(step(enrollment) == "pair", "dedicated approval permits verified exchange")
assert(step(enrollment,nil) == "sleep", "expired code can fail after certificate approval")
preferences.pairingCode = "12345678"
captured.lifecycle_handlers.infoChanged(driver,device)
enrollment = spawned[#spawned]
local kind, code = step(enrollment)
assert(kind == "pair" and code == "12345678", "new code pairs immediately without toggling approval")
captured.lifecycle_handlers.init(driver,device)
enrollment = spawned[#spawned]
assert(step(enrollment) == "pair", "approved pin survives driver restart before pairing succeeds")
assert(step(enrollment,{deviceId="12345678-1234-1234-1234-123456789abc",token=string.rep("d",32)}) == "request")
assert(device:get_field("tlsTrust").certificate == "candidate-certificate")
preferences.verifyCertificate = true
captured.lifecycle_handlers.infoChanged(driver,device)
assert(device:get_field("tlsTrust") == nil, "explicit reset clears trust")
assert(step(spawned[#spawned]) == "discover")
print("PASS certificate discovery, approval gate, persistence and explicit trust reset")

local resetWorker = spawned[#spawned]
assert(step(resetWorker,"replacement-certificate") == "sleep")
preferences.pairingCode = "87654321"
captured.lifecycle_handlers.infoChanged(driver,device)
assert(device:get_field("tlsTrust") == nil, "enabled approval switch must not approve a replacement certificate")
assert(step(spawned[#spawned]) == "discover", "trust reset still requires explicit comparison approval")
preferences.pcAddress = "192.168.1.21"
captured.lifecycle_handlers.infoChanged(driver,device)
assert(step(spawned[#spawned]) == "discover", "different PC cannot inherit certificate approval")
print("PASS expired-code retry retains approved pin; replacement certificates require approval")
