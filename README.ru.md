# VEGAS Pro 2027 Build 31 → OpenAI Codex

Форк [United Taps VEGAS Pro MCP](https://github.com/unitedtaps2/vegas-pro-mcp)
для **Boris FX VEGAS Pro 2027.0.0 Build 31**, версия `2.1.1-vegas2027.31`.
Оригинальная лицензия MIT и авторство United Taps сохранены в `LICENSE` и `NOTICE.md`.

Сохранены 9 инструментов и 100 действий: проект, таймлайн, клипы и группы,
звук и шины, эффекты OFX, Pan/Crop, Track Motion, ключевые кадры, медиа и рендер.
Форк работает только с `vegas2027.exe` и API версии **2027.0.0.31**.
Установленные рядом VEGAS 23, 2026, другие сборки 2027 не выбираются.

## Установка

Нужны Windows x64, лицензированный VEGAS указанной сборки, Node.js 22+,
Windows PowerShell 5.1 и .NET Framework 4.8. Visual Studio не требуется.

Сохраните работу и закройте все экземпляры VEGAS.
В PowerShell из папки проекта выполните:

```powershell
npm ci --ignore-scripts
npm run install:extension
npm run setup:codex
npm run vegas
npm run probe
```

Если скачан установочный архив с `node_modules` и `dist`, шаг `npm ci` можно
пропустить. Для установки уже собранной DLL вместо `install:extension`:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\install-extension.ps1 -SkipBuild
```

Установщик ищет нужный VEGAS в стандартных папках и реестре Windows и проверяет
фактические версии EXE и DLL API. Необычный путь можно указать явно:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\install-extension.ps1 -VegasInstallPath 'D:\Apps\Vegas Pro 2027'
```

DLL устанавливается в `Documents\Vegas Application Extensions` текущего
пользователя, с учётом перенаправленной папки Documents. Уже установленная DLL
сохраняется в `backups` перед заменой. Токен создаётся локально с ограниченными
правами; при повторной установке сохраняется. SHA-256 манифеста проверяется
перед установкой готовой DLL. Проприетарный API VEGAS в архив не включается.

В меню **Tools → Extensions → United Taps VEGAS MCP Status** доступен статус.
После регистрации сервера переподключите MCP или перезапустите Codex.

## Ручное подключение к Codex

Если Codex CLI отсутствует в PATH, получите готовый фрагмент настроек:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\setup-codex.ps1 -PrintOnly
```

Вставьте результат в `%USERPROFILE%\.codex\config.toml` (либо файл в
`CODEX_HOME`, если он задан). Типичный пример:

```toml
[mcp_servers.vegas-pro-2027]
command = 'C:\Program Files\nodejs\node.exe'
args = ['F:\YouTube\Codex\vegas-pro-mcp\src\server.mjs']
startup_timeout_sec = 30
tool_timeout_sec = 210
```

Это MCP STDIO: Codex запускает Node, расширение в VEGAS принимает команды
по локальному именованному каналу. Codex и VEGAS должны работать под одной
учётной записью Windows. Если расширение установлено с нестандартным `-InstallRoot`,
добавьте `VEGAS_MCP_INSTALL_ROOT` в таблицу окружения сервера. Токен в конфигурацию
не вставляйте. [Официальная документация Codex](https://developers.openai.com/codex/mcp/).

## Первые команды

1. Откройте и сохраните отдельный тестовый `.veg`.
2. «Вызови vegas_capabilities и vegas_status. Проверь Build 31».
3. «Изучи проект и таймлайн. Ничего не меняй».
4. «Поставь курсор на 1 секунду».
5. «Уменьши громкость выбранного аудиоклипа на 6 dB».

Перед изменением Codex должен прочитать актуальный таймлайн и передать точный
`project_path`, `event_id`, `expected_start_frames`, `expected_length_frames`.
При нескольких экземплярах Build 31 требуется `vegas_pid` из статуса.
Для перемещения используются поля `new_start_frames` / `new_track_index`,
для обрезки — `new_length_frames` / `new_end_frames`, для курсора — `cursor_frames`.
Дополнительные примеры: [USAGE.md](docs/USAGE.md), полный список:
[контракт действий](docs/VEGAS-MCP-ACTION-CONTRACT.md).

## Защита и диагностика

Перед изменениями контента сохраняется отдельная `.veg`-копия в
`Documents\VEGAS MCP Backups`, затем создаётся именованный шаг Undo.
Копии содержат проект, без исходных медиа. Исходные файлы не изменяются.
Опасные режимы требуют предусмотренных контрактом явных `allow_*` флагов.
Снимки и рендеры отказываются перезаписывать существующие файлы.
После таймаута сначала проверьте состояние VEGAS: уже начатая операция может
завершиться позже. Автоматического повтора отправленного изменения нет.

```powershell
npm run doctor
npm run probe
powershell.exe -NoProfile -File .\scripts\doctor.ps1 -RequireLive
```

Журнал расширения: `Vegas Application Extensions\UnitedTaps.VegasMcp.runtime\vegas-extension.log`.
Он ограничен примерно 2 MiB и может содержать приватные пути. Диагностика Node
идёт в stderr; `VEGAS_MCP_LOG_LEVEL=info` включает время успешных действий.
Запросы, параметры и токены в диагностике Node не печатаются.

FFmpeg нужен только для LUFS-анализа. Если он отсутствует, остальные функции
работают; для анализа задайте `ffmpeg_path` или PATH процесса VEGAS.

## Проверки и ограничения

```powershell
npm run check
npm run smoke
npm run test:native
npm run test:installer
npm run test:live:prepare
npm run test:live
```

Последние две команды запускайте при закрытом VEGAS. Они создают собственный
проект и тестовые медиа в `work\live-*`; тест отказывается менять другой проект.
Результаты сохраняются в `acceptance-report.json` рядом с тестовым `.veg`.

Что проверено реально, автоматическими моделями и что остаётся неподтверждённым:
[отчёт аудита](docs/VEGAS2027-AUDIT.md).
Наличие 100 маршрутов не означает, что каждый плагин, формат и шаблон рендера
проверен. Экспериментальный `ripple_delete` сохраняет ограничения оригинала
по группам, автоматизации и ключевым кадрам.
