using System;
using System.IO;
using System.Runtime.CompilerServices;

namespace SMU_Revamp.Tests
{
    internal static class TestEnvironment
    {
        [ModuleInitializer]
        internal static void Initialize()
        {
            var isolatedDir = Path.Combine(Path.GetTempPath(), "SMU_Revamp_TestConfig", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(isolatedDir);
            Environment.SetEnvironmentVariable("SMU_REVAMP_CONFIG_DIR", isolatedDir);
        }
    }
}
