import argparse
import hashlib
import json
import subprocess
import zipfile
import xml.etree.ElementTree as ET
from pathlib import Path


def require(condition, message):
    if not condition:
        raise RuntimeError(message)

root = Path(__file__).resolve().parent.parent
version = ET.parse(root / "src/DubDeck.App/DubDeck.App.csproj").findtext(".//Version")
parser = argparse.ArgumentParser(description="Verify local release downloads; source equality is optional for historical releases.")
parser.add_argument("--manifest", type=Path, default=root / "artifacts" / f"release-{version}-validation" / "packages.json")
parser.add_argument("--check-source", action="store_true", help="Require this working tree to match the recorded release source snapshot")
args = parser.parse_args()
manifest = json.loads(args.manifest.read_text(encoding="utf-8-sig"))
def digest(stream):
    return hashlib.file_digest(stream, "sha256").hexdigest()
archive = None
for asset in manifest["assets"]:
    path = Path(asset["path"])
    if not path.is_file():
        options = [root / "artifacts" / asset["name"],
                   root / "artifacts" / f'DubDeck-{manifest["version"]}-Portable' / asset["name"]]
        path = next((candidate for candidate in options if candidate.is_file()), path)
    require(path.is_file(), f"Missing release asset: {path}")
    require(path.stat().st_size == asset["bytes"], f"Size mismatch: {path}")
    with path.open("rb") as stream:
        require(digest(stream) == asset["sha256"], f"SHA-256 mismatch: {path}")
    if path.suffix.lower() == ".zip":
        archive = path
require(archive is not None, "No folder ZIP in release manifest")
with zipfile.ZipFile(archive) as package:
    require(package.testzip() is None, "ZIP CRC verification failed")
    require(len(package.infolist()) == manifest["zipEntries"], "ZIP entry count mismatch")
    with package.open("DubDeck.dll") as stream:
        require(digest(stream) == manifest["folderDllSha256"], "Application DLL differs from recorded release")
    summary = args.manifest.parent / "test-summary.json"
    if summary.is_file() and json.loads(summary.read_text(encoding="utf-8-sig")).get("modernPointerInput"):
        config = json.loads(package.read("DubDeck.runtimeconfig.json"))
        require(config["runtimeOptions"]["configProperties"]["Switch.System.Windows.Input.Stylus.EnablePointerSupport"] is True,
                "Modern pointer input is disabled in the packaged runtime configuration")
checksums = args.manifest.parent / "SHA256SUMS.txt"
if checksums.is_file():
    for asset in manifest["assets"]:
        require(f'{asset["sha256"]}  {asset["name"]}' in checksums.read_text(encoding="utf-8-sig"),
                f'Missing checksum entry for {asset["name"]}')
if args.check_source:
    source = json.loads((args.manifest.parent / "source-files.json").read_text(encoding="utf-8-sig"))
    raw = subprocess.check_output(
        ["git", "ls-files", "-c", "-o", "--exclude-standard", "-z", "--",
         "src", "tests", "scripts", "docs", "licenses", "README.md", "LICENSE",
         "THIRD-PARTY-NOTICES.md", "global.json", "DubDeck.slnx"], cwd=root)
    names = {path.decode("utf-8") for path in raw.split(b"\0") if path}
    current_files = {name for name in names if "_wpftmp" not in name and (root / name).is_file()}
    recorded_files = set(source["files"])
    require(current_files == recorded_files, "Working-tree source file set differs: "
        f"added {sorted(current_files - recorded_files)}, removed {sorted(recorded_files - current_files)}")
    for name, expected in source["files"].items():
        with (root / name).open("rb") as stream:
            require(digest(stream) == expected, f"Working-tree source differs: {name}")
print(f'Verified {manifest["version"]}: all downloads, ZIP CRCs, DLL checksum, and recorded configuration.')
