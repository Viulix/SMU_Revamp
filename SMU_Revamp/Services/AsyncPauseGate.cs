using System;
using System.Threading;
using System.Threading.Tasks;

namespace SMU_Revamp.Services
{
    /// <summary>
    /// Cooperative pause gate for long-running loops (e.g. wafer scans).
    ///
    /// Callers await <see cref="WaitAsync"/> at safe points between work items.
    /// While paused the call blocks until <see cref="Resume"/> is invoked or the
    /// cancellation token fires (then an OperationCanceledException is thrown,
    /// which also works while paused - a stop request must never deadlock).
    /// </summary>
    public sealed class AsyncPauseGate
    {
        private TaskCompletionSource? _resume;

        public bool IsPaused { get; private set; }

        public void Pause()
        {
            if (IsPaused) return;
            IsPaused = true;
            _resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public void Resume()
        {
            if (!IsPaused) return;
            IsPaused = false;
            _resume?.TrySetResult();
            _resume = null;
        }

        /// <summary>
        /// Blocks while paused. Returns the waited duration in milliseconds
        /// (0 when the gate was not paused).
        /// </summary>
        public async Task<double> WaitAsync(CancellationToken cancellationToken)
        {
            var resume = _resume;
            if (!IsPaused || resume == null) return 0;

            long startMs = Environment.TickCount64;

            var completed = await Task.WhenAny(
                resume.Task,
                Task.Delay(Timeout.Infinite, cancellationToken));

            cancellationToken.ThrowIfCancellationRequested();
            _ = completed;

            return Environment.TickCount64 - startMs;
        }
    }
}
