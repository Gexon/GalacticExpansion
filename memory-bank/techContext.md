# Tech Context — GalacticExpansion (GLEX)

## Технологический стек

### Основные технологии

| Технология | Версия | Назначение |
|------------|--------|------------|
| **C#** | 8.0+ | Основной язык разработки |
| **.NET Framework** | 4.8 | Runtime (требование Empyrion) |
| **Newtonsoft.Json** | 13.0.3+ | Сериализация state.json и конфигурации |
| **NLog** | 5.0+ | Структурированное логирование |
| **xUnit** | 2.6+ | Unit и Integration тестирование |
| **Moq** | 4.20+ | Мокирование зависимостей в тестах |

### Внешние зависимости

**Обязательные (от Empyrion):**
- `ModApi.dll` — интерфейсы ModAPI
- `Mif.dll` — ModInterface Framework

---

## Empyrion ModAPI

### Ключевые концепции

**ModInterface:**
```csharp
public interface ModInterface {
    void Init(IModApi modApi);
    void Shutdown();
    void Game_Start(ModGameAPI dediAPI);
    void Game_Event(CmdId eventId, ushort seqNr, object data);
    void Game_Update();
}
```

**Типы запросов (CmdId):**
- `Request_Entity_Spawn` — создание сущности
- `Request_Entity_Destroy` — удаление сущности
- `Request_Entity_Teleport` — телепортация
- `Request_GlobalStructure_List` — список всех структур
- `Request_Player_Info` — информация об игроке
- `Request_ConsoleCommand` — выполнение консольной команды
- `Request_Structure_Touch` — обновление таймера распада структуры
- `Request_Playfield_Entity_List` — список всех сущностей на playfield

**Типы событий:**
- `Event_Player_ChangedPlayfield` — игрок сменил playfield
- `Event_Statistics` — статистика (убийства, разрушения)
- `Event_ChatMessage` — сообщение в чате
- `Event_GameEvent` — события PDA (волны атак, достижения и т.д.)

### Новые возможности API (после декомпиляции 1.15)

**IEntity - Прямое управление сущностями:**
- `MoveForward(float speed)` — движение вперед
- `Move(Vector3 direction)` — движение в направлении
- `MoveStop()` — остановка движения
- `Position { get; set; }` — прямая установка позиции
- `Rotation { get; set; }` — прямая установка поворота
- `DamageEntity(int damageAmount, int damageType)` — нанесение урона

**IPlayfield - Работа с местностью:**
- `GetTerrainHeightAt(float x, float z)` — точное получение высоты рельефа ⭐
- `SpawnEntity(string entityType, Vector3 pos, Quaternion rot)` — простой спавн
- `LockStructureDevice(...)` — блокировка устройств на структурах

**IPda - Расширенные возможности:**
- `CreateWaveAttack(WaveStartData waveStart)` — программное создание волн атак
- `SpawnEntityAtPosition(Vector3 position, string className, string faction, ...)` — спавн с фракцией
- `CreateTimer(string id, float duration, Action timerAction)` — таймеры

**IStructure - Информация о структуре:**
- `LastVisitedTicks` — время последнего посещения (для decay)
- `SetFaction(FactionGroup group, int entityId)` — смена фракции
- `GetDockedVessels()` — список пристыкованных кораблей

**События и делегаты:**
- `IPlayfield.OnEntityLoaded/OnEntityUnloaded` — подписка на загрузку/выгрузку сущностей
- `IApplication.Update/FixedUpdate` — регулярные обновления для логики
- `IStructure.SignalChanged` — отслеживание изменений сигналов

**Новые типы сущностей:**
- `EntityType.NPCFighter` — НПС-истребитель
- `EntityType.Civilian` — гражданский НПС
- `EntityType.TroopTransport` — транспорт войск

### Ограничения API

1. ~~**Нет прямого управления AI:**~~ **✅ Частично решено** — доступны методы Move/MoveForward для базового управления
2. ~~**Нет API для определения высоты поверхности:**~~ **✅ Решено** — доступен `IPlayfield.GetTerrainHeightAt()`
3. **Нет API для получения ресурсных депозитов:** Используется симуляция
4. **Нет API для урона конкретным блокам:** Только уничтожение структуры целиком или DamageEntity()
5. **Асинхронность:** Все запросы callback-based (требуется адаптер для async/await)

---

## Структура проекта

```
GalacticExpansion/
├── src/
│   ├── GalacticExpansion.Core/          # Основная логика
│   │   ├── Simulation/                  # Core Loop
│   │   ├── Gateway/                     # API Adapter
│   │   ├── State/                       # Persistence
│   │   ├── Spawning/                    # Entity Management
│   │   ├── AIM/                         # AIM Orchestrator
│   │   ├── Placement/                   # Location Finding
│   │   ├── Tracking/                    # Player/Structure Tracking
│   │   ├── Threat/                      # Threat Director
│   │   ├── Transport/                   # Transport Manager
│   │   └── Economy/                     # Resource Simulation
│   ├── GalacticExpansion.Models/        # Data Models
│   │   ├── Colony.cs
│   │   ├── SimulationState.cs
│   │   └── Configuration.cs
│   └── GalacticExpansion/               # Entry Point
│       ├── ModMain.cs
│       └── DependencyInjection.cs
├── tests/
│   ├── GalacticExpansion.Tests.Unit/
│   └── GalacticExpansion.Tests.Integration/
└── docs/
    └── architecture/                     # Документация
```

