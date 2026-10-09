using System.Net;
using System.Text;
using SfUi.Etl.Sources;
using Xunit;

namespace SfUi.Tests;

public class RestSourceTests
{
    // ---- 基本 ----

    [Fact]
    public void None_SinglePage_ParsesRecordsAndColumns()
    {
        var handler = new ScriptedHandler(
            (200, """[{"Name":"Alice","Age":30},{"Name":"Bob","Nested":{"x":1},"Flag":true,"Missing":null}]""", null));
        var source = Create(new RestSourceOptions { Url = "https://api.example.com/v1/items" }, handler);

        Assert.Equal(1, handler.RequestUrls.Count);
        Assert.Equal("https://api.example.com/v1/items", handler.RequestUrls[0]);
        Assert.Equal(new[] { "Name", "Age", "Nested", "Flag", "Missing" }, source.Columns);

        var rows = source.ReadRows().ToList();
        Assert.Equal(2, rows.Count);
        Assert.Equal("Alice", rows[0][0]);
        Assert.Equal("30", rows[0][1]);
        Assert.Equal("Bob", rows[1][0]);
        Assert.Equal("""{"x":1}""", rows[1][2]);
        Assert.Equal("true", rows[1][3]);
        Assert.Null(rows[1][4]);
    }

    [Fact]
    public void ArrayProperty_ObjectRoot_UsesProperty()
    {
        var handler = new ScriptedHandler((200, """{"data":[{"Id":"a"}]}""", null));
        var source = Create(new RestSourceOptions { Url = "https://api.example.com/v1/items", ArrayProperty = "data" }, handler);

        Assert.Equal(new[] { "Id" }, source.Columns);
        Assert.Equal("a", source.ReadRows().Single()[0]);
    }

    [Fact]
    public void EmptyBody_YieldsNoRecords()
    {
        var handler = new ScriptedHandler((200, "", null));
        var source = Create(new RestSourceOptions { Url = "https://api.example.com/v1/items" }, handler);

        Assert.Empty(source.Columns);
        Assert.Empty(source.ReadRows());
    }

    // ---- 認証 ----

    [Fact]
    public void Bearer_SendsAuthorizationHeader()
    {
        var handler = new ScriptedHandler((200, "[]", null));
        Create(new RestSourceOptions
        {
            Url = "https://api.example.com/v1/items",
            AuthKind = "Bearer",
            BearerToken = "tok-123",
        }, handler);

        var auth = handler.Requests[0].Headers.Authorization;
        Assert.NotNull(auth);
        Assert.Equal("Bearer", auth!.Scheme);
        Assert.Equal("tok-123", auth.Parameter);
    }

    [Fact]
    public void Basic_SendsBase64Credentials()
    {
        var handler = new ScriptedHandler((200, "[]", null));
        Create(new RestSourceOptions
        {
            Url = "https://api.example.com/v1/items",
            AuthKind = "Basic",
            BasicUser = "u1",
            BasicPassword = "p@ss",
        }, handler);

        var auth = handler.Requests[0].Headers.Authorization;
        Assert.NotNull(auth);
        Assert.Equal("Basic", auth!.Scheme);
        Assert.Equal(
            Convert.ToBase64String(Encoding.UTF8.GetBytes("u1:p@ss")),
            auth.Parameter);
    }

    [Fact]
    public void CustomHeaders_Applied_IgnoringBlankAndCommentLines()
    {
        var handler = new ScriptedHandler((200, "[]", null));
        Create(new RestSourceOptions
        {
            Url = "https://api.example.com/v1/items",
            AuthKind = "Header",
            Headers = "X-Api-Key: abc\r\n# comment\r\n\r\nX-Extra: two",
        }, handler);

        var request = handler.Requests[0];
        Assert.Equal("abc", string.Join(",", request.Headers.GetValues("X-Api-Key")));
        Assert.Equal("two", string.Join(",", request.Headers.GetValues("X-Extra")));
    }

    // ---- ページング ----

    [Fact]
    public void Offset_PagesWithParameters_StopsOnShortPage()
    {
        var handler = new ScriptedHandler(
            (200, """[{"Id":"1"},{"Id":"2"}]""", null),
            (200, """[{"Id":"3"},{"Id":"4"}]""", null),
            (200, """[{"Id":"5"}]""", null));
        var source = Create(new RestSourceOptions
        {
            Url = "https://api.example.com/v1/items?token=t",
            Paging = "Offset",
            PageSize = 2,
        }, handler);

        Assert.Equal(3, handler.RequestUrls.Count);
        Assert.Equal("https://api.example.com/v1/items?token=t&offset=0&limit=2", handler.RequestUrls[0]);
        Assert.Equal("https://api.example.com/v1/items?token=t&offset=2&limit=2", handler.RequestUrls[1]);
        Assert.Equal("https://api.example.com/v1/items?token=t&offset=4&limit=2", handler.RequestUrls[2]);
        Assert.Equal(5, source.ReadRows().Count());
    }

