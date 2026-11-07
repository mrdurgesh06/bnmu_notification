using LoginAPI.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Data;

namespace LoginAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class StudentLoginController : ControllerBase
    {
        private readonly IConfiguration _config;

        public StudentLoginController(IConfiguration config)
        {
            _config = config;
        }

        // POST api/StudentLogin/auth
        [HttpPost("auth")]
        public IActionResult Auth([FromBody] StudentLoginModel req)
        {
            if (string.IsNullOrWhiteSpace(req.Id) || string.IsNullOrWhiteSpace(req.Password))
                return BadRequest("Id and Password are required.");

            try
            {
                string connStr = _config.GetConnectionString("DefaultConnection");

                using var con = new SqlConnection(connStr);
                using var cmd = new SqlCommand("studentlogin", con)
                {
                    CommandType = CommandType.StoredProcedure
                };

                con.Open();
                using var reader = cmd.ExecuteReader();

                while (reader.Read())
                {
                    string id = reader["id"].ToString();
                    string password = reader["password"].ToString();

                    if (id == req.Id.Trim() && password == req.Password.Trim())
                    {
                        return Ok(new
                        {
                            message = "Login successful",
                            id = id
                        });
                    }
                }

                return Unauthorized("Invalid credentials.");
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Server error: {ex.Message}");
            }
        }
    }
}