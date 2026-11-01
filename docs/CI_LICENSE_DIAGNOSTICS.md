# Dependency licence diagnostics

Inventory captured 2026-11-01 from the exact versions in committed NuGet lock files and the Milkdown npm lock file. This is an evidence and completeness audit, not a legal opinion. A reference by a source project indicates potential runtime reachability, not proof of inclusion in the portable ZIP.

## Findings requiring release review

- Microsoft.Web.WebView2 1.0.2903.40 NuGet metadata points to a BSD-style licence file, which is bundled along with the package NOTICE. The application also requires the separately installed Microsoft Edge WebView2 Evergreen runtime. Full corresponding-source availability for all WebView2 binaries is not verified, so compliance with an exclusively open-source dependency requirement remains unproven. Replacing this dependency requires a separate product decision. The three vendored DLLs match by SHA-256 the x64 loader and managed DLLs in the published NuGet package at that version; the bundled LICENSE.txt and NOTICE.txt were extracted from that exact package.
- `LibGit2Sharp.NativeBinaries` carries libgit2 GPL-2.0-only terms with an express linking exception and additional terms. The actual package licence is included as `THIRD-PARTY-LICENSES/libgit2-LICENSE.txt`; the exact licensing implications and any corresponding-source duties require review.
- noVNC core is MPL-2.0. Its original attribution and licence text are included; source-code and modification obligations must be reviewed for distribution.
- SQLite package metadata points to a public-domain statement. Check how the public-domain dedication applies in relevant jurisdictions.
- The MIT licences require preservation of their respective copyright and permission notices. The Milkdown inventory is conservative and includes all 212 non-dev npm packages in the lock, even if a package does not reach the built bundle. Remark-math 6.0.0 has no licence file in the published npm package; its tagged upstream licence is included. Whether every NuGet transitive or other vendored copyright notice is present is not yet established.
- NuGet packages with legacy licence URLs should be checked against the versioned source. The NuGet manifest record alone does not establish all redistribution obligations.

## NuGet lock inventory

Each metadata link points to the exact package version `.nuspec`. `src` means at least one source-project lock lists the package; `tests` means only test/fixture locks list it. File-based licences were read from their published packages.

