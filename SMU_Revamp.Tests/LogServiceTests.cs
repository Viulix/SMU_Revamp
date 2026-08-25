using System;
using System.IO;
using SMU_Revamp.Services;
using Xunit;

namespace SMU_Revamp.Tests
{
    public class LogServiceTests : IDisposable
    {
        private readonly string _tempDir;

        public LogServiceTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "smu_log_tests_" + Guid.NewGuid().ToString("N"));
            LogService.Instance.SetLogDirectory(_tempDir);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
            }
            catch { /* best effort */ }
        }

        [Fact]
        public void Write_CreatesDailyFileWithLevelAndMessage()
        {
            var log = LogService.Instance;
            log.Warning("something odd happened");

            string expectedFile = Path.Combine(_tempDir, $"smu_{DateTime.Now:yyyyMMdd}.log");
            Assert.True(File.Exists(expectedFile), $"Expected log file at {expectedFile}");

            string content = File.ReadAllText(expectedFile);
            Assert.Contains("[WARNING]", content);
            Assert.Contains("something odd happened", content);
            // Timestamp prefix present
            Assert.Contains(DateTime.Now.ToString("yyyy-MM-dd"), content);
        }

        [Fact]
        public void Session_WritesDistinctMarker()
        {
            var log = LogService.Instance;
            log.Session("My Session");

            string content = File.ReadAllText(Path.Combine(_tempDir, $"smu_{DateTime.Now:yyyyMMdd}.log"));
            Assert.Contains("=== My Session", content);
            Assert.Contains("===================================================", content);
        }

        [Fact]
        public void Error_WithException_IncludesMessage()
        {
            var log = LogService.Instance;
            try
            {
                throw new InvalidOperationException("boom boom");
            }
            catch (Exception ex)
            {
                log.Error("op failed", ex);
            }

            string content = File.ReadAllText(Path.Combine(_tempDir, $"smu_{DateTime.Now:yyyyMMdd}.log"));
            Assert.Contains("[ERROR]", content);
            Assert.Contains("op failed", content);
            Assert.Contains("boom boom", content);
        }

        [Fact]
        public void Truncate_ShortensLongStringsAndKeepsSingleLine()
        {
            Assert.Equal(string.Empty, LogService.Truncate(null));
            Assert.Equal(string.Empty, LogService.Truncate(""));
            Assert.Equal("abc", LogService.Truncate("abc"));

            string longLine = new('x', 500);
            string truncated = LogService.Truncate(longLine, 100);
            Assert.Equal(100 + "... (+400 chars)".Length, truncated.Length);
            Assert.EndsWith("... (+400 chars)", truncated);

            string multiLine = LogService.Truncate("a\r\nb");
            Assert.DoesNotContain("\r", multiLine);
            Assert.DoesNotContain("\n", multiLine);
            Assert.Contains("\\r\\n", multiLine);
        }

        [Fact]
        public void InstallConsoleTee_CapturesConsoleLinesIntoLog()
        {
            var log = LogService.Instance;
            var originalOut = Console.Out;
            try
            {
                log.InstallConsoleTee();
                Assert.True(log.IsConsoleTeeInstalled, "Console tee was not installed.");

                Console.WriteLine("tee capture probe 12345");

                string content = File.ReadAllText(Path.Combine(_tempDir, $"smu_{DateTime.Now:yyyyMMdd}.log"));
                Assert.Contains("tee capture probe 12345", content);
            }
            finally
            {
                Console.SetOut(originalOut);
            }
        }
    }
}
