using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using GalacticExpansion.Core.State;
using GalacticExpansion.Models;
using Xunit;

namespace GalacticExpansion.Tests.Unit.State
{
    /// <summary>
    /// Unit-тесты для StateStore.
    /// Проверяют корректность сохранения, загрузки, бэкапов и восстановления.
    /// </summary>
    public class StateStoreTests : IDisposable
    {
        private readonly string _testDataPath;
        private readonly StateStore _stateStore;

        public StateStoreTests()
        {
            // Создаем временную папку для тестов
            _testDataPath = Path.Combine(Path.GetTempPath(), $"GLEX_Test_{Guid.NewGuid()}");
            Directory.CreateDirectory(_testDataPath);
            _stateStore = new StateStore(_testDataPath);
        }

        public void Dispose()
        {
            // Очищаем тестовые данные
            if (Directory.Exists(_testDataPath))
            {
                Directory.Delete(_testDataPath, recursive: true);
            }
        }

        [Fact]
        public async Task LoadAsync_CreatesNewStateWhenFileDoesNotExist()
        {
            // Act
            var state = await _stateStore.LoadAsync();

            // Assert
            Assert.NotNull(state);
            Assert.Equal(1, state.Version);
            Assert.Empty(state.Colonies);
            Assert.Empty(state.Playfields);
        }

        [Fact]
        public async Task SaveAsync_CreatesFileSuccessfully()
        {
            // Arrange
            var state = new SimulationState();
            state.Colonies.Add(new Colony("Akua", 2, new Vector3(1000, 150, -500)));

            // Act
            await _stateStore.SaveAsync(state);

            // Assert
            Assert.True(File.Exists(_stateStore.StatePath));
        }

        [Fact]
        public async Task LoadAsync_LoadsSavedStateCorrectly()
        {
            // Arrange
            var originalState = new SimulationState();
            var colony = new Colony("Akua", 2, new Vector3(1000, 150, -500))
            {
                Stage = ColonyStage.BaseL2,
                ThreatLevel = 3
            };
            colony.Resources.VirtualResources = 5000;
            colony.Resources.ProductionRate = 200;
            originalState.AddColony(colony);

            await _stateStore.SaveAsync(originalState);

            // Act
            var loadedState = await _stateStore.LoadAsync();

            // Assert
            Assert.NotNull(loadedState);
            Assert.Single(loadedState.Colonies);
            
            var loadedColony = loadedState.Colonies[0];
            Assert.Equal("Akua", loadedColony.Playfield);
            Assert.Equal(2, loadedColony.FactionId);
            Assert.Equal(ColonyStage.BaseL2, loadedColony.Stage);
            Assert.Equal(3, loadedColony.ThreatLevel);
            Assert.Equal(5000, loadedColony.Resources.VirtualResources);
            Assert.Equal(200, loadedColony.Resources.ProductionRate);
        }

        [Fact]
        public async Task CreateBackupAsync_CreatesBackupFile()
        {
            // Arrange
            var state = new SimulationState();
            state.Colonies.Add(new Colony("Akua", 2, new Vector3(1000, 150, -500)));
            await _stateStore.SaveAsync(state);

            // Act
            await _stateStore.CreateBackupAsync();

            // Assert
            var backupFiles = Directory.GetFiles(_stateStore.BackupPath, "state_backup_*.json");
            Assert.NotEmpty(backupFiles);
        }

        [Fact]
        public async Task RestoreFromBackupAsync_RestoresLatestBackup()
        {
            // Arrange
            var state1 = new SimulationState();
            state1.Colonies.Add(new Colony("Akua", 2, new Vector3(1000, 150, -500)));
            await _stateStore.SaveAsync(state1);
            await _stateStore.CreateBackupAsync();

            // Ждем немного, чтобы timestamp был другим
            await Task.Delay(1100);

            var state2 = new SimulationState();
            state2.Colonies.Add(new Colony("Omicron", 2, new Vector3(2000, 200, -1000)));
            await _stateStore.SaveAsync(state2);
            await _stateStore.CreateBackupAsync();

            // Act
            var restoredState = await _stateStore.RestoreFromBackupAsync();

            // Assert
            Assert.NotNull(restoredState);
            Assert.Single(restoredState.Colonies);
            // Должна восстановиться последняя версия (state2)
            Assert.Equal("Omicron", restoredState.Colonies[0].Playfield);
        }

        [Fact]
        public async Task CleanupOldBackupsAsync_KeepsOnlyRecentBackups()
        {
            // Arrange
            var state = new SimulationState();
            await _stateStore.SaveAsync(state);

            // Создаем несколько бэкапов
            for (int i = 0; i < 5; i++)
            {
                await _stateStore.CreateBackupAsync();
                await Task.Delay(100); // Небольшая задержка для разных timestamp
            }

            // Act
            await _stateStore.CleanupOldBackupsAsync(keepCount: 3);

            // Assert
            var backupFiles = Directory.GetFiles(_stateStore.BackupPath, "state_backup_*.json");
            Assert.Equal(3, backupFiles.Length);
        }

        [Fact]
        public async Task SaveAsync_IsAtomic_NoPartialFiles()
        {
            // Arrange
            var state = new SimulationState();
            for (int i = 0; i < 100; i++)
            {
                state.Colonies.Add(new Colony($"Playfield{i}", 2, new Vector3(i * 1000, 150, -500)));
            }

            // Act
            await _stateStore.SaveAsync(state);

            // Assert
            // Проверяем, что временный файл не остался
            var tempFile = _stateStore.StatePath + ".tmp";
            Assert.False(File.Exists(tempFile), "Temporary file should not exist after save");

            // Проверяем, что основной файл существует и валиден
            Assert.True(File.Exists(_stateStore.StatePath));
            var loadedState = await _stateStore.LoadAsync();
            Assert.Equal(100, loadedState.Colonies.Count);
        }
    }
}
