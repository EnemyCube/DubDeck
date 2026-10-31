# Local release tools

These tracked helpers retain the reusable logic previously stored in version-specific scripts under artifacts/. They do not upload, push, tag or publish to GitHub.

- Build.ps1 builds the app and runs core tests; -Publish and -Portable create distribution editions.
- Test-Release.ps1 validates an actual app executable, using repository fixtures by default and accepting explicit fixture/report directories.
- Package-Release.py creates a folder ZIP, verifies CRCs and file contents, copies fresh packages if needed, and records manifests/checksums.
- Snapshot-Source.py records the current source tree, including untracked source files and excluding deleted files.
- Verify-Release.py verifies downloaded/stored release assets from their manifest without requiring an expanded folder copy.

Generate fixtures before native/desktop tests:

    ./scripts/Generate-Fixtures.ps1 -IncludeSoak

Validate a built application:

    ./scripts/Test-Release.ps1 -ApplicationPath <path-to-DubDeck.exe> -ReportName folder-workflow -FixtureDirectory <fixtures> -ReportDirectory <reports>
    ./scripts/Test-Release.ps1 -ApplicationPath <path-to-DubDeck.exe> -Mode performance -ReportName folder-performance -FixtureDirectory <fixtures> -ReportDirectory <reports>

Record and package a fresh release after selecting its version:

    python scripts/Snapshot-Source.py --output artifacts/release-<version>-validation/source-files.json
    python scripts/Package-Release.py <staging-root> --version <version>

The staging root must contain DubDeck-<version>/ and DubDeck-<version>-Portable/, with the portable EXE already named DubDeck-<version>-win.x64-portable.exe. The staging root may be artifacts/ when Build.ps1 created both editions there. Existing ZIPs or package manifests are not overwritten; choose a fresh version/evidence directory.

Verify an existing release, even if its expanded folder has been archived:

    python scripts/Verify-Release.py

Use --check-source only when the current checkout is expected to match the historical release snapshot. Later source edits and archived unused assets deliberately make that strict comparison fail.

All native libraries copied for the current app/tests are x64. The source PNG branding preview is generated under artifacts/icon-preview/; the SVG source and ICO remain under src/DubDeck.App/Assets/.
