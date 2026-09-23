"""Local Lua syntax/protocol checks. pip install lupa==2.8 PyYAML==6.0.2"""
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / ".tools/python"))
from lupa.lua54 import LuaRuntime
import yaml

lua = LuaRuntime(unpack_returned_tuples=True, register_builtins=False, register_eval=False)
lua.execute("package.path = ... .. '/?.lua;' .. package.path", (ROOT / "edge-driver/src").as_posix())
for path in (ROOT / "edge-driver/src").glob("*.lua"):
    lua.execute("assert(load(...))", path.read_text(encoding="utf-8"))
    print("PASS Lua syntax", path.name)
lua.execute((ROOT / "tests/edge_protocol_test.lua").read_text(encoding="utf-8"))
lua.globals().TEST_ROOT = ROOT.as_posix()
lua.execute((ROOT / "tests/edge_driver_test.lua").read_text(encoding="utf-8"))
lua.execute((ROOT / "tests/edge_tls_client_test.lua").read_text(encoding="utf-8"))
lua.execute('package.loaded["sha2"] = nil; load = nil')
sha = lua.eval('require("sha2").sha256')
import hashlib
for data in ["", "abc", "a" * 1000, "YWJj"]:
    assert sha(data) == hashlib.sha256(data.encode()).hexdigest()
print("PASS SHA256 known vectors with Edge-style disabled dynamic loading")
for path in (ROOT / "edge-driver").rglob("*.yml"):
    yaml.safe_load(path.read_text(encoding="utf-8"))
profile = yaml.safe_load((ROOT / "edge-driver/profiles/media-bridge.yml").read_text(encoding="utf-8"))
assert len(profile["components"]) == 1
assert {c["id"] for c in profile["components"][0]["capabilities"]} == {
    "audioVolume", "audioMute", "mediaPlayback", "mediaTrackControl", "audioTrackData", "refresh"}
print("PASS YAML and one-device media-only profile")
preferences = {p["name"]: p for p in profile["preferences"]}
defaults = {}
for name, preference in preferences.items():
    assert 3 <= len(preference["title"]) <= 36, f"Preference title length: {name}"
    definition = preference["definition"]
    assert "default" in definition, f"Missing explicit default: {name}"
    value = definition["default"]
    defaults[name] = value
    if preference["preferenceType"] == "string":
        assert isinstance(value, str), f"Quote string default: {name}"
        assert 0 <= definition["minLength"] <= definition["maxLength"] <= 36
        assert definition["minLength"] <= len(value) <= definition["maxLength"]
        assert definition["stringType"] in {"text", "password", "paragraph"}
    elif preference["preferenceType"] == "enumeration":
        assert value in definition["options"]
    elif preference["preferenceType"] == "boolean":
        assert isinstance(value, bool)
    else:
        assert preference["preferenceType"] == "integer"
        assert isinstance(value, int) and definition["minimum"] <= value <= definition["maximum"]
validate = lua.eval('require("protocol").configuration')
assert validate(lua.table_from(defaults)) is None
paired = dict(pcAddress="192.168.1.20", pcPort=8765,
              deviceId="12345678-1234-1234-1234-123456789abc", token="a" * 32)
assert validate(lua.table_from(paired)) is not None
for field in ["pcAddress"]:
    incomplete = {**paired, field: defaults[field]}
    assert validate(lua.table_from(incomplete)) is None, f"Sentinel accepted: {field}"
assert "token" not in preferences and "deviceId" not in preferences
assert preferences["pairingCode"]["preferenceType"] == "integer"
assert preferences["pairingCode"]["definition"] == {"minimum": 0, "maximum": 9999999999, "default": 0}
main = profile["components"][0]
assert main["id"] == "main" and main["categories"] == [{"name": "SmartMonitor"}]
playback = next(c for c in main["capabilities"] if c["id"] == "mediaPlayback")
assert playback["config"]["values"] == [
    {"key": "playbackStatus.value", "enabledValues": ["playing", "paused", "stopped"]},
    {"key": "{{enumCommands}}", "enabledValues": ["play", "pause"]}]
assert all(c["version"] == 1 for c in main["capabilities"])
print("PASS profile preference bounds/defaults, unpaired sentinels and presentation configuration")

speaker = yaml.safe_load((ROOT / "edge-driver/profiles/media-bridge-speaker.yml").read_text(encoding="utf-8"))
assert speaker["preferences"] == profile["preferences"]
assert speaker["components"][0]["capabilities"] == main["capabilities"]
assert speaker["components"][0]["categories"] == [{"name": "Speaker"}]
print("PASS icon profiles retain identical controls and pairing preferences")
