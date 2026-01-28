# Справочник конфигурации GalacticExpansion (GLEX)

**Версия:** 1.0  
**Дата:** 24.01.2026  
**Статус:** Утверждено

---

## 1. Обзор системы конфигурации

### 1.1 Расположение файла

**Путь:** `[EmpyrionRoot]/Content/Mods/GalacticExpansion/Configuration.json`

**Формат:** JSON с комментариями (при использовании Newtonsoft.Json)

### 1.2 Применение изменений

- Конфигурация читается **при старте мода**
- Для применения изменений требуется **перезапуск сервера**
- Невалидные значения заменяются на **значения по умолчанию** с записью в лог

---

## 2. Справочник параметров

### 2.1 Общие параметры

#### Version
**Тип:** String  
**По умолчанию:** `"1.0"`  
**Описание:** Версия конфигурационного файла

```json
"Version": "1.0"
```

#### LogLevel
**Тип:** String (Enum)  
**Допустимые значения:** `Trace`, `Debug`, `Information`, `Warning`, `Error`, `Critical`  
**По умолчанию:** `"Information"`  
**Описание:** Уровень логирования мода

```json
"LogLevel": "Information"
```

**Рекомендации:**
- `Debug` — для разработки и отладки
- `Information` — для production
- `Warning` — для минимального логирования

---

### 2.2 Simulation — Параметры симуляции

#### Simulation.TickIntervalMs
**Тип:** Integer  
**По умолчанию:** `1000`  
**Диапазон:** 500-5000  
**Описание:** Интервал обновления симуляции в миллисекундах

```json
"Simulation": {
  "TickIntervalMs": 1000
}
```

**Влияние на производительность:**
- Меньше значение = выше нагрузка на CPU
- Больше значение = медленнее реакция на события

#### Simulation.SaveIntervalMinutes
**Тип:** Integer  
**По умолчанию:** `1`  
**Диапазон:** 1-60  
**Описание:** Интервал автосохранения state.json (при наличии изменений)

#### Simulation.StateBackupIntervalHours
**Тип:** Integer  
**По умолчанию:** `24`  
**Описание:** Интервал создания бэкапов state.json

#### Simulation.KeepBackupCount
**Тип:** Integer  
**По умолчанию:** `10`  
**Описание:** Количество хранимых бэкапов

---

### 2.3 HomePlayfield — Материнская планета

#### HomePlayfield
**Тип:** String  
**По умолчанию:** `"Akua"`  
**Описание:** Имя playfield для первой колонии Zirax

```json
"HomePlayfield": "Akua"
```

**Важно:** Имя должно точно соответствовать имени playfield в игре

---

### 2.4 EnableExpansion — Включение экспансии

#### EnableExpansion
**Тип:** Boolean  
**По умолчанию:** `true`  
**Описание:** Включает/выключает экспансию на новые планеты (Post-MVP feature)

```json
"EnableExpansion": true
```

---

### 2.5 Limits — Лимиты и квоты

#### Limits.MaxColoniesPerPlayfield
**Тип:** Integer  
**По умолчанию:** `1`  
**Диапазон:** 1-10  
**Описание:** Максимальное количество колоний Zirax на одном playfield

**Для MVP:** Рекомендуется `1`

#### Limits.MaxActiveAIVessels
**Тип:** Integer  
**По умолчанию:** `5`  
**Диапазон:** 0-20  
**Описание:** Максимальное количество активных AI-кораблей на планете

#### Limits.MaxGuardsNearColony
**Тип:** Integer  
**По умолчанию:** `10`  
**Диапазон:** 0-50  
**Описание:** Максимальное количество NPC-охранников возле колонии

#### Limits.MaxBuildersNearColony
**Тип:** Integer  
**По умолчанию:** `5`  
**Диапазон:** 0-20  
**Описание:** Максимальное количество NPC-строителей возле колонии

#### Limits.MaxResourceOutposts
**Тип:** Integer  
**По умолчанию:** `3`  
**Диапазон:** 0-10  
**Описание:** Максимальное количество ресурсных аванпостов на колонию

#### Limits.MaxDroneWavesPerHour
**Тип:** Integer  
**По умолчанию:** `4`  
**Диапазон:** 0-10  
**Описание:** Максимальное количество волн дронов в час

#### Limits.MaxAIMCommandsPerMinute
**Тип:** Integer  
**По умолчанию:** `10`  
**Диапазон:** 1-60  
**Описание:** Максимальное количество AIM-команд в минуту (rate limiting)

