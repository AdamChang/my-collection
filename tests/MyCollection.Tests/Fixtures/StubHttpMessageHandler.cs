using System.Net;

namespace MyCollection.Tests.Fixtures;

/// <summary>依 request URI 回傳預先準備好的回應，並記錄呼叫過的 URI。</summary>
public sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
{
    public List<Uri> Requests { get; } = [];

    /// <summary>最後一次請求的 body。IGDB 用 POST + APIcalypse 純文字查詢，斷言查詢內容需要它。</summary>
    public string? LastRequestBody { get; private set; }

    public System.Net.Http.Headers.HttpRequestHeaders? LastRequestHeaders { get; private set; }

    public static StubHttpMessageHandler Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(_ => new HttpResponseMessage(status)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
        });

    public static StubHttpMessageHandler Html(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(_ => new HttpResponseMessage(status)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "text/html")
        });

    public static StubHttpMessageHandler Status(HttpStatusCode status) =>
        new(_ => new HttpResponseMessage(status));

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request.RequestUri!);
        LastRequestHeaders = request.Headers;
        LastRequestBody = request.Content is null
            ? null
            : await request.Content.ReadAsStringAsync(cancellationToken);

        return responder(request);
    }

    public HttpClient CreateClient(string baseAddress) =>
        new(this) { BaseAddress = new Uri(baseAddress) };
}
