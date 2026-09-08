// Path: BA/Core/Content/Preview/PreviewQueueItem.cs
using System;

namespace BA.Core.Content.Preview
{
    public sealed class PreviewQueueItem
    {
        public long Id { get; set; }
        public string FamilyPath { get; set; } = string.Empty;
        public PreviewQueueStatus Status { get; set; }
        public int AttemptCount { get; set; }
        public DateTime? LastAttemptUtc { get; set; }
        public string LastError { get; set; } = string.Empty;
        public DateTime EnqueuedUtc { get; set; }
    }
}