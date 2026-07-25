namespace MyCollection.Application.Common;

public interface IPasswordHasher
{
    string Hash(string password);

    /// <summary>雜湊格式無效時回傳 false，不擲例外。</summary>
    bool Verify(string hash, string password);
}