#### Limits.MaxRequestsPerSecond
**Тип:** Integer  
**По умолчанию:** `10`  
**Диапазон:** 1-100  
**Описание:** Максимальное количество API-запросов в секунду (rate limiting)

---

### 2.6 Zirax — Настройки фракции Zirax

#### Zirax.FactionId
**Тип:** Integer  
**По умолчанию:** `2`  
**Описание:** ID фракции Zirax в игре

**Важно:** Не изменять без необходимости

#### Zirax.DropShips
**Тип:** Array of Objects  
**Описание:** Конфигурация логистических кораблей

```json
"DropShips": [
  {
    "PrefabName": "GLEX_DropShip_T1",
    "Type": "SV",
    "SpawnAltitude": 500.0,
    "FlightDurationSeconds": 30
  }
]
```

**Поля DropShip:**
- `PrefabName` (String) — имя префаба корабля
- `Type` (String) — тип: `SV` или `CV`
- `SpawnAltitude` (Float) — высота спавна в метрах
- `FlightDurationSeconds` (Integer) — длительность полета перед исчезновением

#### Zirax.Stages
**Тип:** Array of Objects  
**Описание:** Конфигурация стадий развития колонии

```json
"Stages": [
  {
    "Stage": "BaseL1",
    "PrefabName": "GLEX_Base_L1",
    "RequiredResources": 1000,
    "ProductionRate": 150.0,
    "MinTimeSeconds": 1800
  }
]
```

**Поля Stage:**
- `Stage` (String) — имя стадии: `ConstructionYard`, `BaseL1`, `BaseL2`, `BaseL3`, `BaseMax`
- `PrefabName` (String) — имя префаба структуры
- `RequiredResources` (Float) — необходимое количество виртуальных ресурсов для перехода
- `ProductionRate` (Float) — скорость производства ресурсов в час
- `MinTimeSeconds` (Integer) — минимальное время в стадии (секунды)

**Балансировка:**
- Увеличение `RequiredResources` → медленнее развитие
- Увеличение `ProductionRate` → быстрее развитие
- `MinTimeSeconds` предотвращает мгновенные переходы

#### Zirax.ResourceOutposts
**Тип:** Array of Objects  
**Описание:** Типы ресурсных аванпостов

```json
"ResourceOutposts": [
  {
    "Type": "Iron",
    "PrefabName": "GLEX_Miner_Iron",
    "ProductionRate": 75.0
  }
]
```

#### Zirax.Guards
**Тип:** Array of Objects  
**Описание:** Типы и количество охранников

```json
"Guards": [
  {
    "Type": "ZiraxMale",
    "Count": 5
  }
]
```

---

### 2.7 AIM — Advanced Intelligent Mechanics

#### AIM.AllowedCommands
**Тип:** Array of Strings  
**По умолчанию:** `["aim aga", "aim tdw", "aim adb"]`  
**Описание:** Whitelist разрешенных AIM-команд

**Безопасность:** Не добавляйте другие команды без понимания последствий

#### AIM.RateLimitPerMinute
**Тип:** Integer  
**По умолчанию:** `10`  
**Описание:** Максимальное количество AIM-команд в минуту

#### AIM.DefaultGuardRange
**Тип:** Integer  
**По умолчанию:** `500`  
**Описание:** Радиус охраны по умолчанию для `aim aga` (метры)

#### AIM.DroneWaveCooldownMinutes
**Тип:** Integer  
**По умолчанию:** `15`  
**Описание:** Минимальный интервал между волнами дронов (минуты)

---

### 2.8 Placement — Размещение структур

#### Placement.MinDistanceFromPlayers
**Тип:** Float  
**По умолчанию:** `500.0`  
**Описание:** Минимальная дистанция от игроков при размещении (метры)

#### Placement.MinDistanceFromPlayerStructures
**Тип:** Float  
**По умолчанию:** `1000.0`  
**Описание:** Минимальная дистанция от структур игроков (метры)

#### Placement.SearchRadius
**Тип:** Float  
**По умолчанию:** `2000.0`  
**Описание:** Радиус поиска подходящего места (метры)

#### Placement.PreferredAltitude
**Тип:** Float  
**По умолчанию:** `150.0`  
**Описание:** Предпочтительная высота для размещения (метры)

#### Placement.MaxPlacementAttempts
**Тип:** Integer  
**По умолчанию:** `10`  
**Описание:** Максимальное количество попыток найти место

---

