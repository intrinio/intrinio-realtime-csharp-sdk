using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Intrinio.Realtime;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Intrinio.Collections.RingBuffers;

namespace Intrinio.Realtime.Tests;

// Mock for IHttpClient
public class MockHttpClient : IHttpClient
{
    private readonly HttpRequestHeaders _defaultRequestHeaders = new HttpRequestMessage().Headers;
    private Dictionary<string, string> _responses = new Dictionary<string, string>();

    public void SetResponse(string url, string response)
    {
        _responses[url] = response;
    }

    public Uri? BaseAddress { get; set; }
    public HttpRequestHeaders DefaultRequestHeaders => _defaultRequestHeaders;
    public Version DefaultRequestVersion { get; set; } = HttpVersion.Version11;
    public HttpVersionPolicy DefaultVersionPolicy { get; set; } = HttpVersionPolicy.RequestVersionOrLower;
    public long MaxResponseContentBufferSize { get; set; } = 1024 * 1024;
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(100);

    public void CancelPendingRequests() { }

    public async Task<HttpResponseMessage> GetAsync(string? requestUri)
    {
        if (requestUri != null && _responses.TryGetValue(requestUri, out string resp))
        {
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(resp) };
        }
        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    // Other overloads and methods throw NotImplementedException
    public Task<HttpResponseMessage> GetAsync(string? requestUri, HttpCompletionOption completionOption) => throw new NotImplementedException();
    public Task<HttpResponseMessage> GetAsync(string? requestUri, HttpCompletionOption completionOption, CancellationToken cancellationToken) => throw new NotImplementedException();
    public Task<HttpResponseMessage> GetAsync(string? requestUri, CancellationToken cancellationToken) => throw new NotImplementedException();
    public Task<HttpResponseMessage> GetAsync(Uri? requestUri) => throw new NotImplementedException();
    public Task<HttpResponseMessage> GetAsync(Uri? requestUri, HttpCompletionOption completionOption) => throw new NotImplementedException();
    public Task<HttpResponseMessage> GetAsync(Uri? requestUri, HttpCompletionOption completionOption, CancellationToken cancellationToken) => throw new NotImplementedException();
    public Task<HttpResponseMessage> GetAsync(Uri? requestUri, CancellationToken cancellationToken) => throw new NotImplementedException();

    public Task<byte[]> GetByteArrayAsync(string? requestUri) => throw new NotImplementedException();
    public Task<byte[]> GetByteArrayAsync(string? requestUri, CancellationToken cancellationToken) => throw new NotImplementedException();
    public Task<byte[]> GetByteArrayAsync(Uri? requestUri) => throw new NotImplementedException();
    public Task<byte[]> GetByteArrayAsync(Uri? requestUri, CancellationToken cancellationToken) => throw new NotImplementedException();

    public Task<Stream> GetStreamAsync(string? requestUri) => throw new NotImplementedException();
    public Task<Stream> GetStreamAsync(string? requestUri, CancellationToken cancellationToken) => throw new NotImplementedException();
    public Task<Stream> GetStreamAsync(Uri? requestUri) => throw new NotImplementedException();
    public Task<Stream> GetStreamAsync(Uri? requestUri, CancellationToken cancellationToken) => throw new NotImplementedException();

    public Task<string> GetStringAsync(string? requestUri) => throw new NotImplementedException();
    public Task<string> GetStringAsync(string? requestUri, CancellationToken cancellationToken) => throw new NotImplementedException();
    public Task<string> GetStringAsync(Uri? requestUri) => throw new NotImplementedException();
    public Task<string> GetStringAsync(Uri? requestUri, CancellationToken cancellationToken) => throw new NotImplementedException();

    public Task<HttpResponseMessage> DeleteAsync(string? requestUri) => throw new NotImplementedException();
    public Task<HttpResponseMessage> DeleteAsync(string? requestUri, CancellationToken cancellationToken) => throw new NotImplementedException();
    public Task<HttpResponseMessage> DeleteAsync(Uri? requestUri) => throw new NotImplementedException();
    public Task<HttpResponseMessage> DeleteAsync(Uri? requestUri, CancellationToken cancellationToken) => throw new NotImplementedException();

    public Task<HttpResponseMessage> PatchAsync(string? requestUri, HttpContent? content) => throw new NotImplementedException();
    public Task<HttpResponseMessage> PatchAsync(string? requestUri, HttpContent? content, CancellationToken cancellationToken) => throw new NotImplementedException();
    public Task<HttpResponseMessage> PatchAsync(Uri? requestUri, HttpContent? content) => throw new NotImplementedException();
    public Task<HttpResponseMessage> PatchAsync(Uri? requestUri, HttpContent? content, CancellationToken cancellationToken) => throw new NotImplementedException();

    public Task<HttpResponseMessage> PostAsync(string? requestUri, HttpContent? content) => throw new NotImplementedException();
    public Task<HttpResponseMessage> PostAsync(string? requestUri, HttpContent? content, CancellationToken cancellationToken) => throw new NotImplementedException();
    public Task<HttpResponseMessage> PostAsync(Uri? requestUri, HttpContent? content) => throw new NotImplementedException();
    public Task<HttpResponseMessage> PostAsync(Uri? requestUri, HttpContent? content, CancellationToken cancellationToken) => throw new NotImplementedException();

    public Task<HttpResponseMessage> PutAsync(string? requestUri, HttpContent? content) => throw new NotImplementedException();
    public Task<HttpResponseMessage> PutAsync(string? requestUri, HttpContent? content, CancellationToken cancellationToken) => throw new NotImplementedException();
    public Task<HttpResponseMessage> PutAsync(Uri? requestUri, HttpContent? content) => throw new NotImplementedException();
    public Task<HttpResponseMessage> PutAsync(Uri? requestUri, HttpContent? content, CancellationToken cancellationToken) => throw new NotImplementedException();

    public HttpResponseMessage Send(HttpRequestMessage request) => throw new NotImplementedException();
    public HttpResponseMessage Send(HttpRequestMessage request, HttpCompletionOption completionOption) => throw new NotImplementedException();
    public HttpResponseMessage Send(HttpRequestMessage request, HttpCompletionOption completionOption, CancellationToken cancellationToken) => throw new NotImplementedException();
    public HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken) => throw new NotImplementedException();

    public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request) => throw new NotImplementedException();
    public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, HttpCompletionOption completionOption) => throw new NotImplementedException();
    public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, HttpCompletionOption completionOption, CancellationToken cancellationToken) => throw new NotImplementedException();
    public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => throw new NotImplementedException();

    public void Dispose() { }
}