    [Fact]
    public void Link_FollowsRelNextUrl()
    {
        var handler = new ScriptedHandler(
            (200, """[{"Id":"1"}]""", "<https://api.example.com/v1/items?page=2>; rel=\"next\", <https://api.example.com/v1/items?page=9>; rel=\"last\""),
            (200, """[{"Id":"2"},{"Id":"3"}]""", null));
        var source = Create(new RestSourceOptions
        {
            Url = "https://api.example.com/v1/items",
            Paging = "Link",
        }, handler);

        Assert.Equal(2, handler.RequestUrls.Count);
        Assert.Equal("https://api.example.com/v1/items", handler.RequestUrls[0]);
        Assert.Equal("https://api.example.com/v1/items?page=2", handler.RequestUrls[1]);
        Assert.Equal(3, source.ReadRows().Count());
    }

    [Fact]
    public void Cursor_RepeatsWithCursorParam_UntilEmpty()
    {
        var handler = new ScriptedHandler(
            (200, """{"data":[{"Id":"1"}],"nextCursor":"abc"}""", null),
            (200, """{"data":[{"Id":"2"}],"nextCursor":null}""", null));
        var source = Create(new RestSourceOptions
        {
            Url = "https://api.example.com/v1/items",
            Paging = "Cursor",
            ArrayProperty = "data",
        }, handler);

        Assert.Equal(2, handler.RequestUrls.Count);
        Assert.Equal("https://api.example.com/v1/items", handler.RequestUrls[0]);
        Assert.Equal("https://api.example.com/v1/items?cursor=abc", handler.RequestUrls[1]);
        Assert.Equal(2, source.ReadRows().Count());
    }

    [Fact]
    public void MaxPages_Throws()
    {
        var handler = new ScriptedHandler(
            (200, """[{"Id":"1"},{"Id":"2"}]""", null),
            (200, """[{"Id":"3"},{"Id":"4"}]""", null),
            (200, """[{"Id":"5"},{"Id":"6"}]""", null));
        var options = new RestSourceOptions
        {
            Url = "https://api.example.com/v1/items",
            Paging = "Offset",
            PageSize = 2,
            MaxPages = 2,
        };

        var ex = Assert.Throws<InvalidOperationException>(() => Create(options, handler));
        Assert.Contains("最大ページ数", ex.Message);
        Assert.Equal(2, handler.RequestUrls.Count);
    }

    // ---- エラー ----

    [Fact]
    public void NonSuccess_Throws_WithStatusAndBody()
    {
        var handler = new ScriptedHandler((404, "not found", null));
        var ex = Assert.Throws<InvalidOperationException>(() => Create(
            new RestSourceOptions { Url = "https://api.example.com/v1/items" }, handler));

        Assert.Contains("404", ex.Message);
        Assert.Contains("not found", ex.Message);
    }

    [Fact]
    public void NonJsonBody_Throws()
    {
        var handler = new ScriptedHandler((200, "<html>oops</html>", null));
        var ex = Assert.Throws<InvalidOperationException>(() => Create(
            new RestSourceOptions { Url = "https://api.example.com/v1/items" }, handler));

        Assert.Contains("JSON", ex.Message);
    }

    [Fact]
    public void InvalidUrl_Throws()
    {
        Assert.Throws<ArgumentException>(() => new RestSource(new RestSourceOptions { Url = "not a url" }));
        Assert.Throws<ArgumentException>(() => new RestSource(new RestSourceOptions { Url = "ftp://example.com/x" }));
    }

    // ---- ヘルパー ----

    private static RestSource Create(RestSourceOptions options, ScriptedHandler handler)
        => new(options, new HttpClient(handler));

    private sealed class ScriptedHandler : HttpMessageHandler
    {
        private readonly Queue<(int Status, string Body, string? Link)> _pages = new();

        public ScriptedHandler(params (int Status, string Body, string? Link)[] pages)
        {
            foreach (var page in pages)
            {
                _pages.Enqueue(page);
            }
        }

        public List<HttpRequestMessage> Requests { get; } = new();

        public List<string> RequestUrls { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            RequestUrls.Add(request.RequestUri!.ToString());

            var (status, body, link) = _pages.Count > 0 ? _pages.Dequeue() : (200, "[]", null);
            var response = new HttpResponseMessage((HttpStatusCode)status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
            if (link is not null)
            {
                response.Headers.TryAddWithoutValidation("Link", link);
            }

            return Task.FromResult(response);
        }
    }
}
