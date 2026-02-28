using AgAiHub.Configuration;
using AgAiHub.Middleware;
using AgAiHub.Services;

var builder = WebApplication.CreateBuilder(args);

// ─── Configuration ───
builder.Services.Configure<AiHubOptions>(
    builder.Configuration.GetSection(AiHubOptions.SectionName));

// ─── AI Provider (swap by changing "Provider" in config) ───
var provider = builder.Configuration.GetValue<string>("AiHub:Provider") ?? "AzureOpenAi";

if (provider.Equals("OpenAi", StringComparison.OrdinalIgnoreCase))
    builder.Services.AddSingleton<IAiProvider, OpenAiProvider>();
else
    builder.Services.AddSingleton<IAiProvider, AzureOpenAiProvider>();

// ─── Core Services ───
builder.Services.AddSingleton<UsageTracker>();
builder.Services.AddScoped<AiService>();

// ─── API ───
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "AG AI Hub", Version = "v1" });
});

// ─── CORS (products call this API) ───
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

var app = builder.Build();

// ─── Swagger (dev only) ───
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors();

// ─── API Key Auth ───
app.UseMiddleware<ApiKeyAuthMiddleware>();

app.MapControllers();

app.Run();
