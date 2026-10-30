# Windows build and portable package

Use Windows 11 x64 and the free .NET 10 SDK. `global.json` selects SDK 10.0.103 or a later 10.0.1xx patch (`latestPatch`). Windows SDK reference packs and NuGet packages are restored from nuget.org; the pinned versions and hashes are in the project lock files. Visual Studio Professional, a paid subscription, a signing certificate and private feeds are not required. Build using PowerShell from the repository root. The normal portable edition requires the free Microsoft Edge WebView2 Evergreen Runtime on the target Windows machine; it is not the separate fixed-runtime edition.

```powershell
dotnet --version
dotnet restore Heimdall.slnx --locked-mode
dotnet build Heimdall.slnx -c Debug --no-restore
dotnet format Heimdall.slnx --no-restore --verify-no-changes
dotnet build Heimdall.slnx -c Release --no-restore
dotnet test Heimdall.slnx -c Release --no-build --filter "Category!=CIUnstable&Category!=RequiresDesktop" --logger trx --results-directory TestResults
```

The commands above include compiler analyzers and treat warnings as errors, as configured by `Directory.Build.props`. TRX files are written under `TestResults`. `RequiresDesktop` and `CIUnstable` are separately reported, not substitutes for a real interactive Windows test. To run the optional categories locally, use the same test command with `--filter "Category=RequiresDesktop"` or `--filter "Category=CIUnstable"`. Some UI tests need an interactive desktop; tests that launch the product are opt-in (`$env:HEIMDALL_UITESTS_LAUNCH_PRODUCT = '1'`) and can affect the current user profile.

To make the same self-contained .NET Standard portable package used by PR CI:

```powershell
dotnet restore src/Heimdall.App/Heimdall.App.csproj -r win-x64 --locked-mode -p:SelfContained=true
dotnet publish src/Heimdall.App/Heimdall.App.csproj -c Release -r win-x64 --self-contained true --no-restore -p:PublishSingleFile=false -p:HeimdallEdition=Standard -o artifacts/portable
Copy-Item LICENSE,THIRD-PARTY-NOTICES.md artifacts/portable/
Compress-Archive -Path artifacts/portable/* -DestinationPath artifacts/Teleclav-portable-win-x64.zip
Get-FileHash artifacts/Teleclav-portable-win-x64.zip -Algorithm SHA256
```

The executable currently retains the upstream name `Heimdall.exe`. Extract the archive before running. The archive is unsigned. A .NET self-contained publish includes the .NET runtime but does **not** bundle the WebView2 runtime. The PR artifact is a build output, not a signed release and not evidence that UI interaction or SSH/SFTP works. The zip and its hash are available in the `Teleclav-portable-win-x64` Actions artifact for 14 days. Successful publishing and hashing do not prove independent byte-for-byte reproducibility.

The existing `Build.ps1` supports additional local builds and installers; its Release path has more requirements, including Inno Setup, WiX and, for the fixed-runtime edition, a separately installed WebView2 runtime. `Release.bat` invokes a publishing workflow and must not be used merely to test a local build. The simple portable commands above do not invoke it or publish anything.

For the additional repository gate, install free Python 3 and run `python -m unittest discover -s tests/infrastructure -v` followed by `python scripts/check_repository_integrity.py`. It checks solution/project references, lock JSON presence, notices and vendored terminal file hashes; it is not a full license-compliance audit. CI also uses the image's Node/npm for the committed milkdown editor tests and dependency audit. WiX 6.0.2 and Inno Setup 6.6.0 are used only by the separate packaging workflow, not the PR portable build.

If restore fails, verify network access to nuget.org and that the required SDK is installed; do not regenerate lock files to hide a locked-mode mismatch. If WebView2 is missing, install the free Evergreen Runtime from Microsoft. If the published program fails to start, inspect its runtime dependencies and Windows logs on the target machine; do not infer success from the zip alone. If tests need desktop access or fail intermittently, inspect their TRX results and category rather than disabling the required test lane.
