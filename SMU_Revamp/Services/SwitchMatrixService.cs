using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
namespace SMU_Revamp.Services
{
    /// <summary>
    /// Service implementing basic VISA connectivity for a switch matrix instrument.
    /// Translates functionality from the provided Visual Basic switchmatrix module.
    /// Singleton instance: access via SwitchMatrixService.Instance.
    /// </summary>
    public sealed class SwitchMatrixService : IInstrumentService, ISwitchMatrixService
    {
        private static readonly Lazy<SwitchMatrixService> _instance = new(() => new SwitchMatrixService());

        /// <summary>
        /// Gets the singleton instance.
        /// </summary>
        public static SwitchMatrixService Instance => _instance.Value;

        private readonly IGpibSession _gpibSession;
        private bool _isConnected = false;
        private int _timeoutMilliseconds = 5000;

        /// <summary>
        /// Default resource string used by the VB example.
        /// </summary>
        public const string DefaultResource = "GPIB0::23::INSTR";

        public bool IsConnected => _isConnected;

        public string CurrentResourceString { get; private set; } = string.Empty;

        public event EventHandler? Connected;
        public event EventHandler? Disconnected;
        public event EventHandler<InstrumentErrorEventArgs>? Error;

        private SwitchMatrixService()
        {
            _gpibSession = CreateSession();
        }

        private IGpibSession CreateSession()
        {
            return new VisaGpibSession();
        }

        /// <summary>
        /// Discover instruments using a simple query. This implementation returns an empty list
        /// if discovery is not supported by the local VISA provider.
        /// </summary>
        public Task<IEnumerable<string>> DiscoverInstrumentsAsync()
        {
            // Simple discovery fallback: return the default resource used in legacy code.
            return Task.FromResult<IEnumerable<string>>(new[] { DefaultResource });
        }

        /// <summary>
        /// Connects to the given VISA resource string.
        /// </summary>
        public async Task ConnectAsync(string resourceString)
        {
            if (IsConnected)
                throw new InvalidOperationException("Already connected to an instrument.");

            try
            {
                await _gpibSession.OpenAsync(resourceString, _timeoutMilliseconds).ConfigureAwait(false);
                CurrentResourceString = resourceString;
                _isConnected = true;
                Connected?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                Error?.Invoke(this, new InstrumentErrorEventArgs(ex.Message));
                throw;
            }
        }

        /// <summary>
        /// Disconnects from the current instrument and releases the session.
        /// </summary>
        public async Task DisconnectAsync()
        {
            try
            {
                await _gpibSession.CloseAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Error?.Invoke(this, new InstrumentErrorEventArgs(ex.Message));
            }
            finally
            {
                _isConnected = false;
                CurrentResourceString = string.Empty;
                Disconnected?.Invoke(this, EventArgs.Empty);
            }
        }

        public void Dispose()
        {
            try { (_gpibSession as IDisposable)?.Dispose(); } catch { }
        }

        /// <summary>
        /// Send a command that does not expect a response.
        /// </summary>
        public async Task SendCommandAsync(string command)
        {
            if (!IsConnected)
                throw new InvalidOperationException("Not connected to an instrument.");

            try
            {
                await _gpibSession.WriteAsync(command + "\n").ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Error?.Invoke(this, new InstrumentErrorEventArgs(ex.Message));
                throw;
            }
        }

        /// <summary>
        /// Send a query and read the response.
        /// </summary>
        public async Task<string> QueryAsync(string query)
        {
            if (!IsConnected)
                throw new InvalidOperationException("Not connected to an instrument.");

            try
            {
                await _gpibSession.WriteAsync(query + "\n").ConfigureAwait(false);
                var resp = await _gpibSession.ReadAsync(256).ConfigureAwait(false);
                return resp;
            }
            catch (Exception ex)
            {
                Error?.Invoke(this, new InstrumentErrorEventArgs(ex.Message));
                throw;
            }
        }

        /// <summary>
        /// Send raw binary data to instrument.
        /// </summary>
        public async Task SendRawDataAsync(byte[] data)
        {
            if (!IsConnected)
                throw new InvalidOperationException("Not connected to an instrument.");

            try
            {
                await _gpibSession.WriteBytesAsync(data).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Error?.Invoke(this, new InstrumentErrorEventArgs(ex.Message));
                throw;
            }
        }

