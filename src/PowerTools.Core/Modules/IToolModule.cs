namespace PowerTools.Core.Modules;

public interface IToolModule
{
    string ModuleId { get; }
    string DisplayName { get; }
    Task InitializeAsync();
}
