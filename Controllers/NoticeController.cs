using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Notification.Models;
using Notification.Services;
using System.Data;

namespace Notification.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class NoticeController : ControllerBase
    {
        private readonly FcmService _fcmService;
        private readonly IConfiguration _config;

        public NoticeController(FcmService fcmService, IConfiguration config)
        {
            _fcmService = fcmService;
            _config = config;
        }

        /// <summary>
        /// POST api/Notice/Publish
        ///
        /// Publishes a notice as a push notification to every installed
        /// app instance (topic broadcast). Kept for manual/ad-hoc pushes;
        /// the normal flow for notices saved via the admin Web Forms app
        /// is now NoticePushWorker, which targets only the notice's
        /// actual recipients.
        /// </summary>
        [HttpPost("Publish")]
        public async Task<IActionResult> Publish([FromBody] NoticePushRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Title))
                return BadRequest("Title is required.");

            request.Id ??= Guid.NewGuid().ToString();
            request.PublishDate ??= DateTime.UtcNow.ToString("yyyy-MM-dd");

            var messageId = await _fcmService.PublishNoticeAsync(request);

            if (messageId == null)
            {
                return StatusCode(503,
                    "Notice accepted, but push wasn't sent — Firebase isn't configured on the server yet " +
                    "(missing firebase-service-account.json). See README notes for setup.");
            }

            return Ok(new
            {
                message = "Notice published",
                fcmMessageId = messageId,
                notice = request,
            });
        }

        /// <summary>
        /// GET api/Notice/GetByStudent/{stuid}
        ///
        /// Returns all notices targeted to the given student (via
        /// Erp_SendNotification_trn.fk_Stuid), newest first. stuid comes
        /// from the login response (ERP_Student_Mst.pk_Stuid) and should
        /// be saved on the Flutter side after login.
        /// </summary>
        [HttpGet("GetByStudent/{stuid}")]
        public IActionResult GetNoticesByStudent(long stuid)
        {
            try
            {
                string connStr = _config.GetConnectionString("DefaultConnection");

                using var con = new SqlConnection(connStr);
                using var cmd = new SqlCommand("USP_GetNoticesByStudent", con)
                {
                    CommandType = CommandType.StoredProcedure
                };
                cmd.Parameters.AddWithValue("@Stuid", stuid);

                con.Open();
                using var reader = cmd.ExecuteReader();

                var notices = new List<Dictionary<string, object?>>();
                while (reader.Read())
                {
                    var notice = new Dictionary<string, object?>();
                    for (int i = 0; i < reader.FieldCount; i++)
                    {
                        notice[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                    }
                    notices.Add(notice);
                }

                return Ok(notices);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Server error: {ex.Message}");
            }
        }

        /// <summary>
        /// POST api/Notice/RegisterDevice
        ///
        /// Call this right after a successful login. Maps this device's
        /// FCM token to the logged-in student's Stuid, so
        /// NoticePushWorker knows where to deliver their targeted
        /// notices. A token belongs to one student at a time — if a
        /// different student logs in on the same phone, this replaces
        /// the previous mapping.
        /// </summary>
        [HttpPost("RegisterDevice")]
        public IActionResult RegisterDevice([FromBody] RegisterDeviceRequest request)
        {
            if (request == null ||
                string.IsNullOrWhiteSpace(request.Stuid) ||
                string.IsNullOrWhiteSpace(request.FcmToken))
            {
                return BadRequest("Stuid and FcmToken are required.");
            }

            if (!long.TryParse(request.Stuid, out var stuid))
            {
                return BadRequest("Stuid must be numeric.");
            }

            try
            {
                string connStr = _config.GetConnectionString("DefaultConnection");
                using var con = new SqlConnection(connStr);
                con.Open();

                using (var del = new SqlCommand(
                    "DELETE FROM Notice_DeviceTokens WHERE fcm_token = @token", con))
                {
                    del.Parameters.AddWithValue("@token", request.FcmToken);
                    del.ExecuteNonQuery();
                }

                using var ins = new SqlCommand(
                    "INSERT INTO Notice_DeviceTokens (fk_Stuid, fcm_token, updated_at) " +
                    "VALUES (@stuid, @token, GETDATE())", con);
                ins.Parameters.AddWithValue("@stuid", stuid);
                ins.Parameters.AddWithValue("@token", request.FcmToken);
                ins.ExecuteNonQuery();

                return Ok(new { message = "Device registered" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Server error: {ex.Message}");
            }
        }

        /// <summary>
        /// POST api/Notice/UnregisterDevice
        ///
        /// Call this on logout. Removes this device's token so it stops
        /// receiving push notifications until someone logs in again on
        /// it — this is what makes pushes go only to logged-in students.
        /// </summary>
        [HttpPost("UnregisterDevice")]
        public IActionResult UnregisterDevice([FromBody] UnregisterDeviceRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.FcmToken))
                return BadRequest("FcmToken is required.");

            try
            {
                string connStr = _config.GetConnectionString("DefaultConnection");
                using var con = new SqlConnection(connStr);
                using var cmd = new SqlCommand(
                    "DELETE FROM Notice_DeviceTokens WHERE fcm_token = @token", con);
                cmd.Parameters.AddWithValue("@token", request.FcmToken);

                con.Open();
                cmd.ExecuteNonQuery();

                return Ok(new { message = "Device unregistered" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Server error: {ex.Message}");
            }
        }
    }
}
