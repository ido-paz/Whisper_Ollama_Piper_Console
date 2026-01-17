using System.Diagnostics;
using Whisper.net;
using Whisper.net.Ggml;

Console.WriteLine("You can speak, I am recording ...");

// Record 3 seconds of microphone audio
string latest_Recording_AudioPath = "latest_recording_output.wav";
int recordingDurationInSeconds = 10;
await RecordAudioAsync(latest_Recording_AudioPath,recordingDurationInSeconds);

// Transcribe the recorded audio
await TranscribeAudioAsync(latest_Recording_AudioPath);

static async Task RecordAudioAsync(string outputPath, int durationInSeconds = 3)
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
            {
                Console.WriteLine("Failed to start ffmpeg process.");
                return;
            }

            var errorTask = process.StandardError.ReadToEndAsync();
            var outputTask = process.StandardOutput.ReadToEndAsync();

            await process.WaitForExitAsync();

            var error = await errorTask;
            var output = await outputTask;

            if (process.ExitCode == 0)
                Console.WriteLine($"Audio recorded successfully to {outputPath}");
            else
                Console.WriteLine($"Error recording audio: {error}");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Exception occurred: {ex.Message}");
        Console.WriteLine("\nNote: ffmpeg must be installed and in your system PATH.");
        Console.WriteLine("Download from: https://ffmpeg.org/download.html");
    }
}
// Transcribe audio using Whisper.net
static async Task TranscribeAudioAsync(string audioPath)
{
    try
    {
        if (!File.Exists(audioPath))
        {
            Console.WriteLine($"Audio file not found: {audioPath}");
            return;
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
                    Console.WriteLine("\n=== Transcription Result ===");
                    await foreach (var segment in processor.ProcessAsync(fileStream))
                    {
                        Console.WriteLine($"[{segment.Start:hh\\:mm\\:ss} --> {segment.End:hh\\:mm\\:ss}] {segment.Text}");
                    }
                }
            }
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error transcribing audio: {ex.Message}");
    }
}
