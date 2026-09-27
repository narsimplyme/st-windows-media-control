local p=require "protocol"
local m=require "app_components"
local function app(slot,key) return {slot=slot,key=string.rep(key,64),name="App "..key,active=true,volume=30,muted=false} end
assert(p.apps({},2))
assert(p.apps({app(1,"a"),app(3,"b")},2))
assert(not p.apps({app(1,"a"),app(1,"b")},2))
assert(not p.apps({app(1,"a"),app(2,"a")},2))
for _,i in ipairs({0,6,1.5}) do assert(not p.apps({app(i,"a")},2)) end
assert(not p.apps(nil,2) and not p.apps({bad=app(1,"a")},2))
local d={fields={},events={},profile={components={}}}
for i=1,5 do d.profile.components["app"..i]={id="app"..i} end
function d:get_field(k) return self.fields[k] end
function d:set_field(k,v) self.fields[k]=v end
function d:emit_component_event(c,e) self.events[#self.events+1]={id=c.id,event=e} end
local legacy={id="old",parent_assigned_child_key=string.rep("a",64),offline=function() end}
function d:get_child_list() return {legacy,{id="unrelated"}} end
local driver={deleted={}}
function driver:try_delete_device(id) self.deleted[#self.deleted+1]=id end
m.sync(driver,d,{app(1,"a"),app(3,"b")},true)
assert(#d.events==20 and #driver.deleted==1 and driver.deleted[1]=="old")
assert(m.binding(d,"app1").key==string.rep("a",64) and not m.binding(d,"app2"))
m.sync(driver,d,{app(1,"a"),app(3,"b")},true)
assert(#d.events==20 and #driver.deleted==1,"no redundant events or deletion flood")
m.sync(driver,d,{app(3,"b")})
assert(not m.binding(d,"app1") and m.binding(d,"app3"))
assert(d.fields.appSlots.app1.name=="Not configured" and d.fields.appSlots.app1.volume==0 and not d.fields.appSlots.app1.muted)
local inactive=app(3,"b");inactive.active=false;inactive.volume=85;inactive.muted=true
m.sync(driver,d,{inactive})
assert(not m.binding(d,"app3") and d.fields.appSlots.app3.name=="App b" and d.fields.appSlots.app3.volume==0)
m.sync(driver,d,{app(2,"c"),app(3,"b")})
assert(m.binding(d,"app2").key==string.rep("c",64) and m.binding(d,"app3").key==string.rep("b",64))
m.reset(d);assert(not m.binding(d,"app2"))
local before=#d.events;m.sync(driver,d,{app(2,"c"),app(3,"b")})
assert(#d.events==before+20,"restart restores every component")
print("PASS component validation, routing, empty/inactive clearing, stable slots, migration and reconnect")
