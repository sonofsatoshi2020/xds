using System;
using Microsoft.Extensions.Logging;
using NLog;
using UnnamedCoin.Bitcoin.Configuration.Logging;
using ILogger = Microsoft.Extensions.Logging.ILogger;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace UnnamedCoin.Bitcoin.Utilities
{
    /// <summary>
    ///     Logger that prefixes every log with specified string.
    /// </summary>
    /// <remarks>
    ///     TODO: Currently this is not compatible with logging to the console.
    ///     This means that if you use a prefix logger for logging in a class,
    ///     the logging output will not go to the console even if the logging
    ///     level is at or above the minimum logging level for the console.
    /// </remarks>
    public class PrefixLogger : ILogger
    {
        /// <summary>Internal console logger instance.</summary>
        readonly ILogger consoleLogger;

        /// <summary>Internal NLog logger instance.</summary>
        readonly Logger logger;

        /// <summary>Prefix to put in front of every message.</summary>
        readonly string prefix;

        /// <summary>Wrapper class type for the NLog callsite to skip it.</summary>
        readonly Type wrapperType;

        /// <summary>
        ///     Creates a logger instance with given prefix.
        /// </summary>
        /// <param name="loggerFactory">Factory to create loggers.</param>
        /// <param name="categoryName">Category name for messages produced by the logger.</param>
        /// <param name="prefix">String to be put in front of each log of the newly created logger.</param>
        public PrefixLogger(ILoggerFactory loggerFactory, string categoryName, string prefix = null)
        {
            this.logger = LogManager.GetLogger(categoryName);
            this.consoleLogger = loggerFactory.GetConsoleLoggerProvider().CreateLogger(categoryName);

            this.prefix = prefix != null ? prefix : string.Empty;
            this.wrapperType = typeof(PrefixLogger);
        }

        /// <inheritdoc />
        public IDisposable BeginScope<TState>(TState state)
        {
            if (state == null)
                throw new ArgumentNullException(nameof(state));

            return NestedDiagnosticsLogicalContext.Push(state);
        }

        /// <inheritdoc />
        public bool IsEnabled(LogLevel logLevel)
        {
            return IsEnabled(logLevel.ToNLogLevel());
        }

        /// <inheritdoc />
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception,
            Func<TState, Exception, string> formatter)
        {
            // First we take care about output to console.
            if (this.consoleLogger.IsEnabled(logLevel))
                this.consoleLogger.Log(logLevel, eventId, state, exception,
                    (s, e) => { return this.prefix + formatter(s, e); });

            // The rest of the method cares about logging via NLog to files.
            var nLogLevel = logLevel.ToNLogLevel();
            if (!IsEnabled(nLogLevel))
                return;

            if (formatter == null)
                throw new ArgumentNullException(nameof(formatter));

            var message = this.prefix + formatter(state, exception);

            var eventInfo = LogEventInfo.Create(nLogLevel, this.logger.Name, message);
            eventInfo.Exception = exception;
            this.logger.Log(this.wrapperType, eventInfo);
        }

        /// <summary>
        ///     Checks if the given log level is enabled.
        /// </summary>
        /// <param name="logLevel">Log level to check.</param>
        /// <returns><c>true</c> if the log level is enabled, <c>false</c> otherwise.</returns>
        bool IsEnabled(NLog.LogLevel logLevel)
        {
            return this.logger.IsEnabled(logLevel);
        }
    }
}