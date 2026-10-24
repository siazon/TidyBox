namespace PowerTools.Core.Modules;

public class ModuleRegistry
{
    private readonly Dictionary<string, IToolModule> _modules = new();

    /// <summary>Registers a module. A module with the same (case-sensitive) <see cref="IToolModule.ModuleId"/> replaces the earlier one.</summary>
    public void Register(IToolModule module)
    {
        _modules[module.ModuleId] = module;
    }

    public IReadOnlyList<IToolModule> GetAll()
    {
        return _modules.Values.ToList().AsReadOnly();
    }

    public IToolModule? GetById(string moduleId)
    {
        return _modules.TryGetValue(moduleId, out var module) ? module : null;
    }
}
