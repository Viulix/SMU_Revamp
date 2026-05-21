using System.Threading.Tasks;

namespace SMU_Revamp.Services
{
    /// <summary>
    /// Switch matrix specific operations extracted from the legacy VB module.
    /// This keeps routing logic separate from low-level VISA handling.
    /// </summary>
    public interface ISwitchMatrixService
    {
        /// <summary>
        /// Reads human-readable connection/card information from the matrix.
        /// Mirrors the legacy "read_connection" behavior.
        /// </summary>
        /// <returns>A concatenated string with card descriptions.</returns>
        Task<string> ReadConnectionAsync();

        /// <summary>
        /// Creates the requested connection on the switch matrix.
        /// Mirrors the legacy "create_connection" behavior.
        /// </summary>
        /// <param name="x">First endpoint identifier (format may vary).</param>
        /// <param name="y">Second endpoint identifier (format may vary).</param>
        /// <param name="overrideCheck">If true, perform the routing; otherwise skip (legacy override flag).</param>
        /// <returns>The channel string used for the connection (for logging).</returns>
        Task<string> CreateConnectionAsync(object x, object y, bool overrideCheck = false);
    }
}
