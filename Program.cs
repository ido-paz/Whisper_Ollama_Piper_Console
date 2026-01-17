using System.Diagnostics;
using System.Text;
using Whisper.net;
using Whisper.net.Ggml;

Console.WriteLine("You can speak, I am recording ...");
// Record 3 seconds of microphone audio
string latest_Recording_AudioPath = "latest_recording_output.wav";
int recordingDurationInSeconds = 3;
if(await Try_RecordAudioAsync(latest_Recording_AudioPath,recordingDurationInSeconds))
{
    Console.WriteLine("Recording completed.");
    // Transcribe the recorded audio
    string transcription = await Get_TranscribeAudioAsync(latest_Recording_AudioPath);
    if (transcription != null)
    {
        Console.WriteLine("\n=== Transcription Result ===");
        Console.WriteLine(transcription);
    }
}
else
{
    Console.WriteLine("Recording failed.");
}

// Record audio from microphone using ffmpeg
static async Task<bool> Try_RecordAudioAsync(string outputPath, int durationInSeconds = 3)
{
    try
    {
        if (File.Exists(outputPath))
            File.Delete(outputPath);    
        var startInfo = new ProcessStartInfo
        {
            FileName = @"C:\ffmpeg\ffmpeg-master-latest-win64-gpl\bin\ffmpeg.exe",
            Arguments = $"-f dshow -i audio=\"Microphone Array (Intel® Smart Sound Technology for Digital Microphones)\" -t {durationInSeconds} -acodec pcm_s16le -ar 16000 \"{outputPath}\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using (var process = Process.Start(startInfo))
        {
            if (process == null)
                throw new Exception("Failed to start ffmpeg process.");

            var errorTask = process.StandardError.ReadToEndAsync();
            var outputTask = process.StandardOutput.ReadToEndAsync();

            await process.WaitForExitAsync();

            var error = await errorTask;
            var output = await outputTask;

            if (process.ExitCode != 0)
                throw new Exception($"Error recording audio: {error}");
            Console.WriteLine($"Audio recorded successfully to {outputPath}");
            return true;    
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Exception occurred: {ex.Message}");
        return false;
    }
}
// Transcribe audio using Whisper.net
static async Task<string> Get_TranscribeAudioAsync(string audioPath)
{
    try
    {
        StringBuilder stringBuilder= new StringBuilder();
        if (!File.Exists(audioPath))
        {
            Console.WriteLine($"Audio file not found: {audioPath}");
            return null;
        }

        Console.WriteLine("Loading Whisper model...");
        
        // Download or use base model - you can specify different model sizes
        // Available models: tiny, base, small, medium, large
        using (var whisperFactory = WhisperFactory.FromPath("ggml-base.bin"))
        {
            using (var processor = whisperFactory.CreateBuilder()
                .WithLanguage("auto")
                .Build())
            {
                Console.WriteLine($"Transcribing {audioPath}...");
                
                using (var fileStream = File.OpenRead(audioPath))
                {                    
                    await foreach (var segment in processor.ProcessAsync(fileStream))
                    {
                        stringBuilder.AppendLine($"[{segment.Start:hh\\:mm\\:ss} --> {segment.End:hh\\:mm\\:ss}] {segment.Text}");
                    }
                }
            }
        }
        return stringBuilder.ToString();
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error transcribing audio: {ex.Message}");
        return null;
    }
}
