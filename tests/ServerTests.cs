using System.Net;
using System.Net.Http;
using System.Net.Sockets;

using Microsoft.VisualStudio.TestTools.UnitTesting;

[assembly: DoNotParallelize]

namespace Say.Tests;

[TestClass]
public sealed class ServerTests
{
    [AssemblyInitialize]
    public static void ConfigureHttpConnections(TestContext context)
        => ServicePointManager.DefaultConnectionLimit = 10;

    [TestMethod]
    [DataRow("say", HttpStatusCode.OK, "Text to say")]
    [DataRow("SAY/", HttpStatusCode.OK, "Bonjour café")]
    [DataRow("say", HttpStatusCode.OK, "")]
    [DataRow("say", HttpStatusCode.MethodNotAllowed, null)]
    [DataRow("missing", HttpStatusCode.NotFound, null)]
    public async Task RoutesRequests(string path, HttpStatusCode expected, string? text)
    {
        await using var server = new TestServer();
        await server.AssertResponseAsync(path, expected, text);
    }

    [TestMethod]
    public async Task IncompleteUploadDoesNotBlockOtherRequests()
    {
        await using var server = new TestServer();
        using var upload = await server.OpenIncompletePostAsync();
        await server.AssertResponseAsync("say", HttpStatusCode.MethodNotAllowed);
        await server.AssertResponseAsync("say", HttpStatusCode.OK, "Hi");
    }

    [TestMethod]
    public async Task AbortedUploadDoesNotTerminateServer()
    {
        await using var server = new TestServer();
        using var upload = await server.OpenIncompletePostAsync();
        upload.Client.LingerState = new LingerOption(true, 0);
        upload.Dispose();
        // Let the body-read and response-cleanup failures occur before checking survival.
        await Task.Delay(200);
        await server.AssertResponseAsync("say", HttpStatusCode.MethodNotAllowed);
        await server.AssertResponseAsync("say", HttpStatusCode.OK, "Still running");
    }

    [TestMethod]
    public async Task ConcurrentSpeechRequestsComplete()
    {
        await using var server = new TestServer();
        await Task.WhenAll(
            server.AssertResponseAsync("say", HttpStatusCode.OK, "First speech request"),
            server.AssertResponseAsync("say", HttpStatusCode.OK, "Second speech request"),
            server.AssertResponseAsync("say", HttpStatusCode.MethodNotAllowed));
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task ShutdownCompletesBeforeStartupOrWhileIdle(bool alreadyCanceled)
    {
        await using var server = new TestServer(alreadyCanceled);
        await server.StopAsync();
    }

    [TestMethod]
    public async Task ShutdownDrainsIncompleteUpload()
    {
        await using var server = new TestServer();
        using var upload = await server.OpenIncompletePostAsync();
        await server.StopAsync();
    }

    [TestMethod]
    public async Task ShutdownInterruptsActiveAndQueuedSpeech()
    {
        await using var server = new TestServer();
        server.Synth.Rate = 0;
        TaskCompletionSource<bool> speechStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        server.Synth.SpeakStarted += (sender, args) => speechStarted.TrySetResult(true);
        string longText = string.Join(" ", Enumerable.Repeat("This speech must stop promptly when shutdown is requested.", 100));
        using var activeText = new StringContent(longText);
        Task<HttpResponseMessage> active = server.Client.PostAsync("say", activeText);
        await TestServer.WithinAsync(speechStarted.Task, "Speech did not start.");
        using var queuedText = new StringContent("Queued speech must also stop.");
        Task<HttpResponseMessage> queued = server.Client.PostAsync("say", queuedText);
        await server.AssertResponseAsync("missing", HttpStatusCode.NotFound);
        Assert.IsFalse(active.IsCompleted, "The first speech request must still be active when shutdown begins.");

        await server.StopAsync();
        await ObserveStoppedRequestAsync(active);
        await ObserveStoppedRequestAsync(queued);
    }

    static async Task ObserveStoppedRequestAsync(Task<HttpResponseMessage> request)
    {
        try { using var response = await request; }
        catch (HttpRequestException) { }
        catch (TaskCanceledException) { }
    }
}
