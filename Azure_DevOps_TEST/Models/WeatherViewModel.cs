namespace Azure_DevOps_TEST.Models
{
    public class WeatherViewModel
    {
        public WeatherForecast? CurrentWeather { get; set; }
        public List<WeatherForecast> Forecast { get; set; } = [];
        public string? ErrorMessage { get; set; }
        public string Environment { get; set; } = string.Empty;
        public bool ApiKeyConfigured { get; set; }
        public bool DatabaseConfigured { get; set; }
    }
}
