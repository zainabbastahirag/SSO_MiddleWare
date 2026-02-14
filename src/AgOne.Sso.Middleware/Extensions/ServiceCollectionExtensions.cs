using AgOne.Sso.Models;
using AgOne.Sso.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AgOne.Sso.Extensions;

/// <summary>
/// Extension methods for registering AG ONE SSO services in the DI container.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers all AG ONE SSO services: options, token client, and supporting services.
    /// Reads configuration from the "AgOneSso" section of appsettings.json.
    /// 
    /// <code>
    /// // In Program.cs:
    /// builder.Services.AddAgOneSso(builder.Configuration);
    /// </code>
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The application configuration.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddAgOneSso(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        return services.AddAgOneSso(configuration, _ => { });
    }

    /// <summary>
    /// Registers all AG ONE SSO services with optional programmatic configuration override.
    /// 
    /// <code>
    /// // In Program.cs:
    /// builder.Services.AddAgOneSso(builder.Configuration, options =>
    /// {
    ///     options.AnonymousPaths.Add("/api/health");
    ///     options.IsAgOneGateway = true; // If this IS AG ONE
    /// });
    /// </code>
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The application configuration.</param>
    /// <param name="configure">Optional action to further configure options after binding.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddAgOneSso(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<AgOneSsoOptions> configure)
    {
        // Bind the configuration section
        services.Configure<AgOneSsoOptions>(options =>
        {
            configuration.GetSection(AgOneSsoOptions.SectionName).Bind(options);
            configure(options);
        });

        // Read options to configure HttpClient
        var options = new AgOneSsoOptions();
        configuration.GetSection(AgOneSsoOptions.SectionName).Bind(options);
        configure(options);

        // Register the AG ONE token client as a typed HttpClient
        if (!options.IsAgOneGateway)
        {
            services.AddHttpClient<IAgOneTokenClient, AgOneTokenClient>(client =>
            {
                if (!string.IsNullOrEmpty(options.AgOneBaseUrl))
                {
                    client.BaseAddress = new Uri(options.AgOneBaseUrl.TrimEnd('/') + "/");
                }

                client.Timeout = TimeSpan.FromSeconds(30);
                client.DefaultRequestHeaders.Add("Accept", "application/json");
            })
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                // Trust AG ONE's SSL certificate in development
                ServerCertificateCustomValidationCallback =
                    HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
            });
        }
        else
        {
            // On AG ONE gateway, register a no-op token client
            services.AddSingleton<IAgOneTokenClient, NoOpTokenClient>();
        }

        return services;
    }

    /// <summary>
    /// No-op token client used when running on the AG ONE gateway itself.
    /// AG ONE handles its own token lifecycle, so no external refresh calls are needed.
    /// </summary>
    private class NoOpTokenClient : IAgOneTokenClient
    {
        public Task<string?> ValidateAndRefreshTokenAsync(string currentToken, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<string?>(null);
        }
    }
}
