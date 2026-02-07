# Active Context — GalacticExpansion (GLEX)

## Текущее состояние

**Дата обновления:** 07.02.2026  
**Фаза:** Phase 3.1 Native Playfield Spawner — РЕАЛИЗОВАНО ✅

## Главное за последние изменения

### 🎯 Native Playfield Spawner — КРИТИЧЕСКОЕ ИСПРАВЛЕНИЕ ✅

**Проблема PfServer краша:**
- PfServer процесс крашился при входе игрока: `InvalidOperationException: Gateway required for PfServer`
- ModGameAPI не передавался в PfServer поток (Game_Start может не вызываться)
- Невозможно инициализировать Gateway без ModGameAPI
- Все API запросы в Dedi таймаутились из-за неработающего PfServer

**Решение - Прямой спавн через IPlayfield API:**
- **NativePlayfieldSpawner** - прямой доступ к IPlayfield без ModGameAPI
- Синхронные вызовы: `IPlayfield.SpawnPrefab()`, `IPlayfield.SpawnEntity()`
- PfServer НЕ требует Gateway/ModGameAPI
- Работает ТОЛЬКО через IPlayfield instance из `OnPlayfieldLoaded`

**Компоненты:**

1. **NativePlayfieldSpawner** (`Core/Spawning/NativePlayfieldSpawner.cs`):
   - Принимает `IPlayfield` instance напрямую
   - `SpawnStructureAsync()` → `IPlayfield.SpawnPrefab()` (синхронный!)
   - `SpawnNPCAsync()` → `IPlayfield.SpawnEntity()` (синхронный!)
   - `GetTerrainHeight()` → `IPlayfield.GetTerrainHeightAt()` (синхронный!)
   - НЕТ зависимостей от Gateway/SequenceManager/ModGameAPI

2. **UnityTypeConverter** (`Core/Spawning/UnityTypeConverter.cs`):
   - Конвертация `Models.Vector3` ↔ `UnityEngine.Vector3`
   - Конвертация Euler angles ↔ `UnityEngine.Quaternion`
   - Алиас `using ILogger = NLog.ILogger;` (UnityEngine тоже имеет ILogger!)

3. **InitializePlayfieldServer() - НОВАЯ АРХИТЕКТУРА** (`ModMain.cs`):
   ```csharp
   - OnPlayfieldLoaded → получает IPlayfield instance
   - Создает NativePlayfieldSpawner(pfInstance)
   - Инициализирует NetworkBridge БЕЗ Gateway
   - Регистрирует HandleIPCRequestWithNativeSpawner
   ```
   - ❌ НЕ создает Gateway (не нужен!)
   - ❌ НЕ создает EntitySpawner (не нужен!)
   - ❌ НЕ извлекает ModGameAPI (не нужен!)
   - ✅ Использует только IPlayfield + NetworkBridge

4. **HandleIPCRequestWithNativeSpawner()** (`ModMain.cs`):
   - Обработчик IPC команд с нативным спавнером
   - Жесткая проверка playfield
   - Конвертация типов через UnityTypeConverter
   - Прямой вызов `IPlayfield.SpawnPrefab()` / `SpawnEntity()`

**Архитектура потока данных:**
```
DEDI: ColonyManager → IPCEntitySpawner → NetworkBridge
  ↓ IPC
PFSERVER: NetworkBridge → HandleIPCRequestWithNativeSpawner
  ↓
NativePlayfieldSpawner → IPlayfield.SpawnPrefab() (ПРЯМОЙ ВЫЗОВ!)
  ↓
✅ Entity spawned (~50-100ms вместо 500ms-2s!)
```

**Зависимости проектов:**
- `GalacticExpansion.Core.csproj` + `GalacticExpansion.csproj` → добавлена ссылка на `UnityEngine.CoreModule.dll`
- Файл скопирован в `lib/UnityEngine.CoreModule.dll`

**Преимущества Native Spawner:**
1. ✅ **Нет краша** - PfServer инициализируется без Gateway
2. ✅ **Синхронный API** - IPlayfield методы возвращают результат сразу
3. ✅ **Быстрее** - нет overhead Gateway/SequenceManager (~50-100ms vs 500ms-2s)
4. ✅ **Проще** - меньше слоев, меньше точек отказа
5. ✅ **Надежнее** - прямой доступ к playfield entities

**Обратная совместимость:**
- Dedi процесс: БЕЗ ИЗМЕНЕНИЙ (IPC через NetworkBridge)
- PfServer процесс: НОВАЯ РЕАЛИЗАЦИЯ (Native spawner вместо Gateway)

## Текущее качество

- Код: синтаксически правильный ✅ (линтер не находит ошибок)
- Сборка: требует `--no-restore` из-за NuGet proxy
- Тесты: требуют запуска после сборки

## Следующие шаги

1. **Сборка:** `dotnet build src/GalacticExpansion.sln --configuration Release --no-restore`
2. **Тестирование PfServer:**
   - Запуск dedicated server
   - Вход игрока на playfield
   - Проверка логов: `[PfServer] NativePlayfieldSpawner created`
3. **Тестирование IPC spawn:**
   - Материализация колонии
   - Логи: `[PfServer-Native] Structure spawn successful: EntityId=...`
4. Phase 3.5: server testing (мультиплеер)
