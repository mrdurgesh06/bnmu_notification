using Microsoft.Data.SqlClient;
using Notification.Models;

namespace Notification.Services
{
    /// <summary>
    /// Polls Erp_SendNotification every 20 seconds for notices that
    /// haven't been pushed yet (tracked in Notice_PushLog — a table that
    /// belongs entirely to this API; Erp_SendNotification itself is never
    /// modified, so the admin Web Forms app needs zero changes).
    ///
    /// For each new notice, looks up its targeted students
    /// (Erp_SendNotification_trn) and pushes to whichever of them
    /// currently have a registered device token (Notice_DeviceTokens —
    /// populated on login, removed on logout via NoticeController's
    /// RegisterDevice/UnregisterDevice endpoints). A student who isn't
    /// logged in on any device at push time simply doesn't get a push for
    /// that notice — they'll still see it in their list next time they
    /// open the app, since that always reads live from the database.
    /// </summary>
    public class NoticePushWorker : BackgroundService
    {
        private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

        private readonly IConfiguration _config;
        private readonly FcmService _fcmService;
        private readonly ILogger<NoticePushWorker> _logger;

        public NoticePushWorker(IConfiguration config, FcmService fcmService, ILogger<NoticePushWorker> logger)
        {
            _config = config;
            _fcmService = fcmService;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("NoticePushWorker started — polling every {Seconds}s.", PollInterval.TotalSeconds);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ProcessPendingNoticesAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "NoticePushWorker: error while processing pending notices.");
                }

                try
                {
                    await Task.Delay(PollInterval, stoppingToken);
                }
                catch (TaskCanceledException)
                {
                    // Expected on shutdown.
                }
            }
        }

        private async Task ProcessPendingNoticesAsync()
        {
            var connStr = _config.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("DefaultConnection is not configured.");

            var pending = new List<(int NotifyId, NoticePushRequest Notice)>();

            using (var con = new SqlConnection(connStr))
            using (var cmd = new SqlCommand(@"
                SELECT 
                    sn.pk_notify,
                    LEFT(sn.description, 60) 
                        + CASE WHEN LEN(sn.description) > 60 THEN '...' ELSE '' END AS Title,
                    sn.description  AS Description,
                    cp.description  AS CollegeName,
                    CONVERT(VARCHAR(10), sn.dated, 120) AS PublishDate,
                    sn.filename     AS AttachmentName
                FROM Erp_SendNotification sn
                LEFT JOIN ERP_College_Profile_Mst cp ON cp.pk_CollegeId = sn.fk_collegeid
                LEFT JOIN Notice_PushLog pl ON pl.fk_notify = sn.pk_notify
                WHERE pl.fk_notify IS NULL
                ORDER BY sn.dated ASC", con))
            {
                await con.OpenAsync();
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var notifyId = Convert.ToInt32(reader["pk_notify"]);
                    var notice = new NoticePushRequest
                    {
                        Id = notifyId.ToString(),
                        Title = reader["Title"] as string ?? "New Notice",
                        Description = reader["Description"] as string ?? "",
                        CollegeName = reader["CollegeName"] as string ?? "All Colleges",
                        PublishDate = reader["PublishDate"] as string,
                        AttachmentName = reader["AttachmentName"] as string,
                    };
                    pending.Add((notifyId, notice));
                }
            }

            if (pending.Count == 0) return;

            foreach (var (notifyId, notice) in pending)
            {
                var tokens = await GetTokensForNoticeAsync(connStr, notifyId);

                if (tokens.Count > 0)
                {
                    var sent = await _fcmService.SendToTokensAsync(tokens, notice);
                    _logger.LogInformation(
                        "NoticePushWorker: notice {Id} pushed to {Sent}/{Total} device(s).",
                        notifyId, sent, tokens.Count);
                }
                else
                {
                    _logger.LogInformation(
                        "NoticePushWorker: notice {Id} has no logged-in recipients right now — skipped push.",
                        notifyId);
                }

                await MarkAsPushedAsync(connStr, notifyId);
            }
        }

        private static async Task<List<string>> GetTokensForNoticeAsync(string connStr, int notifyId)
        {
            var tokens = new List<string>();

            using var con = new SqlConnection(connStr);
            using var cmd = new SqlCommand(@"
                SELECT DISTINCT dt.fcm_token
                FROM Erp_SendNotification_trn snt
                INNER JOIN Notice_DeviceTokens dt ON dt.fk_Stuid = snt.fk_Stuid
                WHERE snt.fk_notify = @notify", con);
            cmd.Parameters.AddWithValue("@notify", notifyId);

            await con.OpenAsync();
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                tokens.Add(reader.GetString(0));
            }

            return tokens;
        }

        private static async Task MarkAsPushedAsync(string connStr, int notifyId)
        {
            using var con = new SqlConnection(connStr);
            using var cmd = new SqlCommand(
                "INSERT INTO Notice_PushLog (fk_notify, pushed_at) VALUES (@notify, GETDATE())", con);
            cmd.Parameters.AddWithValue("@notify", notifyId);

            await con.OpenAsync();
            await cmd.ExecuteNonQueryAsync();
        }
    }
}
