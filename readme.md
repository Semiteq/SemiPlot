# SemiPlot

[![CI](https://github.com/Semiteq/SemiPlot/actions/workflows/ci.yml/badge.svg)](https://github.com/Semiteq/SemiPlot/actions/workflows/ci.yml)
![C#](https://img.shields.io/badge/C%23-.NET-blue)
![.NET](https://img.shields.io/badge/.NET-10%2B-512BD4)

SemiPlot — приложение для просмотра графиков и трендов технологических параметров промышленных установок. Использует SimpleScada как источник данных.

[Документация](./docs/architecture/README.md)

<div align="center">
    <img src=./logo.png width=400 />
</div>

---

## Возможности

**Тренд-вьювер**

- Перья (источники данных): добавление/удаление на лету, включение/выключение, логическая группировка
- Мультиоси: индивидуальная шкала min/max на каждое перо одновременно, плюс общие шкалы для групп
- Курсор/перекрестье с чтением значения **всех** видимых перьев в точке
- Минилегенда: чекбокс / цвет / имя / текущее значение, перья сгруппированы
- Слои агрегации архива: raw / минута / час / сутки — слой выбирается по ширине окна и ширине холста
  вместе (самый грубый слой, шаг которого укладывается в пиксельную колонку)
- Навигация по времени: зум/пан, переход к началу/концу, выбор диапазона
- Реалтайм и история через единый интерфейс `IDataProvider`

---

## Архитектура

- **Платформа:** .NET 10, C# 14, Windows
- **Оболочка:** Avalonia 12.0.5 (Win32 + Skia + HarfBuzz) + ReactiveUI
- **Графика:** ScottPlot.Avalonia 5.1.59 (нативный рендеринг, SkiaSharp)
- **Данные:** абстракция `IDataProvider`; единственная реализация — `PostgresDataProvider`,
  который читает архив PostgreSQL от Simple-Scada 2: каталог переменных, границы архива,
  окно истории и опрос живого края (см. документацию)

Подробно — в `docs/architecture/` (overview, charting, trend-interaction, data-integration).

---

## Требования

| Компонент       | Требование                                                    |
| --------------- | ------------------------------------------------------------- |
| ОС              | Windows 10 или Windows 11 (64-bit)                            |
| Среда сборки    | .NET 10 SDK                                                   |
| Источник данных | Архив PostgreSQL от Simple-Scada 2, подготовленный SemiBase. Без него приложение показывает ошибку старта в главном окне вместо графика |
| Тестовый стенд  | Только для интеграционных тестов: Docker (или иная среда контейнеров). `semibase` приходит слоем образа из `ghcr.io/semiteq/semibase`, ставить его на машину не нужно; без среды контейнеров эти тесты падают, а не пропускаются |

---

## Сборка и запуск

```powershell
# Сборка
dotnet build SemiPlot/SemiPlot.UI/SemiPlot.UI.csproj

# Запуск
dotnet run --project SemiPlot/SemiPlot.UI/SemiPlot.UI.csproj

# Тесты
dotnet test SemiPlot.slnx
```

Демо-стенд с одноразовым архивом и живой записью поднимается одной командой:
`dotnet run --project SemiPlot/SemiPlot.AppHost` — контейнер PostgreSQL, `converge`, писатель и
вьювер стартуют в порядке зависимостей и останавливаются вместе (см. `docs/architecture/bench.md`).

Весь проект `SemiPlot.Tests.Integration` — тесты стенда и сквозные сценарии — поднимает PostgreSQL
в контейнере. Образ стенда собирается из `SemiPlot/bench/Dockerfile`:
он забирает `/semibase` из `ghcr.io/semiteq/semibase:latest` и выполняет `semibase bench` из
`/docker-entrypoint-initdb.d/` — до того, как откроется опубликованный порт. Отдельный бинарник
`semibase` на машине не нужен: `dotnet test SemiPlot.slnx` запускает эти тесты сам.

Прогон ничего после себя не оставляет: собранный образ стенда, контейнер, шаблонная база и все её
клоны удаляются вместе с прогоном. Скачанные из реестра образы (`postgres:17-alpine`,
`ghcr.io/semiteq/semibase:latest`) остаются — намеренно; тег `latest` фикстура подтягивает перед
сборкой образа.

Если контейнерной среды нет, тесты **падают**, а не пропускаются: фикстура пробрасывает ошибку, и
xunit заваливает всю коллекцию с `TestPipelineException`. `dotnet test SemiPlot.slnx` на такой
машине красный, а зелёным остаётся только `SemiPlot.Tests.Unit`.
