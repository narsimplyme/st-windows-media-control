"""Run the published EXE without any neighboring DLL or JSON files."""
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile

source = Path(sys.argv[1]).resolve()
with tempfile.TemporaryDirectory(prefix="stwmc-single-exe-") as folder:
    target = Path(folder) / "STMediaBridge.Agent.exe"
    shutil.copyfile(source, target)
    assert list(Path(folder).iterdir()) == [target]
    subprocess.run([sys.executable, str(Path(__file__).with_name("http_smoke.py")), str(target), "--https"], check=True)
    print("PASS standalone EXE works without adjacent runtime/configuration files")
