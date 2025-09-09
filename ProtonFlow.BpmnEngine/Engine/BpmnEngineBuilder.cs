namespace BpmnEngine.Engine;

using BpmnEngine.Interfaces;
using BpmnEngine.Models;
using BpmnEngine.Stores;
using Microsoft.Extensions.DependencyInjection;

public class BpmnEngineBuilder
{
    private readonly IServiceCollection _services = new ServiceCollection();

    public static BpmnEngineBuilder Create() => new();

    public BpmnEngineBuilder UseInMemory()
    {
        _services.AddSingleton<IProcessStore, InMemoryProcessStore>();
        _services.AddSingleton<IInstanceStore, InMemoryInstanceStore>();
        _services.AddSingleton<IProcessExecutor, SimpleProcessExecutor>();
        _services.AddSingleton<BpmnRuntimeEngine>();
        return this;
    }

    /// <summary>
    /// Allows advanced scenarios (like EF Core persistence extension package) to configure services directly
    /// without modifying core engine assembly. The provided delegate receives the underlying <see cref="IServiceCollection"/>.
    /// </summary>
    public BpmnEngineBuilder ConfigureServices(Action<IServiceCollection> configure)
    {
        configure(_services);
        return this;
    }

    public BpmnEngineBuilder AddTaskHandler(string type, Func<BpmnEngine.Runtime.TaskContext, Task> handler)
    {
        _services.AddSingleton<ITaskHandler>(new DelegatedTaskHandler(type, handler));
        return this;
    }

    public BpmnRuntimeEngine Build()
    {
        return _services.BuildServiceProvider().GetRequiredService<BpmnRuntimeEngine>();
    }
}

internal class DelegatedTaskHandler : ITaskHandler
{
    private readonly Func<BpmnEngine.Runtime.TaskContext, Task> _handler;
    public string Type { get; }

    public DelegatedTaskHandler(string type, Func<BpmnEngine.Runtime.TaskContext, Task> handler)
    {
        Type = type;
        _handler = handler;
    }

    public Task ExecuteAsync(BpmnEngine.Runtime.TaskContext context, CancellationToken ct = default) => _handler(context);
}
