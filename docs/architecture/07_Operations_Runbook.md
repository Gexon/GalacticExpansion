# Руководство по эксплуатации GalacticExpansion (GLEX)

**Версия:** 1.0  
**Дата:** 24.01.2026  
**Статус:** Утверждено

---

## 1. Установка и первый запуск

### 1.1 Требования

**Сервер:**
- Empyrion: Galactic Survival v1.15 Experimental (Dedicated Server)
- .NET Framework 4.8+
- 16+ GB RAM (рекомендуется 32 GB)
- CPU: 4+ ядра @ 3.0 GHz+
- 500 MB свободного места

**Сценарий:** Default Multiplayer (ванильный)

### 1.2 Установка

**Шаг 1:** Скачать архив GLEX

**Шаг 2:** Распаковать в директорию:
```
[EmpyrionRoot]/Content/Mods/GalacticExpansion/
```

**Структура после распаковки:**
```
Content/Mods/GalacticExpansion/
├── GalacticExpansion.dll
├── Newtonsoft.Json.dll
├── NLog.dll
├── Configuration.json
└── Prefabs/
    ├── GLEX_DropShip_T1.epb
    ├── GLEX_ConstructionYard.epb
    ├── GLEX_Base_L1.epb
    └── ...
```

**Шаг 3:** Настроить `Configuration.json` (опционально)

**Шаг 4:** Запустить dedicated server

### 1.3 Проверка установки

**В логах сервера должны быть строки:**
```
[INFO] GalacticExpansion v1.0 loading...
[INFO] Configuration loaded successfully
[INFO] Simulation Engine started
[INFO] Colony initialized on Akua: col_akua_001
```

**Проверка состояния:**
```
Saves/Games/[SaveGameName]/Mods/GalacticExpansion/
├── state.json (должен создаться)
├── Logs/
│   └── GLEX_20260124.log
└── backups/
```

---

## 2. Конфигурация dedicated server

### 2.1 Базовая конфигурация

**Файл:** `Configuration.json`

**Минимальная конфигурация для старта:**
```json
{
  "Version": "1.0",
  "LogLevel": "Information",
  "HomePlayfield": "Akua"
}
```

**Рекомендуемая конфигурация для production:**
```json
{
  "Version": "1.0",
  "LogLevel": "Information",
  "HomePlayfield": "Akua",
  
  "Limits": {
    "MaxColoniesPerPlayfield": 1,
    "MaxGuardsNearColony": 10,
    "MaxDroneWavesPerHour": 4
  },
  
  "Simulation": {
    "SaveIntervalMinutes": 1,
    "StateBackupIntervalHours": 24
  }
}
```

### 2.2 Настройка баланса

**Для easier difficulty:**
- Увеличить `Zirax.Stages[].MinTimeSeconds`
- Уменьшить `Zirax.Stages[].ProductionRate`
- Уменьшить `Limits.MaxGuardsNearColony`
- Увеличить `AIM.DroneWaveCooldownMinutes`

**Для harder difficulty:**
- Уменьшить `Zirax.Stages[].MinTimeSeconds`
- Увеличить `Zirax.Stages[].ProductionRate`
- Увеличить `Limits.MaxColoniesPerPlayfield`
- Уменьшить `AIM.DroneWaveCooldownMinutes`

---

## 3. Мониторинг (логи, метрики)

### 3.1 Расположение логов

**Путь:** `Saves/Games/[SaveGameName]/Mods/GalacticExpansion/Logs/GLEX_[date].log`

**Формат:** Структурированные логи с временными метками

### 3.2 Уровни логирования

| Уровень | Использование |
|---------|---------------|
| **Trace** | Детальная отладка (не рекомендуется для production) |
| **Debug** | Отладочная информация |
| **Information** | Нормальная работа (рекомендуется) |
| **Warning** | Предупреждения о потенциальных проблемах |
| **Error** | Ошибки, требующие внимания |
| **Critical** | Критические ошибки, останавливающие мод |

### 3.3 Важные события в логах

