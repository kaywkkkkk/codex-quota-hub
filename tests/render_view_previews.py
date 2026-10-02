"""Render the real WPF views with fictional account data."""

import os
import shutil
import subprocess
import tempfile
from pathlib import Path
from PIL import Image


ROOT = Path(__file__).resolve().parents[1]
PROJECT = ROOT / "tests" / "CodexQuotaHub.Desktop.Visual" / "CodexQuotaHub.Desktop.Visual.csproj"
VISUAL_DLL = (
    ROOT / "tests" / "CodexQuotaHub.Desktop.Visual" / "bin" / "Release"
    / "net10.0-windows" / "CodexQuotaHub.Desktop.Visual.dll"
)
OUT = ROOT / "docs" / "previews"


def find_dotnet():
    configured = os.environ.get("CODEX_QUOTA_HUB_DOTNET")
    if configured and Path(configured).is_file():
        return configured
    on_path = shutil.which("dotnet")
    if on_path:
        return on_path
    dotnet_root = os.environ.get("DOTNET_ROOT")
    if dotnet_root:
        executable = Path(dotnet_root) / "dotnet.exe"
        if executable.is_file():
            return str(executable)
    raise SystemExit("找不到 .NET SDK；請安裝 .NET 10 或設定 CODEX_QUOTA_HUB_DOTNET。")


def main():
    dotnet = find_dotnet()
    build = [dotnet, "build", str(PROJECT), "-c", "Release"]
    offline = subprocess.run([*build, "--no-restore"], cwd=ROOT, check=False)
    if offline.returncode != 0:
        subprocess.run(build, cwd=ROOT, check=True)
    OUT.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix="codex-quota-hub-previews-") as temporary:
        database = Path(temporary) / "sample.db"
        for mode in ("detailed", "grid", "compact", "credits"):
            output = OUT / ("reset-credit-details.png" if mode == "credits" else f"{mode}.png")
            rendered = Path(temporary) / f"{mode}.png"
            subprocess.run(
                [dotnet, str(VISUAL_DLL), str(database), str(rendered), mode],
                cwd=ROOT,
                check=True,
            )
            with Image.open(rendered) as preview:
                if preview.getbbox() is None:
                    raise RuntimeError("WPF 畫面擷取為空白；未覆蓋既有預覽圖。")
            shutil.copyfile(rendered, output)
            print(output)


if __name__ == "__main__":
    main()
