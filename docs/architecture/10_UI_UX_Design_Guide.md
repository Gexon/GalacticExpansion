# UI/UX Design Guide GalacticExpansion (GLEX)

**Версия:** 1.0  
**Дата:** 24.01.2026  
**Статус:** Утверждено

---

## 1. Обзор UI/UX для server-side мода

### 1.1 Особенности server-side модов

GLEX — **server-side DLL мод**, который не требует client-side модификаций. Это накладывает ограничения на UI/UX:

**Доступные каналы взаимодействия:**
- **Chat-команды:** Через игровой чат (faction chat, global chat)
- **HUD сообщения:** Через игровые notification
- **Лог-файлы:** Для администраторов сервера

**Недоступно:**
- Custom UI elements (кнопки, окна)
- Client-side HUD overlays
- Пользовательские GUI

---

## 2. Chat-команды и интерфейс

### 2.1 Общие принципы

**Принципы дизайна команд:**
1. **Краткость:** Команды должны быть короткими и запоминающимися
2. **Консистентность:** Единый префикс для всех команд
3. **Понятность:** Очевидное назначение из имени команды
4. **Обратная связь:** Немедленный feedback после выполнения
5. **Помощь:** Команда `help` для всех модулей

### 2.2 Структура команд

**Формат:**
```
\<prefix> <command> [arguments]
```

**Примеры:**
```
\glex help                    # Справка по командам
\glex status                  # Статус симуляции
\glex colony list             # Список колоний
\glex colony info <id>        # Информация о колонии
```

### 2.3 Префиксы команд

**Рекомендуемый префикс:** `\glex`

**Альтернативы:**
- `/glex` — если `\` недоступен
- `!glex` — для ботов/автоматизации

**Конфигурация:**
```json
{
  "ChatCommands": {
    "Prefix": "\\glex",
    "AliasPrefix": "/glex"
  }
}
```

---

## 3. Информирование игроков

### 3.1 Типы сообщений

#### Welcome Message (при входе на сервер)

```
[GLEX] Добро пожаловать на сервер с GalacticExpansion!
Планета Akua захвачена фракцией Zirax.
Используйте \glex help для справки.
```

#### Event Notifications (при важных событиях)

```
[GLEX] Внимание! Zirax построили новую базу на Akua: BaseL2
[GLEX] Логистический корабль Zirax замечен в атмосфере планеты
[GLEX] Волна дронов направляется к вашей базе!
```

#### Status Updates (периодические)

```
[GLEX] Колония Zirax на Akua: BaseL2 (75% до следующего улучшения)
```

### 3.2 Каналы коммуникации

**Global Chat:**
- Важные системные сообщения
- Welcome messages
- Critical warnings

**Faction Chat:**
- Команды администрирования
- Детальная информация

**Private Messages:**
- Персональные уведомления
- Responses на команды

### 3.3 Форматирование сообщений

**Цветовое кодирование (если поддерживается игрой):**

```csharp
// Информация
[c][0080FF]INFO:[/c] Сообщение

// Предупреждение
[c][FFAA00]WARNING:[/c] Сообщение

// Ошибка
[c][FF0000]ERROR:[/c] Сообщение

// Успех
[c][00FF00]SUCCESS:[/c] Сообщение
```

**Пример реализации:**

```csharp
public enum MessageType
{
    Info,
    Warning,
    Error,
    Success
}

public string FormatMessage(string message, MessageType type)
{
    var color = type switch
    {
        MessageType.Info => "0080FF",
        MessageType.Warning => "FFAA00",
        MessageType.Error => "FF0000",
        MessageType.Success => "00FF00",
        _ => "FFFFFF"
    };
    
    var prefix = type switch
    {
        MessageType.Info => "INFO",
        MessageType.Warning => "WARNING",
        MessageType.Error => "ERROR",
        MessageType.Success => "SUCCESS",
        _ => "GLEX"
    };
    
    return $"[c][{color}][GLEX {prefix}]:[/c] {message}";
}
```

---

## 4. Команды для игроков

### 4.1 Help Commands

#### \glex help
**Описание:** Показать список всех команд

**Вывод:**
```
[GLEX] Доступные команды:
  \glex help            - Показать эту справку
  \glex status          - Статус симуляции
  \glex colony list     - Список колоний Zirax
  \glex colony info <id> - Информация о колонии
  \glex threat         - Текущий уровень угрозы
