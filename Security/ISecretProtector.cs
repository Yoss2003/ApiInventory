namespace InventoryAPI.Security;

public interface ISecretProtector
{
    string Protect(string plaintext);
    string Unprotect(string storedValue);
    bool IsProtected(string? storedValue);
}