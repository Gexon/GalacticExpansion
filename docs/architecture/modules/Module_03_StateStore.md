# Модуль: State Store

**Назначение:** Управление сохранением и загрузкой состояния симуляции

---

## Ответственность

- Загрузка state.json при старте
- Атомарная запись state.json
- Создание и управление бэкапами
- Версионирование и миграции схемы
- Обработка ошибок при загрузке

---

## Интерфейсы

```csharp
public interface IStateStore
{
    Task<SimulationState> LoadAsync();
    Task SaveAsync(SimulationState state);
    Task<SimulationState> CreateBackupAsync();
    Task<bool> RestoreFromBackupAsync(string backupPath);
}
```

---

## Зависимости

- `Newtonsoft.Json` — сериализация JSON
- Файловая система — чтение/запись файлов

---

## Ключевые классы

1. **StateStore** — основной класс персистентности
2. **SimulationState** — модель состояния
3. **StateMigrator** — миграции между версиями

---

## Примеры использования

```csharp
// Загрузка
var state = await _stateStore.LoadAsync();

// Сохранение
state.LastUpdate = DateTime.UtcNow;
await _stateStore.SaveAsync(state);

// Бэкап
await _stateStore.CreateBackupAsync();
```

---

## Тесты

- `StateStore_LoadsAndSavesCorrectly()`
- `StateStore_AtomicWrite_NoCorruption()`
- `StateStore_RestoresFromBackupOnError()`
- `StateMigrator_MigratesV1ToV2()`
