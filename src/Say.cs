using ManyConsole.CommandLineUtils;

using Say;

ConsoleCommandDispatcher.DispatchCommand(
    new[] { new SayCommand() },
    args, Console.Out);

class SayCommand : VoiceCommand
{
    bool verbose;

    public SayCommand()
    {
        IsCommand("say");

        SkipsCommandSummaryBeforeRunning();

        HasOption("verbose", "Verbose output", v => verbose = v == null || bool.Parse(v));

        AllowsAnyAdditionalArguments("phrases to say aloud");
    }

    public override int Run(string[] remainingArguments)
    {
        if (verbose)
        {
            Console.WriteLine($"Rate: {Synth.Rate}, Volume: {Synth.Volume}, Voice: {Synth.Voice.Name}");
        }

        foreach (string line in remainingArguments)
        {
            Synth.Speak(line);
        }

        return 0;
    }
}
