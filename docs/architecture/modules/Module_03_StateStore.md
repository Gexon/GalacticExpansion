# Модуль: State Store

**Приоритет разработки:** 1 (Критический - базовая инфраструктура)  
**Зависимости:** Newtonsoft.Json  
**Статус:** 🟢 Спецификация готова

---

## 1. Назначение

State Store управляет **персистентностью состояния симуляции**: загрузка при старте, атомарное сохранение, версионирование, миграции схемы данных и создание бэкапов.

**Ключевая особенность:** Атомарная запись через temp file → rename, защита от повреждения данных.

---

## 2. Интерфейс

```csharp
/// <summary>
/// Хранилище состояния симуляции
/// Паттерн: Repository
/// </summary>
public interface IStateStore
{
    /// <summary>
    /// Загрузка состояния из state.json
    /// При ошибке пытается восстановить из бэкапа
    /// </summary>
    Task<SimulationState> LoadAsync();
    
    /// <summary>
    /// Атомарное сохранение состояния
    /// Использует temp file → rename для предотвращения повреждения
    /// </summary>
    Task SaveAsync(SimulationState state);
    
    /// <summary>
    /// Создание бэкапа текущего состояния
    /// </summary>
    Task<SimulationState> CreateBackupAsync();
    
    /// <summary>
    /// Восстановление из конкретного бэкапа
    /// </summary>
    Task<bool> RestoreFromBackupAsync(string backupPath);
}
```

---

## 3. Структура файлов

```
[EmpyrionRoot]/Saves/Games/[SaveGameName]/Mods/GalacticExpansion/
├── state.json                    # Текущее состояние
├── state.json.tmp                # Временный файл при сохранении
├── backups/
│   ├── state_backup_20260128_120000.json
│   ├── state_backup_20260128_130000.json
│   └── ...                       # Хранится последние 10 бэкапов
└── state.json.failed_12345678    # Поврежденный файл (если было восстановление)
```

---

## 4. Модель данных

```csharp
/// <summary>
/// Состояние симуляции (сохраняется в state.json)
/// </summary>
public class SimulationState
{
    public const int CurrentVersion = 1;
    
    public int Version { get; set; } = CurrentVersion;
    public DateTime CreatedAt { get; set; }
    public DateTime LastUpdate { get; set; }
    public DateTime LastSaveTime { get; set; }
    
    public List<Colony> Colonies { get; set; } = new();
    public Dictionary<string, PlayfieldState> Playfields { get; set; } = new();
    
    [JsonIgnore]
    public bool IsDirty { get; set; }  // Флаг несохраненных изменений
}

/// <summary>
/// Состояние playfield
/// </summary>
public class PlayfieldState
{
    public string Name { get; set; }
    public bool HasPlayers { get; set; }
    public DateTime LastPlayerActivity { get; set; }
    public List<string> ColonyIds { get; set; } = new();
}
```

---

## 5. Реализация

### 5.1 Атомарное сохранение

```csharp
public async Task SaveAsync(SimulationState state)
{
    await _saveSemaphore.WaitAsync();  // Только один поток сохраняет
    
    try
    {
        state.LastUpdate = DateTime.UtcNow;
        
        var json = JsonConvert.SerializeObject(state, Formatting.Indented, GetJsonSettings());
        
        // АТОМАРНАЯ ЗАПИСЬ:
        // 1. Записываем во временный файл
        var tempFile = _stateFilePath + ".tmp";
        await File.WriteAllTextAsync(tempFile, json);
        
        // 2. Атомарное переименование (гарантирует целостность)
        if (File.Exists(_stateFilePath))
            File.Replace(tempFile, _stateFilePath, null);
        else
            File.Move(tempFile, _stateFilePath);
        
        _logger.LogDebug("State saved successfully");
    }
    finally
    {
        _saveSemaphore.Release();
    }
}
```

### 5.2 Загрузка с восстановлением из бэкапа

