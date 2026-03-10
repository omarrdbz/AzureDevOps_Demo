using Azure_DevOps_TEST.Models;

namespace Azure_DevOps_TEST.Services
{
    public interface IWeatherService
    {
        Task<WeatherForecast?> GetCurrentWeatherAsync(string city);
        Task<List<WeatherForecast>> GetForecastAsync(string city, int days = 5);
    }
}
