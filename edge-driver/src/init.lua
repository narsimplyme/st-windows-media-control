local Driver = require "st.driver"
local caps = require "st.capabilities"
local cosock = require "cosock"
local socket = require "cosock.socket"
local log = require "log"
local client = require "client"
local protocol = require "protocol"
local workers = {}
local SLOT = "st-mediabridge-manual-v1"
local creating = false

local function emit(device, s, old)
  local a, m = s.audio, s.media
  if not old then
    -- Advertise the driver's stable command set, not state-dependent Windows
    -- flags. The companion checks whether each action is currently available.
    device:emit_event(caps.mediaPlayback.supportedPlaybackCommands({"play", "pause"}))
  end
  if a.available then
    if not old or not old.audio.available or a.volume ~= old.audio.volume then device:emit_event(caps.audioVolume.volume(a.volume)) end
    if not old or not old.audio.available or a.muted ~= old.audio.muted then
      device:emit_event(caps.audioMute.mute(a.muted and "muted" or "unmuted"))
    end
  end
  if not old or m.playback ~= old.media.playback then device:emit_event(caps.mediaPlayback.playbackStatus(m.playback)) end
  if not old or m.title ~= old.media.title or m.artist ~= old.media.artist or m.source ~= old.media.source or
      m.album ~= old.media.album or m.albumArtUrl ~= old.media.albumArtUrl then
    local art_url = m.albumArtUrl or ""
    device:emit_event(caps.audioTrackData.audioTrackData({title=m.title, artist=m.artist, mediaSource=m.source,
      album=m.album or "", albumArtUrl=art_url}))
  end
  -- Lack of an output endpoint leaves last known volume intact, not a false zero.
  if a.available then device:online() else device:offline() end
end

local function configuration(device)
  return protocol.configuration(device.preferences, device:get_field("pairingCredentials"))
end
local function remember(device, config)
  device:set_field("pairingCredentials", {ip=config.ip, port=config.port, code=config.code,
    deviceId=config.id, token=config.token}, {persist=true})
end
local function restart(_, device)
  creating = false
  local config = configuration(device)
  if config and config.token then remember(device, config) end
  local profile = device.preferences.deviceIcon == "speaker" and "media-bridge-speaker" or "media-bridge"
  -- Nonpersistent cache allows retry on driver restart, and prevents metadata
  -- infoChanged events from recursively issuing the same update.
  if device:get_field("requestedIconProfile") ~= profile then
    device:set_field("requestedIconProfile", profile)
    local ok = pcall(device.try_update_metadata, device, {profile=profile})
    if not ok then
      device:set_field("requestedIconProfile", nil)
      log.warn("ST Windows Media Control: icon profile update failed; refresh to retry")
    end
  end
  local worker = {refresh = true}
  workers[device.id] = worker -- Invalidates old requests on preference changes/removal.
  device:offline()
  if not config then
    log.info("ST Windows Media Control: enter the PC address and 10-digit pairing code in Settings")
    return
  end
  cosock.spawn(function()
    local previous, delay, last_error = nil, 1, nil
    while workers[device.id] == worker do
      if not config.token then
        local ok, paired = pcall(client.request, config, "/v1/pair", {code=config.code})
        if workers[device.id] ~= worker then return end
        if ok and protocol.credentials(paired) then
          config.id, config.token = paired.deviceId, paired.token
          remember(device, config)
          delay = 1
        else
          device:offline()
          if not last_error then log.warn("ST Windows Media Control: pairing failed; check the code in the PC tray") end
          last_error = "pairing failed"
          socket.sleep(delay)
          delay = math.min(delay * 2, 30)
        end
      end
      if config.token then
      local path = "/v1/state"
      if previous and not worker.refresh then
        path = string.format("/v1/events?epoch=%s&after=%d", previous.epoch, previous.revision)
      end
      worker.refresh = false
      local ok, state, err = pcall(client.request, config, path)
      if workers[device.id] ~= worker then return end
      if not ok then state, err = nil, "request failed" end
      if state and not protocol.valid(state, config.id) then state, err = nil, "invalid state or device ID mismatch" end
      if state then
        if last_error then log.info("ST Windows Media Control: connection restored") end
        last_error, delay = nil, 1
        if protocol.newer(previous, state) then
          emit(device, state, previous)
          previous = state
        elseif state.audio.available then
          device:online()
        end
        socket.sleep(0.1) -- Bound event rate while the volume wheel is turning.
      else
        device:offline()
        if err ~= last_error then log.warn("ST Windows Media Control: " .. (err or "connection failed")) end
        last_error = err
        socket.sleep(delay)
        delay = math.min(delay * 2, 30)
      end
      end
    end
  end, "mediabridge-state-" .. device.id)
end

local function command(device, name, value)
  local config = configuration(device)
  if not config or not config.token then return end
  -- These handlers run in the device's ordered coroutine. The long-poll has its
  -- own coroutine, so it cannot stall commands. Never replay a timed-out skip.
  local ok, result, err = pcall(client.request, config, "/v1/command", {command=name, value=value})
  if not ok or not result then log.warn("ST Windows Media Control: command " .. name .. " failed (" .. (ok and err or "network") .. ")") end
end
local function simple(name) return function(_, device) command(device, name) end end
local function playback(name)
  return function(_, device, cmd)
    -- Some standard Speaker cards send stop even with a play/pause profile.
    -- Treat that as pause, never toggle: repeated taps must not resume audio.
    log.info("ST Windows Media Control: playback " .. cmd.command .. " -> " .. name)
    command(device, name)
  end
end
local function discovery(driver)
  for _, device in ipairs(driver:get_devices()) do
    if device.device_network_id == SLOT then return end
  end
  if creating then return end
  creating = true
  driver:try_create_device({type="LAN", device_network_id=SLOT,
    label="ST Windows Media Control", profile="media-bridge", manufacturer="ST Windows Media Control", model="Windows media"})
  driver:call_with_delay(30, function() creating = false end)
end
Driver("st-mediabridge", {
  discovery = discovery,
  lifecycle_handlers = {
    init = restart,
    infoChanged = restart,
    removed = function(_, device) workers[device.id] = nil end,
  },
  supported_capabilities = {caps.audioVolume, caps.audioMute, caps.mediaPlayback, caps.mediaTrackControl, caps.audioTrackData, caps.refresh},
  capability_handlers = {
    [caps.audioVolume.ID] = {
      setVolume = function(_, d, c) command(d, "setVolume", c.args.volume) end,
      volumeUp = function(_, d) command(d, "adjustVolume", 5) end,
      volumeDown = function(_, d) command(d, "adjustVolume", -5) end,
    },
    [caps.audioMute.ID] = {
      mute = function(_, d) command(d, "setMute", true) end,
      unmute = function(_, d) command(d, "setMute", false) end,
      setMute = function(_, d, c) command(d, "setMute", c.args.state == "muted") end,
    },
    [caps.mediaPlayback.ID] = {play = playback("play"), pause = playback("pause"), stop = playback("pause")},
    [caps.mediaTrackControl.ID] = {nextTrack = simple("next"), previousTrack = simple("previous")},
    [caps.refresh.ID] = {refresh = function(driver, device) restart(driver, device) end},
  },
}):run()
