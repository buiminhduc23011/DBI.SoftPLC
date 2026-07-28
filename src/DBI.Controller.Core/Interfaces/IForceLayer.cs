namespace DBI.Controller.Core.Interfaces;

public interface IForceLayer
{
    void SetForce(string tag, object value);
    void ClearForce(string tag);
    void ClearAllForces();
    bool IsForced(string tag);
    IReadOnlyDictionary<string, object> GetAllForces();
}
