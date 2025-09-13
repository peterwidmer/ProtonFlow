using BpmnEngine.Engine;
using BpmnEngine.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ProtonFlow.Persistence.EfCore.Adapters;
using ProtonFlow.Persistence.EfCore.Storage;

namespace ProtonFlow.Persistence.EfCore.Extensions;

/// <summary>
/// Extension helpers enabling consumers to plug EF Core backed persistence into the ProtonFlow engine builder in a fluent manner.
/// Usage:
/// var engine = BpmnEngineBuilder.Create()
///     .UseEntityFramework(options => options.UseSqlite("Data Source=protonflow.db"))
///     .AddTaskHandler(...)
///     .Build();
/// </summary>
public static class BpmnEngineBuilderEfExtensions
{
    /// <summary>
    /// Registers EF Core backed persistence (process store + instance store + executor + runtime engine).
    /// Caller must supply a provider builder (e.g., UseSqlite / UseSqlServer / UseInMemoryDatabase).
    /// </summary>
    public static BpmnEngineBuilder UseEntityFramework(this BpmnEngineBuilder builder, Action<DbContextOptionsBuilder> dbOptions)
    {
        return builder.ConfigureServices(services =>
        {
            services.AddDbContext<ProtonFlowDbContext>(dbOptions, contextLifetime: ServiceLifetime.Scoped, optionsLifetime: ServiceLifetime.Singleton);
            services.AddScoped<IBpmnStorage, EfBpmnStorage>();
            // Adapter registrations to satisfy existing engine persistence contracts
            services.AddScoped<IProcessStore, EfProcessStore>();
            services.AddScoped<IInstanceStore, EfInstanceStore>();
            services.AddScoped<IJobStore, EfJobStore>();
            services.AddScoped<IProcessExecutor, SimpleProcessExecutor>();
            services.AddScoped<BpmnRuntimeEngine>();
        });
    }
}
