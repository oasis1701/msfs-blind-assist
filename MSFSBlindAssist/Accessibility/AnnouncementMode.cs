namespace MSFSBlindAssist.Accessibility;

/// <summary>How announcements are delivered (Settings, Announcements): through the running screen reader, or with
/// the Windows voice (SAPI) whatever reader is running.</summary>
public enum AnnouncementMode
{
    ScreenReader,
    SAPI
}
