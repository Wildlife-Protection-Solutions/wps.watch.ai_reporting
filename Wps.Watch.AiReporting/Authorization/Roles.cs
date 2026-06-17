namespace Wps.Watch.AiReporting.Authorization;

/// <summary>
/// Role names mirrored from wps.watch.api's <c>RoleNames</c>. Phase 1 carries
/// copies; consolidate into a shared package later.
/// </summary>
public static class Roles
{
    public const string SystemAdmin = "System Admin";
    public const string OrgAdmin = "Org Admin";
    public const string IncidentManager = "Incident Manager";
    public const string Viewer = "Viewer";
    public const string GpsViewer = "GPS Viewer";
    public const string Volunteer = "Volunteer";
    public const string PhotoUploader = "Photo Uploader";
    public const string PhotoTagger = "Photo Tagger";
}
