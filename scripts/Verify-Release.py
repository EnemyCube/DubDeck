import argparse
import hashlib
import json
import zipfile
import xml.etree.ElementTree as ET
from pathlib import Path

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
    assert path.is_file(), f"Missing release asset: {path}"
    assert path.stat().st_size == asset["bytes"], f"Size mismatch: {path}"
    with path.open("rb") as stream:
        assert digest(stream) == asset["sha256"], f"SHA-256 mismatch: {path}"
    if path.suffix.lower() == ".zip":
        archive = path
assert archive is not None, "No folder ZIP in release manifest"
with zipfile.ZipFile(archive) as package:
    assert package.testzip() is None, "ZIP CRC verification failed"
    assert len(package.infolist()) == manifest["zipEntries"], "ZIP entry count mismatch"
    with package.open("DubDeck.dll") as stream:
        assert digest(stream) == manifest["folderDllSha256"], "Application DLL differs from recorded release"
    summary = args.manifest.parent / "test-summary.json"
    if summary.is_file() and json.loads(summary.read_text(encoding="utf-8-sig")).get("modernPointerInput"):
        config = json.loads(package.read("DubDeck.runtimeconfig.json"))
        assert config["runtimeOptions"]["configProperties"]["Switch.System.Windows.Input.Stylus.EnablePointerSupport"] is True
checksums = args.manifest.parent / "SHA256SUMS.txt"
if checksums.is_file():
    for asset in manifest["assets"]:
        assert f'{asset["sha256"]}  {asset["name"]}' in checksums.read_text(encoding="utf-8-sig")
if args.check_source:
    source = json.loads((args.manifest.parent / "source-files.json").read_text(encoding="utf-8-sig"))
    for name, expected in source["files"].items():
        with (root / name).open("rb") as stream:
            assert digest(stream) == expected, f"Working-tree source differs: {name}"
print(f'Verified {manifest["version"]}: all downloads, ZIP CRCs, DLL checksum, and recorded configuration.')
