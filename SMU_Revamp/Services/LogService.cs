using System;
using System.IO;
using System.Text;

namespace SMU_Revamp.Services
{
    public enum LogLevel
    {
        Debug,
        Info,
        Warning,
        Error
    }

    /// <summary>
    /// File-based application logger with daily rolling log files.
    ///
    /// Logs are written to "%AppData%/SMU_Revamp/logs/smu_yyyyMMdd.log".
    /// Logging must never throw and must never block the caller noticeably;
    /// all failures are swallowed after a best-effort debug trace.
    ///
    /// InstallConsoleTee() additionally mirrors everything written to the
    /// console into the log, which captures output from measurement plans
    /// and services without requiring changes in those call sites.
    /// </summary>
    public sealed class LogService
    {
        private static readonly Lazy<LogService> _instance = new(() => new LogService());

        public static LogService Instance => _instance.Value;

        public const int DefaultMaxLineLength = 240;

        private readonly object _gate = new();
        private string _logDirectory;
        private string _currentFileName = string.Empty;
        private bool _consoleTeeInstalled;

        private LogService()
        {
            _logDirectory = ComputeDefaultLogDirectory();
        }

        /// <summary>
        /// Fired for every written entry. Used e.g. to surface compliance
        /// warnings in the UI. Handlers must marshal to the UI thread themselves.
        /// </summary>
        public event Action<LogLevel, string>? EntryLogged;

        /// <summary>Gets the directory the log files are written to.</summary>
        public string LogDirectory => _logDirectory;

        /// <summary>Whether console output is currently mirrored into the log.</summary>
        public bool IsConsoleTeeInstalled => _consoleTeeInstalled;

        /// <summary>Gets the log file currently being written.</summary>
        public string CurrentLogFile => Path.Combine(_logDirectory, _currentFileName);

        /// <summary>
        /// Overrides the log directory (used by tests and future settings).
        /// </summary>
        public void SetLogDirectory(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory)) return;
            lock (_gate)
            {
                _logDirectory = directory;
                _currentFileName = string.Empty;
            }
        }

        public void Debug(string message) => Write(LogLevel.Debug, message);
        public void Info(string message) => Write(LogLevel.Info, message);
        public void Warning(string message) => Write(LogLevel.Warning, message);
        public void Error(string message) => Write(LogLevel.Error, message);
        public void Error(string message, Exception ex) => Write(LogLevel.Error, $"{message}: {ex}");

        /// <summary>Writes a visually distinct session marker into the log.</summary>
        public void Session(string title)
        {
            Write(LogLevel.Info, "===================================================");
            Write(LogLevel.Info, $"=== {title}");
            Write(LogLevel.Info, "===================================================");
        }

        public void Write(LogLevel level, string message)
        {
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level.ToString().ToUpperInvariant()}] {message}";
            try
            {
                lock (_gate)
                {
                    Directory.CreateDirectory(_logDirectory);
                    string fileName = $"smu_{DateTime.Now:yyyyMMdd}.log";
                    if (fileName != _currentFileName)
                    {
                        _currentFileName = fileName;
                    }
                    File.AppendAllText(CurrentLogFile, line + Environment.NewLine);
                }
            }
            catch (Exception ex)
            {
                // Logging must never take the app down.
                System.Diagnostics.Debug.WriteLine($"[LogService] Write failed: {ex.Message}");
            }

            try
            {
                EntryLogged?.Invoke(level, message);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LogService] EntryLogged handler failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Mirrors all console output into the log so existing
        /// Console.WriteLine diagnostics (measurement plans etc.) are captured.
        /// </summary>
        public void InstallConsoleTee()
        {
            if (_consoleTeeInstalled) return;
            try
            {
                // Note: Console.Out returns a synchronized wrapper, so installed
                // state is tracked with a flag rather than a type check.
                Console.SetOut(new LogConsoleTeeWriter(Console.Out, this));
                Console.SetError(new LogConsoleTeeWriter(Console.Error, this));
                _consoleTeeInstalled = true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LogService] Failed to install console tee: {ex.Message}");
            }
        }

        /// <summary>Shortens long payloads (instrument responses) for the log.</summary>
        public static string Truncate(string? value, int maxLength = DefaultMaxLineLength)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            var singleLine = value.Replace("\r", "\\r").Replace("\n", "\\n");
            return singleLine.Length <= maxLength ? singleLine : singleLine.Substring(0, maxLength) + $"... (+{singleLine.Length - maxLength} chars)";
        }

        private static string ComputeDefaultLogDirectory()
        {
            try
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "SMU_Revamp",
                    "logs");
            }
            catch
            {
                return Path.Combine(Path.GetTempPath(), "SMU_Revamp_logs");
            }
        }
    }

    /// <summary>
    /// TextWriter that forwards everything to the original console and logs
    /// complete lines as Info entries.
    /// </summary>
    internal sealed class LogConsoleTeeWriter : TextWriter
    {
        private readonly TextWriter _inner;
        private readonly LogService _log;

        public LogConsoleTeeWriter(TextWriter inner, LogService log)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _log = log ?? throw new ArgumentNullException(nameof(log));
        }

        public override Encoding Encoding => _inner.Encoding;

        public override void Write(char value)
        {
            _inner.Write(value);
        }

        public override void Write(string? value)
        {
            _inner.Write(value);
        }

        public override void WriteLine()
        {
            _inner.WriteLine();
        }

        public override void WriteLine(string? value)
        {
            _inner.WriteLine(value);
            if (!string.IsNullOrWhiteSpace(value))
            {
                _log.Write(LogLevel.Info, LogService.Truncate(value, 500));
            }
        }
    }
}