```

#### \glex status
**Описание:** Общий статус симуляции

**Вывод:**
```
[GLEX] Статус симуляции:
  Активных колоний: 2
  Планеты: Akua (1 колония), Omicron (1 колония)
  Симуляция работает: 5 часов 23 минуты
```

### 4.2 Colony Commands

#### \glex colony list
**Описание:** Список всех колоний

**Вывод:**
```
[GLEX] Колонии Zirax:
  1. col_akua_001 (Akua) - BaseL2 - 75% до BaseL3
  2. col_omicron_001 (Omicron) - BaseL1 - 30% до BaseL2
```

#### \glex colony info <id>
**Описание:** Детальная информация о колонии

**Вывод:**
```
[GLEX] Информация о колонии col_akua_001:
  Локация: Akua (1234, 150, -987)
  Стадия: BaseL2
  Ресурсов: 5432 / 10000 (54%)
  Охранников: 10
  Ресурсных аванпостов: 3
  Последнее обновление: 5 минут назад
```

### 4.3 Threat Commands

#### \glex threat
**Описание:** Текущий уровень угрозы

**Вывод:**
```
[GLEX] Уровень угрозы на Akua: ВЫСОКИЙ
  Причины:
    - Вы находитесь в радиусе 500м от базы Zirax
    - Недавние разрушения структур Zirax
  Рекомендация: Ожидайте усиленных патрулей
```

---

## 5. Команды для администраторов

### 5.1 Admin Commands (требуют прав администратора)

#### \glex admin reload
**Описание:** Перезагрузить конфигурацию без рестарта сервера

**Вывод:**
```
[GLEX] Конфигурация перезагружена успешно
```

#### \glex admin save
**Описание:** Принудительное сохранение state.json

**Вывод:**
```
[GLEX] Состояние сохранено: state.json (45 KB)
```

#### \glex admin backup
**Описание:** Создать бэкап состояния

**Вывод:**
```
[GLEX] Бэкап создан: state_backup_20260124_153045.json
```

#### \glex admin colony spawn <playfield>
**Описание:** Принудительное создание колонии

**Вывод:**
```
[GLEX] Колония создана на Omicron: col_omicron_002
```

#### \glex admin stats
**Описание:** Статистика производительности

**Вывод:**
```
[GLEX] Статистика:
  Tick time (avg): 45ms
  API requests/sec: 3.2
  Memory usage: 87 MB
  Uptime: 12 hours 45 minutes
