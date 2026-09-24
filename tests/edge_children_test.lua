local caps = require "st.capabilities"
local clock = 100
package.loaded["cosock.socket"].gettime = function() return clock end
local children = require "app_children"
local protocol = require "protocol"
local list, created, deleted = {}, {}, {}
local parent = {id="parent", get_child_list=function() return list end}
local driver = {try_create_device=function(_, data) created[#created+1]=data end,
  try_delete_device=function(_, id) deleted[#deleted+1]=id end}
local app = {key=string.rep("a",64), name="Spotify", active=true, volume=25, muted=false}
assert(protocol.apps({app}))
assert(not protocol.apps({app, app}), "duplicate keys must fail before deleting any devices")
assert(not protocol.apps({wrong=app}), "malformed app list cannot imply deselection")
assert(not protocol.apps({{key=app.key,name="Spotify",active=true,volume=0/0,muted=false}}))
children.sync(driver,parent,{app})
children.sync(driver,parent,{app})
assert(#created == 1 and created[1].type == "EDGE_CHILD" and created[1].label == "PC Spotify")
assert(created[1].parent_assigned_child_key == app.key and created[1].profile == "app-volume")
local child = {id="child1",network_type="EDGE_CHILD",parent_assigned_child_key=app.key,fields={},events={}}
function child:get_field(k) return self.fields[k] end
function child:set_field(k,v) self.fields[k]=v end
function child:emit_event(e) self.events[#self.events+1]=e end
function child:online() self.connected=true end
function child:offline() self.connected=false end
list={child}
children.sync(driver,parent,{app})
assert(child.connected and #child.events == 2)
child.label="Renamed by user"
app={key=app.key,name="Spotify",active=true,volume=70,muted=true}
children.sync(driver,parent,{app})
assert(child.label=="Renamed by user" and child.events[3].value==70 and child.events[4].value=="muted")
app={key=app.key,name="Spotify",active=false,volume=70,muted=true}
children.sync(driver,parent,{app})
assert(not child.connected and #deleted==0, "app exit never deletes child")
app={key=app.key,name="Spotify",active=true,volume=60,muted=false}
children.sync(driver,parent,{app})
assert(child.connected and #created==1, "app restart reuses child")
children.sync(driver,parent,nil)
assert(#deleted==0, "older companion cannot delete children")
children.sync(driver,parent,{})
children.sync(driver,parent,{})
assert(#deleted==1 and deleted[1]==child.id, "uncheck physically deletes once")
children.sync(driver,parent,{app})
assert(#created==1, "recheck while deletion pending must await removal")
children.removed(child);list={}
children.sync(driver,parent,{app})
assert(#created==2, "recheck creates replacement after deletion")
-- Late child creation after an uncheck is deleted on reconciliation.
children.sync(driver,parent,{})
list={child}
children.sync(driver,parent,{})
assert(#deleted==2)
print("PASS app children creation, removal, delayed lifecycle, renaming, reconnect and schema guards")

local many = {}
for i=1,64 do many[i]={key=string.format("%064x",i),name="App "..i,active=false,volume=0,muted=false} end
assert(protocol.apps(many))
many[65]={key=string.rep("f",64),name="Too many",active=false,volume=0,muted=false}
assert(not protocol.apps(many))
local secondParent={id="second-parent",get_child_list=function() return {} end}
local oldCount=#created
children.sync(driver,secondParent,{app,{key=string.rep("b",64),name="Discord",active=true,volume=33,muted=false}})
assert(#created==oldCount+2, "multiple apps and parents have independent creation keys")
