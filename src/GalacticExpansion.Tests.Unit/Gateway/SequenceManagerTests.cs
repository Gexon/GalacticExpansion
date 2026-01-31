using System;
using System.Threading.Tasks;
using GalacticExpansion.Core.Gateway;
using Xunit;

namespace GalacticExpansion.Tests.Unit.Gateway
{
    /// <summary>
    /// Unit-тесты для SequenceManager.
    /// Проверяют корректность генерации SeqNr, сопоставления запросов/ответов и обработки таймаутов.
    /// </summary>
    public class SequenceManagerTests
    {
        [Fact]
        public void GetNextSequence_ReturnsUniqueNumbers()
        {
            // Arrange
            var manager = new SequenceManager();

            // Act
            var seq1 = manager.GetNextSequence();
            var seq2 = manager.GetNextSequence();
            var seq3 = manager.GetNextSequence();

            // Assert
            Assert.NotEqual(seq1, seq2);
            Assert.NotEqual(seq2, seq3);
            Assert.NotEqual(seq1, seq3);
            Assert.True(seq1 > 0);
            Assert.True(seq2 > seq1);
            Assert.True(seq3 > seq2);
        }

        [Fact]
        public async Task RegisterResponse_CompletesSuccessfully()
        {
            // Arrange
            var manager = new SequenceManager();
            var seqNr = manager.GetNextSequence();
            var expectedData = "test response";

            // Act
            var responseTask = manager.RegisterResponse<string>(seqNr, timeoutMs: 5000);
            var completed = manager.CompleteResponse(seqNr, expectedData);
            var actualData = await responseTask;

            // Assert
            Assert.True(completed);
            Assert.Equal(expectedData, actualData);
        }

        [Fact]
        public async Task RegisterResponse_TimesOutWhenNoResponse()
        {
            // Arrange
            var manager = new SequenceManager();
            var seqNr = manager.GetNextSequence();

            // Act
            var responseTask = manager.RegisterResponse<string>(seqNr, timeoutMs: 100);

            // Assert
            await Assert.ThrowsAsync<TimeoutException>(() => responseTask);
        }

        [Fact]
        public void CompleteResponse_ReturnsFalseForUnknownSeqNr()
        {
            // Arrange
            var manager = new SequenceManager();
            ushort unknownSeqNr = 9999;

            // Act
            var completed = manager.CompleteResponse(unknownSeqNr, "test data");

            // Assert
            Assert.False(completed);
        }

        [Fact]
        public async Task CompleteWithError_ThrowsException()
        {
            // Arrange
            var manager = new SequenceManager();
            var seqNr = manager.GetNextSequence();
            var expectedException = new InvalidOperationException("Test error");

            // Act
            var responseTask = manager.RegisterResponse<string>(seqNr, timeoutMs: 5000);
            var completed = manager.CompleteWithError(seqNr, expectedException);

            // Assert
            Assert.True(completed);
            var actualException = await Assert.ThrowsAsync<InvalidOperationException>(() => responseTask);
            Assert.Equal("Test error", actualException.Message);
        }

        [Fact]
        public void CancelAll_CancelsAllPendingRequests()
        {
            // Arrange
            var manager = new SequenceManager();
            var seqNr1 = manager.GetNextSequence();
            var seqNr2 = manager.GetNextSequence();
            var seqNr3 = manager.GetNextSequence();

            var task1 = manager.RegisterResponse<string>(seqNr1, timeoutMs: 10000);
            var task2 = manager.RegisterResponse<string>(seqNr2, timeoutMs: 10000);
            var task3 = manager.RegisterResponse<string>(seqNr3, timeoutMs: 10000);

            // Act
            manager.CancelAll();

            // Assert
            Assert.ThrowsAsync<OperationCanceledException>(() => task1);
            Assert.ThrowsAsync<OperationCanceledException>(() => task2);
            Assert.ThrowsAsync<OperationCanceledException>(() => task3);
            Assert.Equal(0, manager.PendingCount);
        }

        [Fact]
        public void PendingCount_ReflectsCorrectCount()
        {
            // Arrange
            var manager = new SequenceManager();

            // Act & Assert
            Assert.Equal(0, manager.PendingCount);

            var seqNr1 = manager.GetNextSequence();
            manager.RegisterResponse<string>(seqNr1, timeoutMs: 10000);
            Assert.Equal(1, manager.PendingCount);

            var seqNr2 = manager.GetNextSequence();
            manager.RegisterResponse<string>(seqNr2, timeoutMs: 10000);
            Assert.Equal(2, manager.PendingCount);

            manager.CompleteResponse(seqNr1, "data");
            Assert.Equal(1, manager.PendingCount);

            manager.CompleteResponse(seqNr2, "data");
            Assert.Equal(0, manager.PendingCount);
        }
    }
}
