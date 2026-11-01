<!--
  Copyright 2026 Julien Bombled

  Licensed under the Apache License, Version 2.0 (the "License");
  you may not use this file except in compliance with the License.
  You may obtain a copy of the License at

      http://www.apache.org/licenses/LICENSE-2.0
-->

# Third-party notices

*Also available in French: [THIRD-PARTY-NOTICES.fr.md](THIRD-PARTY-NOTICES.fr.md) and Spanish: [THIRD-PARTY-NOTICES.es.md](THIRD-PARTY-NOTICES.es.md).*

Heimdall is licensed under the Apache License 2.0 (see [LICENSE](LICENSE)). It
redistributes the third-party components listed below, each under its own licence.
This file covers what ships to a user. Components used only to build or test
Heimdall are listed separately at the end and are not redistributed.

Every licence recorded here was read from the component itself, not inferred:
NuGet licences come from the `<license>` element of each package's `.nuspec`, and
the vendored components from their upstream licence text. See
[How this file is verified](#how-this-file-is-verified).

## Vendored components

These are committed to the repository and shipped inside the installer.

| Component | Version | Publisher | Licence | Upstream |
|---|---|---|---|---|
| PuTTY `plink.exe` | Release 0.83 | Simon Tatham | MIT | https://www.chiark.greenend.org.uk/~sgtatham/putty/ |
| gsudo `gsudo.exe` | 2.5.1 | Gerardo Grignoli | MIT | https://github.com/gerardog/gsudo |
| draw.io embed | 31.4.5 | JGraph Ltd | Apache-2.0 | https://github.com/jgraph/drawio |
| xterm.js and add-ons | 5.5.0 / 0.10.0 / 0.18.0 | xterm.js contributors | MIT | https://github.com/xtermjs/xterm.js |
| noVNC core | 1.5.0 | noVNC authors | MPL-2.0 | https://github.com/novnc/noVNC |
| pako | 1.0.3 | pako authors | MIT / Zlib | https://github.com/nodeca/pako |
| Milkdown | 7.23.0 | Milkdown contributors | MIT | https://github.com/Milkdown/milkdown |
| Microsoft Edge WebView2 SDK | 1.0.2903.40 | Microsoft Corporation | BSD-style package licence; see bundled licence and notice | https://developer.microsoft.com/microsoft-edge/webview2/ |

PuTTY is copyright 1997-2026 Simon Tatham. Only `plink.exe` is redistributed, not
the full PuTTY suite.

The draw.io tree under `src/Heimdall.App/Assets/drawio/` is a pruned subset of the
upstream distribution; what was removed and why is recorded in
[VENDORED.md](src/Heimdall.App/Assets/drawio/VENDORED.md).

### WebView2 SDK and runtime

The Microsoft.Web.WebView2 1.0.2903.40 NuGet package contains a BSD-style
`LICENSE.txt` and a separate third-party `NOTICE.txt`. Both are included in
`THIRD-PARTY-LICENSES/`. The application also requires Microsoft Edge WebView2
Evergreen Runtime, a separately installed Microsoft runtime. Availability of
complete corresponding source for all WebView2 binaries has not been verified;
therefore an exclusively open-source dependency claim remains unproven.

## Included third-party licence texts

The portable ZIP includes `THIRD-PARTY-LICENSES/` with licence texts for xterm.js,
noVNC, libgit2 and SQLite, and a conservative inventory of non-dev npm package
licence texts for the Milkdown build. This inventory includes packages that may
not be shipped in the bundle. The libgit2 native package includes GPL-2.0 terms with
a linking exception and additional notices: read the bundled text in full. The
noVNC core source included in this build is available in the Teleclav repository:
https://github.com/landquart/Teleclav/tree/8010d81b7f1c48887a17ab33de2df36167e475d5/src/Heimdall.App/Assets/vnc/core
and upstream at https://github.com/novnc/noVNC/tree/v1.5.0/core. The pako licence
is included with its vendored files. These additions do not establish
that all transitive dependency notices are complete; verify redistribution terms
and copyright notices for the actual shipped bundle before release.

## NuGet packages redistributed with the application

Direct references from the shipped projects under `src/`.

| Package | Version | Licence |
|---|---|---|
| AvalonEdit | 6.3.1.120 | MIT |
| CommunityToolkit.Mvvm | 8.4.0 | MIT |
| FluentFTP | 54.2.0 | MIT |
| JsonSchema.Net | 7.0.4 | MIT |
| Konscious.Security.Cryptography.Argon2 | 1.3.1 | MIT |
| LibGit2Sharp | 0.31.0 | MIT |
| LibGit2Sharp.NativeBinaries (libgit2) | 2.0.323 | GPL-2.0-only with linking exception; see bundled licence |
| SQLite | 3.53.4 | Public domain declaration; see bundled licence |
| Microsoft.EntityFrameworkCore.Sqlite | 10.0.14 | MIT |
| Microsoft.Extensions.Caching.Memory | 10.0.14 | MIT |
| Microsoft.Extensions.DependencyInjection | 10.0.14 | MIT |
| Microsoft.Extensions.DependencyInjection.Abstractions | 10.0.14 | MIT |
| Microsoft.Extensions.Logging.Abstractions | 10.0.14 | MIT |
| SQLitePCLRaw.bundle_e_sqlite3 | 3.0.5 | Apache-2.0 |
| SSH.NET | 2026.0.0 | MIT |
| System.Management | 10.0.14 | MIT |
| System.Security.Cryptography.ProtectedData | 10.0.14 | MIT |
| ThemeForge.Theme | 2.1.0 | Apache-2.0 |
| YamlDotNet | 16.3.0 | MIT |

`ThemeForge.Theme` is published by the author of Heimdall and is itself Apache-2.0.

Counting transitive dependencies, 44 distinct packages reach the shipped
application. The table above lists the direct references; the complete closure,
including versions resolved at build time, is produced by:

```bash
dotnet list src/Heimdall.App/Heimdall.App.csproj package --include-transitive
```

## Not redistributed: build and test only

These are referenced by projects under `tests/` and never reach a user's machine.
They are listed for completeness, not as a redistribution notice.

| Package | Version | Licence |
|---|---|---|
| FlaUI.Core | 5.0.0 | MIT |
| FlaUI.UIA3 | 5.0.0 | MIT |
| FluentAssertions | 6.12.2 | Apache-2.0 |
| Microsoft.Extensions.TimeProvider.Testing | 10.8.0 | MIT |
| Microsoft.NET.Test.Sdk | 17.14.1 | MIT |
| Xunit.StaFact | 1.1.11 | MS-PL |
| coverlet.collector | 10.1.0 | MIT |
| xunit | 2.9.3 | Apache-2.0 |
| xunit.runner.visualstudio | 3.1.4 | Apache-2.0 |

## How this file is verified

Licences are read from the components, never assumed from reputation:

- NuGet packages: the `<license type="expression">` element of the `.nuspec` in
  the local package cache. `LibGit2Sharp` predates that element and declares
  `<license type="file">`, so its licence was read from the `LICENSE.md` shipped
  inside the package.
- `plink.exe` and `gsudo.exe`: the publisher's own licence page, cross-checked
  against the version metadata embedded in the binary.
- draw.io: the version and licence recorded in `VENDORED.md`, alongside upstream.

Re-check this file whenever a dependency is added, removed, or upgraded across a
major version, and whenever a binary under `Assets/` or `lib/` is refreshed.

Last verified: 2026-08-31, against commit `9c4241d6`.
