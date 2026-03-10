namespace Azure_DevOps_TEST.Models
{
    public class WeatherForecast
    {
        public string City { get; set; } = string.Empty;
        public double TemperatureCelsius { get; set; }
        public double TemperatureFahrenheit => TemperatureCelsius * 9 / 5 + 32;
        public string Description { get; set; } = string.Empty;
        public string Icon { get; set; } = string.Empty;
        public int Humidity { get; set; }
        public double WindSpeedKmh { get; set; }
        public DateTime Date { get; set; }
    }
}
