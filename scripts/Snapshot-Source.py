import argparse
import hashlib
import json
import subprocess
from concurrent.futures import ThreadPoolExecutor
from datetime import datetime, timezone
from pathlib import Path

root = Path(__file__).resolve().parent.parent
parser = argparse.ArgumentParser(description="Record the current source tree without overwriting historical release evidence.")
parser.add_argument("--output", type=Path)
args = parser.parse_args()
stamp = datetime.now(timezone.utc).strftime("%Y%m%d-%H%M%S-%f")
target = args.output or root / "artifacts" / "source-snapshots" / (stamp + ".json")
if target.exists():
    parser.error("Snapshot already exists; choose a fresh output path.")
raw = subprocess.check_output(
    ["git", "ls-files", "-c", "-o", "--exclude-standard", "-z", "--",
     "src", "tests", "scripts", "docs", "licenses", "README.md", "LICENSE",
     "THIRD-PARTY-NOTICES.md", "global.json", "DubDeck.slnx"], cwd=root)
paths = sorted({p.decode("utf-8") for p in raw.split(b"\0") if p})
paths = [p for p in paths if "_wpftmp" not in p and (root / p).is_file()]
def fingerprint(path):
    with (root / path).open("rb") as stream:
        return path, hashlib.file_digest(stream, "sha256").hexdigest()
with ThreadPoolExecutor(max_workers=12) as workers:
    hashes = dict(workers.map(fingerprint, paths))
result = {
    "baseCommit": subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=root, text=True).strip(),
    "workingTree": subprocess.check_output(["git", "status", "--short"], cwd=root, text=True),
    "files": hashes,
}
target.parent.mkdir(parents=True, exist_ok=True)
target.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
print(f"Recorded {len(hashes)} source fingerprints: {target}")
