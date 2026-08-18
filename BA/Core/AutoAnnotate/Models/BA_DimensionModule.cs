namespace BA.BIM.Core.Dimensioning.Infrastructure
{
    /// <summary>
    /// Static access point for the Auto-Dimension module's ExternalEvent bridge.
    /// Initialize() must be called from BaApplication.OnStartup (Revit API thread);
    /// Shutdown() from BaApplication.OnShutdown.
    /// </summary>
    public static class BA_DimensionModule
    {
        public static BA_DimensionRevitBridge Bridge { get; private set; }

        public static void Initialize()
        {
            if (Bridge != null) return; // idempotent - guards double OnStartup calls
            Bridge = new BA_DimensionRevitBridge();
        }

        public static void Shutdown()
        {
            Bridge?.Dispose();
            Bridge = null;
        }
    }
}