using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SMU_Revamp.Services
{
    /// <summary>
    /// Interface for instrument service providing basic VISA connectivity.
    /// Handles communication with VISA-compliant instruments over various connection types
    /// (USB, Ethernet, Serial, GPIB, etc.)
    /// </summary>
    public interface IInstrumentService : IDisposable
    {
        /// <summary>
        /// Gets a value indicating whether the service is currently connected to an instrument.
        /// </summary>
        bool IsConnected { get; }

        /// <summary>
        /// Gets the currently connected instrument resource string (e.g., "USB::0x1234::0x5678::SERIAL::INSTR").
        /// </summary>
        string CurrentResourceString { get; }

        /// <summary>
        /// Discovers available VISA-compliant instruments on the system.
        /// </summary>
        /// <returns>A collection of resource strings for available instruments.</returns>
        Task<IEnumerable<string>> DiscoverInstrumentsAsync();

        /// <summary>
        /// Connects to a VISA instrument using the specified resource string.
        /// </summary>
        /// <param name="resourceString">The VISA resource string identifying the instrument.</param>
        /// <returns>A task representing the asynchronous connection operation.</returns>
        /// <exception cref="InvalidOperationException">Thrown if already connected to an instrument.</exception>
        Task ConnectAsync(string resourceString);

        /// <summary>
        /// Disconnects from the currently connected instrument.
        /// </summary>
        /// <returns>A task representing the asynchronous disconnection operation.</returns>
        Task DisconnectAsync();

        /// <summary>
        /// Sends a command to the instrument without expecting a response.
        /// </summary>
        /// <param name="command">The VISA command string to send (e.g., "*RST" for reset).</param>
        /// <returns>A task representing the asynchronous send operation.</returns>
        /// <exception cref="InvalidOperationException">Thrown if not connected to an instrument.</exception>
        Task SendCommandAsync(string command);

        /// <summary>
        /// Sends a query command to the instrument and retrieves the response.
        /// </summary>
        /// <param name="query">The VISA query command string (e.g., "*IDN?" for identification).</param>
        /// <returns>The response string from the instrument.</returns>
        /// <exception cref="InvalidOperationException">Thrown if not connected to an instrument.</exception>
        Task<string> QueryAsync(string query);

        /// <summary>
        /// Sends raw binary data to the instrument.
        /// </summary>
        /// <param name="data">The binary data to send.</param>
        /// <returns>A task representing the asynchronous send operation.</returns>
        /// <exception cref="InvalidOperationException">Thrown if not connected to an instrument.</exception>
        Task SendRawDataAsync(byte[] data);

        /// <summary>
        /// Reads raw binary data from the instrument.
        /// </summary>
        /// <param name="bufferSize">The maximum number of bytes to read.</param>
        /// <returns>The binary data read from the instrument.</returns>
        /// <exception cref="InvalidOperationException">Thrown if not connected to an instrument.</exception>
        Task<byte[]> ReadRawDataAsync(int bufferSize);

        /// <summary>
        /// Clears the input and output buffers for the instrument connection.
        /// </summary>
        /// <returns>A task representing the asynchronous clear operation.</returns>
        /// <exception cref="InvalidOperationException">Thrown if not connected to an instrument.</exception>
        Task ClearBuffersAsync();

        /// <summary>
        /// Sets the timeout value for all instrument operations.
        /// </summary>
        /// <param name="timeoutMilliseconds">The timeout duration in milliseconds.</param>
        void SetTimeout(int timeoutMilliseconds);

        /// <summary>
        /// Gets the current timeout value for instrument operations.
        /// </summary>
        /// <returns>The timeout duration in milliseconds.</returns>
        int GetTimeout();

        /// <summary>
        /// Occurs when the instrument connection is established.
        /// </summary>
        event EventHandler Connected;

        /// <summary>
        /// Occurs when the instrument connection is closed.
        /// </summary>
        event EventHandler Disconnected;

        /// <summary>
        /// Occurs when an error occurs during instrument communication.
        /// </summary>
        event EventHandler<InstrumentErrorEventArgs> Error;
    }

    /// <summary>
    /// Event arguments for instrument errors.
    /// </summary>
    public class InstrumentErrorEventArgs : EventArgs
    {
        /// <summary>
        /// Gets the error message.
        /// </summary>
        public string Message { get; set; }

        /// <summary>
        /// Gets the error code returned by the VISA library.
        /// </summary>
        public int ErrorCode { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="InstrumentErrorEventArgs"/> class.
        /// </summary>
        /// <param name="message">The error message.</param>
        /// <param name="errorCode">The VISA error code.</param>
        public InstrumentErrorEventArgs(string message, int errorCode = 0)
        {
            Message = message;
            ErrorCode = errorCode;
        }
    }
}
