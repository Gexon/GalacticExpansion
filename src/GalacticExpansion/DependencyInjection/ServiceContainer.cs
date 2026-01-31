using System;
using System.Collections.Generic;

namespace GalacticExpansion.DependencyInjection
{
    /// <summary>
    /// Простой DI контейнер для управления зависимостями мода.
    /// Поддерживает регистрацию и разрешение сервисов (Singleton паттерн).
    /// 
    /// Примечание: Это упрощенная реализация DI без поддержки
    /// конструкторной инъекции и фабрик. Достаточно для наших нужд.
    /// </summary>
    public class ServiceContainer
    {
        private readonly Dictionary<Type, object> _services = new Dictionary<Type, object>();
        private readonly object _lockObject = new object();

        /// <summary>
        /// Регистрирует сервис в контейнере.
        /// Если сервис с таким типом уже зарегистрирован, он будет заменен.
        /// </summary>
        /// <typeparam name="TService">Тип сервиса (обычно интерфейс)</typeparam>
        /// <param name="instance">Экземпляр сервиса</param>
        public void Register<TService>(TService instance) where TService : class
        {
            if (instance == null)
                throw new ArgumentNullException(nameof(instance));

            lock (_lockObject)
            {
                var serviceType = typeof(TService);
                
                if (_services.ContainsKey(serviceType))
                {
                    // Заменяем существующий сервис
                    _services[serviceType] = instance;
                }
                else
                {
                    // Регистрируем новый сервис
                    _services.Add(serviceType, instance);
                }
            }
        }

        /// <summary>
        /// Разрешает (получает) сервис из контейнера.
        /// </summary>
        /// <typeparam name="TService">Тип сервиса</typeparam>
        /// <returns>Экземпляр сервиса</returns>
        /// <exception cref="InvalidOperationException">Если сервис не зарегистрирован</exception>
        public TService Resolve<TService>() where TService : class
        {
            lock (_lockObject)
            {
                var serviceType = typeof(TService);
                
                if (_services.TryGetValue(serviceType, out var service))
                {
                    return (TService)service;
                }

                throw new InvalidOperationException(
                    $"Service of type {serviceType.Name} is not registered in the container. " +
                    $"Make sure to call Register<{serviceType.Name}>() before resolving it.");
            }
        }

        /// <summary>
        /// Пытается разрешить сервис из контейнера.
        /// </summary>
        /// <typeparam name="TService">Тип сервиса</typeparam>
        /// <param name="service">Экземпляр сервиса (если найден)</param>
        /// <returns>true, если сервис найден; false в противном случае</returns>
        public bool TryResolve<TService>(out TService? service) where TService : class
        {
            lock (_lockObject)
            {
                var serviceType = typeof(TService);
                
                if (_services.TryGetValue(serviceType, out var serviceObj))
                {
                    service = (TService)serviceObj;
                    return true;
                }

                service = null;
                return false;
            }
        }

        /// <summary>
        /// Проверяет, зарегистрирован ли сервис
        /// </summary>
        public bool IsRegistered<TService>() where TService : class
        {
            lock (_lockObject)
            {
                return _services.ContainsKey(typeof(TService));
            }
        }

        /// <summary>
        /// Удаляет сервис из контейнера
        /// </summary>
        public bool Unregister<TService>() where TService : class
        {
            lock (_lockObject)
            {
                return _services.Remove(typeof(TService));
            }
        }

        /// <summary>
        /// Очищает все зарегистрированные сервисы
        /// </summary>
        public void Clear()
        {
            lock (_lockObject)
            {
                _services.Clear();
            }
        }

        /// <summary>
        /// Получает количество зарегистрированных сервисов
        /// </summary>
        public int Count
        {
            get
            {
                lock (_lockObject)
                {
                    return _services.Count;
                }
            }
        }
    }
}
