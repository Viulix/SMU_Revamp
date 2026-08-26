using System;
using System.Threading;
using System.Threading.Tasks;
using SMU_Revamp.Services;
using Xunit;

namespace SMU_Revamp.Tests
{
    public class AsyncPauseGateTests
    {
        [Fact]
        public async Task WaitAsync_WhenNotPaused_ReturnsImmediatelyWithZero()
        {
            var gate = new AsyncPauseGate();

            double waited = await gate.WaitAsync(CancellationToken.None);

            Assert.Equal(0, waited);
            Assert.False(gate.IsPaused);
        }

        [Fact]
        public async Task WaitAsync_BlocksUntilResume()
        {
            var gate = new AsyncPauseGate();
            gate.Pause();
            Assert.True(gate.IsPaused);

            Task<double> waitTask = gate.WaitAsync(CancellationToken.None);

            // Give the wait task a moment to start blocking.
            await Task.Delay(50);
            Assert.False(waitTask.IsCompleted, "Wait should block while paused.");

            gate.Resume();
            double waited = await waitTask;

            Assert.False(gate.IsPaused);
            Assert.True(waited >= 40, $"Expected at least ~50 ms pause, got {waited} ms.");
        }

        [Fact]
        public async Task WaitAsync_CancelWhilePaused_ThrowsOperationCanceled()
        {
            var gate = new AsyncPauseGate();
            gate.Pause();

            using var cts = new CancellationTokenSource(80);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => gate.WaitAsync(cts.Token));

            // The stop path must not have silently resumed the gate; teardown
            // is the caller's job (mirrors the scan finally-block behaviour).
            Assert.True(gate.IsPaused);
        }

        [Fact]
        public void Resume_WithoutPause_IsNoOp()
        {
            var gate = new AsyncPauseGate();
            gate.Resume();
            Assert.False(gate.IsPaused);
        }

        [Fact]
        public void Pause_Twice_IsIdempotent()
        {
            var gate = new AsyncPauseGate();
            gate.Pause();
            gate.Pause();

            Assert.True(gate.IsPaused);

            // A single resume must release the wait (no stacked gates).
            Task<double> waitTask = gate.WaitAsync(CancellationToken.None);
            gate.Resume();
            var completed = waitTask.Wait(TimeSpan.FromSeconds(2));
            Assert.True(completed, "Single resume must release a doubly paused gate.");
        }

        [Fact]
        public async Task Resume_BeforeWait_DoesNotBlock()
        {
            var gate = new AsyncPauseGate();
            gate.Pause();
            gate.Resume();

            double waited = await gate.WaitAsync(CancellationToken.None);

            Assert.Equal(0, waited);
        }
    }
}
