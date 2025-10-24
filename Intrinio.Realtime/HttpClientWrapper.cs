using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Intrinio.Realtime;

public class HttpClientWrapper : IHttpClient
{
    private readonly HttpClient _wrapped;

    public HttpClientWrapper(HttpClient wrapped)
    {
        _wrapped = wrapped ?? throw new ArgumentNullException(nameof(wrapped));
    }

    public Uri? BaseAddress
    {
        get => _wrapped.BaseAddress;
        set => _wrapped.BaseAddress = value;
    }

    public HttpRequestHeaders DefaultRequestHeaders => _wrapped.DefaultRequestHeaders;

    public Version DefaultRequestVersion
    {
        get => _wrapped.DefaultRequestVersion;
        set => _wrapped.DefaultRequestVersion = value;
    }

    public HttpVersionPolicy DefaultVersionPolicy
    {
        get => _wrapped.DefaultVersionPolicy;
        set => _wrapped.DefaultVersionPolicy = value;
    }

    public long MaxResponseContentBufferSize
    {
        get => _wrapped.MaxResponseContentBufferSize;
        set => _wrapped.MaxResponseContentBufferSize = value;
    }

    public TimeSpan Timeout
    {
        get => _wrapped.Timeout;
        set => _wrapped.Timeout = value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void CancelPendingRequests() => _wrapped.CancelPendingRequests();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Task<HttpResponseMessage> DeleteAsync(string? requestUri) => _wrapped.DeleteAsync(requestUri);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Task<HttpResponseMessage> DeleteAsync(string? requestUri, CancellationToken cancellationToken) => _wrapped.DeleteAsync(requestUri, cancellationToken);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Task<HttpResponseMessage> DeleteAsync(Uri? requestUri) => _wrapped.DeleteAsync(requestUri);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Task<HttpResponseMessage> DeleteAsync(Uri? requestUri, CancellationToken cancellationToken) => _wrapped.DeleteAsync(requestUri, cancellationToken);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Task<HttpResponseMessage> GetAsync(string? requestUri) => _wrapped.GetAsync(requestUri);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Task<HttpResponseMessage> GetAsync(string? requestUri, HttpCompletionOption completionOption) => _wrapped.GetAsync(requestUri, completionOption);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Task<HttpResponseMessage> GetAsync(string? requestUri, HttpCompletionOption completionOption, CancellationToken cancellationToken) => _wrapped.GetAsync(requestUri, completionOption, cancellationToken);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Task<HttpResponseMessage> GetAsync(string? requestUri, CancellationToken cancellationToken) => _wrapped.GetAsync(requestUri, cancellationToken);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Task<HttpResponseMessage> GetAsync(Uri? requestUri) => _wrapped.GetAsync(requestUri);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Task<HttpResponseMessage> GetAsync(Uri? requestUri, HttpCompletionOption completionOption) => _wrapped.GetAsync(requestUri, completionOption);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Task<HttpResponseMessage> GetAsync(Uri? requestUri, HttpCompletionOption completionOption, CancellationToken cancellationToken) => _wrapped.GetAsync(requestUri, completionOption, cancellationToken);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Task<HttpResponseMessage> GetAsync(Uri? requestUri, CancellationToken cancellationToken) => _wrapped.GetAsync(requestUri, cancellationToken);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Task<byte[]> GetByteArrayAsync(string? requestUri) => _wrapped.GetByteArrayAsync(requestUri);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Task<byte[]> GetByteArrayAsync(string? requestUri, CancellationToken cancellationToken) => _wrapped.GetByteArrayAsync(requestUri, cancellationToken);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Task<byte[]> GetByteArrayAsync(Uri? requestUri) => _wrapped.GetByteArrayAsync(requestUri);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Task<byte[]> GetByteArrayAsync(Uri? requestUri, CancellationToken cancellationToken) => _wrapped.GetByteArrayAsync(requestUri, cancellationToken);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Task<Stream> GetStreamAsync(string? requestUri) => _wrapped.GetStreamAsync(requestUri);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Task<Stream> GetStreamAsync(string? requestUri, CancellationToken cancellationToken) => _wrapped.GetStreamAsync(requestUri, cancellationToken);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Task<Stream> GetStreamAsync(Uri? requestUri) => _wrapped.GetStreamAsync(requestUri);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Task<Stream> GetStreamAsync(Uri? requestUri, CancellationToken cancellationToken) => _wrapped.GetStreamAsync(requestUri, cancellationToken);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Task<string> GetStringAsync(string? requestUri) => _wrapped.GetStringAsync(requestUri);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Task<string> GetStringAsync(string? requestUri, CancellationToken cancellationToken) => _wrapped.GetStringAsync(requestUri, cancellationToken);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Task<string> GetStringAsync(Uri? requestUri) => _wrapped.GetStringAsync(requestUri);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Task<string> GetStringAsync(Uri? requestUri, CancellationToken cancellationToken) => _wrapped.GetStringAsync(requestUri, cancellationToken);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Task<HttpResponseMessage> PatchAsync(string? requestUri, HttpContent? content) => _wrapped.PatchAsync(requestUri, content);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Task<HttpResponseMessage> PatchAsync(string? requestUri, HttpContent? content, CancellationToken cancellationToken) => _wrapped.PatchAsync(requestUri, content, cancellationToken);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Task<HttpResponseMessage> PatchAsync(Uri? requestUri, HttpContent? content) => _wrapped.PatchAsync(requestUri, content);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Task<HttpResponseMessage> PatchAsync(Uri? requestUri, HttpContent? content, CancellationToken cancellationToken) => _wrapped.PatchAsync(requestUri, content, cancellationToken);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Task<HttpResponseMessage> PostAsync(string? requestUri, HttpContent? content) => _wrapped.PostAsync(requestUri, content);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Task<HttpResponseMessage> PostAsync(string? requestUri, HttpContent? content, CancellationToken cancellationToken) => _wrapped.PostAsync(requestUri, content, cancellationToken);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Task<HttpResponseMessage> PostAsync(Uri? requestUri, HttpContent? content) => _wrapped.PostAsync(requestUri, content);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Task<HttpResponseMessage> PostAsync(Uri? requestUri, HttpContent? content, CancellationToken cancellationToken) => _wrapped.PostAsync(requestUri, content, cancellationToken);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Task<HttpResponseMessage> PutAsync(string? requestUri, HttpContent? content) => _wrapped.PutAsync(requestUri, content);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Task<HttpResponseMessage> PutAsync(string? requestUri, HttpContent? content, CancellationToken cancellationToken) => _wrapped.PutAsync(requestUri, content, cancellationToken);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Task<HttpResponseMessage> PutAsync(Uri? requestUri, HttpContent? content) => _wrapped.PutAsync(requestUri, content);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Task<HttpResponseMessage> PutAsync(Uri? requestUri, HttpContent? content, CancellationToken cancellationToken) => _wrapped.PutAsync(requestUri, content, cancellationToken);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public HttpResponseMessage Send(HttpRequestMessage request) => _wrapped.Send(request);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public HttpResponseMessage Send(HttpRequestMessage request, HttpCompletionOption completionOption) => _wrapped.Send(request, completionOption);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public HttpResponseMessage Send(HttpRequestMessage request, HttpCompletionOption completionOption, CancellationToken cancellationToken) => _wrapped.Send(request, completionOption, cancellationToken);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken) => _wrapped.Send(request, cancellationToken);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request) => _wrapped.SendAsync(request);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, HttpCompletionOption completionOption) => _wrapped.SendAsync(request, completionOption);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, HttpCompletionOption completionOption, CancellationToken cancellationToken) => _wrapped.SendAsync(request, completionOption, cancellationToken);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => _wrapped.SendAsync(request, cancellationToken);

    public void Dispose() => _wrapped.Dispose();
}