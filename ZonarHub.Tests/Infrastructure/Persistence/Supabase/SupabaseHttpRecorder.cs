using System.Net;
using System.Text;

namespace ZonarHub.Tests.Infrastructure.Persistence.Supabase;

/// <summary>
/// Fake PostgREST endpoint for repository tests: records every request (method, path, decoded query parts, Prefer
/// header, body) and answers with <c>respond</c>. The client is what <see cref="IHttpClientFactory"/> hands the
/// repositories for the Supabase client name.
/// </summary>
internal sealed class SupabaseHttpRecorder
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

    public SupabaseHttpRecorder(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        _respond = respond;
        Factory = new ClientFactory(new HttpClient(new RecordingHandler(this)) { BaseAddress = new Uri("https://example.invalid") });
    }

    public List<RecordedRequest> Requests { get; } = [];

    public IHttpClientFactory Factory { get; }

    public RecordedRequest Single() => Assert.Single(Requests);

    public static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) => new(status)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };

    /// <summary>A JSON body with the <c>Content-Range</c> PostgREST sends for <c>Prefer: count=exact</c>.</summary>
    public static HttpResponseMessage Counted(string body, string contentRange)
    {
        var response = Json(body);
        response.Content.Headers.TryAddWithoutValidation("Content-Range", contentRange);
        return response;
    }

    /// <summary>A PostgREST error body (<c>{ code, message, details, hint }</c>).</summary>
    public static HttpResponseMessage Problem(HttpStatusCode status, string code, string message) =>
        Json($$"""{"code":"{{code}}","message":"{{message.Replace("\"", "\\\"")}}","details":null,"hint":null}""", status);

    private sealed class ClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class RecordingHandler(SupabaseHttpRecorder recorder) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            var uri = request.RequestUri!;
            var query = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries).Select(Uri.UnescapeDataString).ToList();
            var prefer = request.Headers.TryGetValues("Prefer", out var values) ? string.Join(",", values) : null;
            recorder.Requests.Add(new RecordedRequest(request.Method.Method, uri.AbsolutePath, query, prefer, body, uri.Query));
            return recorder._respond(request);
        }
    }
}

/// <param name="Query">Query parts, URL-decoded.</param>
/// <param name="RawQuery">The query string as sent (still encoded).</param>
internal sealed record RecordedRequest(
    string Method,
    string Path,
    IReadOnlyList<string> Query,
    string? Prefer,
    string? Body,
    string RawQuery);
