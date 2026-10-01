using System.Net;
using System.Speech.Synthesis;

namespace Say;

class ServeCommand
{
    readonly SpeechSynthesizer synth;
    readonly List<string> urls;

    public ServeCommand(SpeechSynthesizer synth, List<string> urls)
    {
        this.synth = synth;
        this.urls = urls;
    }

    public async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        using var listener = new HttpListener();
        using var stopping = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var speechLock = new SemaphoreSlim(1, 1);
        List<Task> requests = new();
        if (urls.Count == 0)
            listener.Prefixes.Add("http://localhost:5000/");
        else
            foreach (string url in urls)
                listener.Prefixes.Add(url.EndsWith("/", StringComparison.Ordinal) ? url : url + "/");

        listener.Start();
        foreach (string prefix in listener.Prefixes)
            Console.WriteLine($"Listening on {prefix}");
        Console.WriteLine("POST text to /say. Press Ctrl+C to stop.");

        EventHandler<SpeakStartedEventArgs> speechStartedHandler = (sender, args) =>
        {
            if (stopping.IsCancellationRequested)
                synth.SpeakAsyncCancelAll();
        };
        synth.SpeakStarted += speechStartedHandler;
        using var shutdown = stopping.Token.Register(() =>
        {
            synth.SpeakAsyncCancelAll();
            listener.Stop();
        });
        ConsoleCancelEventHandler cancelHandler = (sender, args) =>
        {
            args.Cancel = true;
            stopping.Cancel();
        };
        Console.CancelKeyPress += cancelHandler;
        try
        {
            while (listener.IsListening)
            {
                HttpListenerContext context;
                try
                {
                    context = await listener.GetContextAsync();
                }
                catch (HttpListenerException) when (stopping.IsCancellationRequested)
                {
                    break;
                }
                catch (ObjectDisposedException) when (stopping.IsCancellationRequested)
                {
                    break;
                }

                requests.RemoveAll(request => request.IsCompleted);
                requests.Add(HandleRequestAsync(context, speechLock, stopping.Token));
            }
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
            stopping.Cancel();
            try
            {
                await Task.WhenAll(requests);
            }
            finally
            {
                synth.SpeakStarted -= speechStartedHandler;
            }
        }
        return 0;
    }

    async Task HandleRequestAsync(HttpListenerContext context, SemaphoreSlim speechLock, CancellationToken stopping)
    {
        try
        {
            try
            {
                if (!string.Equals(context.Request.Url.AbsolutePath.TrimEnd('/'), "/say", StringComparison.OrdinalIgnoreCase))
                    context.Response.StatusCode = (int)HttpStatusCode.NotFound;
                else if (context.Request.HttpMethod != "POST")
                {
                    context.Response.StatusCode = (int)HttpStatusCode.MethodNotAllowed;
                    context.Response.AddHeader("Allow", "POST");
                }
                else
                {
                    using var reader = new StreamReader(context.Request.InputStream);
                    string text = await reader.ReadToEndAsync();
                    await speechLock.WaitAsync(stopping);
                    try
                    {
                        await Task.Run(() => synth.Speak(text), stopping);
                    }
                    finally
                    {
                        speechLock.Release();
                    }
                    context.Response.StatusCode = (int)HttpStatusCode.OK;
                }
            }
            catch (Exception exception)
            {
                if (!stopping.IsCancellationRequested)
                    Console.Error.WriteLine(exception.Message);
                context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
            }
            finally
            {
                context.Response.Close();
            }
        }
        catch (Exception exception)
        {
            if (!stopping.IsCancellationRequested)
                Console.Error.WriteLine(exception.Message);
        }
    }
}
