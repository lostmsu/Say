using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Speech.Synthesis;
using System.Text;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Say.Tests;

sealed class TestServer
{
    readonly CancellationTokenSource stopping = new();
    readonly Task<int> running;

    public SpeechSynthesizer Synth { get; }
    public HttpClient Client { get; }
    public Uri Url { get; }

    public TestServer(bool alreadyCanceled = false)
    {
        var portProbe = new TcpListener(IPAddress.Loopback, 0);
        portProbe.Start();
        int port;
        try { port = ((IPEndPoint)portProbe.LocalEndpoint).Port; }
        finally { portProbe.Stop(); }

        Url = new($"http://localhost:{port}/");
        Synth = new() { Volume = 0, Rate = 10 };
        Client = new(new HttpClientHandler { UseProxy = false })
        {
            BaseAddress = Url,
            Timeout = TimeSpan.FromSeconds(5)
        };
        if (alreadyCanceled)
            stopping.Cancel();
        running = new ServeCommand(Synth, new() { Url.AbsoluteUri }).RunAsync(stopping.Token);
    }

    public async Task<TcpClient> OpenIncompletePostAsync()
    {
        var client = new TcpClient();
        try
        {
            await client.ConnectAsync(Url.Host, Url.Port);
            byte[] request = Encoding.ASCII.GetBytes($"POST /say HTTP/1.1\r\nHost: {Url.Authority}\r\nContent-Length: 100\r\n\r\nx");
            await client.GetStream().WriteAsync(request, 0, request.Length);
            // Allow the server to enter the incomplete body's read before the next request.
            await Task.Delay(200);
            return client;
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    public async Task AssertResponseAsync(string path, HttpStatusCode expected, string? text = null)
    {
        using var request = new HttpRequestMessage(text is null ? HttpMethod.Get : HttpMethod.Post, path);
        if (text is not null)
            request.Content = new StringContent(text, Encoding.UTF8, "text/plain");
        using var response = await Client.SendAsync(request);
        Assert.AreEqual(expected, response.StatusCode);
        Assert.AreEqual(string.Empty, await response.Content.ReadAsStringAsync());
        if (expected == HttpStatusCode.MethodNotAllowed)
            CollectionAssert.AreEqual(new[] { "POST" }, response.Content.Headers.Allow.ToArray());
    }

    public async Task StopAsync()
    {
        stopping.Cancel();
        await WithinAsync(running, "Server shutdown timed out.");
        Assert.AreEqual(0, await running);
    }

    public async Task DisposeAsync()
    {
        try { await StopAsync(); }
        finally
        {
            Client.Dispose();
            Synth.Dispose();
            stopping.Dispose();
        }
    }

    public static async Task WithinAsync(Task task, string message)
    {
        Assert.AreSame(task, await Task.WhenAny(task, Task.Delay(5000)), message);
        await task;
    }
}
