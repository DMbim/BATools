using System;

namespace BA.Core.Export.Infrastructure
{
    /// <summary>
    /// Tracks whether a synchronize-with-central is currently in progress
    /// for the active document, so the export scheduler can avoid firing
    /// mid synchronize. Revit does not expose an IsSynchronizing flag
    /// directly, this is toggled explicitly by
    /// BaApplication.OnDocumentSynchronizingWithCentral/OnDocumentSynchronizedWithCentral.
    ///
    /// IsSynchronizing alone is not safe to trust indefinitely. It is set
    /// true when DocumentSynchronizingWithCentral fires, and is only
    /// guaranteed to be reset by OnDocumentSynchronizedWithCentral if the
    /// synchronize actually completes, or by BaApplication's own catch
    /// block if the ledger sync logic itself throws. A synchronize that
    /// fails or is cancelled for a reason outside that ledger logic
    /// entirely (a network drop, a permissions conflict, the user
    /// cancelling a later native Revit dialog) never reaches either reset
    /// path, DocumentSynchronizedWithCentral simply never fires, and this
    /// flag stays true for the rest of the session. Confirmed as the
    /// cause of the export scheduler silently going quiet after an
    /// interrupted synchronize.
    ///
    /// IsStale is a self-healing safety net for exactly that case.
    /// Callers that would otherwise skip work because IsSynchronizing is
    /// true should check IsStale first and treat a stale flag as false,
    /// logging that it auto-cleared. A real synchronize with central does
    /// not legitimately take anywhere near StaleAfter, so this does not
    /// risk interrupting one that is actually still running.
    /// </summary>
    public static class SynchronizeGuard
    {
        public static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(10);

        private static bool _isSynchronizing;
        private static DateTime? _setAtUtc;

        public static bool IsSynchronizing
        {
            get => _isSynchronizing;
            set
            {
                _isSynchronizing = value;
                _setAtUtc = value ? DateTime.UtcNow : (DateTime?)null;
            }
        }

        public static bool IsStale =>
            _isSynchronizing && _setAtUtc.HasValue && (DateTime.UtcNow - _setAtUtc.Value) > StaleAfter;
    }
}