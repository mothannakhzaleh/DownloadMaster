namespace DownloadMaster.Services.Speech;

/// <summary>
/// Picks the text-to-speech engine for this OS:
/// Windows → System.Speech; macOS → <c>say</c>; Linux → <c>espeak-ng</c> / <c>espeak</c>, else <c>spd-say</c> (speak only).
/// </summary>
internal static class SpeechEngineFactory
{
    public static ISpeechEngine Create()
    {
        if (OperatingSystem.IsWindows())
        {
            try
            {
                return new WindowsSpeechEngine();
            }
            catch (Exception ex)
            {
                // e.g. Windows Server Core / N editions without the speech runtime.
                return new NullSpeechEngine("Windows speech is not available: " + ex.Message);
            }
        }

        if (OperatingSystem.IsMacOS())
        {
            var say = SpeechProcess.FindExecutable("say");
            return say is not null
                ? new MacSayEngine(say)
                : new NullSpeechEngine("The macOS 'say' command was not found.");
        }

        var espeak = SpeechProcess.FindExecutable("espeak-ng", "espeak");
        if (espeak is not null)
            return new ESpeakEngine(espeak);

        var spdSay = SpeechProcess.FindExecutable("spd-say");
        if (spdSay is not null)
            return new SpeechDispatcherEngine(spdSay);

        return new NullSpeechEngine("No speech engine found. Install espeak-ng, e.g. sudo apt install espeak-ng.");
    }
}
