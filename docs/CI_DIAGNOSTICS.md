# Windows CI diagnostics (F0; investigation in progress)

This is a diagnostic checkpoint, not confirmation of a working CI or a release.

Baseline: integration/drag-drop-acceptance at `8010d81b7f1c48887a17ab33de2df36167e475d5`; master `1959e57c7351d73f1d349b606b19bf6090273db9`. The dependent PR is deliberately a draft.

## Observations

- The GitHub public repository workflow-runs API returned zero runs, and the workflow-list API returned zero registered workflows. This does not establish the cause. The Actions permissions endpoint requires authentication; the investigation has not established whether Actions is disabled, restricted or blocked by another account setting.
- At the integration head, master and PR #8 base, `.github/workflows/ci.yml` has an unfiltered `pull_request` trigger. Topic-branch push is not an event covered by its push filter; opening this diagnostic PR tests PR event processing.
- SDK: `global.json` selects 10.0.103 with latestPatch. There are 18 lock files. NuGet.Config clears sources then selects nuget.org, and sets a Windows-style global package path. Directory.Build.props enables lock files and win-x64 runtime identifiers.
- Existing CI uses Windows `windows-2025-vs2026`, contents: read, pinned action SHAs and checkout with persist-credentials false. The upstream tag-to-SHA mappings were verified; actual runner scheduling has not been verified here.
- Existing mandatory test step excludes CIUnstable and RequiresDesktop; separate informational steps use continue-on-error. At baseline, TRX upload ignored missing files; the PR now makes missing required TRX an error. The isolated OpenSSH test requires explicit server configuration and skips without it.
- The repository integrity check verifies existence of notices and selected asset hashes, not license compatibility. The baseline portable job checked the executable and wrote a hash; this PR adds a ZIP content check, but cannot yet confirm application launch.
- In Ubuntu 24.04, the infrastructure suite passed 7 tests and the repository integrity script passed. SDK 10.0.103 was downloaded; the installer reported ownership errors during extraction, although dotnet --info worked. A locked restore of tests/Heimdall.Core.Tests/Heimdall.Core.Tests.csproj passed with NUGET_PACKAGES set to a Linux path. A later full solution restore succeeded after installing the missing Windows Desktop reference pack as detailed below; Windows/portable restore is still unverified.
- Core Linux run: 4,758 passed, 195 failed, none skipped; TRX generated. [Individual failures and provisional message-based categories](CI_CORE_LINUX_FAILURES.md) do not substitute for root-cause analysis. Three typography guard failures were fixed (audit, acceptance document and French locale); the relevant 21-test suite passed after fixes. The full suite was rerun after the fixes: 4,761 passed, 192 failed, none skipped. A later Linux cross-publish produced the test ZIP documented below.
- Linux locked solution restore, using `-p:EnableWindowsTargeting=true --disable-parallel -m:1`, failed NU1004 for several projects. NuGet says the Heimdall.Core project reference dependencies changed; the Linux cause was subsequently identified as detailed below; Windows behavior remains unconfirmed. Lock files were not regenerated. Separate locked restores of Core and SFTP test projects succeeded.
- SFTP Linux run: 572 passed, 2 failed and 1 skipped; TRX captured. The two failed test names are `CloseEdit_EditorStillRunning_KeepsTheStagedCopy` and `CloseEdit_EditorExited_DeletesTheStagedCopy`.
- A draft PR was opened against the integration branch; querying PR-triggered runs for its initial and subsequent heads returned none. The reason remains unverified.

## Still required

Determine the actual Actions configuration with authorized access and demonstrate a successful Windows run; inspect every failed test with TRX; investigate locked restore on Windows; implement and regression-test necessary fixes; verify ZIP contents and retain checksums and TRX. Do not merge based on this checkpoint.

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

The portable ZIP checker validates archive integrity, required application/runtime, resource and notice paths and the runtime configuration. Four fixture-based Python tests first failed because the gate did not exist, and then passed after implementation; all 11 infrastructure Python tests passed. A real cross-publish initially exposed an incorrect expected filename; after correction the ZIP passed the gate. The gate has not been run on Windows. Required TRX upload was changed to fail on missing files; the Actions behavior of that change is unverified.

