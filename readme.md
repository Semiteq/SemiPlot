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
- Слои агрегации архива: raw / минута / час / сутки
- Навигация по времени: зум/пан, переход к началу/концу, выбор диапазона
- Реалтайм и история через единый интерфейс `IDataProvider`

---

## Архитектура

- **Платформа:** .NET 10, C# 14, Windows
- **Оболочка:** Avalonia 11.3.x (Win32 + Skia) + ReactiveUI
- **Графика:** ScottPlot.Avalonia 5.1.57 (нативный рендеринг, SkiaSharp)
- **Данные:** абстракция `IDataProvider`; сейчас — `RandomStubDataProvider`,
  далее — `SimpleScadaDataProvider` (OPC UA + SQL архив, см. документацию)

Подробно — в `docs/architecture/` (overview, charting, trend-interaction, data-integration).

---

## Требования

| Компонент       | Требование                                                    |
| --------------- | ------------------------------------------------------------- |
| ОС              | Windows 10 или Windows 11 (64-bit)                            |
| Среда сборки    | .NET 10 SDK                                                   |
| Источник данных | На текущем этапе не требуется (заглушка); далее — SimpleScada |

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
