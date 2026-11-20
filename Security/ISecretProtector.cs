namespace InventoryAPI.Security;

public interface ISecretProtector
{
    string Protect(string plaintext);
    string ProtectStoredValue(string? storedValue);
    bool TryUnprotect(string? storedValue, out string plaintext);
    string Unprotect(string storedValue);
    bool IsProtected(string? storedValue);
}