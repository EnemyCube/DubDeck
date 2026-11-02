import argparse
import xml.etree.ElementTree as ET
import hashlib
import json
import shutil
import subprocess
from concurrent.futures import ThreadPoolExecutor
import sys
import zipfile
from pathlib import Path


def require(condition, message):
    if not condition:
        raise RuntimeError(message)

workspace = Path(__file__).resolve().parent.parent
parser = argparse.ArgumentParser(description="Package and verify a fresh local DubDeck release; never upload or overwrite an existing release.")
parser.add_argument("stage", type=Path, help="Directory containing the versioned folder and portable editions")
parser.add_argument("--version", default=ET.parse(workspace / "src/DubDeck.App/DubDeck.App.csproj").findtext(".//Version"))
parser.add_argument("--output", type=Path, default=workspace / "artifacts")
parser.add_argument("--evidence", type=Path)
args = parser.parse_args()
version = args.version
stage = args.stage.resolve()
artifacts = args.output.resolve()
evidence = (args.evidence or artifacts / f"release-{version}-validation").resolve()
if (evidence / "packages.json").exists():
    parser.error("Release evidence already exists; choose a new version or evidence directory.")
artifacts.mkdir(parents=True, exist_ok=True)
evidence.mkdir(parents=True, exist_ok=True)
folder = stage / f"DubDeck-{version}"
portable_dir = stage / f"DubDeck-{version}-Portable"
portable = portable_dir / f"DubDeck-{version}-win.x64-portable.exe"
archive = stage / f"DubDeck-{version}-win-x64.zip"

def digest(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()

require(folder.is_dir() and portable.is_file(), "Missing staged folder or portable edition")
require(list(portable_dir.iterdir()) == [portable], "Portable edition must contain one EXE")
require(not archive.exists(), "Archive already exists")
files = sorted(path for path in folder.rglob("*") if path.is_file())
with zipfile.ZipFile(archive, "x", compression=zipfile.ZIP_DEFLATED, compresslevel=6) as output:
    for path in files:
        output.write(path, path.relative_to(folder).as_posix())
print(f"Created ZIP with {len(files)} files", flush=True)
with zipfile.ZipFile(archive) as package:
    require(package.testzip() is None, "ZIP CRC check failed")
    require(len(package.infolist()) == len(files), "ZIP entry count differs from staged folder")
    for entry in package.infolist():
        with package.open(entry) as stream:
            actual = hashlib.file_digest(stream, "sha256").hexdigest()
        require(actual == digest(folder / entry.filename), f"Archive differs: {entry.filename}")
print("Every ZIP entry passed CRC and SHA-256 comparison", flush=True)

for source in (folder, portable_dir):
    destination = artifacts / source.name
    if source.resolve() == destination.resolve():
        print(f"Using already-staged {source.name}", flush=True)
        continue
    require(not destination.exists(), f"Destination already exists: {destination}")
    copied = subprocess.run(
        ["robocopy", str(source), str(destination), "/E", "/MT:16", "/R:1", "/W:1",
         "/NFL", "/NDL", "/NJH", "/NJS", "/NP"], capture_output=True, text=True)
    if copied.returncode >= 8:
        raise RuntimeError(f"Package copy failed: {copied.returncode}\n{copied.stdout}\n{copied.stderr}")
    source_files = [path for path in source.rglob("*") if path.is_file()]
    def verify_file(path):
        require(digest(path) == digest(destination / path.relative_to(source)), str(path))
    with ThreadPoolExecutor(max_workers=12) as workers:
        list(workers.map(verify_file, source_files))
    print(f"Copied and verified {source.name}", flush=True)
destination_zip = artifacts / archive.name
if archive.resolve() != destination_zip.resolve():
    require(not destination_zip.exists(), f"Destination already exists: {destination_zip}")
    shutil.copy2(archive, destination_zip)
require(digest(archive) == digest(destination_zip), "Copied ZIP differs from staging")
assets = [
    artifacts / portable_dir.name / portable.name,
    destination_zip,
]
manifest = {
    "version": version,
    "zipEntries": len(files),
    "crcVerified": True,
    "allZipEntriesMatchFolder": True,
    "copiedPackagesMatchStaging": True,
    "folderDllSha256": digest(artifacts / folder.name / "DubDeck.dll"),
    "assets": [{"path": str(path), "name": path.name, "bytes": path.stat().st_size, "sha256": digest(path)} for path in assets],
}
checksum_text = "".join(f'{item["sha256"]}  {item["name"]}\n' for item in manifest["assets"])
for target in (evidence / "SHA256SUMS.txt", artifacts / "SHA256SUMS.txt"):
    target.write_text(checksum_text, encoding="utf-8")
(evidence / "packages.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
print(json.dumps(manifest, indent=2), flush=True)