## Actions owner check

GitHub documents that Actions must be enabled in the Actions tab of a fork ([events reference](https://docs.github.com/en/actions/reference/workflows-and-actions/events-that-trigger-workflows)). The current public API reports no runs. This is compatible with Actions being disabled, but is not proof: the authenticated repository Actions settings were unavailable to this investigation. The owner can open the repository Actions tab and, if an enable-workflows button is displayed, enable it. Also inspect Settings > Actions > General for repository or account restrictions; GitHub states that a GitHub-controlled disabled state may require support ([settings documentation](https://docs.github.com/en/repositories/managing-your-repositorys-settings-and-features/enabling-features-for-your-repository/managing-github-actions-settings-for-a-repository)). Once enabled, trigger a new PR synchronize event or re-run a workflow if one appears, and inspect its run and logs. Do not interpret an empty list as a runner failure.

GitHub documents `windows-2025-vs2026` as a standard hosted Windows runner for public repositories ([runner documentation](https://docs.github.com/en/actions/reference/runners/github-hosted-runners)); actual scheduling in this repository remains unverified. The three Actions SHAs in CI match the upstream tags named in the comments, verified with `git ls-remote` on 2026-10-31. Matching a tag is not a full supply-chain audit.

Comparison against master `1959e57c7351d73f1d349b606b19bf6090273db9` showed no changes under `src/Heimdall.Core` or `tests/Heimdall.Core.Tests` in the integration branch. This does not prove every failure is pre-existing: tests can depend on files elsewhere, as the corrected documentation and locale failures demonstrate. The failure table records per-test first error lines; assertions without conclusive evidence remain unresolved.

Debug solution build passed on Linux using Windows targeting: 0 warnings and 0 errors. `dotnet format` failed before analysis because this environment denies the named pipe socket (`SocketException` 13), so formatting is unverified. The full Core test rerun on Linux yielded 4,761 passed, 192 failed and 0 skipped. Windows execution remains necessary.

## Cross-published test ZIP (2026-10-31)

Both Debug and Release solution builds passed on Linux with Windows targeting, zero warnings and zero errors. The self-contained win-x64 publish used the following after installing the Windows Desktop reference pack; the normal restore with `-r win-x64` on Linux failed NU1004 because linux-x64 was added to the runtime identifiers. Explicit properties below kept the graph locked to win-x64. No lock files were changed. NuGet audit was disabled only for this local restore; it still needs its separate mandatory scan.

```sh
export NUGET_PACKAGES=/path/to/nuget-cache
dotnet restore src/Heimdall.App/Heimdall.App.csproj --locked-mode -p:RuntimeIdentifiers=win-x64 -p:RuntimeIdentifier=win-x64 -p:SelfContained=true -p:EnableWindowsTargeting=true -p:NuGetAudit=false -m:1
dotnet publish src/Heimdall.App/Heimdall.App.csproj -c Release --no-restore -p:RuntimeIdentifiers=win-x64 -p:RuntimeIdentifier=win-x64 -p:SelfContained=true -p:EnableWindowsTargeting=true -p:PublishSingleFile=false -p:HeimdallEdition=Standard -o artifacts/portable -m:1
```

The Linux-built unsigned test ZIP is [available to the owner](https://chatgpt.com/library?file_id=libfile_82def2bd4628819193dab393b33a32d3&account_id=personal); SHA-256 is `ed62450c4a0866855d72a836f6156497950d8239e9a5687448905d0a87c94bff`. The ZIP checker passed against it after correcting its initial assumption about a nonexistent Desktop deps file. It includes .NET runtime files, but requires WebView2 Evergreen Runtime. It was not launched on Windows, and is not a release or proof of transfer safety.

The portable-checker infrastructure suite now passes 11 tests; the entire Python infrastructure suite passes 18. Crapper on the Python checker with collected subprocess coverage: maximum CRAP 6.2; Dryer at 0.80 found no candidates in the checker and existing integrity script; Mutator killed 8 of 8 generated mutations, using `--no-coverage` (uncovered mutation status was not measured). These tools do not cover the workflow YAML or the C# code in this investigation. Formatting remains blocked by a Linux named-pipe permission error.
