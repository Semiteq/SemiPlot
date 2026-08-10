# Source registry and citation convention

Every non-obvious factual claim in `docs/architecture/*` carries an inline marker resolved here.
The point is that no one re-derives a fact that was already established, and that the strength of
each claim is visible without reading the source.

## Marker grammar

| Class | Marker | Meaning |
| --- | --- | --- |
| Vendor manual | `[MAN:<page>]` | Stated by the Simple-Scada 2 manual. Strongest vendor evidence. |
| Vendor forum | `[FORUM:<topic>]` | Stated by the vendor's own account on the official forum. Authoritative but informal; check the era flag. |
| Our measurement | `[MEAS:<artifact>]` | Measured by us from a named artifact. Strongest evidence for behavior, weakest for generality. |
| Our decision | `[DEC:<key>]` | A choice we made. Not a fact about the world. |
| Unverified | `UNVERIFIED` | Believed but not established. Never assert an unverified claim without this marker. |

Rules:

- A claim resting on two classes carries both markers; that combination is the strongest evidence
  available here (e.g. vendor says X, we measured X).
- Russian quotes are reproduced verbatim, followed by an English rendering.
- Dates belong in this file only. Living architecture docs state the current design, not its history.

## Vendor manual

Online at `https://simple-scada.com/help/manual/<page>.html`. The same manual ships offline with the
product as `Help/ru/Simple-Scada 2 Руководство.chm`; the offline copy tracks the installed build and
is the version cited here.

| Marker | Page | Section used |
| --- | --- | --- |
| `[MAN:tablestruct]` | `tablestruct.html` | Структура таблиц — column definitions of `trends` and `messages`, meaning of `l` and `q` |
| `[MAN:trendsset]` | `trendsset.html` | Настройки проекта: Тренды — «Ограничение архива трендов», «Интервал архивации по-умолчанию» |
| `[MAN:vararchive]` | `vararchive.html` | Архивирование переменных — archiving type, deadband, interval |
| `[MAN:archsysv2]` | `archsysv2.html` | Система архивации v2 — differences from v1, in-memory accumulation limit during a database outage |
| `[MAN:messet]` | `messet.html` | Настройки проекта: Сообщения — «Ограничение архива сообщений» |
| `[MAN:db-access-rights]` | `db-access-rights.html` | Права доступа к БД — the privilege set the SCADA needs, and when `DROP` can be withheld |
| `[MAN:postgresql]` | `postgresql.html` | PostgreSQL setup for the archive |
| `[MAN:whats-new]` | `whats-new.html` | Release notes — archive system v2 history |

## Vendor forum

Official forum at `https://simple-scada.com/forum/index.php?topic=<topic>.0`. All entries below are
replies from the vendor's own account.

| Marker | Topic | Date | Era | Load-bearing content |
| --- | --- | --- | --- | --- |
| `[FORUM:1032]` | 1032 «Интеграл в скаде» | 2020-06-20 | v2 | Point budget per layer: «Основной слой содержит все точки тренда (самый точный и самый медленный). Минутный слой содержит четыре точки за каждую минуту. Часовой четыре точки в час. Суточный - четыре в день (наименее точный и самый быстрый).» — the minute layer holds four points per minute, the hour layer four per hour, the day layer four per day. |
| `[FORUM:1974]` | 1974 «Экспорт данных» | 2025-03-03 | v2 | Selection rule: «часть данных будет пропущена, будет взято максимальное отклонение тренда за соответствующий интервал» — part of the data is skipped and the maximum deviation of the trend over the corresponding interval is taken. |
| `[FORUM:1454]` | 1454 | 2022-06-23 | v2 | Observed density: «если переменная за минуту менялась 60 раз, то в основном слое будет 60 точек, а в минутном 2-4» — 60 changes in a minute give 60 raw points and 2 to 4 minute-layer points. |
| `[FORUM:345]` | 345 «Работа системы архивирования данных» | 2017-03-15 | **v1** | Flush cadence: «данные накапливаются в буферы и сбрасываются на жесткий диск каждые 5 минут (для основного слоя) ... Минутный слой сбрасывается каждую минуту, часовой - час, суточный - день.» Era flag matters: this describes archive system v1. Whether v2 kept these cadences is `UNVERIFIED`. |
| `[FORUM:1847]` | 1847 | 2024-07-31 | v2 | Buffering: values accumulate in memory and are flushed periodically; a rarely changing variable reaches the database rarely; a write can also land within a millisecond. |
| `[FORUM:1388]` | 1388 | — | v2 | Partitioning: «Новая система архивации разбивает БД на разделы для увеличения быстродействия». |
| `[FORUM:1753]` | 1753 | — | — | The product is written in Object Pascal (Delphi). |

Vendor download page `https://simple-scada.com/download/` defines the demo builds: `DEMO-64`
(feature-limited, 64 tags) and `DEMO-TIME` (full functionality, server limited to one hour of
continuous operation; a restart grants another hour).

## Our measurements

| Marker | Artifact | What it establishes |
| --- | --- | --- |
| `[MEAS:dump-20260805]` | `pg_dump` custom-format archive of the SCADA database `Database_test`, taken on a working installation. Two variables (`id` 0 and 1), about two hours of data, twelve project start/stop cycles, values written by a project script. Expanded with `pg_restore -f`. | Verified DDL of `trends` and `messages`; daily partition naming; coarse-layer rows are verbatim copies of raw rows (170 of 170 identical in timestamp, value and quality); layers strictly nested; gap markers replicated into every layer and aligned with `messages` project stop/start rows within 30 ms; `v` never null; change-based archiving writes two rows per change at a 100 ms poll. |
| `[MEAS:install-inspection]` | The installed product tree (`DEMO-TIME` build): language resource files, the offline manual, and the executable images. | No setting anywhere configures or disables layer thinning — the word does not occur in any editor, server or options resource string. `Server.exe` carries no readable strings and no .NET metadata (the product is native Delphi with the server image additionally protected), so the thinning algorithm is not readable from the binaries. |

## Our decisions

| Marker | Decision |
| --- | --- |
| `[DEC:read-only-consumer]` | SemiPlot is a strict read-only consumer of `trends` and `messages`. No writes, no schema changes, no indexes, no triggers on vendor objects. |
| `[DEC:vendor-layers]` | Wide time windows are served by the vendor's own archive layers rather than by summary tables of our own. No rollup tables, no aggregator service, no scheduler, no PostgreSQL extensions. Rationale in `history-read-path-evaluation.md`. |
| `[DEC:common-retention]` | One retention depth applies to all archived data. Raw samples are never dropped earlier than the coarse layers. |
| `[DEC:semiplot-tags]` | The absent variable-number-to-name mapping is supplied by our own `semiplot_tags` table, populated by hand during commissioning. |
| `[DEC:additive-objects]` | Everything we add to the database is prefixed `semiplot_`. |
