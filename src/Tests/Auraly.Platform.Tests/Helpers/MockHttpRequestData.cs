using System.Net;
using System.Security.Claims;
using System.Text;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace Auraly.Platform.Tests.Helpers;

public class MockHttpRequestData : HttpRequestData
{
    private readonly MemoryStream _bodyStream;
    private readonly FunctionContext _functionContext;

    public MockHttpRequestData(FunctionContext functionContext, string method, string queryString, string? body = null)
        : base(functionContext)
    {
        _functionContext = functionContext;
        Method = method;
        Url = new Uri($"https://localhost/api/webhook{queryString}");
        
        if (body != null)
        {
            _bodyStream = new MemoryStream(Encoding.UTF8.GetBytes(body));
        }
        else
        {
            _bodyStream = new MemoryStream();
        }
    }

    public override Stream Body => _bodyStream;

    public override HttpHeadersCollection Headers { get; } = new HttpHeadersCollection();

    public override IReadOnlyCollection<IHttpCookie> Cookies { get; } = new List<IHttpCookie>();

    public override Uri Url { get; }

    public override IEnumerable<ClaimsIdentity> Identities { get; } = new List<ClaimsIdentity>();

    public override string Method { get; }

    public override HttpResponseData CreateResponse()
    {
        return new MockHttpResponseData(_functionContext);
    }
}

public class MockHttpResponseData : HttpResponseData
{
    private readonly MemoryStream _bodyStream;

    public MockHttpResponseData(FunctionContext functionContext)
        : base(functionContext)
    {
        Headers = new HttpHeadersCollection();
        _bodyStream = new MemoryStream();
        Body = _bodyStream;
    }

    public override HttpStatusCode StatusCode { get; set; } = HttpStatusCode.OK;

    public override HttpHeadersCollection Headers { get; set; }

    public override Stream Body { get; set; }

    public override HttpCookies Cookies { get; } = new MockHttpCookies();

    public async Task WriteStringAsync(string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        await _bodyStream.WriteAsync(bytes, 0, bytes.Length);
        _bodyStream.Position = 0;
    }
}

public class MockHttpCookies : HttpCookies
{
    public override void Append(IHttpCookie cookie)
    {
        // Mock implementation
    }

    public override void Append(string name, string value)
    {
        // Mock implementation
    }

    public override IHttpCookie CreateNew()
    {
        return new MockHttpCookie();
    }
}

public class MockHttpCookie : IHttpCookie
{
    public string Name { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public DateTimeOffset? Expires { get; set; }
    public double? MaxAge { get; set; }
    public string? Domain { get; set; }
    public string? Path { get; set; }
    public bool? Secure { get; set; }
    public bool? HttpOnly { get; set; }
    public SameSite SameSite { get; set; }
}
