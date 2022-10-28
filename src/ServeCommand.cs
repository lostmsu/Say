namespace Say;

class ServeCommand : VoiceCommand
{
    readonly List<string> urls = new();
    public ServeCommand()
    {
        IsCommand("serve");
        
        HasOption("url=", "The URL to serve", url => urls.Add(url));

        AllowsAnyAdditionalArguments("web host arguments");
    }

    public override int Run(string[] remainingArguments)
    {
        var app = WebApplication.Create(remainingArguments);

        if (urls.Count > 0)
        {
            foreach (string url in urls)
            {
                app.Urls.Add(url);
            }
        }

        app.MapPost("/say", async (HttpRequest request) =>
        {
            string text = await new StreamReader(request.Body).ReadToEndAsync();
            Synth.Speak(text);
            return Results.Ok();
        });

        app.Run();

        return 0;
    }
}