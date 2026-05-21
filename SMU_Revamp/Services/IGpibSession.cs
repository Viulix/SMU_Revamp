using System.Threading.Tasks;

namespace SMU_Revamp.Services
{
    /// <summary>
    /// Lightweight abstraction over a GPIB/VISA session used by higher-level services.
    /// Implementations can wrap Keysight/NI VISA objects and make testing easier.
    /// </summary>
    public interface IGpibSession
    {
        /// <summary>
        /// Opens a session to the specified resource string.
        /// </summary>
        Task OpenAsync(string resourceString, int timeoutMs);

        /// <summary>
        /// Closes the session and releases resources.
        /// </summary>
        Task CloseAsync();

        /// <summary>
        /// Writes a text command to the instrument.
        /// </summary>
        Task WriteAsync(string command);

        /// <summary>
        /// Reads text response (up to maxChars or until termination).
        /// </summary>
        Task<string> ReadAsync(int maxChars);

        /// <summary>
        /// Reads raw bytes from the instrument.
        /// </summary>
        Task<byte[]> ReadBytesAsync(int count);

        /// <summary>
        /// Write raw bytes to the instrument.
        /// </summary>
        Task WriteBytesAsync(byte[] data);

        /// <summary>
        /// Clear the instrument buffers / session.
        /// </summary>
        Task ClearAsync();

        /// <summary>
        /// Gets or sets the session timeout in milliseconds.
        /// </summary>
        int Timeout { get; set; }
    }
}
