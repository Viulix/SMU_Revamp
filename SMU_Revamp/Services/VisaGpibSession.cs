using System;
using System.Threading.Tasks;
using Ivi.Visa;

namespace SMU_Revamp.Services
{
    /// <summary>
    /// A simple IGpibSession implementation using modern IVI VISA.NET APIs.
    /// Wraps low-level VISA COM calls to make higher-level services testable.
    /// </summary>
    public class VisaGpibSession : IGpibSession, IDisposable
    {
        private IMessageBasedSession? _session;
        private int _timeout = 5000;

        public int Timeout
        {
            get => _timeout;
            set
            {
                _timeout = value;
                try
                {
                    _session?.TimeoutMilliseconds = _timeout;
                }
                catch { }
            }
        }

        public VisaGpibSession() { }

        public Task OpenAsync(string resourceString, int timeoutMs)
        {
            return Task.Run(() =>
            {
                _timeout = timeoutMs;

                if (!VisaRuntimeGuard.TryEnsureAvailable(out var runtimeError))
                {
                    throw new InvalidOperationException(runtimeError);
                }

                try
                {
                    // Open a VISA session via the global resource manager and require message-based I/O.
                    _session = GlobalResourceManager.Open(resourceString, AccessModes.None, _timeout) as IMessageBasedSession;
                    if (_session == null)
                    {
                        throw new InvalidOperationException(
                            $"Resource '{resourceString}' is not a message-based VISA session.");
                    }

                    try { _session.TimeoutMilliseconds = _timeout; } catch { }
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException(
                        "Unable to open VISA session. Ensure a vendor VISA runtime is installed (for example NI-VISA or Keysight IO Libraries), " +
                        "verify the resource string, and confirm the instrument is reachable.",
                        ex);
                }
            });
        }

        public Task CloseAsync()
        {
            return Task.Run(() =>
            {
                if (_session != null)
                {
                    try { _session.Dispose(); } catch { }
                    _session = null;
                }
            });
        }

        public Task WriteAsync(string command)
        {
            return Task.Run(() =>
            {
                if (_session == null)
                    throw new InvalidOperationException("Session not open.");

                _session.RawIO.Write(command);
            });
        }

        public Task<string> ReadAsync(int maxChars)
        {
            return Task.Run(() =>
            {
                if (_session == null)
                    throw new InvalidOperationException("Session not open.");

                return _session.RawIO.ReadString(maxChars) ?? string.Empty;
            });
        }

        public Task<byte[]> ReadBytesAsync(int count)
        {
            return Task.Run(() =>
            {
                if (_session == null)
                    throw new InvalidOperationException("Session not open.");

                return _session.RawIO.Read(count);
            });
        }

        public Task WriteBytesAsync(byte[] data)
        {
            return Task.Run(() =>
            {
                if (_session == null)
                    throw new InvalidOperationException("Session not open.");

                _session.RawIO.Write(data);
            });
        }

        public Task ClearAsync()
        {
            return Task.Run(() =>
            {
                if (_session == null)
                    throw new InvalidOperationException("Session not open.");

                try { _session.Clear(); } catch { }
            });
        }

        public void Dispose()
        {
            try { _session?.Dispose(); } catch { }
            _session = null;
        }
    }
}