        /// <summary>
        /// Read raw binary data from instrument up to bufferSize.
        /// </summary>
        public async Task<byte[]> ReadRawDataAsync(int bufferSize)
        {
            if (!IsConnected)
                throw new InvalidOperationException("Not connected to an instrument.");

            try
            {
                return await _gpibSession.ReadBytesAsync(bufferSize).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Error?.Invoke(this, new InstrumentErrorEventArgs(ex.Message));
                throw;
            }
        }

        /// <summary>
        /// Clear instrument buffers.
        /// </summary>
        public async Task ClearBuffersAsync()
        {
            if (!IsConnected)
                throw new InvalidOperationException("Not connected to an instrument.");

            try
            {
                await _gpibSession.ClearAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Error?.Invoke(this, new InstrumentErrorEventArgs(ex.Message));
                throw;
            }
        }

        public void SetTimeout(int timeoutMilliseconds)
        {
            _timeoutMilliseconds = timeoutMilliseconds;
            try
            {
                _gpibSession.Timeout = timeoutMilliseconds;
            }
            catch { }
        }

        public int GetTimeout() => _timeoutMilliseconds;

        // --- Methods translated from the VB module ---

        /// <summary>
        /// Reads the connection/card descriptions from the switch matrix and returns them concatenated.
        /// Mirrors the VB <c>read_connection</c> behavior.
        /// </summary>
        public async Task<string> ReadConnectionAsync()
        {
            try
            {
                var session = CreateSession();
                try
                {
                    await session.OpenAsync(DefaultResource, _timeoutMilliseconds).ConfigureAwait(false);
                    await session.WriteAsync(":CLOS:CARD? 1\n").ConfigureAwait(false);
                    var t1 = await session.ReadAsync(50).ConfigureAwait(false);

                    await session.WriteAsync(":CLOS:CARD? 2\n").ConfigureAwait(false);
                    var t2 = await session.ReadAsync(50).ConfigureAwait(false);

                    return (t1 ?? string.Empty).Trim() + " " + (t2 ?? string.Empty).Trim();
                }
                finally
                {
                    try { await session.CloseAsync().ConfigureAwait(false); } catch { }
                }
            }
            catch (Exception ex)
            {
                Error?.Invoke(this, new InstrumentErrorEventArgs(ex.Message));
                throw;
            }
        }

        /// <summary>
        /// Creates a connection on the switch matrix similar to the VB <c>create_connection</c>.
        /// If <paramref name="overrideCheck"/> is true the method will perform the connection; otherwise it will do nothing.
        /// Returns the channel string that was used (for logging/verification).
        /// </summary>
        public async Task<string> CreateConnectionAsync(object x, object y, bool overrideCheck = false)
        {
            if (!overrideCheck)
                return string.Empty;

            try
            {
                string xs = x?.ToString() ?? string.Empty;
                string ys = y?.ToString() ?? string.Empty;

                string channelstring;
                if (xs.Length > 4)
                    channelstring = "@" + xs + ", " + ys;
                else
                    channelstring = "@1" + xs + ", 1" + ys;

                var session = CreateSession();
                try
                {
                    await session.OpenAsync(DefaultResource, _timeoutMilliseconds).ConfigureAwait(false);
                    Thread.Sleep(10);
                    await session.WriteAsync("*RST\n").ConfigureAwait(false);
                    await session.WriteAsync(":ROUT:CONN:RULE ALL,FREE\n").ConfigureAwait(false);
                    await session.WriteAsync(":ROUT:CONN:SEQ ALL,BBM\n").ConfigureAwait(false);

                    await session.WriteAsync(":ROUT:CLOSE (" + channelstring + ")\n").ConfigureAwait(false);
                    Thread.Sleep(5);
                    await session.WriteAsync("*OPC?\n").ConfigureAwait(false);
                    await session.ReadAsync(10).ConfigureAwait(false);
                    Thread.Sleep(5);
                    return channelstring;
                }
                finally
                {
                    try { await session.CloseAsync().ConfigureAwait(false); } catch { }
                }
            }
            catch (Exception ex)
            {
                Error?.Invoke(this, new InstrumentErrorEventArgs(ex.Message));
                throw;
            }
        }
    }
}
