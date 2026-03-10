using Azure_DevOps_TEST.Models;
using Microsoft.Extensions.Options;

namespace Azure_DevOps_TEST.Services
{
    /// <summary>
    /// Servicio de clima que simula llamadas a una API externa.
    /// En un entorno real, usaría HttpClient con la ApiKey configurada.
    /// Los secretos (ApiKey, ConnectionString) se inyectan desde Azure DevOps Variable Groups.
    /// </summary>
    public class WeatherService : IWeatherService
    {
        private readonly WeatherSettings _settings;
        private readonly ILogger<WeatherService> _logger;
        private readonly IHttpClientFactory _httpClientFactory;

        private static readonly string[] Descriptions =
        [
            "Soleado", "Parcialmente nublado", "Nublado", "Lluvia ligera",
            "Lluvia", "Tormenta", "Nieve", "Niebla", "Ventoso", "Despejado"
        ];

        private static readonly string[] Icons =
        [
            "☀️", "⛅", "☁️", "🌦️", "🌧️", "⛈️", "❄️", "🌫️", "💨", "🌤️"
        ];

        public WeatherService(
            IOptions<WeatherSettings> settings,
            ILogger<WeatherService> logger,
            IHttpClientFactory httpClientFactory)
        {
            _settings = settings.Value;
            _logger = logger;
            _httpClientFactory = httpClientFactory;
        }

        public Task<WeatherForecast?> GetCurrentWeatherAsync(string city)
        {
            if (string.IsNullOrWhiteSpace(_settings.ApiKey) ||
                _settings.ApiKey.Contains("REPLACE", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning(
                    "WeatherApi:ApiKey no está configurada correctamente. " +
                    "En Azure DevOps, configure este valor en el Variable Group correspondiente.");
            }

            _logger.LogInformation(
                "Obteniendo clima actual para {City} usando base URL: {BaseUrl}",
                city, _settings.BaseUrl);

            var random = new Random();
            var index = random.Next(Descriptions.Length);

            var forecast = new WeatherForecast
            {
                City = city,
                TemperatureCelsius = Math.Round(random.NextDouble() * 35 + 5, 1),
                Description = Descriptions[index],
                Icon = Icons[index],
                Humidity = random.Next(20, 95),
                WindSpeedKmh = Math.Round(random.NextDouble() * 50, 1),
                Date = DateTime.Now
            };

            return Task.FromResult<WeatherForecast?>(forecast);
        }

        public Task<List<WeatherForecast>> GetForecastAsync(string city, int days = 5)
        {
            _logger.LogInformation(
                "Obteniendo pronóstico de {Days} días para {City}", days, city);

            var random = new Random();
            var forecasts = new List<WeatherForecast>();

            for (int i = 0; i < days; i++)
            {
                var index = random.Next(Descriptions.Length);
                forecasts.Add(new WeatherForecast
                {
                    City = city,
                    TemperatureCelsius = Math.Round(random.NextDouble() * 35 + 5, 1),
                    Description = Descriptions[index],
                    Icon = Icons[index],
                    Humidity = random.Next(20, 95),
                    WindSpeedKmh = Math.Round(random.NextDouble() * 50, 1),
                    Date = DateTime.Now.AddDays(i + 1)
                });
            }

            return Task.FromResult(forecasts);
        }
    }
}
