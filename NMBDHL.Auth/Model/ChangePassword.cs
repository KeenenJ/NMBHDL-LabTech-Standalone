using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NMBDHL.Auth.Model
{
    public class ChangePassword
    {
        [Required]
        [RegularExpression("^[a-z0-9]+@[a-zA-Z0-9]+\\.[a-zA-Z]{2,}$", ErrorMessage = "Example. johndoe06@gmail.com. Must " +
        "contain @ sign after local name and end with . followed by top-level domain ")]
        public string Email { get; set; }
        [Required]
        [DataType(DataType.Password)]
        [Display(Name = "New Password")]
        [RegularExpression(@"^(?=.[A-Za-z])(?=.\d)[A-Za-z\d]{6,}$", ErrorMessage = "Password must contain atleast 6 letters and/or numbers")]
        public string NewPassword { get; set; }
        [Required]
        [System.ComponentModel.DataAnnotations.Compare("NewPassword", ErrorMessage = "Passwords do not match.")]
        [DataType(DataType.Password)]
        [Display(Name ="Confirm New Password")]
        public string ConfirmNewPassword { get; set; }
    }
}
