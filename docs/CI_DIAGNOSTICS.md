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
- Full Core test execution and TRX analysis are in progress. Earlier reports of 195 Linux failures are historical, not a classification. No portable artifact was produced in this investigation.

## Still required

Determine the actual Actions configuration with authorized access and demonstrate a successful Windows run; inspect every failed test with TRX; investigate locked restore separately for Windows and self-contained win-x64; implement and regression-test necessary fixes; verify ZIP contents and retain checksums and TRX. Do not merge based on this checkpoint.
