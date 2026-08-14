namespace Auraly.Platform.Application.Auth.Interfaces;

public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string password, string hash);
}
