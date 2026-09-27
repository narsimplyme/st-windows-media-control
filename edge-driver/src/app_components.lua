local caps = require "st.capabilities"
local socket = require "cosock.socket"
local M = {}
M.capability = caps["oceangarden54575.appName"]
function M.is_legacy_child(device)
  local key = device.parent_assigned_child_key
  return type(key) == "string" and #key == 64 and not key:find("[^0-9a-f]")
end
function M.reset(device)
  device:set_field("appSlots", nil)
end
function M.sync(driver, device, apps, retireLegacy)
  local old = device:get_field("appSlots") or {}
  local slots = {}
  for _, app in ipairs(apps or {}) do slots["app" .. app.slot] = app end
  for i = 1, 5 do
    local id = "app" .. i
    local app = slots[id] or {name="Not configured", volume=0, muted=false, active=false}
    local before = old[id]
    local component = device.profile.components[id]
    if component then
      -- Explicit refresh must also repair names missed during profile propagation.
      if not before or before.name ~= app.name then device:emit_component_event(component, M.capability.appName(app.name, {state_change=true})) end
      local volume, muted = app.active and app.volume or 0, app.active and app.muted or false
      if not before or before.volume ~= volume then device:emit_component_event(component, caps.audioVolume.volume(volume)) end
      if not before or before.muted ~= muted then device:emit_component_event(component, caps.audioMute.mute(muted and "muted" or "unmuted")) end
      slots[id] = {key=app.key, slot=i, name=app.name, active=app.active, volume=volume, muted=muted}
    else
      -- Profile propagation can lag state delivery. Retry all events next time.
      slots[id] = nil
    end
  end
  device:set_field("appSlots", slots)
  -- Retire only this parent's old app children after the new protocol is live.
  if not retireLegacy then return end
  local now = socket.gettime()
  local retries = device:get_field("legacyAppDeletes") or {}
  for _, child in ipairs(device:get_child_list()) do
    if M.is_legacy_child(child) and (not retries[child.id] or now - retries[child.id] >= 30) then
      retries[child.id] = now
      child:offline()
      pcall(driver.try_delete_device, driver, child.id)
    end
  end
  device:set_field("legacyAppDeletes", retries)
end
function M.binding(device, component)
  local app = (device:get_field("appSlots") or {})[component]
  if app and app.key and app.active then return app end
end
return M
