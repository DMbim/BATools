namespace BA.Core.Export.Infrastructure
{
    /// <summary>
    /// Tracks whether a synchronize-with-central is currently in progress for
    /// the active document, so the export scheduler can avoid firing mid
    /// synchronize. Revit does not expose an IsSynchronizing flag directly,
    /// this must be toggled explicitly:
    /// set true at the start of the existing DocumentSynchronizingWithCentral
    /// handler (already wired for the Type Data Ledger sync engine), and set
    /// false at the start of DocumentSynchronizedWithCentral, and in a catch
    /// block if the synchronize is cancelled or fails, so the flag can never
    /// get stuck true.
    /// </summary>
    public static class SynchronizeGuard
    {
        public static bool IsSynchronizing { get; set; }
    }
}
