using System.Reflection;
using FluentValidation;
using ZonarHub.Application.Common;
using ZonarHub.Application.Abstractions;
using ZonarHub.Application.Features.AdminPermissions;
using Microsoft.Extensions.DependencyInjection;

namespace ZonarHub.Application;

/// <summary>
/// Composition root for the Application layer.
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// Registers MediatR handlers, FluentValidation validators, and cross-cutting pipeline behaviors.
    /// </summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var assembly = Assembly.GetExecutingAssembly();

        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(assembly);
            cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
        });

        services.AddValidatorsFromAssembly(assembly, includeInternalTypes: true);
        services.AddScoped<IUserPermissionService, UserPermissionService>();

        return services;
    }
}
