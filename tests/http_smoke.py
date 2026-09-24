"""Run against a locally built agent, using a temporary identity and loopback.
With --exercise-audio, briefly lower volume by one point and restore it.
No playback commands or persistent installation changes are made.
"""
import argparse
import concurrent.futures
import json
import secrets
import socket
import ssl
import subprocess
import tempfile
import time
import urllib.error
import urllib.request
import uuid
from pathlib import Path

parser = argparse.ArgumentParser()
parser.add_argument("agent", type=Path)
parser.add_argument("--exercise-audio", action="store_true")
parser.add_argument("--https", action="store_true")
parser.add_argument("--audio-probe", type=Path, help="Independent Core Audio test utility DLL")
args = parser.parse_args()
def probe(*arguments):
    return json.loads(subprocess.check_output(["dotnet", str(args.audio_probe.resolve()), *arguments],
                      creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0)))
with socket.socket() as s:
    s.bind(("127.0.0.1", 0))
    port = s.getsockname()[1]
token = secrets.token_hex(16)
base = f"{'https' if args.https else 'http'}://127.0.0.1:{port}"
tls_context = None

def request(path, body=None, auth=token):
    req = urllib.request.Request(base + path, data=json.dumps(body).encode() if body is not None else None,
                                 headers={"Authorization": "Bearer " + auth, "Content-Type": "application/json"})
    try:
        with urllib.request.urlopen(req, timeout=26, context=tls_context) as response:
            return response.status, json.load(response)
    except urllib.error.HTTPError as e:
        return e.code, None

