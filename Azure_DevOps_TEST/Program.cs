using Azure_DevOps_TEST.Models;
using Azure_DevOps_TEST.Services;

var builder = WebApplication.CreateBuilder(args);

// Configuración fuertemente tipada — los valores se inyectan desde:
// - appsettings.json (desarrollo local)
// - Azure DevOps Variable Groups (CI/CD pipelines)
// - Variables de entorno (contenedores / IIS)
builder.Services.Configure<WeatherSettings>(
    builder.Configuration.GetSection(WeatherSettings.SectionName));

// Servicios
builder.Services.AddHttpClient();
builder.Services.AddScoped<IWeatherService, WeatherService>();
builder.Services.AddControllersWithViews();

// Health checks — usado por el pipeline CD para verificar post-deploy
builder.Services.AddHealthChecks();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

app.UseAuthorization();

// Endpoint de health check — el pipeline CD consulta este endpoint post-deploy
app.MapHealthChecks("/health");

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();


app.Run();
