using System.Speech.Synthesis;

using ManyConsole.CommandLineUtils;

namespace Say;

abstract class VoiceCommand : ConsoleCommand
{
    protected SpeechSynthesizer Synth { get; } = new();

    protected VoiceCommand()
    {
        HasOption("r|rate=", $"{nameof(Synth.Rate)} of speech [-10 .. 10]", rate => Synth.Rate = int.Parse(rate));
        HasOption("vol|volume=", $"{nameof(Synth.Volume)} of speech [0 .. 100]", volume => Synth.Volume = int.Parse(volume));
        HasOption("v|voice=", $"Name of the {nameof(Synth.Voice)} to use", voice => Synth.SelectVoice(voice));
    }
}
