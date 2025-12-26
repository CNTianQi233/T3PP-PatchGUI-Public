using System;
using System.Diagnostics;

namespace PatchGUI.Core
{
    internal static class PatchLoggerExtensions
    {
        public static IDisposable Step(this IPatchLogger logger, string stepName)
        {
            if (logger == null) throw new ArgumentNullException(nameof(logger));
            if (string.IsNullOrWhiteSpace(stepName)) stepName = "(unnamed)";
            return new StepScope(logger, stepName);
        }

        public static void LogException(this IPatchLogger logger, string context, Exception ex)
        {
            if (logger == null) throw new ArgumentNullException(nameof(logger));
            if (ex == null) throw new ArgumentNullException(nameof(ex));
            logger.Error($"{context}: {ex}");
        }

        private sealed class StepScope : IDisposable
        {
            private readonly IPatchLogger _logger;
            private readonly string _stepName;
            private readonly Stopwatch _sw;
            private bool _disposed;

            public StepScope(IPatchLogger logger, string stepName)
            {
                _logger = logger;
                _stepName = stepName;
                _sw = Stopwatch.StartNew();
                _logger.Info($"==> {_stepName}");
            }

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                _sw.Stop();
                _logger.Info($"<== {_stepName} ({_sw.ElapsedMilliseconds} ms)");
            }
        }
    }
}