```csharp
public async Task<SimulationState> LoadAsync()
{
    if (!File.Exists(_stateFilePath))
        return null;  // Первый запуск
    
    try
    {
        var json = await File.ReadAllTextAsync(_stateFilePath);
        var state = JsonConvert.DeserializeObject<SimulationState>(json, GetJsonSettings());
        
        if (state == null)
            return await TryRestoreFromBackupAsync();
        
        // Миграция при необходимости
        if (state.Version < SimulationState.CurrentVersion)
            state = await MigrateStateAsync(state);
        
        return state;
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error loading state, trying backup...");
        return await TryRestoreFromBackupAsync();
    }
}
```

### 5.3 Версионирование и миграции

```csharp
private async Task<SimulationState> MigrateStateAsync(SimulationState oldState)
{
    // Бэкап перед миграцией
    var migrationBackup = Path.Combine(_backupDirectory, 
        $"state_pre_migration_v{oldState.Version}_{DateTime.UtcNow.Ticks}.json");
    await File.WriteAllTextAsync(migrationBackup, 
        JsonConvert.SerializeObject(oldState, Formatting.Indented));
    
    // Применение миграций
    var currentVersion = oldState.Version;
    while (currentVersion < SimulationState.CurrentVersion)
    {
        currentVersion++;
        _logger.LogInformation($"Applying migration to version {currentVersion}");
        
        switch (currentVersion)
        {
            case 2:
                oldState = MigrateToVersion2(oldState);
                break;
            // Добавлять новые миграции здесь
        }
        
        oldState.Version = currentVersion;
    }
    
    return oldState;
}
```

---

## 6. JSON настройки сериализации

```csharp
private JsonSerializerSettings GetJsonSettings()
{
    return new JsonSerializerSettings
    {
        TypeNameHandling = TypeNameHandling.Auto,
        NullValueHandling = NullValueHandling.Ignore,
        DefaultValueHandling = DefaultValueHandling.Include,
        Formatting = Formatting.Indented,  // Читаемый JSON
        Converters = new List<JsonConverter>
        {
            new StringEnumConverter()  // Enum как строки
        }
    };
}
```

---

## 7. Управление бэкапами

**Стратегия:**
- Бэкап создается вручную через `CreateBackupAsync()`
- Хранится последние 10 бэкапов (старые автоматически удаляются)
- Бэкап перед каждой миграцией схемы
- Формат имени: `state_backup_yyyyMMdd_HHmmss.json`

**Восстановление:**
```csharp
// Автоматическое (при ошибке загрузки)
var state = await _stateStore.LoadAsync();  // Попытается бэкапы

// Ручное (из конкретного файла)
await _stateStore.RestoreFromBackupAsync("backups/state_backup_20260128_120000.json");
```

---

## 8. Чеклист разработчика

**Этап 1: Базовая персистентность (1 день)**
- [ ] Реализовать `IStateStore`
- [ ] `LoadAsync()` / `SaveAsync()`
- [ ] Атомарная запись
- [ ] Unit-тесты

**Этап 2: Бэкапы (0.5 дня)**
- [ ] `CreateBackupAsync()`
- [ ] `RestoreFromBackupAsync()`
- [ ] Автоматическая очистка старых

**Этап 3: Версионирование (1 день)**
- [ ] Система миграций
- [ ] Пример миграции v1→v2
- [ ] Тесты миграций

---

## 9. Известные проблемы

### 9.1 Повреждение state.json при крэше

**Причина:** Запись не завершена

**Решение:** Атомарная запись через temp file (уже реализовано)

### 9.2 Большой размер state.json

**Причина:** Много колоний и данных

**Решение:** 
- Сжатие JSON (удаление null полей)
- Опциональный GZIP (в будущем)

---

## 10. Производительность

**Метрики:**
- Load time: 50-200 мс (зависит от размера state.json)
- Save time: 100-300 мс (атомарная запись)
- Размер файла: ~10-50 KB для 10 колоний

**Рекомендации:**
- Сохранять не чаще 1 раза в минуту
- Не сохранять при каждом изменении (использовать `IsDirty` флаг)

---

## 11. Связь с другими документами

- **[Module_01_Core_Loop.md](Module_01_Core_Loop.md)** — использует StateStore при старте/остановке
- **[05_Схема_данных.md](../05_Схема_данных.md)** — детальная структура SimulationState

---

**Последнее обновление:** 28.01.2026  
**Размер:** ~330 строк
