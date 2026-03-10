namespace Azure_DevOps_TEST.Models
{
    /// <summary>
    /// Configuración de la API de clima. En Azure DevOps, estos valores se inyectan
    /// desde Variable Groups cifrados (vg-weatherapp-dev, vg-weatherapp-test, vg-weatherapp-prod).
    /// </summary>
    public class WeatherSettings
    {
        public const string SectionName = "WeatherApi";

        /// <summary>
        /// API Key para el servicio de clima externo.
        /// 🔒 SECRETO — En Azure DevOps se configura como variable cifrada en el Variable Group.
        /// </summary>
        public string ApiKey { get; set; } = string.Empty;

        /// <summary>
        /// URL base del servicio de clima.
        /// </summary>
        public string BaseUrl { get; set; } = string.Empty;

        /// <summary>
        /// Ciudad por defecto para consultas de clima.
        /// </summary>
        public string DefaultCity { get; set; } = "Madrid";

        /// <summary>
        /// Duración de caché en minutos.
        /// </summary>
        public int CacheDurationMinutes { get; set; } = 10;
    }
}
