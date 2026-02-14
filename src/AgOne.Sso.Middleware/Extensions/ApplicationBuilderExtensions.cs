using Microsoft.AspNetCore.Builder;

namespace AgOne.Sso.Extensions;

/// <summary>
/// Extension methods for adding the AG ONE SSO middleware to the request pipeline.
/// </summary>
public static class ApplicationBuilderExtensions
{
    /// <summary>
    /// Adds the AG ONE SSO middleware to the pipeline.
    /// Should be placed AFTER UseRouting() but BEFORE UseAuthorization() and MapControllers().
    /// 
    /// <code>
    /// // In Program.cs:
    /// app.UseRouting();
    /// app.UseAgOneSso();       // ← Add here
    /// app.UseAuthorization();
    /// app.MapControllers();
    /// </code>
    /// </summary>
    /// <param name="app">The application builder.</param>
    /// <returns>The application builder for chaining.</returns>
    public static IApplicationBuilder UseAgOneSso(this IApplicationBuilder app)
    {
        return app.UseMiddleware<AgOneSsoMiddleware>();
    }
}
