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
        /// Discovers available VISA-compliant instruments on the system.
        /// </summary>
        /// <returns>A collection of resource strings for available instruments.</returns>
        Task<IEnumerable<string>> DiscoverInstrumentsAsync();


        /// <summary>
        /// Sends a command to the instrument without expecting a response.
        /// </summary>
        /// <param name="command">The VISA command string to send (e.g., "*RST" for reset).</param>
        /// <returns>A task representing the asynchronous send operation.</returns>
        /// <exception cref="InvalidOperationException">Thrown if not connected to an instrument.</exception>
        Task SendCommandAsync(string command);

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
        /// Occurs when an error occurs during instrument communication.
        /// </summary>
        event EventHandler<InstrumentErrorEventArgs> Error;
    }

    /// <summary>
    /// Event arguments for instrument errors.
    /// </summary>
    /// <remarks>
    /// Initializes a new instance of the <see cref="InstrumentErrorEventArgs"/> class.
    /// </remarks>
    /// <param name="message">The error message.</param>
    /// <param name="errorCode">The VISA error code.</param>
    public class InstrumentErrorEventArgs(string message, int errorCode = 0) : EventArgs
    {
        /// <summary>
        /// Gets the error message.
        /// </summary>
        public string Message { get; set; } = message;

        /// <summary>
        /// Gets the error code returned by the VISA library.
        /// </summary>
        public int ErrorCode { get; set; } = errorCode;
    }
}
