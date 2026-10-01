using System.Globalization;
using System.Speech.Synthesis;

using Say;

try
{
    return await RunAsync(args);
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception.Message);
    return 1;
}

static async Task<int> RunAsync(string[] args)
{
    List<string> text = new();
    List<string> urls = new();
    bool serve = false;
    bool verbose = false;
    bool parseOptions = true;
    int rate = 0;
    int volume = 100;
    string? voice = null;

    for (int i = 0; i < args.Length; i++)
    {
        string argument = args[i];
        if (!parseOptions || !argument.StartsWith("-", StringComparison.Ordinal))
        {
            text.Add(argument);
            continue;
        }

        string option = argument;
        string? value = null;
        int equalsIndex = argument.IndexOf('=');
        if (equalsIndex >= 0)
        {
            option = argument.Substring(0, equalsIndex);
            value = argument.Substring(equalsIndex + 1);
        }

        switch (option)
        {
            case "--help":
                RequireFlag(option, value);
                PrintUsage(Console.Out);
                return 0;
            case "--serve":
                RequireFlag(option, value);
                serve = true;
                break;
            case "--verbose":
                verbose = value is null || bool.Parse(value);
                break;
            case "-r":
            case "--rate":
                rate = ReadNumber(option, ReadValue(option, value, args, ref i), -10, 10);
                break;
            case "--vol":
            case "--volume":
                volume = ReadNumber(option, ReadValue(option, value, args, ref i), 0, 100);
                break;
            case "-v":
            case "--voice":
                voice = ReadValue(option, value, args, ref i);
                break;
            case "--url":
                urls.Add(ReadValue(option, value, args, ref i));
                break;
            case "--":
                RequireFlag(option, value);
                parseOptions = false;
                break;
            default:
                throw new ArgumentException($"Unknown option: {option}. Use say --help for usage.");
        }
    }

    if (!serve && text.Count == 0)
    {
        PrintUsage(Console.Error);
        return 1;
    }
    if (serve && text.Count > 0)
        throw new ArgumentException("--serve does not accept text arguments.");
    if (!serve && urls.Count > 0)
        throw new ArgumentException("--url requires --serve.");

    using var synth = new SpeechSynthesizer { Rate = rate, Volume = volume };
    if (voice is not null)
        synth.SelectVoice(voice);
    if (verbose)
        Console.WriteLine($"Rate: {synth.Rate}, Volume: {synth.Volume}, Voice: {synth.Voice.Name}");

    if (serve)
        return await new ServeCommand(synth, urls).RunAsync();

    synth.Speak(string.Join(" ", text));
    return 0;
}

static void RequireFlag(string option, string? value)
{
    if (value is not null)
        throw new ArgumentException($"{option} does not accept a value.");
}

static string ReadValue(string option, string? value, string[] args, ref int index)
{
    if (value is null)
    {
        if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal))
            throw new ArgumentException($"Missing value for {option}.");
        value = args[++index];
    }
    if (value.Length == 0)
        throw new ArgumentException($"Missing value for {option}.");
    return value;
}

static int ReadNumber(string option, string value, int minimum, int maximum)
{
    if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number)
        || number < minimum || number > maximum)
        throw new ArgumentException($"{option} must be an integer between {minimum} and {maximum}.");
    return number;
}

static void PrintUsage(TextWriter output)
{
    output.WriteLine("Usage:");
    output.WriteLine("  say <text> <to> <say>");
    output.WriteLine("  say --help");
    output.WriteLine("  say --serve [--url <url>]");
    output.WriteLine();
    output.WriteLine("Text arguments are joined with spaces and spoken aloud.");
    output.WriteLine("Options:");
    output.WriteLine("  -r, --rate <rate>      Speech rate [-10 .. 10] (default: 0)");
    output.WriteLine("      --volume <volume>  Volume [0 .. 100] (default: 100; alias: --vol)");
    output.WriteLine("  -v, --voice <name>     Voice name");
    output.WriteLine("      --verbose          Print speech settings");
    output.WriteLine("      --url <url>        Server URL (repeatable; default: http://localhost:5000/)");
    output.WriteLine("      --                 Treat remaining arguments as text");
    output.WriteLine();
    output.WriteLine("Server: POST plain text to /say. Press Ctrl+C to stop.");
}
