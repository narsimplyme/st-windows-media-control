local caps = require "st.capabilities"
local socket = require "cosock.socket"
local M = {}
local pending, deleting = {}, {}
function M.is_child(device) return device.network_type == "EDGE_CHILD" end
function M.key(device)
  local key = device.parent_assigned_child_key
  if type(key) == "string" and #key == 64 and not key:find("[^0-9a-f]") then return key end
end
function M.offline(parent)
  for _, child in ipairs(parent:get_child_list()) do if M.key(child) then child:offline() end end
end
function M.sync(driver, parent, apps)
  if apps == nil then return end -- Old companion: never interpret missing data as deselection.
  local wanted, existing = {}, {}
  for _, app in ipairs(apps) do wanted[app.key] = app end
  local now = socket.gettime()
  pending[parent.id] = pending[parent.id] or {}
  local creating = pending[parent.id]
  for _, child in ipairs(parent:get_child_list()) do
    local key = M.key(child)
    if key then
      existing[key] = true; creating[key] = nil
      local app = wanted[key]
      if not app or deleting[child.id] then
        child:offline()
        if not deleting[child.id] or now - deleting[child.id] > 30 then
          deleting[child.id] = now
          local ok = pcall(driver.try_delete_device, driver, child.id)
          if not ok then deleting[child.id] = nil end
        end
      else
        local old = child:get_field("appVolumeState")
        if app.active then
          if not old or not old.active or old.volume ~= app.volume then child:emit_event(caps.audioVolume.volume(app.volume)) end
          if not old or not old.active or old.muted ~= app.muted then child:emit_event(caps.audioMute.mute(app.muted and "muted" or "unmuted")) end
          child:online()
        else child:offline() end
        child:set_field("appVolumeState", app)
      end
    end
  end
  for key, app in pairs(wanted) do
    if not existing[key] and (not creating[key] or now - creating[key] > 60) then
      creating[key] = now
      local ok = pcall(driver.try_create_device, driver, {type="EDGE_CHILD", parent_device_id=parent.id,
        parent_assigned_child_key=key, label="PC " .. app.name, profile="app-volume",
        manufacturer="ST Windows Media Control", model="Windows app volume"})
      if not ok then creating[key] = nil end
    end
  end
end
function M.removed(device)
  deleting[device.id] = nil
  if not M.is_child(device) then pending[device.id] = nil end
end
return M
