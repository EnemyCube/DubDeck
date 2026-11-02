"""Regression checks for release verification using disposable synthetic artifacts."""
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import unittest
import zipfile


class ReleaseToolsTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix="dubdeck-release-tools-")
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        (self.root / "scripts").mkdir()
        (self.root / "src/DubDeck.App").mkdir(parents=True)
        (self.root / "artifacts/evidence").mkdir(parents=True)
        scripts = Path(__file__).resolve().parent
        for name in ("Verify-Release.py", "Package-Release.py"):
            shutil.copyfile(scripts / name, self.root / "scripts" / name)
        (self.root / "src/DubDeck.App/DubDeck.App.csproj").write_text(
            "<Project><PropertyGroup><Version>test</Version></PropertyGroup></Project>", encoding="utf-8")
        (self.root / "src/Main.cs").write_text("original source", encoding="utf-8")
        subprocess.run(["git", "init", "-q", str(self.root)], check=True, capture_output=True)
        files = {path.relative_to(self.root).as_posix(): self.digest(path.read_bytes())
                 for parent in ("src", "scripts") for path in (self.root / parent).rglob("*") if path.is_file()}
        (self.root / "artifacts/evidence/source-files.json").write_text(json.dumps({"files": files}), encoding="utf-8")
        archive = self.root / "artifacts/test.zip"
        with zipfile.ZipFile(archive, "x") as package:
            package.writestr("DubDeck.dll", b"dll")
        self.manifest = self.root / "artifacts/evidence/packages.json"
        self.manifest.write_text(json.dumps({"version": "test", "zipEntries": 1,
            "folderDllSha256": self.digest(b"dll"), "assets": [{"path": str(archive), "name": archive.name,
            "bytes": archive.stat().st_size, "sha256": self.digest(archive.read_bytes())}]}), encoding="utf-8")

    @staticmethod
    def digest(data):
        return hashlib.sha256(data).hexdigest()

    def verify(self, expected_success, error=None):
        for optimize in (False, True):
            with self.subTest(optimize=optimize):
                result = subprocess.run([sys.executable, *(["-O"] if optimize else []),
                    str(self.root / "scripts/Verify-Release.py"), "--manifest", str(self.manifest), "--check-source"],
                    capture_output=True, text=True)
                self.assertEqual(result.returncode == 0, expected_success, result.stdout + result.stderr)
                if error:
                    self.assertIn(error, result.stderr)

    def test_matching_source_is_accepted(self):
        self.verify(True)

    def test_added_source_is_rejected(self):
        (self.root / "src/Added.cs").write_text("new source", encoding="utf-8")
        self.verify(False, "source file set differs")

    def test_deleted_source_is_rejected(self):
        (self.root / "src/Main.cs").unlink()
        self.verify(False, "source file set differs")

    def test_modified_source_is_rejected(self):
        (self.root / "src/Main.cs").write_text("changed source", encoding="utf-8")
        self.verify(False, "Working-tree source differs")

    def test_incorrect_checksum_is_rejected(self):
        data = json.loads(self.manifest.read_text(encoding="utf-8"))
        data["assets"][0]["sha256"] = "0" * 64
        self.manifest.write_text(json.dumps(data), encoding="utf-8")
        self.verify(False, "SHA-256 mismatch")

    def test_packaging_preserves_existing_destination(self):
        for optimize in (False, True):
            with self.subTest(optimize=optimize):
                stage = self.root / ("optimized-stage" if optimize else "stage")
                folder = stage / "DubDeck-test"
                portable = stage / "DubDeck-test-Portable"
                folder.mkdir(parents=True)
                portable.mkdir()
                (folder / "DubDeck.dll").write_bytes(b"new staged DLL")
                (portable / "DubDeck-test-win.x64-portable.exe").write_bytes(b"portable")
                output = self.root / ("optimized-output" if optimize else "output")
                destination = output / "DubDeck-test"
                destination.mkdir(parents=True)
                existing = destination / "DubDeck.dll"
                existing.write_bytes(b"existing release DLL")
                result = subprocess.run([sys.executable, *(["-O"] if optimize else []),
                    str(self.root / "scripts/Package-Release.py"), str(stage), "--output", str(output)],
                    capture_output=True, text=True)
                self.assertNotEqual(result.returncode, 0, result.stdout + result.stderr)
                self.assertIn("Destination already exists", result.stderr)
                self.assertEqual(existing.read_bytes(), b"existing release DLL")


if __name__ == "__main__":
    unittest.main()