**Нормальная работа:**
```
[INFO] Simulation tick completed in 15ms
[INFO] State saved successfully
[INFO] Colony col_akua_001 upgraded to BaseL2
[INFO] Player entered Akua, activating defenses
```

**Предупреждения:**
```
[WARN] Rate limit reached for AIM commands
[WARN] No suitable location found after 5 attempts
[WARN] Structure 12345 not found, skipping update
```

**Ошибки:**
```
[ERROR] Failed to spawn entity: timeout after 5000ms
[ERROR] Failed to save state: IOException
[CRITICAL] ModAPI not responding, stopping mod
```

### 3.4 Метрики производительности

**Ключевые метрики в логах:**
- Simulation tick time (должно быть < 100ms)
- API request response time (должно быть < 1000ms)
- State save time (должно быть < 500ms)

---

## 4. Backup и восстановление

### 4.1 Автоматические бэкапы

**Расположение:** `Saves/Games/[SaveGameName]/Mods/GalacticExpansion/backups/`

**Частота:** Раз в 24 часа (настраивается)

**Хранение:** Последние 10 бэкапов (настраивается)

**Имя файла:** `state_backup_yyyyMMdd_HHmmss.json`

### 4.2 Ручное создание бэкапа

**Способ 1: Копирование файла**
```bash
cp state.json state_manual_backup_$(date +%Y%m%d).json
```

**Способ 2: Остановка сервера → копирование**
1. Остановить dedicated server
2. Скопировать `state.json`
3. Запустить сервер

### 4.3 Восстановление из бэкапа

**При автоматическом восстановлении:**
- Мод автоматически попытается восстановиться из последнего валидного бэкапа при ошибке чтения `state.json`

**При ручном восстановлении:**
1. Остановить dedicated server
2. Найти нужный бэкап в `backups/`
3. Скопировать его в `state.json`:
   ```bash
   cp backups/state_backup_20260124_120000.json state.json
   ```
4. Запустить сервер
5. Проверить логи на наличие ошибок

---

## 5. Обновление мода

### 5.1 Процедура обновления

**Автоматическое обновление (рекомендуется):**

Используйте скрипт `deploy_mod.cmd` из папки `tools`:

```cmd
# После успешной сборки проекта
cd tools
deploy_mod.cmd Release
```

Скрипт автоматически:
- ✅ Создаст бэкапы DLL и state.json
- ✅ Скопирует новые файлы в папку мода
- ✅ Сохранит пользовательские настройки (Configuration.json, state.json)
- ✅ Очистит старые бэкапы (оставит последние 10)

---

**Ручное обновление:**

**Шаг 1: Создание бэкапа**
```bash
# Бэкап состояния
cp state.json state_pre_update_backup.json

# Бэкап DLL
cp GalacticExpansion.dll GalacticExpansion.dll.old
```

**Шаг 2: Остановка сервера**

**Шаг 3: Замена файлов**
- Заменить `GalacticExpansion.dll` и зависимости
- НЕ заменять `Configuration.json` (сохранить настройки)
- НЕ заменять `state.json`

**Шаг 4: Проверка миграций**
- Прочитать CHANGELOG новой версии
- Проверить, требуется ли миграция state.json

**Шаг 5: Запуск сервера**

**Шаг 6: Проверка логов**
```
[INFO] GalacticExpansion v1.1 loading...
[INFO] Migrating state from v1 to v2
[INFO] Migration completed successfully
```

### 5.2 Откат обновления

**Если обновление неудачно:**

**Метод 1: Восстановление из автоматических бэкапов (если использовался deploy_mod.cmd)**

1. Остановить сервер
2. Найти последний бэкап в папке `backups/`:
   ```cmd
   dir /o:d backups\GalacticExpansion.dll.*.backup
   ```
3. Восстановить DLL:
   ```cmd
   copy /Y backups\GalacticExpansion.dll.20260129_143022.backup GalacticExpansion.dll
   ```
4. При необходимости восстановить state:
   ```cmd
   copy /Y backups\state_pre_update_20260129_143022.json state.json
   ```
5. Запустить сервер

**Метод 2: Ручное восстановление (при ручном обновлении)**

