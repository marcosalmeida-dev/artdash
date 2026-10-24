using System.Globalization;
using ArtDash.Application.Common.Behaviors;
using ArtDash.Application.Common.Events;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ArtDash.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var assembly = typeof(DependencyInjection).Assembly;

        // FluentValidation ships Portuguese messages for its built-in rules.
        ValidatorOptions.Global.LanguageManager.Culture = new CultureInfo("pt-BR");
        services.AddValidatorsFromAssembly(assembly, includeInternalTypes: true);

        services.AddScoped<ISender, Sender>();

        // Registration order is pipeline order: logging wraps validation wraps the handler.
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));

        services.AddScoped<IDomainEventDispatcher, DomainEventDispatcher>();
        services.TryAddSingleton(TimeProvider.System);

        RegisterHandlers(services, assembly);

        return services;
    }

    /// <summary>Registers every request handler and domain event handler found in the assembly.</summary>
    private static void RegisterHandlers(IServiceCollection services, System.Reflection.Assembly assembly)
    {
        var handlerTypes = assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false, IsGenericTypeDefinition: false });

        foreach (var type in handlerTypes)
        {
            foreach (var contract in type.GetInterfaces().Where(i => i.IsGenericType))
            {
                var definition = contract.GetGenericTypeDefinition();
                if (definition == typeof(IRequestHandler<,>) || definition == typeof(IDomainEventHandler<>))
                {
                    services.AddScoped(contract, type);
                }
            }
        }
    }
}
