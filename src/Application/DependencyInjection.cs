namespace Application;

using Application.Common.Behaviors;
using Application.Dashboard;
using Application.TontineManagement.Services;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(typeof(DependencyInjection).Assembly);
        });

        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly);

        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));

        services.AddScoped<ITontineAccessService, TontineAccessService>();
        services.AddScoped<IDashboardReadService, DashboardReadService>();
        services.AddScoped<Application.ProjectManagement.ProjetService>();
        services.AddScoped<Application.Gouvernance.Annuaire>();
        services.AddScoped<Application.Gouvernance.GouvernanceService>();

        return services;
    }
}
