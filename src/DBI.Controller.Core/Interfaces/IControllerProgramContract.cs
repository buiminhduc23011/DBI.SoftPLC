namespace DBI.Controller.Core.Interfaces;

/// <summary>
/// Contract toi thieu ma Runtime can de nap va chay chuong trinh dieu khien.
/// </summary>
public interface IControllerProgramContract
{
    void Initialize(IMemoryImage memoryImage);
    void OnStart();
    void Execute();
    void OnStop();
}
