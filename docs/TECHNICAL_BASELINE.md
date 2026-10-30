# Teleclav: технический baseline

Срез исходников `1959e57c7351d73f1d349b606b19bf6090273db9`; 30.10.2026. GitHub подтвердил, что в момент проверки `master` Teleclav и `master` VBlackJack/Heimdall указывают на этот SHA. Результаты ниже — статический аудит, если явно не указан запуск. **Частично** означает наличие кода для части требования, а не пройденную приёмку; **не проверено** — доказательств для вывода недостаточно.

## Сборка и контроль

- `global.json`: SDK 10.0.103, rollForward latestPatch; `Directory.Build.props`: warnings as errors, style, lock files, `win-x64`. App: `net10.0-windows10.0.19041.0`, WPF/WinForms; Core, Sftp и Ssh: net10.0; Terminal и TwinShell: net10.0-windows.
- Сборка: `dotnet restore Heimdall.slnx --locked-mode`, затем `dotnet build Heimdall.slnx --no-restore -c Release`, `dotnet test Heimdall.slnx --no-build -c Release --filter "Category!=CIUnstable&Category!=RequiresDesktop"` на Windows. `.github/workflows/ci.yml` дополнительно проверяет формат, выполняет npm и PowerShell suites, анализ зависимостей и собирает coverage/TRX. `CIUnstable` и `RequiresDesktop` — информационные, не блокируют успех CI; чистый CI не доказывает успешность этих сценариев.
- Основные прямые версии: SSH.NET 2026.0.0; FluentFTP 54.2.0; CommunityToolkit.Mvvm 8.4.0; Argon2 1.3.1; ProtectedData, EF Core Sqlite и Microsoft.Extensions 10.0.14; ThemeForge.Theme 2.1.0; AvalonEdit 6.3.1.120; WebView2 SDK 1.0.2903.40 (THIRD-PARTY-NOTICES); WiX 6.0.2, Inno Setup 6.6.0. Точные прямые и транзитивные зависимости зафиксированы в `*.csproj`, `packages.lock.json`, JS lock-файлах и `.config/dotnet-tools.json`; SDK latestPatch может выбрать более новый patch.
- Тестовые проекты решения: Heimdall.App.Tests, App.UiTests, Core.Tests, Rdp.Tests, Sftp.Tests, Ssh.Tests, Terminal.Tests, TwinShell.Infrastructure.Tests; fixture `tests/fixtures/Heimdall.UpdateStub`. См. `Heimdall.slnx`, `tests/`.
- На исходном SHA upstream: [CI success](https://github.com/VBlackJack/Heimdall/actions/runs/40883016529), [Packaging success](https://github.com/VBlackJack/Heimdall/actions/runs/40883016528). В Teleclav запусков Actions на момент проверки не обнаружено. Packaging использует stand-in WebView2 runtime (`.github/workflows/packaging.yml`), поэтому не подтверждает пригодность настоящего bundled runtime. Вывод о CI касается только его заданных ворот и не заменяет проверку non-blocking lanes и Windows Explorer.
- Локально: Linux x86-64; `dotnet --info` завершился 127 (`dotnet: command not found`); Windows-десктоп и сервер Ubuntu с тестовой учётной записью недоступны. Сборка и тесты **BLOCKED / NOT RUN**; известные ошибки реальной сборки этого SHA не установлены. TDD не применяется к документальным изменениям. Crapper, Dryer, Mutator отсутствуют и **NOT RUN**; функциональный код не менялся. Запущенные upstream проверки не являются локальными.

## Карта зависимостей

`Heimdall.App` (WPF, панель, очередь, диалоги) ссылается на Core, Ssh, Sftp, Terminal, Rdp и все три TwinShell; удалить компоненты без проверки прямых и косвенных связей нельзя. `Heimdall.Ssh` → Core + SSH.NET; `Heimdall.Sftp` → Core + Ssh + SSH.NET/FluentFTP; Terminal → Core; TwinShell.Core → Core, Persistence → Core и TwinShell.Core, Infrastructure → TwinShell.Core/Persistence. Полные ссылки — в соответствующих csproj.

Терминал: `App/Services/Handlers/SshHandler.cs`, `App/Views/EmbeddedSshView.xaml.cs`, `App/Assets/terminal.html` и vendored xterm, SSH и WebView2. Терминальный пакет содержит также локальные/другие терминальные механизмы; не следует считать весь пакет конкретной SSH-реализацией. SFTP: WPF-обработчики → `EmbeddedSftpViewModel.Transfers.cs` и очередь → `SftpBrowser.cs`/SSH.NET, планировщики и проверки путей. Local panel → `LocalFileBrowserView.xaml.cs`/ViewModel. Explorer → WPF `DataFormats.FileDrop`; обратное направление сейчас выставляет только внутренний формат `Heimdall.SftpRemoteEntries`. `SftpDropTarget.cs` только подсвечивает строку, не реализует обмен с Shell. `FileListDragSource.cs` определяет начало drag и сохраняет множественное выделение; формат задаёт вызывающий view.

## Drag & Drop: сценарии

Статусы ниже фиксируют статический путь в коде, не результат реального перетаскивания. Сокращения: EV = `src/Heimdall.App/Views/EmbeddedSftpView.xaml.cs`, LV = `src/Heimdall.App/Views/LocalFileBrowserView.xaml.cs`, T = `src/Heimdall.App/ViewModels/EmbeddedSftpViewModel.Transfers.cs`, VM = `src/Heimdall.App/ViewModels/EmbeddedSftpViewModel.cs`.

| Сценарий | Статус | Основание и недостающая проверка |
|---|---|---|
| A Explorer → SFTP | частично | EV принимает FileDrop, определяет target и вызывает `UploadEntriesAsync`; T/VM планируют передачу файлов и папок. Реальный Explorer и сервер не проверены. |
| B локальная панель → SFTP | частично | LV `StartLocalDrag` выставляет FileDrop с путями выделенных строк; далее тот же EV/T. Нужен end-to-end, включая выбор и ошибки. |
| C SFTP → локальная панель | частично | EV выставляет `SftpRemoteDragPayload`; `LocalBrowserDrop.Classify` распознаёт, LV вызывает `DownloadFilesAsync`, T использует `RemoteDownloadTreePlanner`. Фактическое исполнение не проверено. |
| D SFTP → Explorer | отсутствует в текущем коде | EV `StartRemoteDrag` выставляет только внутренний формат; `SftpRemoteDragPayload.cs` прямо говорит, что другой процесс не увидит понятный формат и что виртуальные файлы — отдельная работа. Обработчик рекурсивной загрузки не предоставляет Shell IDataObject/FileContents. Нужны реализация и тест Explorer. |
| E папки | частично | Исходящие/входящие планировщики обходят деревья, но D отсутствует; remote planner допускает только File/Directory, ограничивает глубину, пропускает неподдерживаемые типы и небезопасные имена; upload сообщает о пропущенных reparse points. Полное копирование всех типов не доказано. |
| F несколько объектов | частично | `FileListDragSource.cs` хранит выделение, EV/LV создают списки, очередь принимает набор. Фактическое множественное выделение в WPF/Explorer и все результаты не проверены. |
| G вложенная папка | частично | EV `ResolveDropTargetDirectory` и LV hit-test выбирают папку под указателем, иначе текущий каталог. В Explorer исходящий drop отсутствует; пользовательский сценарий и видимость цели не проверены. |

Тесты: `tests/Heimdall.App.Tests/LocalBrowserHiddenAndDropTests.cs` проверяет классификацию FileDrop/собственного источника; `EmbeddedSftpViewModelHelpersTests.cs` — выбор цели; `EmbeddedSftpTransferQueueTests.cs` — очередь/ограничения move; `tests/Heimdall.Sftp.Tests/RemoteDownloadTreePlannerTests.cs` — планирование дерева, `RemoteUploadTreePlannerTests.cs` — upload planning. Это не подтверждение реального drag между процессами и не свидетельство фактического запуска. Нужен Windows UI/integration suite с настоящими Explorer и OpenSSH, плюс отрицательные случаи и контроль байтов.

## Матрица требований

Обязательные критерии — [TELECLAV_TECH_REQUIREMENTS.md](TELECLAV_TECH_REQUIREMENTS.md). В каждой строке наличие тестов — наличие исходников, не PASS.

| ID | Статус | Доказательства, тесты, необходимое изменение и риск |
|---|---|---|
| R01 | не проверено | App csproj и CI windows-2025-vs2026. Тесты решения существуют; запуск релиза на Windows 11 не выполнен. Проверить чистую машину и зависимости. |
| R02 | частично | `LICENSE` Apache-2.0; `THIRD-PARTY-NOTICES.md` отдельно признаёт закрытые WebView2 redistributables. Проверить все транзитивные и vendored лицензии, attribution, права и доступность исходников. Риск неточного заявления о полностью открытой цепочке. |
| R03 | частично | .NET/WiX/Inno в CI; воспроизводимость без платных инструментов не доказана. Документировать версии и проверить чистую машину. |
| R04 | частично | Ssh/Sftp + SSH.NET, тесты `Heimdall.Ssh.Tests`, `Heimdall.Sftp.Tests`; реальный Ubuntu не проверен. Проверить аутентификацию и сетевые отказы. |
| R05 | частично | `EmbeddedSshView.xaml.cs`, `SshHandler.cs`, xterm/WebView2; `Heimdall.Terminal.Tests`, `EmbeddedSshViewTerminalSizeReplayTests.cs`. Реальный интерактивный SSH на Windows не проверен; зависимость от WebView2. |
| R06 | частично | EV/LV и соответствующие ViewModel, `LocalFileBrowserViewTests.cs`; взаимодействие на целевой ОС не проверено. |
| R07 | частично | A–C имеют маршруты, D отсутствует; тесты очереди/планирования есть. Реализовать Shell bridge, проверить байты. |
| R08 | частично | Планировщики и их тесты есть, D отсутствует, ограничения типов и глубины. Реализовать мост и испытать полное дерево по R08. |
| R09 | частично | Вход через FileDrop есть, исходящий формат внутренний. Нет доказательства Explorer end-to-end. Реализовать внешний Shell формат и протестировать оба направления. |
| R10 | частично | `RemoteDownloadTreePlanner.cs`, `SftpPathGuard`, `LocalDownloadPath`, collision planner и тесты. Проверить перенос имён и структуры, явно сообщать пропуски. |
| R11 | частично | `Heimdall.Ssh/HostKeyTrustService.cs`, `Heimdall.Core/Ssh/`, `Heimdall.Core/Security/`, тесты HostKeyTrustService/HostKeyStore/CredentialProtector. Проверить все реальные маршруты и хранение; наличие защит не доказывает безопасность в целом. |
| R12 | не проверено | Очередь и staging есть (`SftpBrowser.cs`, `SftpAtomicUpload.cs`), но пользовательская сквозная проверка SHA-256 передачи не установлена. Добавить или документировать способ, интеграционно сверять содержимое. Атомарная запись не равна проверке хеша. |
| R13 | частично | T содержит CancellationToken, конфликтный план, итог и предупреждения; `SftpModePreservationTests.cs`, `EmbeddedSftpTransferQueueTests.cs`. Проверить повреждение цели, остатки, отмену и разрыв на живом сервере и Shell bridge. |
| R14 | не проверено | Очередь и async существуют, но измерений UI latency/памяти/скорости нет; тестов, подтверждающих R14, не установлено. Провести измерения по спецификации. |
| R15 | частично | CI locked restore/Release и упаковка; lock-файлы и `.github/workflows/packaging.yml`. У upstream CI success; локально NOT RUN; повторяемость на двух чистых Windows не доказана, packaging runtime stand-in. |
| R16 | не проверено | `config/settings.default.json`, update-компоненты и WebView2 требуют проверки реального поведения. Тестов, доказывающих отсутствие исходящей телеметрии и обязательной сети для всех функций, не установлено. Проверить код, offline и сетевой трассой. |

## Архитектурный выбор и карта доработок

1. Сохранить SSH.NET, существующие SSH, SFTP, UI и очередь. Сначала воспроизвести A–C/E–G и зафиксировать отказы; не переписывать протокол без подтверждённой необходимости.
2. Для D изучить Windows Shell virtual files (`FileGroupDescriptor`/`FileContents`, COM `IDataObject`, потоковая выдача и асинхронные контракты) как узкий адаптер поверх существующего планирования и SFTP. Проверить поддержку папок и пустых каталогов, время жизни сеанса, выбор цели, отмену, конфликт и ошибки Explorer в опытном прототипе до выбора архитектуры. Существующий WPF managed DataObject сам по себе не доказывает доступность нужной COM-семантики.
3. Альтернатива — staging во временной папке с последующей передачей `FileDrop`. Проще совместимость, но задержка до drag, дополнительный диск, секретные остатки и очистка, скачивание даже при отмене; её разрешать только с ясным жизненным циклом и тестами. Подмена внешнего D внутренним C неприемлема. Полная замена движка — существенно больший объём и риск, оснований пока нет. Окончательный выбор зависит от измеренного прототипа.
4. Уточнить обработку непредставимых имён Windows, ссылок, глубины, перезаписи и частичного результата. Нужны безопасная очистка, явный статус по каждому элементу и проверка SHA-256 без предположений о поддержке сервером SFTP hash extension. Не заявлять полный успех при пропусках.
5. Проверить host-key по всем путям, защиту секретов, журналы, диагностику безопасности WebView2/JS и зависимости с версиями, SBOM и лицензии. Компоненты `TwinShell.*`, Rdp, FTP и встроенные assets имеют прямые связи или поставляются проектом; их минимизацию делать отдельно после построения проверенного графа зависимостей, не удалять вслепую.
6. Выделить самостоятельные идентификаторы приложения, пути профиля, имя бинарного файла, installer, update endpoints и метаданные выпуска Teleclav; соблюдать лицензию и авторство, не заменять чужой updater до проверки миграции и безопасности. Без редизайна.
7. Включить бесплатный воспроизводимый Windows CI и записывать полный результат desktop и интеграционных тестов. Утвердить критерии отказов, стабильности и сборок из требований; затем проходить R01–R16 на релизном кандидате.

## Блокирующие риски и ограничения

Внешний исходящий D отсутствует; реальная Windows-сборка, реальные SFTP и UI/Explorer не проверены; состояние лицензирования полного набора компонентов и отсутствие обязательных сетевых обращений не подтверждены; контроль целостности передачи по хешу не установлен. Без этих данных заявлять готовность 1.0 нельзя. Оценка охватывает указанные цепочки и ключевые конфигурации, а не формальную проверку каждой строки и каждой транзитивной зависимости репозитория.
