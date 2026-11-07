using FirebaseAdmin.Messaging;
using Notification.Models;

namespace Notification.Services
{
    /// <summary>
    /// NOTE ON NAMING: this project's root namespace is "Notification",
    /// which collides with FirebaseAdmin's own `Notification` class
    /// (FirebaseAdmin.Messaging.Notification). Every reference to that
    /// type below is fully qualified as
    /// `FirebaseAdmin.Messaging.Notification` for that reason — don't
    /// shorten it, the bare name resolves to this namespace instead and
    /// won't compile.
    /// </summary>
    public class FcmService
    {
        private const string DefaultTopic = "all_students";

        private readonly ILogger<FcmService> _logger;
        private readonly bool _isConfigured;

        private const string DefaultAttachmentsBaseUrl =
            "https://bnmu.collegeesolution.org/BNMU/UNI_DOCUMENT/NOTIFICATION";

        // Only these can be shown as the expanded big picture of a push.
        // PDFs / docs can't be previewed in a notification, so for those
        // only the file name is added to the body (see BuildBody).
        private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".png", ".webp", ".gif", ".bmp"
        };

        private readonly string _attachmentsBaseUrl;

        public FcmService(ILogger<FcmService> logger, IConfiguration config)
        {
            _logger = logger;
            _isConfigured = FirebaseAdmin.FirebaseApp.DefaultInstance != null;

            _attachmentsBaseUrl = (config["AttachmentsBaseUrl"] ?? DefaultAttachmentsBaseUrl).TrimEnd('/');
        }

        /// <summary>
        /// Public URL of the notice's attachment if (and only if) it is an
        /// image, else null (field is then simply omitted from the push).
        /// </summary>
        private string? GetAttachmentImageUrl(NoticePushRequest notice)
        {
            var name = notice.AttachmentName;
            if (string.IsNullOrWhiteSpace(name)) return null;
            if (!ImageExtensions.Contains(Path.GetExtension(name))) return null;
            return $"{_attachmentsBaseUrl}/{Uri.EscapeDataString(name)}";
        }

        /// <summary>
        /// Notification body: the description, plus the attachment's file
        /// name on a new line when there is one, so non-image attachments
        /// (PDF etc.) are still visible in the tray.
        /// </summary>
        private static string BuildBody(NoticePushRequest notice) =>
            string.IsNullOrWhiteSpace(notice.AttachmentName)
                ? notice.Description
                : $"{notice.Description}\n\U0001F4CE {notice.AttachmentName}";

        /// <summary>
        /// Sends a push notification to every device subscribed to
        /// <paramref name="topic"/>. The Flutter app subscribes to
        /// "all_students" on startup, so the default reaches every
        /// installed app instance — no device-token table needed.
        /// Returns null (and logs a warning) if Firebase hasn't been
        /// configured yet (missing service account key), rather than
        /// throwing, so the rest of the API keeps working either way.
        /// </summary>
        public async Task<string?> PublishNoticeAsync(NoticePushRequest notice, string topic = DefaultTopic)
        {
            if (!_isConfigured)
            {
                _logger.LogWarning(
                    "Firebase is not configured (missing firebase-service-account.json) — push not sent.");
                return null;
            }

            var message = new Message
            {
                Topic = topic,
                Notification = new FirebaseAdmin.Messaging.Notification
                {
                    Title = notice.Title,
                    Body = BuildBody(notice),
                },
                Data = new Dictionary<string, string>
                {
                    ["id"] = notice.Id ?? Guid.NewGuid().ToString(),
                    ["title"] = notice.Title,
                    ["description"] = notice.Description,
                    ["category"] = notice.Category,
                    ["collegeName"] = notice.CollegeName,
                    ["publishDate"] = notice.PublishDate ?? DateTime.UtcNow.ToString("yyyy-MM-dd"),
                    ["icon"] = notice.Icon,
                    ["attachmentName"] = notice.AttachmentName ?? "",
                    ["attachmentSize"] = notice.AttachmentSize ?? "",
                    ["importantNote"] = notice.ImportantNote
                        ?? "For further updates, keep checking the official notice board.",
                },
                Android = new AndroidConfig
                {
                    Notification = new AndroidNotification
                    {
                        ChannelId = "bnmu_notices_channel",
                        ImageUrl = GetAttachmentImageUrl(notice),
                    },
                },
            };

            var messageId = await FirebaseMessaging.DefaultInstance.SendAsync(message);
            _logger.LogInformation("FCM push sent to topic '{Topic}': {MessageId}", topic, messageId);
            return messageId;
        }

        /// <summary>
        /// Sends a push notification to a specific list of device tokens
        /// (as opposed to <see cref="PublishNoticeAsync"/>, which
        /// broadcasts to everyone subscribed to a topic). Used by
        /// NoticePushWorker to push a targeted notice only to the
        /// students it's actually meant for, and only to devices that are
        /// currently registered (i.e. someone is logged in on them).
        /// Returns how many of the tokens the push was successfully
        /// delivered to; 0 if Firebase isn't configured or the list is
        /// empty.
        /// </summary>
        public async Task<int> SendToTokensAsync(IReadOnlyList<string> tokens, NoticePushRequest notice)
        {
            if (!_isConfigured || tokens.Count == 0)
            {
                if (!_isConfigured)
                {
                    _logger.LogWarning(
                        "Firebase is not configured (missing firebase-service-account.json) — push not sent.");
                }
                return 0;
            }

            var data = new Dictionary<string, string>
            {
                ["id"] = notice.Id ?? Guid.NewGuid().ToString(),
                ["title"] = notice.Title,
                ["description"] = notice.Description,
                ["category"] = notice.Category,
                ["collegeName"] = notice.CollegeName,
                ["publishDate"] = notice.PublishDate ?? DateTime.UtcNow.ToString("yyyy-MM-dd"),
                ["icon"] = notice.Icon,
                ["attachmentName"] = notice.AttachmentName ?? "",
                ["attachmentSize"] = notice.AttachmentSize ?? "",
                ["importantNote"] = notice.ImportantNote
                    ?? "For further updates, keep checking the official notice board.",
            };

            int successCount = 0;

            // FCM caps a single multicast call at 500 tokens — chunk in
            // case a notice's recipient list is ever larger than that.
            for (int i = 0; i < tokens.Count; i += 500)
            {
                var batch = tokens.Skip(i).Take(500).ToList();

                var message = new MulticastMessage
                {
                    Tokens = batch,
                    Notification = new FirebaseAdmin.Messaging.Notification
                    {
                        Title = notice.Title,
                        Body = BuildBody(notice),
                    },
                    Data = data,
                    Android = new AndroidConfig
                    {
                        Notification = new AndroidNotification
                        {
                            ChannelId = "bnmu_notices_channel",
                            ImageUrl = GetAttachmentImageUrl(notice),
                        },
                    },
                };

                var response = await FirebaseMessaging.DefaultInstance.SendEachForMulticastAsync(message);
                successCount += response.SuccessCount;

                _logger.LogInformation(
                    "FCM multicast for notice {NoticeId}: {Success}/{Total} delivered.",
                    notice.Id, response.SuccessCount, batch.Count);
            }

            return successCount;
        }
    }
}
