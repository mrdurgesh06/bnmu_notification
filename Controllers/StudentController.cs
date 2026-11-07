using Notification.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Data;

namespace Notification.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class StudentController : ControllerBase
    {
        private readonly IConfiguration _config;

        public StudentController(IConfiguration config)
        {
            _config = config;
        }

        /// <summary>
        /// Gets student details by registration number and password.
        /// </summary>
        [HttpPost("GetStudentDetailsByRegNo")]
        [ProducesResponseType(typeof(StudentDetailsDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public IActionResult GetStudentDetailsByRegNo([FromBody] StudentLoginRequest request)
        {
            if (request == null ||
                string.IsNullOrWhiteSpace(request.RegNo) ||
                string.IsNullOrWhiteSpace(request.Pwd))
            {
                return BadRequest("Registration number and password are required.");
            }

            try
            {
                string connStr = _config.GetConnectionString("DefaultConnection");

                using var con = new SqlConnection(connStr);
                using var cmd = new SqlCommand("USP_GetStudentDetailsByRegNo", con)
                {
                    CommandType = CommandType.StoredProcedure
                };

                cmd.Parameters.AddWithValue("@regno", request.RegNo.Trim());
                cmd.Parameters.AddWithValue("@PWD", request.Pwd.Trim());

                con.Open();
                using var reader = cmd.ExecuteReader();

                if (reader.Read())
                {
                    // Put every column into a dictionary (dynamic response)
                    var student = new Dictionary<string, object>();
                    for (int i = 0; i < reader.FieldCount; i++)
                    {
                        student[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                    }

                    return Ok(student);
                }

                return NotFound("Invalid registration number or password.");
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Server error: {ex.Message}");
            }
        }
    }
}