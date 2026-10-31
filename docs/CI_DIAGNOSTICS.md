# Windows CI diagnostics (F0; investigation in progress)

This is a diagnostic checkpoint, not confirmation of a working CI or a release.

Baseline: integration/drag-drop-acceptance at `8010d81b7f1c48887a17ab33de2df36167e475d5`; master `1959e57c7351d73f1d349b606b19bf6090273db9`. The dependent PR is deliberately a draft.

## Observations

- The GitHub public repository workflow-runs API returned zero runs, and the workflow-list API returned zero registered workflows. This does not establish the cause. The Actions permissions endpoint requires authentication; the investigation has not established whether Actions is disabled, restricted or blocked by another account setting.
- At the integration head, master and PR #8 base, `.github/workflows/ci.yml` has an unfiltered `pull_request` trigger. Topic-branch push is not an event covered by its push filter; opening this diagnostic PR tests PR event processing.
- SDK: `global.json` selects 10.0.103 with latestPatch. There are 18 lock files. NuGet.Config clears sources then selects nuget.org, and sets a Windows-style global package path. Directory.Build.props enables lock files and win-x64 runtime identifiers.
- Existing CI uses Windows `windows-2025-vs2026`, contents: read, pinned action SHAs and checkout with persist-credentials false. Windows runner availability and the trust of those particular action revisions have not been verified here.
- Existing mandatory test step excludes CIUnstable and RequiresDesktop; separate informational steps use continue-on-error. TRX upload also ignores missing files. The isolated OpenSSH test requires explicit server configuration and skips without it.
- The repository integrity check verifies existence of notices and selected asset hashes, not license compatibility. Portable publishing checks the executable and writes a hash but does not verify all ZIP content or launch the program.
- In Ubuntu 24.04, the infrastructure suite passed 7 tests and the repository integrity script passed. SDK 10.0.103 was downloaded; the installer reported ownership errors during extraction, although dotnet --info worked. A locked restore of tests/Heimdall.Core.Tests/Heimdall.Core.Tests.csproj passed with NUGET_PACKAGES set to a Linux path. This does not prove solution restore or Windows/portable restore succeeds.
- Core Linux run: 4,758 passed, 195 failed, none skipped; TRX generated. [Individual failures and provisional message-based categories](CI_CORE_LINUX_FAILURES.md) do not substitute for root-cause analysis. Three typography guard failures were fixed (audit, acceptance document and French locale); the relevant 21-test suite passed after fixes. The full suite has not yet been rerun after the fixes. No portable artifact was produced in this investigation.\n- Linux locked solution restore, using `-p:EnableWindowsTargeting=true --disable-parallel -m:1`, failed NU1004 for several projects. NuGet says the Heimdall.Core project reference dependencies changed; the underlying difference and Windows behavior remain unconfirmed. Lock files were not regenerated. Separate locked restores of Core and SFTP test projects succeeded.\n- SFTP Linux run: 572 passed, 2 failed and 1 skipped; TRX captured. The two failed test names are `CloseEdit_EditorStillRunning_KeepsTheStagedCopy` and `CloseEdit_EditorExited_DeletesTheStagedCopy`.\n- A draft PR was opened against the integration branch; querying PR-triggered runs for its initial and subsequent heads returned none. The reason remains unverified.

## Still required

Determine the actual Actions configuration with authorized access and demonstrate a successful Windows run; inspect every failed test with TRX; investigate locked restore separately for Windows and self-contained win-x64; implement and regression-test necessary fixes; verify ZIP contents and retain checksums and TRX. Do not merge based on this checkpoint.

## Commands used on Linux

```sh
python -m unittest discover -s tests/infrastructure -v
python scripts/check_repository_integrity.py
NUGET_PACKAGES=/tmp/teleclav-nuget dotnet restore tests/Heimdall.Core.Tests/Heimdall.Core.Tests.csproj --locked-mode
NUGET_PACKAGES=/tmp/teleclav-nuget dotnet test tests/Heimdall.Core.Tests/Heimdall.Core.Tests.csproj --no-restore --logger 'trx;LogFileName=core-linux.trx' --results-directory /tmp/teleclav-results
```

The original Core TRX was generated locally and is not a GitHub Actions artifact. It contains test names, messages and stacks; the checked-in failure list preserves names and first error lines only. More evidence and a Windows run are still required. For Windows build and portable publish commands see [BUILDING.md](BUILDING.md); those commands are documented, not verified in this run.

## Linux locked restore investigation (2026-10-31)

With SDK 10.0.103 on Linux, the first locked restore of the Windows-targeted SSH tests failed NU1004 for the Heimdall.Core project reference. Diagnostic MSBuild output showed that `Microsoft.WindowsDesktop.App.Ref/10.0.3/data/PackageOverrides.txt` was absent, so framework pruning was computed without the Windows Desktop targeting-pack data. The committed Windows-targeted SSH test lock entry does not contain `System.Security.Cryptography.ProtectedData`, whereas the normal Core test entry does. The missing Windows Desktop pack's PackageOverrides.txt lists that package. This establishes a missing targeting-pack explanation for the tested Linux configuration; it does not establish Windows behavior.

A matching Microsoft.WindowsDesktop.App.Ref 10.0.3 package from nuget.org was extracted into the SDK's `packs/Microsoft.WindowsDesktop.App.Ref/10.0.3` directory. With that pack available, the same SSH test project locked restore passed and the full solution locked restore passed on Linux, using `-p:EnableWindowsTargeting=true -p:NuGetAudit=false -m:1` and an explicit Linux `NUGET_PACKAGES` directory. No lock file was regenerated. Audit was disabled only for this local restore diagnosis, not the CI dependency audit. This installation procedure is a diagnostic workaround, not a verified Windows CI fix. SDKs installed by a system package manager should not be modified in place.

## Portable and test artifacts guard

The portable ZIP checker validates archive integrity, required application/runtime, resource and notice paths and the runtime configuration. Four fixture-based Python tests first failed because the gate did not exist, and then passed after implementation; all 11 infrastructure Python tests passed. The gate has not yet been checked against a fresh real publish or on Windows. Required TRX upload was changed to fail on missing files; the Actions behavior of that change is unverified.