### 2.9 Threat — Система угроз

#### Threat.PlayerProximityWeight
**Тип:** Float  
**По умолчанию:** `10.0`  
**Описание:** Вес фактора близости игроков при расчете угрозы

#### Threat.DestructionWeight
**Тип:** Float  
**По умолчанию:** `20.0`  
**Описание:** Вес фактора разрушений

#### Threat.AttackDecayMinutes
**Тип:** Integer  
**По умолчанию:** `60`  
**Описание:** Время затухания угрозы после атаки (минуты)

#### Threat.StageValueWeight
**Тип:** Float  
**По умолчанию:** `5.0`  
**Описание:** Вес фактора ценности стадии колонии

---

### 2.10 Expansion — Экспансия (Post-MVP)

#### Expansion.TargetPlayfields
**Тип:** Array of Strings  
**Описание:** Список playfield'ов для экспансии

```json
"TargetPlayfields": ["Omicron", "Ningues", "Tallodar"]
```

#### Expansion.TravelTimeMinutes
**Тип:** Integer  
**По умолчанию:** `30`  
**Описание:** Время "путешествия" на новую планету

#### Expansion.MinTimeSinceMaxStageMinutes
**Тип:** Integer  
**По умолчанию:** `120`  
**Описание:** Минимальное время после достижения BaseMax перед экспансией

---

## 3. Примеры конфигураций

### 3.1 MVP Configuration (Balanced)

```json
{
  "Version": "1.0",
  "LogLevel": "Information",
  "HomePlayfield": "Akua",
  "EnableExpansion": false,
  
  "Limits": {
    "MaxColoniesPerPlayfield": 1,
    "MaxActiveAIVessels": 5,
    "MaxGuardsNearColony": 10,
    "MaxResourceOutposts": 3,
    "MaxDroneWavesPerHour": 4
  },
  
  "Zirax": {
    "Stages": [
      {"Stage": "ConstructionYard", "RequiredResources": 0, "ProductionRate": 100, "MinTimeSeconds": 600},
      {"Stage": "BaseL1", "RequiredResources": 1000, "ProductionRate": 150, "MinTimeSeconds": 1800},
      {"Stage": "BaseL2", "RequiredResources": 3000, "ProductionRate": 200, "MinTimeSeconds": 3600}
    ]
  }
}
```

### 3.2 High Difficulty Configuration

```json
{
  "Limits": {
    "MaxColoniesPerPlayfield": 2,
    "MaxGuardsNearColony": 20,
    "MaxResourceOutposts": 5,
    "MaxDroneWavesPerHour": 8
  },
  
  "Zirax": {
    "Stages": [
      {"Stage": "BaseL1", "RequiredResources": 500, "ProductionRate": 200, "MinTimeSeconds": 900}
    ]
  },
  
  "AIM": {
    "DroneWaveCooldownMinutes": 5
  },
  
  "Threat": {
    "PlayerProximityWeight": 20.0,
    "DestructionWeight": 40.0
  }
}
```

### 3.3 Performance Optimized Configuration

```json
{
  "Simulation": {
    "TickIntervalMs": 2000,
    "SaveIntervalMinutes": 5
  },
  
  "Limits": {
    "MaxColoniesPerPlayfield": 1,
    "MaxActiveAIVessels": 3,
    "MaxGuardsNearColony": 5,
    "MaxResourceOutposts": 2,
    "MaxDroneWavesPerHour": 2,
    "MaxRequestsPerSecond": 5
  }
}
```

---

## 4. Валидация и значения по умолчанию

### 4.1 Правила валидации

**При загрузке конфигурации:**
1. Проверка JSON синтаксиса
2. Проверка обязательных полей
3. Проверка диапазонов значений
4. Проверка существования prefab'ов (warning если не найдены)

**При невалидных значениях:**
- Замена на значение по умолчанию
- Запись warning в лог
- Продолжение работы

### 4.2 Пример валидации

```csharp
public class ConfigurationValidator
{
    public void Validate(Configuration config)
    {
        // Проверка Limits
        if (config.Limits.MaxColoniesPerPlayfield < 1)
        {
            _logger.LogWarning("MaxColoniesPerPlayfield < 1, using default: 1");
            config.Limits.MaxColoniesPerPlayfield = 1;
        }
        
        // Проверка Stages
        if (!config.Zirax.Stages.Any())
        {
            _logger.LogError("No stages defined, using default stages");
            config.Zirax.Stages = GetDefaultStages();
        }
    }
}
```