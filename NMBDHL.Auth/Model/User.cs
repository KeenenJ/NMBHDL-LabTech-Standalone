using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NMBDHL.Auth.Model
{
    public class User
    {
        [Key]
        public int UserID { get; set; }

        [Required]
        public string Name { get; set; } = string.Empty;

        [Required]
        public string Surname { get; set; } = string.Empty;

        [Required]
        public string Role { get; set; } = string.Empty;

        public string? IDNumber { get; set; }

        public string? ContactNumber { get; set; }

        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;

        // Stores the password hash returned by usp_Login.
        [Required]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        // Only needed when creating or changing a password.
        [NotMapped]
        [DataType(DataType.Password)]
        public string? ConfirmPassword { get; set; }

        public bool IsFirstLogin { get; set; } = true;

        public bool IsActive { get; set; } = true;

        [NotMapped]
        public string? Allergies { get; set; }
    }
}