| Package | Version | Licence metadata | Scope |
|---|---|---|---|
| [AvalonEdit](https://api.nuget.org/v3-flatcontainer/avalonedit/6.3.1.120/avalonedit.nuspec) | 6.3.1.120 | MIT | src |
| [BouncyCastle.Cryptography](https://api.nuget.org/v3-flatcontainer/bouncycastle.cryptography/2.7.0/bouncycastle.cryptography.nuspec) | 2.7.0 | MIT | src |
| [CommunityToolkit.Mvvm](https://api.nuget.org/v3-flatcontainer/communitytoolkit.mvvm/8.4.0/communitytoolkit.mvvm.nuspec) | 8.4.0 | MIT | src |
| [coverlet.collector](https://api.nuget.org/v3-flatcontainer/coverlet.collector/10.1.0/coverlet.collector.nuspec) | 10.1.0 | MIT | tests |
| [FlaUI.Core](https://api.nuget.org/v3-flatcontainer/flaui.core/5.0.0/flaui.core.nuspec) | 5.0.0 | file: LICENSE.txt | tests |
| [FlaUI.UIA3](https://api.nuget.org/v3-flatcontainer/flaui.uia3/5.0.0/flaui.uia3.nuspec) | 5.0.0 | file: LICENSE.txt | tests |
| [FluentAssertions](https://api.nuget.org/v3-flatcontainer/fluentassertions/6.12.2/fluentassertions.nuspec) | 6.12.2 | Apache-2.0 | tests |
| [FluentFTP](https://api.nuget.org/v3-flatcontainer/fluentftp/54.2.0/fluentftp.nuspec) | 54.2.0 | MIT | src |
| [Humanizer.Core](https://api.nuget.org/v3-flatcontainer/humanizer.core/2.14.1/humanizer.core.nuspec) | 2.14.1 | MIT | src |
| [Interop.UIAutomationClient](https://api.nuget.org/v3-flatcontainer/interop.uiautomationclient/10.19041.0/interop.uiautomationclient.nuspec) | 10.19041.0 | file: LICENSE.txt | tests |
| [Json.More.Net](https://api.nuget.org/v3-flatcontainer/json.more.net/2.0.1.2/json.more.net.nuspec) | 2.0.1.2 | MIT | src |
| [JsonPointer.Net](https://api.nuget.org/v3-flatcontainer/jsonpointer.net/5.0.0/jsonpointer.net.nuspec) | 5.0.0 | MIT | src |
| [JsonSchema.Net](https://api.nuget.org/v3-flatcontainer/jsonschema.net/7.0.4/jsonschema.net.nuspec) | 7.0.4 | MIT | src |
| [Konscious.Security.Cryptography.Argon2](https://api.nuget.org/v3-flatcontainer/konscious.security.cryptography.argon2/1.3.1/konscious.security.cryptography.argon2.nuspec) | 1.3.1 | MIT | src |
| [Konscious.Security.Cryptography.Blake2](https://api.nuget.org/v3-flatcontainer/konscious.security.cryptography.blake2/1.1.1/konscious.security.cryptography.blake2.nuspec) | 1.1.1 | MIT | src |
| [LibGit2Sharp](https://api.nuget.org/v3-flatcontainer/libgit2sharp/0.31.0/libgit2sharp.nuspec) | 0.31.0 | file: App_Readme/LICENSE.md | src |
| [LibGit2Sharp.NativeBinaries](https://api.nuget.org/v3-flatcontainer/libgit2sharp.nativebinaries/2.0.323/libgit2sharp.nativebinaries.nuspec) | 2.0.323 | file: libgit2/libgit2.license.txt | src |
| [Microsoft.CodeCoverage](https://api.nuget.org/v3-flatcontainer/microsoft.codecoverage/17.14.1/microsoft.codecoverage.nuspec) | 17.14.1 | MIT | tests |
| [Microsoft.Data.Sqlite.Core](https://api.nuget.org/v3-flatcontainer/microsoft.data.sqlite.core/10.0.14/microsoft.data.sqlite.core.nuspec) | 10.0.14 | MIT | src |
| [Microsoft.EntityFrameworkCore](https://api.nuget.org/v3-flatcontainer/microsoft.entityframeworkcore/10.0.14/microsoft.entityframeworkcore.nuspec) | 10.0.14 | MIT | src |
| [Microsoft.EntityFrameworkCore.Abstractions](https://api.nuget.org/v3-flatcontainer/microsoft.entityframeworkcore.abstractions/10.0.14/microsoft.entityframeworkcore.abstractions.nuspec) | 10.0.14 | MIT | src |
| [Microsoft.EntityFrameworkCore.Analyzers](https://api.nuget.org/v3-flatcontainer/microsoft.entityframeworkcore.analyzers/10.0.14/microsoft.entityframeworkcore.analyzers.nuspec) | 10.0.14 | MIT | src |
| [Microsoft.EntityFrameworkCore.Relational](https://api.nuget.org/v3-flatcontainer/microsoft.entityframeworkcore.relational/10.0.14/microsoft.entityframeworkcore.relational.nuspec) | 10.0.14 | MIT | src |
| [Microsoft.EntityFrameworkCore.Sqlite](https://api.nuget.org/v3-flatcontainer/microsoft.entityframeworkcore.sqlite/10.0.14/microsoft.entityframeworkcore.sqlite.nuspec) | 10.0.14 | MIT | src |
| [Microsoft.EntityFrameworkCore.Sqlite.Core](https://api.nuget.org/v3-flatcontainer/microsoft.entityframeworkcore.sqlite.core/10.0.14/microsoft.entityframeworkcore.sqlite.core.nuspec) | 10.0.14 | MIT | src |
| [Microsoft.Extensions.Caching.Abstractions](https://api.nuget.org/v3-flatcontainer/microsoft.extensions.caching.abstractions/10.0.14/microsoft.extensions.caching.abstractions.nuspec) | 10.0.14 | MIT | src |
| [Microsoft.Extensions.Caching.Memory](https://api.nuget.org/v3-flatcontainer/microsoft.extensions.caching.memory/10.0.14/microsoft.extensions.caching.memory.nuspec) | 10.0.14 | MIT | src |
| [Microsoft.Extensions.Configuration.Abstractions](https://api.nuget.org/v3-flatcontainer/microsoft.extensions.configuration.abstractions/10.0.14/microsoft.extensions.configuration.abstractions.nuspec) | 10.0.14 | MIT | src |
| [Microsoft.Extensions.DependencyInjection](https://api.nuget.org/v3-flatcontainer/microsoft.extensions.dependencyinjection/10.0.14/microsoft.extensions.dependencyinjection.nuspec) | 10.0.14 | MIT | src |
| [Microsoft.Extensions.DependencyInjection.Abstractions](https://api.nuget.org/v3-flatcontainer/microsoft.extensions.dependencyinjection.abstractions/10.0.14/microsoft.extensions.dependencyinjection.abstractions.nuspec) | 10.0.14 | MIT | src |
| [Microsoft.Extensions.DependencyModel](https://api.nuget.org/v3-flatcontainer/microsoft.extensions.dependencymodel/10.0.14/microsoft.extensions.dependencymodel.nuspec) | 10.0.14 | MIT | src |
| [Microsoft.Extensions.Logging](https://api.nuget.org/v3-flatcontainer/microsoft.extensions.logging/10.0.14/microsoft.extensions.logging.nuspec) | 10.0.14 | MIT | src |
| [Microsoft.Extensions.Logging.Abstractions](https://api.nuget.org/v3-flatcontainer/microsoft.extensions.logging.abstractions/10.0.14/microsoft.extensions.logging.abstractions.nuspec) | 10.0.14 | MIT | src |
| [Microsoft.Extensions.Logging.Abstractions](https://api.nuget.org/v3-flatcontainer/microsoft.extensions.logging.abstractions/8.0.3/microsoft.extensions.logging.abstractions.nuspec) | 8.0.3 | MIT | src |
| [Microsoft.Extensions.Options](https://api.nuget.org/v3-flatcontainer/microsoft.extensions.options/10.0.14/microsoft.extensions.options.nuspec) | 10.0.14 | MIT | src |
| [Microsoft.Extensions.Primitives](https://api.nuget.org/v3-flatcontainer/microsoft.extensions.primitives/10.0.14/microsoft.extensions.primitives.nuspec) | 10.0.14 | MIT | src |
| [Microsoft.Extensions.TimeProvider.Testing](https://api.nuget.org/v3-flatcontainer/microsoft.extensions.timeprovider.testing/10.8.0/microsoft.extensions.timeprovider.testing.nuspec) | 10.8.0 | MIT | tests |
| [Microsoft.NET.Test.Sdk](https://api.nuget.org/v3-flatcontainer/microsoft.net.test.sdk/17.14.1/microsoft.net.test.sdk.nuspec) | 17.14.1 | MIT | tests |
| [Microsoft.TestPlatform.ObjectModel](https://api.nuget.org/v3-flatcontainer/microsoft.testplatform.objectmodel/17.14.1/microsoft.testplatform.objectmodel.nuspec) | 17.14.1 | MIT | tests |
| [Microsoft.TestPlatform.TestHost](https://api.nuget.org/v3-flatcontainer/microsoft.testplatform.testhost/17.14.1/microsoft.testplatform.testhost.nuspec) | 17.14.1 | MIT | tests |
| [Newtonsoft.Json](https://api.nuget.org/v3-flatcontainer/newtonsoft.json/13.0.3/newtonsoft.json.nuspec) | 13.0.3 | MIT | tests |
| [SQLite](https://api.nuget.org/v3-flatcontainer/sqlite/3.53.4/sqlite.nuspec) | 3.53.4 | file: LICENSE.txt | src |
| [SQLitePCLRaw.bundle_e_sqlite3](https://api.nuget.org/v3-flatcontainer/sqlitepclraw.bundle_e_sqlite3/3.0.5/sqlitepclraw.bundle_e_sqlite3.nuspec) | 3.0.5 | Apache-2.0 | src |
| [SQLitePCLRaw.config.e_sqlite3](https://api.nuget.org/v3-flatcontainer/sqlitepclraw.config.e_sqlite3/3.0.5/sqlitepclraw.config.e_sqlite3.nuspec) | 3.0.5 | Apache-2.0 | src |
| [SQLitePCLRaw.core](https://api.nuget.org/v3-flatcontainer/sqlitepclraw.core/3.0.5/sqlitepclraw.core.nuspec) | 3.0.5 | Apache-2.0 | src |
| [SQLitePCLRaw.provider.e_sqlite3](https://api.nuget.org/v3-flatcontainer/sqlitepclraw.provider.e_sqlite3/3.0.5/sqlitepclraw.provider.e_sqlite3.nuspec) | 3.0.5 | Apache-2.0 | src |
| [SSH.NET](https://api.nuget.org/v3-flatcontainer/ssh.net/2026.0.0/ssh.net.nuspec) | 2026.0.0 | MIT | src |
| [System.Configuration.ConfigurationManager](https://api.nuget.org/v3-flatcontainer/system.configuration.configurationmanager/4.4.0/system.configuration.configurationmanager.nuspec) | 4.4.0 | legacy URL: https://github.com/dotnet/corefx/blob/master/LICENSE.TXT | tests |
| [System.Management](https://api.nuget.org/v3-flatcontainer/system.management/10.0.14/system.management.nuspec) | 10.0.14 | MIT | src |
| [System.Security.Cryptography.ProtectedData](https://api.nuget.org/v3-flatcontainer/system.security.cryptography.protecteddata/10.0.14/system.security.cryptography.protecteddata.nuspec) | 10.0.14 | MIT | src |
| [ThemeForge.Theme](https://api.nuget.org/v3-flatcontainer/themeforge.theme/2.1.0/themeforge.theme.nuspec) | 2.1.0 | Apache-2.0 | src |
| [xunit](https://api.nuget.org/v3-flatcontainer/xunit/2.9.3/xunit.nuspec) | 2.9.3 | Apache-2.0 | tests |
| [xunit.abstractions](https://api.nuget.org/v3-flatcontainer/xunit.abstractions/2.0.3/xunit.abstractions.nuspec) | 2.0.3 | legacy URL: https://raw.githubusercontent.com/xunit/xunit/master/license.txt | tests |
| [xunit.analyzers](https://api.nuget.org/v3-flatcontainer/xunit.analyzers/1.18.0/xunit.analyzers.nuspec) | 1.18.0 | Apache-2.0 | tests |
| [xunit.assert](https://api.nuget.org/v3-flatcontainer/xunit.assert/2.9.3/xunit.assert.nuspec) | 2.9.3 | Apache-2.0 | tests |
| [xunit.core](https://api.nuget.org/v3-flatcontainer/xunit.core/2.9.3/xunit.core.nuspec) | 2.9.3 | Apache-2.0 | tests |
| [xunit.extensibility.core](https://api.nuget.org/v3-flatcontainer/xunit.extensibility.core/2.9.3/xunit.extensibility.core.nuspec) | 2.9.3 | Apache-2.0 | tests |
| [xunit.extensibility.execution](https://api.nuget.org/v3-flatcontainer/xunit.extensibility.execution/2.9.3/xunit.extensibility.execution.nuspec) | 2.9.3 | Apache-2.0 | tests |
| [xunit.runner.visualstudio](https://api.nuget.org/v3-flatcontainer/xunit.runner.visualstudio/3.1.4/xunit.runner.visualstudio.nuspec) | 3.1.4 | Apache-2.0 | tests |
| [Xunit.StaFact](https://api.nuget.org/v3-flatcontainer/xunit.stafact/1.1.11/xunit.stafact.nuspec) | 1.1.11 | MS-PL | tests |
| [YamlDotNet](https://api.nuget.org/v3-flatcontainer/yamldotnet/16.3.0/yamldotnet.nuspec) | 16.3.0 | MIT | src |

## npm metadata

Among the 212 non-dev packages in the pinned lock: 208 declare MIT; one each declares `(MPL-2.0 OR Apache-2.0)`, BSD-2-Clause, BSD-3-Clause and ISC. A checked-in inventory combines matching licence texts and is checked against the lock in CI. This checks names and versions, not legal validity or exact bundle composition.
