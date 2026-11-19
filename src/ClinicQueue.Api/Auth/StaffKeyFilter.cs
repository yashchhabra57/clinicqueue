using System.Security.Cryptography;
using System.Text;

namespace ClinicQueue.Api.Auth;

public sealed class StaffOptions
{
    public const string HeaderName = "X-Staff-Key";

    /// <summary>Shared front-desk key. Required outside Development.</summary>
    public string? ApiKey { get; set; }
}

/// <summary>
/// Guards front-desk endpoints with a shared key in the X-Staff-Key header. This is deliberately
/// simple (one key per deployment) and compared in constant time so response timing doesn't leak it.
/// Per-user accounts are on the roadmap.
/// </summary>
public sealed class StaffKeyFilter(StaffOptions options) : IEndpointFilter
{
    private readonly byte[] _expected = Encoding.UTF8.GetBytes(options.ApiKey ?? string.Empty);

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        var supplied = context.HttpContext.Request.Headers[StaffOptions.HeaderName].ToString();
        if (_expected.Length == 0 || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(supplied), _expected))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Staff key required",
                detail: $"Send the front-desk key in the {StaffOptions.HeaderName} header.");
        }

        return await next(context);
    }
}

public static class StaffAuthExtensions
{
    public static IServiceCollection AddStaffAuth(this IServiceCollection services, IConfiguration config, IHostEnvironment env)
    {
        var options = new StaffOptions();
        config.GetSection("Staff").Bind(options);
        if (string.IsNullOrWhiteSpace(options.ApiKey) && !env.IsDevelopment())
        {
            // Fail closed: a production deploy without a key would otherwise expose patient initials.
            throw new InvalidOperationException("Staff:ApiKey must be configured outside Development.");
        }

        services.AddSingleton(options);
        services.AddSingleton<StaffKeyFilter>();
        return services;
    }

    public static TBuilder RequireStaffKey<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder =>
        builder.AddEndpointFilter<TBuilder, StaffKeyFilter>();
}
