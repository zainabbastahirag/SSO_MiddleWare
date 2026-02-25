using Microsoft.Extensions.DependencyInjection;

namespace GraphEmailService;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddGraphEmailService(this IServiceCollection services)
    {
        services.AddScoped<IGraphEmailService, GraphEmailService>();
        return services;
    }
}
