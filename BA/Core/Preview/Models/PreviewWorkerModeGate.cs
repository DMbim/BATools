// Path: BA/Core/Content/Preview/PreviewWorkerModeGate.cs
using System;

namespace BA.Core.Content.Preview
{
    public static class PreviewWorkerModeGate
    {
        public static bool IsPreviewWorkerMode =>
            Environment.GetEnvironmentVariable("BA_PREVIEW_WORKER_MODE") == "1";
    }
}