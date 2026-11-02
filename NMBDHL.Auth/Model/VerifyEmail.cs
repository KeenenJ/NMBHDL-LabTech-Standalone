using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NMBDHL.Auth.Model
{
    public class VerifyEmail
    {
        [Required]
        [RegularExpression("^[a-z0-9]+@[a-zA-Z0-9]+\\.[a-zA-Z]{2,}$", ErrorMessage = "Example. johndoe06@gmail.com. Must " +
        "contain @ sign after local name and end with . followed by top-level domain ")]
        public string Email { get; set; }
    }
}
