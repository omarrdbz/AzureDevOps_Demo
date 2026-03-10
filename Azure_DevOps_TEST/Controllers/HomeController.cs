using Azure_DevOps_TEST.Models;
using Azure_DevOps_TEST.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using System.Diagnostics;

namespace Azure_DevOps_TEST.Controllers
{
    public class HomeController : Controller
    {
        private readonly IWeatherService _weatherService;
        private readonly WeatherSettings _weatherSettings;
        private readonly IConfiguration _configuration;
        private readonly IWebHostEnvironment _environment;

        public HomeController(
            IWeatherService weatherService,
            IOptions<WeatherSettings> weatherSettings,
            IConfiguration configuration,
            IWebHostEnvironment environment)
        {
            _weatherService = weatherService;
            _weatherSettings = weatherSettings.Value;
            _configuration = configuration;
            _environment = environment;
        }

        public async Task<IActionResult> Index(string? city)
        {
            var targetCity = city ?? _weatherSettings.DefaultCity;
            var connectionString = _configuration.GetConnectionString("WeatherDb");

            var viewModel = new WeatherViewModel
            {
                Environment = _environment.EnvironmentName,
                ApiKeyConfigured = !string.IsNullOrWhiteSpace(_weatherSettings.ApiKey)
                    && !_weatherSettings.ApiKey.Contains("REPLACE", StringComparison.OrdinalIgnoreCase),
                DatabaseConfigured = !string.IsNullOrWhiteSpace(connectionString)
                    && !connectionString.Contains("localhost", StringComparison.OrdinalIgnoreCase)
            };

            try
            {
                viewModel.CurrentWeather = await _weatherService.GetCurrentWeatherAsync(targetCity);
                viewModel.Forecast = await _weatherService.GetForecastAsync(targetCity);
            }
            catch (Exception ex)
            {
                viewModel.ErrorMessage = $"Error al obtener datos del clima: {ex.Message}";
            }

            return View(viewModel);
        }

        public IActionResult Privacy()
        {
            return View();
        }

        public IActionResult Config()
        {
            var maskedApiKey = _weatherSettings.ApiKey.Length > 4
                ? new string('*', _weatherSettings.ApiKey.Length - 4) + _weatherSettings.ApiKey[^4..]
                : "****";

            var connectionString = _configuration.GetConnectionString("WeatherDb") ?? "No configurado";
            var maskedCs = connectionString.Length > 10
                ? new string('*', connectionString.Length - 10) + connectionString[^10..]
                : "****";

            ViewData["Environment"] = _environment.EnvironmentName;
            ViewData["ApiKeyMasked"] = maskedApiKey;
            ViewData["BaseUrl"] = _weatherSettings.BaseUrl;
            ViewData["DefaultCity"] = _weatherSettings.DefaultCity;
            ViewData["CacheDuration"] = _weatherSettings.CacheDurationMinutes;
            ViewData["ConnectionStringMasked"] = maskedCs;

            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
