namespace Notification.Models
{
    /// <summary>
    /// Sent right after login. Maps this device's FCM token to the
    /// logged-in student's Stuid in Notice_DeviceTokens, so
    /// NoticePushWorker knows where to push their targeted notices.
    /// </summary>
    public class RegisterDeviceRequest
    {
        public string Stuid { get; set; } = string.Empty;
        public string FcmToken { get; set; } = string.Empty;
    }

    /// <summary>
    /// Sent on logout. Removes this device's token from
    /// Notice_DeviceTokens so it stops receiving push notifications until
    /// someone logs in again on this device.
    /// </summary>
    public class UnregisterDeviceRequest
    {
        public string FcmToken { get; set; } = string.Empty;
    }
}
