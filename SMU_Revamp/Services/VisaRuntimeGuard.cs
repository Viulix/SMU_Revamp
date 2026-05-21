using System;
using System.Runtime.InteropServices;

namespace SMU_Revamp.Services
{
    /// <summary>
    /// Lightweight guard that checks whether the native VISA conflict-manager library
    /// needed by Ivi.Visa is loadable on the current machine.
    /// </summary>
    internal static class VisaRuntimeGuard
    {
        private static readonly string[] CandidateLibraryNames =
        {
            "visaConfMgr.dll",
            "libvisaConfMgr.dll",
            "visaConfMgr.dll.dylib",
            "libvisaConfMgr.dll.dylib",
            "visaConfMgr.so",
            "libvisaConfMgr.so"
        };

        public static bool TryEnsureAvailable(out string error)
        {
            foreach (var name in CandidateLibraryNames)
            {
                if (NativeLibrary.TryLoad(name, out var handle))
                {
                    try
                    {
                        error = string.Empty;
                        return true;
                    }
                    finally
                    {
                        try { NativeLibrary.Free(handle); } catch { }
                    }
                }
            }

            error =
                "No compatible VISA runtime was found. Install a vendor VISA runtime (e.g. NI-VISA or Keysight IO Libraries) " +
                "and ensure native VISA libraries are available in the system library path.";
            return false;
        }
    }
}
