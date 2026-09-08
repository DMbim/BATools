// Path: BA/Core/Content/Preview/PreviewQueueStatus.cs
namespace BA.Core.Content.Preview
{
    public enum PreviewQueueStatus
    {
        Pending = 0,
        Processing = 1,
        Success = 2,
        Failed = 3,
        Quarantined = 4
    }
}