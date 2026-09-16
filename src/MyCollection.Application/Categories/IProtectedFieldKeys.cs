namespace MyCollection.Application.Categories;

/// <summary>
/// 受保護的欄位鍵：來源用字面值定址、不可改名也不可撤回宣告（ADR-0012 §四）。
/// 集合是靜態的，不隨哪些 provider 有註冊而變。
/// </summary>
public interface IProtectedFieldKeys
{
    bool IsProtected(string key);
}
