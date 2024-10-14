using System.ComponentModel.DataAnnotations;

namespace EmailValidationSolution.Models
{
    public class Class
    {
        // You can keep any existing properties or methods in this Class here
    }

    public class EmailValidationModel
    {
        [Required(ErrorMessage = "Email is required")]
        [EmailAddress(ErrorMessage = "Invalid email format")]
        [Display(Name = "Email Address")]
        public string Email { get; set; }

        public bool IsValid { get; set; }

        public bool IsActive { get; set; }

        public string Reason { get; set; }

    

    }

    public class ImportHistory
    {
        public int Id { get; set; }
        public string FileName { get; set; }
        public int ValidCount { get; set; }
        public int ActiveCount { get; set; }
        public DateTime ImportDate { get; set; }
    }
}