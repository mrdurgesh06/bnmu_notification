using Microsoft.EntityFrameworkCore;

namespace Notification.Models
{
    [Keyless]
    public class StudentDetailsDto
    {
        public string College { get; set; }
        public string FullName { get; set; }
        public string MName { get; set; }
        public string FName { get; set; }
    }

    // New: request body model for login-style lookup
    public class StudentLoginRequest
    {
        public string RegNo { get; set; }
        public string Pwd { get; set; }
    }
}