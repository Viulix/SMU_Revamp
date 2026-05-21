using System;
using System.Threading.Tasks;

namespace SMU_Revamp.Services
{
    /// <summary>
    /// A simple IGpibSession implementation using NationalInstruments VISA APIs.
    /// Wraps low-level VISA calls to make higher-level services testable.
    /// </summary>
    public class VisaGpibSession : IGpibSession, IDisposable
    {
        private dynamic? _session;
        private int _timeout = 5000;

        public int Timeout
        {
            get => _timeout;
            set
            {
                _timeout = value;
                try
                {
                    if (_session != null)
                        _session.Timeout = _timeout;
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
                    // Load NationalInstruments.Visa dynamically to open a session
                    var visaAssembly = System.Reflection.Assembly.Load("NationalInstruments.Visa");

                    // Use reflection to access GlobalResourceManager
                    var globalRmType = visaAssembly.GetType("NationalInstruments.Visa.GlobalResourceManager");
                    if (globalRmType == null)
                        throw new InvalidOperationException("Could not load NationalInstruments.Visa.GlobalResourceManager");

                    var accessModesType = visaAssembly.GetType("NationalInstruments.Visa.AccessModes");
                    if (accessModesType == null)
                        throw new InvalidOperationException("Could not load NationalInstruments.Visa.AccessModes");

                    var noneValue = System.Enum.Parse(accessModesType, "None");

                    var openMethod = globalRmType.GetMethod("Open", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static, null, new Type[] { typeof(string), accessModesType, typeof(int) }, null);
                    if (openMethod == null)
                        throw new InvalidOperationException("Could not find Open method on GlobalResourceManager");

                    _session = openMethod.Invoke(null, new object[] { resourceString, noneValue, _timeout });

                    if (_session == null)
                    {
                        throw new InvalidOperationException(
                            $"Resource '{resourceString}' could not be opened or is not a message-based VISA session.");
                    }

                    // Set timeout on the opened session
                    try
                    {
                        var timeoutProp = _session.GetType().GetProperty("Timeout");
                        if (timeoutProp != null)
                            timeoutProp.SetValue(_session, _timeout);
                    }
                    catch { }
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
                    try
                    {
                        var closeMethod = _session.GetType().GetMethod("Close");
                        if (closeMethod != null)
                            closeMethod.Invoke(_session, null);
                        else
                            (_session as IDisposable)?.Dispose();
                    }
                    catch { }
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

                try
                {
                    var writeMethod = _session.GetType().GetMethod("Write", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.IgnoreCase, null, new[] { typeof(string) }, null);
                    if (writeMethod != null)
                        writeMethod.Invoke(_session, new object[] { command });
                    else
                    {
                        // Try RawIO.Write
                        var rawIO = _session.GetType().GetProperty("RawIO")?.GetValue(_session);
                        if (rawIO != null)
                            rawIO.GetType().GetMethod("Write").Invoke(rawIO, new object[] { command });
                    }
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"Write failed: {ex.Message}", ex);
                }
            });
        }

        public Task<string> ReadAsync(int maxChars)
        {
            return Task.Run(() =>
            {
                if (_session == null)
                    throw new InvalidOperationException("Session not open.");

                try
                {
                    var readMethod = _session.GetType().GetMethod("Read", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.IgnoreCase, null, new[] { typeof(int) }, null);
                    if (readMethod != null)
                        return (string)readMethod.Invoke(_session, new object[] { maxChars }) ?? string.Empty;
                    else
                    {
                        // Try RawIO.ReadString
                        var rawIO = _session.GetType().GetProperty("RawIO")?.GetValue(_session);
                        if (rawIO != null)
                            return (string)rawIO.GetType().GetMethod("ReadString").Invoke(rawIO, new object[] { maxChars }) ?? string.Empty;
                    }
                    return string.Empty;
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"Read failed: {ex.Message}", ex);
                }
            });
        }

        public Task<byte[]> ReadBytesAsync(int count)
        {
            return Task.Run(() =>
            {
                if (_session == null)
                    throw new InvalidOperationException("Session not open.");

                try
                {
                    var rawIO = _session.GetType().GetProperty("RawIO")?.GetValue(_session);
                    if (rawIO != null)
                    {
                        var result = rawIO.GetType().GetMethod("Read").Invoke(rawIO, new object[] { count });
                        return (byte[])result;
                    }
                    return new byte[0];
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"Read bytes failed: {ex.Message}", ex);
                }
            });
        }

        public Task WriteBytesAsync(byte[] data)
        {
            return Task.Run(() =>
            {
                if (_session == null)
                    throw new InvalidOperationException("Session not open.");

                try
                {
                    var rawIO = _session.GetType().GetProperty("RawIO")?.GetValue(_session);
                    if (rawIO != null)
                        rawIO.GetType().GetMethod("Write").Invoke(rawIO, new object[] { data });
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"Write bytes failed: {ex.Message}", ex);
                }
            });
        }

        public Task ClearAsync()
        {
            return Task.Run(() =>
            {
                if (_session == null)
                    throw new InvalidOperationException("Session not open.");

                try
                {
                    var clearMethod = _session.GetType().GetMethod("Clear");
                    if (clearMethod != null)
                        clearMethod.Invoke(_session, null);
                }
                catch { }
            });
        }

        public void Dispose()
        {
            try
            {
                var closeMethod = _session?.GetType().GetMethod("Close");
                if (closeMethod != null)
                    closeMethod.Invoke(_session, null);
                else
                    (_session as IDisposable)?.Dispose();
            }
            catch { }
            _session = null;
        }
    }
}