```

---

## 6. Логирование для администраторов

### 6.1 Структура логов

**Формат:**
```
[timestamp] [level] [module] message
```

**Пример:**
```
2026-01-24 15:30:45 [INFO] [SimulationEngine] Simulation tick completed in 45ms
2026-01-24 15:30:46 [INFO] [ColonyManager] Colony col_akua_001 upgraded to BaseL2
2026-01-24 15:30:47 [WARN] [RateLimiter] Rate limit approached: 8/10 requests
2026-01-24 15:30:48 [ERROR] [Gateway] Request timeout after 5000ms: SpawnEntity
```

### 6.2 Категории логов

| Категория | Описание | Когда логировать |
|-----------|----------|------------------|
| **Simulation** | События симуляции | Переходы стадий, создание колоний |
| **Gateway** | API взаимодействие | Запросы, ответы, ошибки |
| **Performance** | Производительность | Медленные операции (> 100ms) |
| **Security** | Безопасность | Rate limit, недопустимые команды |
| **StateStore** | Персистентность | Сохранения, загрузки, бэкапы |

### 6.3 Уровни детализации

**Production (Information):**
```
[INFO] Colony upgraded
[WARN] Rate limit reached
[ERROR] API timeout
```

**Debug:**
```
[DEBUG] Colony resources: 5432 / 10000
[DEBUG] Sending request: SpawnEntity(prefab=GLEX_Base_L2)
[DEBUG] Response received in 234ms
```

**Trace (очень детальный):**
```
[TRACE] Simulation tick start
[TRACE] Module update: ColonyManager (15ms)
[TRACE] Module update: ThreatDirector (8ms)
[TRACE] Simulation tick end (45ms total)
```

---

## 7. Best Practices

### 7.1 Для разработчиков команд

**DO:**
- ✓ Всегда предоставляйте feedback на команду
- ✓ Валидируйте аргументы перед выполнением
- ✓ Используйте понятные сообщения об ошибках
- ✓ Документируйте команды в help
- ✓ Логируйте выполнение admin-команд

**DON'T:**
- ✗ Не используйте слишком длинные команды
- ✗ Не спамите chat сообщениями
- ✗ Не показывайте технические детали игрокам
- ✗ Не забывайте проверять permissions

### 7.2 Для логирования

**DO:**
- ✓ Используйте структурированные логи
- ✓ Включайте context (colony ID, player ID и т.д.)
- ✓ Логируйте начало и конец длительных операций
- ✓ Используйте правильные уровни (не все ERROR)

**DON'T:**
- ✗ Не логируйте sensitive data (пароли, токены)
- ✗ Не спамите логи в production (используйте Debug только при отладке)
- ✗ Не забывайте exception stack traces

---

## 8. Примеры реализации

### 8.1 Chat Command Handler

```csharp
public class ChatCommandHandler
{
    private readonly Dictionary<string, ChatCommand> _commands = new();
    
    public void RegisterCommand(string pattern, Action<ChatInfo, Dictionary<string, string>> handler, string description)
    {
        _commands[pattern] = new ChatCommand
        {
            Pattern = pattern,
            Handler = handler,
            Description = description
        };
    }
    
    public async Task HandleChatMessageAsync(ChatInfo chatInfo)
    {
        var message = chatInfo.msg.Trim();
        
        foreach (var command in _commands.Values)
        {
            var match = Regex.Match(message, command.Pattern);
            if (match.Success)
            {
                var args = match.Groups.Cast<Group>()
                    .Skip(1)
                    .ToDictionary(g => g.Name, g => g.Value);
                
                try
                {
                    command.Handler(chatInfo, args);
                    _logger.LogDebug($"Command executed: {message} by player {chatInfo.playerId}");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Command error: {message}");
                    await SendMessageAsync(chatInfo.playerId, 
                        FormatMessage($"Ошибка выполнения команды: {ex.Message}", MessageType.Error));
                }
                
                return;
            }
        }
        
        // Команда не найдена
        if (message.StartsWith("\\glex"))
        {
            await SendMessageAsync(chatInfo.playerId,
                FormatMessage("Неизвестная команда. Используйте \\glex help", MessageType.Warning));
        }
    }
}
```

### 8.2 Notification System

```csharp
public class NotificationService
{
    private readonly IEmpyrionGateway _gateway;
    
    public async Task NotifyPlayersOnPlayfieldAsync(string playfield, string message, MessageType type = MessageType.Info)
    {
        var players = await _gateway.GetPlayersOnPlayfieldAsync(playfield);
        
        foreach (var player in players)
        {
            await SendNotificationAsync(player.entityId, message, type);
        }
    }
    
    public async Task SendNotificationAsync(int playerId, string message, MessageType type)
    {
        var formatted = FormatMessage(message, type);
        
        await _gateway.SendChatMessageAsync(new ChatInfo
        {
            playerId = playerId,
            msg = formatted,
            type = (byte)ChatType.Private
        });
    }
}
```