1. Остановить сервер
2. Восстановить старую DLL:
   ```bash
   mv GalacticExpansion.dll.old GalacticExpansion.dll
   ```
3. Восстановить state:
   ```bash
   mv state_pre_update_backup.json state.json
   ```
4. Запустить сервер

---

## 6. Troubleshooting FAQ

### 6.1 Мод не загружается

**Симптомы:**
- Нет логов от GLEX
- В логах сервера ошибки загрузки DLL

**Решение:**
1. Проверить версию Empyrion (должна быть 1.15 Experimental)
2. Проверить наличие зависимостей (Newtonsoft.Json.dll, NLog.dll)
3. Проверить права доступа к файлам
4. Проверить логи сервера на наличие детальных ошибок

---

### 6.2 Колонии не спавнятся

**Симптомы:**
- `state.json` содержит колонии, но их нет в игре
- Игроки не видят структуры Zirax

**Решение:**
1. Проверить, что игроки действительно на правильном playfield (`HomePlayfield`)
2. Проверить логи на наличие ошибок спавна:
   ```
   [ERROR] Failed to spawn entity: prefab not found
   ```
3. Проверить наличие префабов в `Content/Prefabs/`
4. Проверить `MainStructureId` в `state.json` — если 0, структура не создана

---

### 6.3 Высокая нагрузка на CPU

**Симптомы:**
- GLEX потребляет > 10% CPU
- Лаги на сервере

**Решение:**
1. Увеличить `Simulation.TickIntervalMs` до 2000-3000
2. Уменьшить лимиты:
   ```json
   "Limits": {
     "MaxGuardsNearColony": 5,
     "MaxResourceOutposts": 2,
     "MaxRequestsPerSecond": 5
   }
   ```
3. Увеличить `Simulation.SaveIntervalMinutes` до 5
4. Проверить логи на наличие ошибок/warnings

---

### 6.4 State.json поврежден

**Симптомы:**
```
[ERROR] Failed to deserialize state (result is null)
[INFO] Attempting to restore from latest backup...
```

**Решение:**
- Мод автоматически восстановится из последнего валидного бэкапа
- Если восстановление не удалось, вручную скопировать бэкап (см. раздел 4.3)

---

### 6.5 AIM команды не работают

**Симптомы:**
- Нет патрулей при входе игроков
- Волны дронов не активируются

**Решение:**
1. Проверить, что AIM включен на сервере
2. Проверить логи на rate limit warnings:
   ```
   [WARN] Rate limit exceeded for AIM commands
   ```
3. Проверить whitelist команд в конфигурации
4. Проверить права мода на выполнение консольных команд

---

### 6.6 Утечки памяти

**Симптомы:**
- Постоянный рост потребления памяти
- Сервер замедляется со временем

**Решение:**
1. Обновиться на последнюю версию мода
2. Проверить логи на повторяющиеся ошибки
3. Периодически рестартовать сервер (раз в 1-2 дня)
4. Сообщить об проблеме разработчикам с приложением логов

---

## 7. Полезные команды и скрипты

### 7.1 Мониторинг логов в реальном времени

**Linux/Mac:**
```bash
tail -f Logs/GLEX_$(date +%Y%m%d).log
```

**Windows PowerShell:**
```powershell
Get-Content "Logs\GLEX_$(Get-Date -Format yyyyMMdd).log" -Wait
```

### 7.2 Поиск ошибок в логах

**Linux/Mac:**
```bash
grep -i error Logs/GLEX_*.log
grep -i critical Logs/GLEX_*.log
```

**Windows PowerShell:**
```powershell
Select-String -Pattern "ERROR" -Path "Logs\GLEX_*.log"
```

### 7.3 Проверка размера state.json

```bash
ls -lh state.json
```

**Нормальный размер:** 10-100 KB  
**Если > 1 MB:** Возможна проблема (слишком много колоний/данных)

---

## 8. Контакты и поддержка

**Документация:** `docs/architecture/`

**Логи для отправки при проблемах:**
- `Logs/GLEX_[date].log` (последний файл)
- `state.json` (если проблема с данными)
- `Configuration.json` (текущие настройки)