---

## Конфигурация

**Файл:** `Configuration.json` (JSON с возможностью комментариев)

**Основные секции:**
- `Simulation` — параметры симуляции (tick interval, save interval)
- `Limits` — лимиты (max colonies, max guards и т.д.)
- `Zirax` — настройки фракции (prefabs, stages, production rates)
- `AIM` — AIM команды и rate limits
- `Placement` — параметры размещения структур
- `Threat` — параметры системы угроз
- `Transport` — параметры транспортной системы (скорость, радиус защиты базы)

---

## Персистентность

### State.json

**Расположение:** `Saves/Games/[SaveGameName]/Mods/GalacticExpansion/state.json`

**Формат:** JSON

**Содержимое:**
- Версия схемы
- Список колоний (позиции, стадии, ресурсы)
- Состояние playfield'ов
- Временные данные (cooldowns, timers)

**Миграции:** Автоматическая миграция при изменении версии схемы

### Бэкапы

**Расположение:** `Saves/Games/[SaveGameName]/Mods/GalacticExpansion/backups/`

**Частота:** Раз в 24 часа (настраивается)

**Хранение:** Последние 10 (настраивается)

---

## Логирование

**Библиотека:** NLog

**Уровни:**
- `Trace` — очень детальная отладка
- `Debug` — отладочная информация
- `Information` — нормальная работа (по умолчанию)
- `Warning` — предупреждения
- `Error` — ошибки
- `Critical` — критичные ошибки

**Файлы:** `Logs/GLEX_yyyyMMdd.log`

**Формат:**
```
[timestamp] [level] [module] message
```

---

## Развертывание

**Установка:**
```
[EmpyrionRoot]/Content/Mods/GalacticExpansion/
├── GalacticExpansion.dll
├── Newtonsoft.Json.dll
├── NLog.dll
├── Configuration.json
└── Prefabs/
    └── ...prefab files...
```

**Runtime Path:**
```
[EmpyrionRoot]/Saves/Games/[SaveGameName]/Mods/GalacticExpansion/
├── state.json
├── Logs/
└── backups/
```

---

## Системные требования

**Сервер:**
- OS: Windows Server 2016+ или Linux (Mono/Wine)
- RAM: 16 GB минимум, 32 GB рекомендуется
- CPU: 4+ ядра @ 3.0 GHz+
- Disk: 500 MB для мода + место для данных симуляции

**Game Version:**
- Empyrion: Galactic Survival v1.15 Experimental
- Dedicated Server mode

---

## Development Workflow

### Local Development

1. Клонировать репозиторий
2. Восстановить NuGet пакеты: `dotnet restore`
3. Собрать проект: `dotnet build`
4. Запустить тесты: `dotnet test`

### Testing

**Unit Tests:**
```bash
dotnet test tests/GalacticExpansion.Tests.Unit
```

**Integration Tests:**
```bash
dotnet test tests/GalacticExpansion.Tests.Integration
```

**Code Coverage:**
```bash
dotnet test --collect:"XPlat Code Coverage"
```

### Debugging

1. Собрать в Debug конфигурации
2. Скопировать DLL в `Content/Mods/GalacticExpansion/`
3. Запустить dedicated server
4. Attach debugger к процессу `EmpyrionDedicated.exe`
5. Установить breakpoints

---

## CI/CD (Будущее)

**GitHub Actions / GitLab CI:**
- Автоматическая сборка на каждый commit
- Запуск Unit и Integration тестов
- Code coverage отчеты
- Static code analysis
- Автоматический релиз при теге

---

## Версионирование

**Semantic Versioning (SemVer):**
- `MAJOR.MINOR.PATCH`
- MAJOR — breaking changes
- MINOR — новые features (backward compatible)
- PATCH — bug fixes

**Пример:** `1.0.0` → `1.1.0` (новая feature) → `1.1.1` (bug fix)

**State Schema Versioning:**
- Независимо от версии мода
- Инкремент при изменении структуры state.json
- Автоматические миграции

---

## Известные проблемы

1. **ModAPI timeout:** Иногда запросы не возвращаются → используем timeout + retry
2. **Prefab не найден:** Сервер не находит prefab → логируем WARNING, пропускаем спавн
3. **Структуры исчезают:** Игра удаляет структуры при определенных условиях → периодически проверяем и пересоздаем
4. **Движение транспорта:** Простое прямолинейное движение без обхода препятствий → используем короткие перелеты на высоте

---

## Модули проекта (краткий обзор)

### Критические модули (MVP)
1. **Empyrion Gateway** — адаптер для ModAPI с async/await
2. **State Store** — персистентность с атомарной записью
3. **Core Loop** — главный simulation engine
4. **Entity Spawner** — создание структур и NPC
5. **Threat Director** — управление угрозами и защитой
6. **Unit Economy** — учет и производство боевых единиц

### Расширенные модули (Post-MVP)
7. **Transport Manager** — транспортировка юнитов для иммерсивности
   - Десантные корабли для доставки защитников к аванпостам
   - Умный выбор: транспорт vs прямой спавн из базы/портала
   - Патрульные корабли из ангара базы
   - Логистические рейсы (визуальный эффект)
8. **Hostility Tracker** — система "Most Wanted" для охоты на врагов
9. **Colony Evolution** — постепенная экспансия к родной планете врага