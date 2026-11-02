using NMBDHL.Auth.Model;
using NMBDHL.DataAccess.SQLDataBaseAccess;
using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace NMBDHL.Auth.Repository
{
    public class AuthRepository : IAuthRepository
    {
        private readonly ISQLDataBaseAccess _db;

        public AuthRepository(ISQLDataBaseAccess db)
        {
            _db = db;
        }

        // Hash the password using SHA-256.
        public static string HashPassword(string password)
        {
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] bytes = Encoding.UTF8.GetBytes(password);
                byte[] hash = sha256.ComputeHash(bytes);

                StringBuilder builder = new StringBuilder();

                foreach (byte b in hash)
                {
                    builder.Append(b.ToString("x2"));
                }

                return builder.ToString();
            }
        }

        // Validate the user's email and password.
        public async Task<User> ValidateUser(Login login)
        {
            if (login == null ||
                string.IsNullOrWhiteSpace(login.Email) ||
                string.IsNullOrEmpty(login.Password))
            {
                return null;
            }

            // Retrieve the user from the database.
            var users = await _db.GetData<User, dynamic>(
                "usp_Login",
                new
                {
                    Email = login.Email.Trim()
                });

            var user = users?.FirstOrDefault();

            if (user == null)
            {
                return null;
            }

            // Ensure the database returned a password hash.
            if (string.IsNullOrWhiteSpace(user.Password))
            {
                return null;
            }

            // Hash the password entered on the login page.
            string enteredHash = HashPassword(login.Password);

            // Get the stored hash from the database.
            string storedHash = user.Password.Trim();

            // Compare the two hexadecimal hash strings.
            if (!string.Equals(
                enteredHash,
                storedHash,
                StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            // Password matches; return the authenticated user.
            return user;
        }

        public async Task<User?> FindUserByEmailAsync(string email)
        {
            var users = await _db.GetData<User, dynamic>(
                "usp_FindUserByEmail",
                new { Email = email.Trim() });

            return users.FirstOrDefault();
        }

        public async Task<bool> UpdatePasswordAsync(
    string email,
    string passwordHash)
        {
            var result = await _db.SaveData(
                "usp_UpdatePassword",
                new
                {
                    Email = email.Trim(),
                    Password = passwordHash
                });

            return result > 0;
        }
    }
}