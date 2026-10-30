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

## Дополнение аудита: результаты Windows CI на исходном SHA

Проверен журнал job [build-and-test](https://github.com/VBlackJack/Heimdall/actions/runs/40883016529) в upstream на Windows runner. В этом историческом запуске фактически выполнены locked restore, Debug и Release build и тесты; оба build сообщили `Build succeeded`. В журнале обязательной группы восемь итогов test runner: 90, 4953, 535, 642, 142, 576, 246 и 11948 тестов, каждая группа завершилась `Test Run Successful`. Это число тестовых результатов, а не количество вручную проверенных сценариев и не доказательство работы с Explorer. Наличие skipped отдельных тестов по этим суммарным строкам не исключается.

Журнал отчёта информационных групп указал 49 результатов `CIUnstable` и 215 `RequiresDesktop`, 264 результата без ошибок; RequiresDesktop runner сообщил 215 passed. Проверка NuGet на момент запуска сообщила отсутствие обнаруженных уязвимых зависимостей в 18 проектах. npm-аудит выдал предупреждение об одном advisory с уровнем ниже high. Эти результаты относятся к состоянию базы уязвимостей и инструментов 29.10.2026, а не гарантируют отсутствие уязвимостей сейчас. TRX и coverage — артефакты указанного запуска с ограниченным сроком хранения. Самостоятельный запуск в этой среде не выполнен. В форке Teleclav запусков на момент проверки не было; установку и релизную сборку Teleclav на Windows 11 не проверяли.

## Уточнённые дефекты, ограничения и действия

| ID | Наблюдение и исходник | Последствие и необходимое действие |
|---|---|---|
| D01 | `EmbeddedSftpView.xaml.cs:StartRemoteDrag` помещает в DataObject только `SftpRemoteDragPayload.FormatName`; комментарий `SftpRemoteDragPayload.cs` явно ограничивает его одним процессом. | Explorer не получает поддерживаемый формат: сделать Shell-адаптер и доказать сценарий отдельно от внутренней загрузки. |
| D02 | `EmbeddedSftpViewModel.Transfers.cs:ExecuteDownloadJobAsync`: после предупреждений о `SkippedUnsupportedPaths` и `SkippedUnsafeNames` возвращается `SftpTransferJobStatus.Completed`; upload также устанавливает Completed при некоторых пропусках и выдаёт предупреждение. | Частично переданное дерево может отображаться завершённым. Добавить различимый частичный итог с перечнем путей, обеспечить, чтобы полный успех означал отсутствие пропусков. Наличие предупреждения не устраняет противоречие с R08/R13. |
| D03 | `RemoteUploadTreePlanner.MaxUploadDepth = 256`; download использует тот же предел. Путь на уровне свыше этого значения вызывает IOException. | При достаточно глубоком дереве нет неограниченной рекурсии; документировать границу, проверить до записи/проанализировать целесообразность предела. Windows path limits также важны. |
| D04 | `LocalDownloadPath`, `SftpPathGuard`, планировщик download и перечисление upload исключают опасные имена и неподдерживаемые типы; upload сообщает о local reparse points. | Полный перенос всех объектов не гарантирован. Специальные типы нельзя разыменовывать по умолчанию, пользователю нужны полный перечень и частичный результат. |
| D05 | `SshHandler.cs` комментирует, что изменение размера после старта на ветке Plink pipe недоступно; основной `SshShellSession` использует SSH.NET ShellStream. | R05 требует изменения размера терминала. Проверить все доступные пути и либо устранить ограничение Plink, либо явно не включать этот путь в принимаемый функционал; проверить key/agent режимы. |
| D06 | `AppSettings.UpdateCheckEnabled` по умолчанию true; `UpdateSource.cs` указывает `VBlackJack`, клиент использует GitHub API. | Это необязательно телеметрия, но выпуск форка может обращаться к upstream и проверять не тот продукт. Разделить идентичность и источник обновлений Teleclav, обеспечить явно необязательную сетевую функцию; провести offline и сетевой тест. |
| D07 | `THIRD-PARTY-NOTICES.md` заявляет для тестов версии coverlet 6.0.4/10.0.1, тогда как все восемь тестовых csproj указывают 10.1.0. | Уведомления устарели относительно исходников; независимо перепроверить лицензии и версии всех распространяемых и тестовых зависимостей, включая закрытые WebView2 SDK и runtime. |
| D08 | Нет установленной по прочитанной цепочке проверки файла по SHA-256 в UI (`SftpBrowser`, transfer ViewModel); staged commit — иной механизм. | Не утверждать, что R12 реализовано. Спроектировать явную сквозную проверку и зафиксировать, что сравнение локального и удалённого содержимого не полагается на необязательное расширение SFTP. |

## Состав проектов и границы ответственности

| Компонент | Роль и связь с выпуском | Риск/проверка |
|---|---|---|
| Heimdall.App | WPF composition root; файловые панели, обработчики drag, очередь, SSH view, диалоги, WebView2 и ресурсы; прямые ссылки на все остальные библиотеки. | Большой монолит, синхронные участки UI и сторонние WebView assets; необходимы испытания диспетчера и границ веб-содержимого. |
| Heimdall.Core | Общие модели, конфигурация, host-key контракты, криптография, обновление, логирование. | Глобальное состояние `CredentialProtector`; секреты необходимо проверять на всех путях и при миграции. |
| Heimdall.Ssh | SSH.NET connection factory, предварительная проверка host key, shell и tunnels, Plink fallback. | Разные реализации аутентификации и проверки требуют отдельных тестов, в т.ч. изменённый ключ и resize. |
| Heimdall.Sftp | SFTP через SSH.NET, FTP через FluentFTP, планирование деревьев, файловые операции, конфликты. | Не весь функционал пакета нужен SSH/SFTP, но прямое удаление FTP требует отдельного анализа связей. |
| Heimdall.Terminal | Локальный terminal/ConPTY и Plink pipe вспомогательные реализации, контракт с Core. | Наивное удаление затронет fallback и другие функции App. |
| TwinShell.Core | Доменные типы и контракты подсистемы командной библиотеки, WPF и ссылка на Core. | Прямая ссылка App: независимость от Teleclav SSH не означает возможности удалить без последствий. |
| TwinShell.Persistence | Хранение команд и состояния, EF Core SQLite, ссылки на Core и TwinShell.Core. | Схема/путь данных и миграции при смене идентичности. |
| TwinShell.Infrastructure | Реализации сервисов и синхронизации, JSON/YAML/git, зависит от TwinShell.Core/Persistence. | Внешняя git-синхронизация и дополнительные зависимости должны быть необязательны. |
| Heimdall.Rdp | Существующая библиотека RDP, напрямую требуется App по csproj. | Не входит в минимальный SSH/SFTP-сценарий, но не удалять до анализа сборочных и runtime зависимостей. |

## Детали безопасности

`SshConnectionFactory` сначала разрешает доверие host key, а для настоящего соединения навешивает `HostKeyReceived` с фиксированным `PinnedFingerprintVerifier`; SFTP и `SshShellSession` используют этот механизм. Есть альтернативная ветка Plink со своей проверкой и уборкой временного файла пароля (`SshHandler.cs`, `Heimdall.Ssh/Plink`). Отдельно тестировать TOFU, mismatch, смену ключа между предварительной проверкой и подключением, туннели, SFTP и fallback; существование тестов не является полной проверкой безопасности.

`CredentialProtector.cs`: обычный режим использует Windows DPAPI с HMAC при настроенном ключе и поддерживает legacy-данные; опциональное хранилище с master password использует ключ vault, блокирует запись при закрытом vault вместо понижения защиты. `VaultLifecycleService` указывает Argon2id; реализация blob — AES-GCM. Из этого не следует защита от другого процесса в том же профиле или отсутствие секретов в логах. Проверять все credential providers, lifetime, экспорты, crash-остатки, их ACL и журналы. `EmbeddedSshView` ограничивает навигацию и входящие WebView-сообщения; проверка защищённости границы WebView остаётся задачей отдельного обзора.

## Практический протокол приёмки A–G

На Windows 11 Explorer и Teleclav запустить с одинаковым уровнем прав; подключить отдельную Ubuntu OpenSSH-среду без производственных данных. Подготовить файл с нулевым размером, бинарный файл, Unicode-имя, файл ≥1 ГиБ и дерево `root/empty`, `root/sub/file`, два выделенных объекта и папку назначения. Для A–D отдельно физически выполнить drag файла, набора и дерева; для E–G повторить в папку под указателем и сравнить результат с независимым manifest путей, размеров и SHA-256, в том числе пустые папки. Для D также проверить фон Explorer и строку папки. Указать версию Windows, режим запуска, настройки OpenSSH, hash манифеста, точный статус и снимок лога. Повторить с конфликтами и отказами прав, отменой во время перечисления/передачи, отключением сети, нехваткой места и недопустимым именем. После отказа зафиксировать состояние источника, существующей цели и временных файлов; не утверждать атомарность всего дерева. Проверить специальные типы и глубину отдельно. Ни один такой сценарий в данном аудите фактически не исполнялся.

## Полный перечень прямых NuGet-ссылок в проектах

Значения извлечены из `PackageReference` файлов проектов на исходном SHA; транзитивные версии и хеши — в соответствующих `packages.lock.json`, а не в этой таблице. Такая инвентаризация не доказывает лицензии или отсутствие уязвимостей.

| Проект | Прямые пакеты и версии |
|---|---|
| `Heimdall.App` | AvalonEdit 6.3.1.120, CommunityToolkit.Mvvm 8.4.0, Microsoft.Extensions.DependencyInjection 10.0.14, System.Management 10.0.14, ThemeForge.Theme 2.1.0 |
| `Heimdall.Core` | CommunityToolkit.Mvvm 8.4.0, Konscious.Security.Cryptography.Argon2 1.3.1, Microsoft.Extensions.DependencyInjection.Abstractions 10.0.14, System.Security.Cryptography.ProtectedData 10.0.14 |
| `Heimdall.Rdp` | нет прямых NuGet-ссылок |
| `Heimdall.Sftp` | FluentFTP 54.2.0, SSH.NET 2026.0.0 |
| `Heimdall.Ssh` | SSH.NET 2026.0.0 |
| `Heimdall.Terminal` | нет прямых NuGet-ссылок |
| `TwinShell.Core` | нет прямых NuGet-ссылок |
| `TwinShell.Infrastructure` | JsonSchema.Net 7.0.4, LibGit2Sharp 0.31.0, YamlDotNet 16.3.0 |
| `TwinShell.Persistence` | Microsoft.EntityFrameworkCore.Sqlite 10.0.14, Microsoft.Extensions.Caching.Memory 10.0.14, SQLitePCLRaw.bundle_e_sqlite3 3.0.5 |
| `Heimdall.App.Tests` | coverlet.collector 10.1.0, FluentAssertions 6.12.2, Microsoft.NET.Test.Sdk 17.14.1, Microsoft.Extensions.TimeProvider.Testing 10.8.0, xunit 2.9.3, xunit.runner.visualstudio 3.1.4 |
| `Heimdall.App.UiTests` | coverlet.collector 10.1.0, Microsoft.NET.Test.Sdk 17.14.1, xunit 2.9.3, xunit.runner.visualstudio 3.1.4, Xunit.StaFact 1.1.11, FlaUI.Core 5.0.0, FlaUI.UIA3 5.0.0 |
| `Heimdall.Core.Tests` | coverlet.collector 10.1.0, Microsoft.NET.Test.Sdk 17.14.1, xunit 2.9.3, xunit.runner.visualstudio 3.1.4 |
| `Heimdall.Rdp.Tests` | coverlet.collector 10.1.0, Microsoft.NET.Test.Sdk 17.14.1, xunit 2.9.3, xunit.runner.visualstudio 3.1.4 |
| `Heimdall.Sftp.Tests` | coverlet.collector 10.1.0, Microsoft.NET.Test.Sdk 17.14.1, xunit 2.9.3, xunit.runner.visualstudio 3.1.4 |
| `Heimdall.Ssh.Tests` | coverlet.collector 10.1.0, Microsoft.Extensions.TimeProvider.Testing 10.8.0, Microsoft.NET.Test.Sdk 17.14.1, xunit 2.9.3, xunit.runner.visualstudio 3.1.4 |
| `Heimdall.Terminal.Tests` | coverlet.collector 10.1.0, FluentAssertions 6.12.2, Microsoft.NET.Test.Sdk 17.14.1, xunit 2.9.3, xunit.runner.visualstudio 3.1.4 |
| `TwinShell.Infrastructure.Tests` | coverlet.collector 10.1.0, FluentAssertions 6.12.2, Microsoft.EntityFrameworkCore.Sqlite 10.0.14, Microsoft.Extensions.Caching.Memory 10.0.14, Microsoft.Extensions.Logging.Abstractions 10.0.14, Microsoft.NET.Test.Sdk 17.14.1, xunit 2.9.3, xunit.runner.visualstudio 3.1.4 |
