using System.Threading.Tasks;

namespace SMU_Revamp.Services
{
    /// <summary>
    /// Abstraction for the prober/stager controller.
    /// Keeps the legacy GPIB command set behind a testable interface.
    /// </summary>
    public interface IProberService
    {
        /// <summary>
        /// Gets or sets whether the prober should be switched into quiet motor mode after moves.
        /// </summary>
        bool QuietMode { get; set; }

        /// <summary>
        /// Aligns the chuck by sending the legacy separation command.
        /// </summary>
        Task ProberAlignAsync();

        /// <summary>
        /// Puts the chuck into contact position by sending the legacy contact command.
        /// </summary>
        Task ProberContactAsync();

        /// <summary>
        /// Moves the chuck relative by the given X/Y values using the legacy command format.
        /// </summary>
        Task<string> MoveProberAsync(double x, double y);

        /// <summary>
        /// Moves the chuck in Z using the legacy command format.
        /// </summary>
        Task<string> MoveProberZAsync(double z);

        /// <summary>
        /// Moves the chuck to an absolute X/Y position using the legacy H mode.
        /// </summary>
        Task<string> MoveProberAbsoluteAsync(double x, double y);

        /// <summary>
        /// Moves the chuck using the legacy Z mode variant.
        /// </summary>
        Task<string> MoveProberAbsAsync(double x, double y);

        /// <summary>
        /// Sends an arbitrary legacy prober command to the device.
        /// </summary>
        Task<string> SendProberAsync(string command);

        /// <summary>
        /// Sets the chuck home position.
        /// </summary>
        Task ProberSetHomeAsync();

        /// <summary>
        /// Returns the chuck to home position.
        /// </summary>
        Task ProberGoHomeAsync();

        /// <summary>
        /// Executes the legacy next-contact sequencing logic.
        /// Returns the updated contact number.
        /// </summary>
        int NextContact(string cellPosition, int contactNumber, bool combSputtering, bool hugeDeltaA, bool hugeDeltaB);

        /// <summary>
        /// Converts an absolute contact index to the legacy relative row/subrow format.
        /// </summary>
        string[] AbsToRel(int absZeile);

        /// <summary>
        /// Converts a legacy relative row/subrow position back to an absolute contact index.
        /// </summary>
        int RelToAbs(int pos, int subpos);
    }
}
