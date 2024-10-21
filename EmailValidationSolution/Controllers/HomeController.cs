using CsvHelper;
using DnsClient;
using EmailValidationSolution.Models;
using Microsoft.AspNetCore.Mvc;
using OfficeOpenXml;
using System.Globalization;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace EmailValidationSolution.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly string _testEmail = "verifier@yourdomain.com";
        private readonly int _smtpTimeout = 10000; // 10 seconds
        private static int _progress = 0;
        private static List<EmailValidationModel> _validationResults = new List<EmailValidationModel>();
        private static DateTime _startTime;
        private static DateTime _endTime;
        private static List<ImportHistory> _importHistory = new List<ImportHistory>();

        public HomeController(ILogger<HomeController> logger)
        {
            _logger = logger;
        }

        // GET: /Home/Index
        public IActionResult Index()
        {
            ViewBag.ImportHistory = _importHistory.Take(3).ToList();
            return View();
        }

        // POST: /Home/Index - Handle file upload and email validation
        [HttpPost]
        public async Task<IActionResult> Index(IFormFile uploadedFile)
        {
            if (uploadedFile == null || uploadedFile.Length == 0)
            {
                ViewBag.Error = "Please upload a valid file.";
                return View();
            }

            _validationResults.Clear();
            _progress = 0;
            _startTime = DateTime.Now;

            try
            {
                ExcelPackage.LicenseContext = LicenseContext.NonCommercial;

                if (Path.GetExtension(uploadedFile.FileName).Equals(".csv", StringComparison.OrdinalIgnoreCase))
                {
                    await ProcessCsvFile(uploadedFile);
                }
                else if (Path.GetExtension(uploadedFile.FileName).Equals(".xlsx", StringComparison.OrdinalIgnoreCase) ||
                         Path.GetExtension(uploadedFile.FileName).Equals(".xls", StringComparison.OrdinalIgnoreCase))
                {
                    await ProcessExcelFile(uploadedFile);
                }
            }
            catch (Exception ex)
            {
                ViewBag.Error = $"Error during validation: {ex.Message}";
                return View();
            }

            _endTime = DateTime.Now;
            ViewBag.StartTime = _startTime;
            ViewBag.EndTime = _endTime;
            ViewBag.Duration = (_endTime - _startTime).TotalSeconds;

            // Update import history
            UpdateImportHistory(uploadedFile.FileName);

            ViewBag.ImportHistory = _importHistory.Take(3).ToList();
            return View(_validationResults);
        }

        private void UpdateImportHistory(string fileName)
        {
            var history = new ImportHistory
            {
                FileName = fileName,
                ImportDate = DateTime.Now,
                TotalValidCount = _validationResults.Count(r => r.IsValid),
                TotalActiveCount = _validationResults.Count(r => r.IsActive)
            };

            _importHistory.Insert(0, history);
            if (_importHistory.Count > 3)
            {
                _importHistory.RemoveAt(3);
            }
        }



        private async Task ProcessCsvFile(IFormFile file)
        {
            using (var reader = new StreamReader(file.OpenReadStream()))
            using (var csv = new CsvReader(reader, CultureInfo.InvariantCulture))
            {
                var records = csv.GetRecords<dynamic>().ToList();
                int totalRecords = records.Count;

                for (int i = 0; i < totalRecords; i++)
                {
                    var email = ((IDictionary<string, object>)records[i])["Email"]?.ToString();
                    if (!string.IsNullOrEmpty(email))
                    {
                        var result = await ValidateEmailAsync(email);
                        _validationResults.Add(result);
                    }
                    _progress = (int)((i + 1) / (double)totalRecords * 100);
                }
            }
        }

        private async Task ProcessExcelFile(IFormFile file)
        {
            using (var stream = new MemoryStream())
            {
                await file.CopyToAsync(stream);
                using (var package = new ExcelPackage(stream))
                {
                    var worksheet = package.Workbook.Worksheets[0];
                    int rowCount = worksheet.Dimension.Rows;

                    for (int row = 2; row <= rowCount; row++)
                    {
                        var email = worksheet.Cells[row, 1].Text;
                        if (!string.IsNullOrEmpty(email)) 
                        {
                            var result = await ValidateEmailAsync(email);
                            _validationResults.Add(result);
                        }
                        _progress = (int)((row - 1) / (double)(rowCount - 1) * 100);
                    }
                }
            }
        }

        [HttpGet]
        public IActionResult GetProgress()
        {
            var elapsedTime = (DateTime.Now - _startTime).TotalSeconds;
            var estimatedTotalTime = _progress > 0 ? (elapsedTime / _progress) * 100 : 0;
            var remainingTime = Math.Max(0, estimatedTotalTime - elapsedTime);

            return Json(new
            {
                progress = _progress,
                elapsedTime = Math.Round(elapsedTime, 2),
                estimatedTotalTime = Math.Round(estimatedTotalTime, 2),
                remainingTime = Math.Round(remainingTime, 2)
            });
        }

        [HttpGet]
        public IActionResult DownloadResults()
        {
            using (var package = new ExcelPackage())
            {
                var worksheet = package.Workbook.Worksheets.Add("Validation Results");
                worksheet.Cells["A1"].Value = "Email"; 
                worksheet.Cells["B1"].Value = "Is Valid";
                worksheet.Cells["C1"].Value = "Is Active";
                worksheet.Cells["D1"].Value = "Reason";

                for (int i = 0; i < _validationResults.Count; i++)
                {
                    var result = _validationResults[i];
                    worksheet.Cells[i + 2, 1].Value = result.Email;
                    worksheet.Cells[i + 2, 2].Value = result.IsValid;
                    worksheet.Cells[i + 2, 3].Value = result.IsActive;
                    worksheet.Cells[i + 2, 4].Value = result.Reason;
                }

                worksheet.Cells.AutoFitColumns();

                var content = package.GetAsByteArray();
                return File(content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "ValidationResults.xlsx");
            }
        }

        // Updated email validation logic
        private async Task<EmailValidationModel> ValidateEmailAsync(string email)
        {
            var result = new EmailValidationModel
            {
                Email = email,
                IsValid = true,
                IsActive = false,
                Reason = "Unknown"
            };

            try
            {
                email = email.Trim().ToLower();
                int i = 0;
                if (string.IsNullOrWhiteSpace(email))
                {
                    i = 1;
                    result.IsValid = false;
                    result.Reason = "Empty or invalid email";
                    return result;
                }

                if (!ValidateBasicFormat(email))
                {
                    i = 1;
                    result.IsValid = false;
                    result.Reason = "Invalid email format";
                    return result;
                }

                // Check for life.com domain
                if (email.Split('@')[1] == "life.com")
                {
                    i = 1;
                    result.IsValid = false;
                    result.Reason = "life.com domain is not allowed";
                    return result;
                }
                if(i == 0)
                {

                    (result.IsActive, result.Reason) = await VerifyEmailExistenceAsync(email);
                }

            }
            catch (Exception ex)
            {
                result.IsValid = false;
                result.Reason = $"Validation error: {ex.Message}";
            }

            return result;
        }

        private bool ValidateBasicFormat(string email)
        {
            string pattern = @"^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$";
            return Regex.IsMatch(email, pattern);
        }

        private async Task<(bool isActive, string reason)> VerifyEmailExistenceAsync(string email)
        {
            try
            {
                var domain = email.Split('@')[1];
                var lookup = new LookupClient();
                var result = await lookup.QueryAsync(domain, QueryType.MX);

                if (!result.Answers.MxRecords().Any())
                {
                    return (false, "No MX records found");
                }

                var mxRecord = result.Answers.MxRecords().OrderBy(x => x.Preference).First().Exchange.Value;

                using (var client = new TcpClient())
                {
                    await client.ConnectAsync(mxRecord, 25);
                    using (var stream = client.GetStream())
                    using (var reader = new StreamReader(stream))
                    using (var writer = new StreamWriter(stream))
                    {
                        writer.AutoFlush = true;
                        await reader.ReadLineAsync(); // Read greeting

                        await writer.WriteLineAsync($"HELO check.com");
                        await reader.ReadLineAsync();

                        await writer.WriteLineAsync($"MAIL FROM:<{_testEmail}>");
                        await reader.ReadLineAsync();

                        await writer.WriteLineAsync($"RCPT TO:<{email}>");
                        var response = await reader.ReadLineAsync();

                        // Check if the response starts with a valid status code (usually 3 digits, like "250", "550", etc.)
                        if (!string.IsNullOrEmpty(response) && response.Length >= 3 && int.TryParse(response.Substring(0, 3), out int code))
                        {
                            if (code == 250)
                            {
                                return (true, "Email exists and is active");
                            }
                            else if (code == 550)
                            {
                                return (false, "Email address doesn't exist");
                            }
                            else
                            {
                                return (false, $"Verification failed with code {code}");
                            }
                        }
                        else
                        {
                            return (false, "Invalid response received from the server");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                return (false, $"Verification error: {ex.Message}");
            }
        }

        public IActionResult Privacy()
        {
            return View();
        }

    }
}