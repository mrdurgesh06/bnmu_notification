namespace Notification.Models
{
    /// <summary>
    /// Field names here intentionally match NoticeModel.fromPushData on the
    /// Flutter side exactly (id, title, description, category,
    /// collegeName, publishDate, icon, attachmentName, attachmentSize,
    /// importantNote) — sending this straight through as the FCM "data"
    /// payload is what lets a tapped notification open the right notice.
    /// </summary>
    public class NoticePushRequest
    {
        public string? Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;

        /// One of: general | exam | college | others
        public string Category { get; set; } = "general";

        public string CollegeName { get; set; } = "All Colleges";

        /// ISO date string, e.g. "2026-07-30". Defaults to today (UTC) if omitted.
        public string? PublishDate { get; set; }

        /// One of: campaign | description | assignment_turned_in | badge |
        /// fact_check | apartment | workspace_premium | local_library
        public string Icon { get; set; } = "campaign";

        public string? AttachmentName { get; set; }
        public string? AttachmentSize { get; set; }
        public string? ImportantNote { get; set; }
    }
}
