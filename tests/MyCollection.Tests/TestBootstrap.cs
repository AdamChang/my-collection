using System.Runtime.CompilerServices;
using MyCollection.Infrastructure.Mongo;

namespace MyCollection.Tests;

/// <summary>
/// 在測試組件載入時就註冊 BSON 慣例。xunit 預設跨 test class 平行執行，
/// 若等到某個測試建構式才註冊，另一個 class 可能已經先序列化並永久快取錯誤的 class map。
/// </summary>
internal static class TestBootstrap
{
    [ModuleInitializer]
    internal static void Initialise() => MongoConventions.Register();
}
