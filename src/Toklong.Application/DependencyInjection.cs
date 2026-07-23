using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Toklong.Domain.Transactions;

namespace Toklong.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddMediatR(configuration => configuration.RegisterServicesFromAssembly(typeof(DependencyInjection).Assembly));
        services.AddSingleton<TransactionTransitionService>();
        return services;
    }
}
