# Tech Context — GalacticExpansion (GLEX)

## Стек

- C# 8.0+, .NET Framework 4.8
- Newtonsoft.Json 13.x
- NLog 5.x
- xUnit 2.6+, Moq 4.20+

## Внешние зависимости (Empyrion)

- `ModApi.dll`, `Mif.dll`, `protobuf-net.dll`

## Ключевые ModAPI возможности

- Спавн/удаление сущностей: `Request_Entity_Spawn`, `Request_Entity_Destroy`
- Список структур: `Request_GlobalStructure_List`
- Защита от decay: `Request_Structure_Touch`
- Высота рельефа: `IPlayfield.GetTerrainHeightAt(x, z)` (через `IPlayfieldWrapper`)

## Структура проекта

- `src/GalacticExpansion.Core` — модули
- `src/GalacticExpansion.Models` — модели данных
- `src/GalacticExpansion` — entry point
- `src/GalacticExpansion.Tests.Unit`, `src/GalacticExpansion.Tests.Integration`

## Локальная разработка

- Сборка: `dotnet build src/GalacticExpansion.sln --configuration Release`
- Тесты: `dotnet test src/GalacticExpansion.sln --configuration Release`

## Логирование

- NLog, путь к конфигу задается явно в `ModMain`.

## Известные нюансы

- В тестах использовать `IPlayfieldWrapper` вместо `IPlayfield`.
- Контракты интерфейсов должны совпадать с реализациями (порядок параметров важен).