with tempfile.TemporaryDirectory(prefix="st-mediabridge-") as directory:
    config = Path(directory) / "agent.json"
    config.write_text(json.dumps(dict(deviceId=str(uuid.uuid4()), token=token, bindAddress="127.0.0.1", port=port)))
    launch = [str(args.agent.resolve())] if args.agent.suffix.lower() == ".exe" else ["dotnet", str(args.agent.resolve())]
    if args.https:
        subprocess.run([*launch, "--enable-https", "--config", str(config)], check=True)
        tls_context = ssl.create_default_context(cafile=str(Path(directory) / "server-tls.pem"))
    process = subprocess.Popen([*launch, "--no-tray", "--config", str(config)],
                               creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0))
    original = None
    native_original = None
    try:
        for _ in range(100):
            if process.poll() is not None:
                raise RuntimeError("Agent exited: " + (Path(directory) / "logs/agent.log").read_text())
            try:
                status, state = request("/v1/state")
                break
            except (OSError, urllib.error.URLError):
                time.sleep(.1)
        else:
            raise RuntimeError("Agent did not start")
        assert status == 200
        if args.https:
            try:
                urllib.request.urlopen(base + "/v1/state", timeout=5)
                raise AssertionError("Untrusted TLS accepted")
            except urllib.error.URLError:
                pass
            try:
                urllib.request.urlopen(base.replace("https:", "http:") + "/v1/state", timeout=5)
                raise AssertionError("Plain HTTP accepted")
            except (OSError, urllib.error.URLError):
                pass
            print("PASS HTTPS verified trust, unknown CA rejection and no plaintext fallback")
        assert request("/v1/pair", {"code": "1234567890"}, auth="")[0] == 401
        for _ in range(4):
            assert request("/v1/pair", {"code": "0000000000"}, auth="")[0] == 401
        assert request("/v1/pair", {"code": "1234567890"}, auth="")[0] == 429
        assert request("/v1/pair", auth="")[0] == 401  # GET cannot exchange credentials
        assert request("/v1/state", auth="1234567890")[0] == 401
        assert request("/v1/state", auth="wrong")[0] == 401
        assert request("/v1/state", auth="0" * 32)[0] == 401
        assert request("/v1/state", auth=token + token)[0] == 401
        wrong_same_length = ("0" if token[0] != "0" else "1") + token[1:]
        assert request("/v1/state", auth=wrong_same_length)[0] == 401
        assert request("/v1/command", {"command": "setVolume", "value": 20}, auth="")[0] == 401
        for value in [-1, 101, 12.5, "50", None, True]:
            assert request("/v1/command", {"command": "setVolume", "value": value})[0] == 400
        assert request("/v1/command", {"command": "setMute", "value": "false"})[0] == 400
        assert request("/v1/command", ["not an object"])[0] == 400
        assert request("/v1/command", {"command": "invalid", "padding": "x" * 2048})[0] == 413
        for command in ["shutdown", "run", "anything"]:
            assert request("/v1/command", {"command": command})[0] == 400
        assert isinstance(state["apps"], list) and state["apps"] == []
        app_command = {"key": "a" * 64, "command": "setVolume", "value": 25}
        assert request("/v1/apps/command", app_command, auth="")[0] == 401
        assert request("/v1/apps/command", app_command)[0] == 409  # unselected / unknown
        for bad in [{**app_command, "key": "bad"}, {**app_command, "value": 101},
                    {**app_command, "value": True}, {**app_command, "command": "run"},
                    {**app_command, "command": "setMute", "value": "false"}]:
            assert request("/v1/apps/command", bad)[0] == 400
        print("PASS app command authentication, selection gate and input validation")
        assert request("/missing")[0] == 404
        assert request("/v1/artwork/cover.jpg", auth="")[0] == 401
        assert request("/v1/artwork/cover.jpg")[0] == 404
        assert "albumArtUrl" not in state["media"]
        assert request("/v1/state", auth="artwork-key")[0] == 401
        assert request("/v1/events?epoch=old&after=99999")[1]["epoch"] == state["epoch"]
        print("PASS HTTP authentication, validation, command allowlist and restart cursor")
        if args.exercise_audio:
            for _ in range(50):
                state = request("/v1/state")[1]
                if state["audio"]["available"]:
                    break
                time.sleep(.1)
            assert state["audio"]["available"], "No audio output endpoint available"
            original = state["audio"]["volume"]
            if args.audio_probe:
                native_original = probe()
            target = original - 1 if original > 0 else 1
            with concurrent.futures.ThreadPoolExecutor() as pool:
                future = pool.submit(request, f'/v1/events?epoch={state["epoch"]}&after={state["revision"]}')
                time.sleep(.2)
                started = time.monotonic()
                assert request("/v1/command", {"command": "setVolume", "value": target})[0] == 200
                updated = future.result()[1]
                # Other native events can arrive first; reconcile until observed.
                while updated["audio"]["volume"] != target and time.monotonic() - started < 5:
                    updated = request(f'/v1/events?epoch={updated["epoch"]}&after={updated["revision"]}')[1]
                assert updated["audio"]["volume"] == target
                assert time.monotonic() - started < 5
            print("PASS native Core Audio volume command and immediate event response")
            if args.audio_probe:
                assert round(probe()["volume"] * 100) == target
                # External Core Audio writes bypass the agent entirely, like a
                # headset/Windows UI change. This proves the notification path.
                external = max(0, target - 1) if target > 0 else 1
                probe("volume", str(external / 100))
                probe("mute", str(not native_original["muted"]))
                started = time.monotonic()
                while time.monotonic() - started < 5:
                    updated = request(f'/v1/events?epoch={updated["epoch"]}&after={updated["revision"]}')[1]
                    if updated["audio"]["volume"] == external and updated["audio"]["muted"] != native_original["muted"]:
                        break
                assert updated["audio"]["volume"] == external
                assert updated["audio"]["muted"] != native_original["muted"]
                assert time.monotonic() - started < 5
                assert request("/v1/command", {"command": "setMute", "value": native_original["muted"]})[0] == 200
                assert probe()["muted"] == native_original["muted"]
                print("PASS external Windows volume/mute callbacks and bidirectional mute")
        print("Audio available:", state["audio"]["available"], "Media session:", state["media"]["available"])
    finally:
        try:
            if native_original is not None:
                probe("volume", str(native_original["volume"]))
                probe("mute", str(native_original["muted"]))
            elif original is not None:
                assert request("/v1/command", {"command": "setVolume", "value": original})[0] == 200
        finally:
            process.terminate()
            process.wait(timeout=10)
