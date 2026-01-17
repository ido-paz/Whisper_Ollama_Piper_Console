using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Whisper.net;
using Whisper.net.Ggml;

// =====================================================
// CONFIGURATION
// =====================================================
string recordingAudioPath = Path.Combine("audio", "latest_recording_output.wav");
int recordingDurationInSeconds = 3;
string whisperLanguage = "en"; // "he" for Hebrew, "es" for Spanish, etc.
string whisperModelPath = Path.Combine("whisper", "ggml-base.bin"); // "ggml-medium.bin" for better accuracy
string ollamaModel = "llama3.1"; // "llama2", "neural-chat", etc.
string ollamaUrl = "http://localhost:11434";
string piperAudioOutput = Path.Combine("audio", "ollama_response_output.wav");
string piperLanguage = "en_US-lessac-medium"; // "he_IL-kalpak-medium" for Hebrew

// =====================================================
// MAIN PIPELINE: Record -> Transcribe -> LLM -> TTS
// =====================================================
// Create audio folder if it doesn't exist
Directory.CreateDirectory("audio");

Console.WriteLine("You can speak, I am recording ...");

if (await RecordAudioAsync(recordingAudioPath, recordingDurationInSeconds))
{
    Console.WriteLine("Recording completed.");

    // Step 1: Transcribe audio to text using Whisper
    string transcription = await TranscribeAudioAsync(recordingAudioPath, whisperLanguage, whisperModelPath);
    if (transcription != null)
    {
        Console.WriteLine("\n=== Transcription Result ===");
        Console.WriteLine(transcription);

        // Step 2: Send transcription to Ollama for processing
        string ollamaResponse = await SendToOllamaAsync(transcription, ollamaModel, ollamaUrl);
        if (ollamaResponse != null)
        {
            Console.WriteLine("\n=== Ollama Response ===");
            Console.WriteLine(ollamaResponse);

            // Step 3: Convert Ollama response to speech using Piper
            await ConvertTextToSpeechAsync(ollamaResponse, piperAudioOutput, piperLanguage);
        }
        else
        {
            Console.WriteLine("Failed to get response from Ollama.");
        }
    }
}
else
{
    Console.WriteLine("Recording failed.");
}

// =====================================================
// AUDIO RECORDING (FFmpeg)
// =====================================================
/// <summary>
/// Records audio from the microphone using FFmpeg.
/// </summary>
static async Task<bool> RecordAudioAsync(string outputPath, int durationInSeconds = 3)
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

            if (process.ExitCode != 0)
                throw new Exception($"Error recording audio: {error}");

            Console.WriteLine($"Audio recorded successfully to {outputPath}");
            return true;
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Exception occurred during recording: {ex.Message}");
        return false;
    }
}

// =====================================================
// SPEECH-TO-TEXT (Whisper.net)
// =====================================================
/// <summary>
/// Transcribes audio file to text using Whisper.net.
/// Available models: tiny, base, small, medium, large
/// </summary>
static async Task<string?> TranscribeAudioAsync(string audioPath, string language = "en", string modelPath = "ggml-base.bin")
{
    try
    {
        if (!File.Exists(audioPath))
        {
            Console.WriteLine($"Audio file not found: {audioPath}");
            return null;
        }

        Console.WriteLine("Loading Whisper model...");

        var stringBuilder = new StringBuilder();
        using (var whisperFactory = WhisperFactory.FromPath(modelPath))
        {
            using (var processor = whisperFactory.CreateBuilder()
                .WithLanguage(language)
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

// =====================================================
// LANGUAGE MODEL (Ollama API)
// =====================================================
/// <summary>
/// Sends text to Ollama LLM via HTTP API and retrieves the response.
/// </summary>
static async Task<string?> SendToOllamaAsync(string text, string model = "llama2", string ollamaUrl = "http://localhost:11434")
{
    try
    {
        using (var httpClient = new HttpClient())
        {
            var requestBody = new
            {
                model = model,
                prompt = text,
                stream = false
            };

            var jsonContent = new StringContent(
                JsonSerializer.Serialize(requestBody),
                Encoding.UTF8,
                "application/json"
            );

            Console.WriteLine($"Sending to Ollama ({model})...");
            var response = await httpClient.PostAsync($"{ollamaUrl}/api/generate", jsonContent);

            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"Error: HTTP {response.StatusCode}");
                return null;
            }

            var responseContent = await response.Content.ReadAsStringAsync();
            using (JsonDocument doc = JsonDocument.Parse(responseContent))
            {
                var root = doc.RootElement;
                if (root.TryGetProperty("response", out var responseProperty))
                {
                    return responseProperty.GetString();
                }
            }

            return null;
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error sending to Ollama: {ex.Message}");
        return null;
    }
}

// =====================================================
// TEXT-TO-SPEECH (Piper)
// =====================================================
/// <summary>
/// Converts text to speech using Piper TTS via Python subprocess.
/// Requires: pip install piper-tts
/// Voice models: en_US-lessac-medium, he_IL-kalpak-medium, etc.
/// </summary>
static async Task<bool> ConvertTextToSpeechAsync(string text, string outputPath = "output.wav", string language = "en_US-lessac-medium")
{
    try
    {
        if (string.IsNullOrEmpty(text))
        {
            Console.WriteLine("Text is empty, skipping TTS conversion.");
            return false;
        }

        if (File.Exists(outputPath))
            File.Delete(outputPath);

        // Generate Python script for Piper
        string pythonScriptPath = Path.Combine(Path.GetTempPath(), "piper_tts.py");
        string voicesDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".local", "share", "piper", "voices"
        );

        string pythonCode = $@"
import sys
import subprocess
from pathlib import Path

try:
    voice_name = '{language}'
    voices_dir = Path(r'{voicesDir}')
    
    voice_file = voices_dir / f'{{voice_name}}.onnx'
    config_file = voices_dir / f'{{voice_name}}.onnx.json'
    
    if not voice_file.exists():
        raise FileNotFoundError(f'Voice file not found: {{voice_file}}')
    if not config_file.exists():
        raise FileNotFoundError(f'Config file not found: {{config_file}}')
    
    text = sys.stdin.read()
    
    result = subprocess.run(
        [sys.executable, '-m', 'piper', 
         '--model', f'{{voices_dir / voice_name}}',
         '--output-file', r'{Path.GetFullPath(outputPath)}'],
        input=text,
        capture_output=True,
        text=True
    )
    
    if result.returncode != 0:
        raise RuntimeError(f'Piper failed: {{result.stderr}}')
    
    print('Success')
except Exception as e:
    import traceback
    traceback.print_exc()
    print(f'Error: {{e}}', file=sys.stderr)
    sys.exit(1)
";

        await File.WriteAllTextAsync(pythonScriptPath, pythonCode);

        var startInfo = new ProcessStartInfo
        {
            FileName = "python.exe",
            Arguments = $"\"{pythonScriptPath}\"",
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardErrorEncoding = Encoding.UTF8,
            StandardOutputEncoding = Encoding.UTF8
        };

        using (var process = Process.Start(startInfo))
        {
            if (process == null)
                throw new Exception("Failed to start Python/Piper process.");

            await process.StandardInput.WriteLineAsync(text);
            await process.StandardInput.FlushAsync();
            process.StandardInput.Dispose();

            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();

            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
                throw new Exception($"Piper error: {error}");

            if (!File.Exists(outputPath))
                throw new Exception($"Output file was not created at {outputPath}");

            Console.WriteLine($"\nAudio generated successfully: {outputPath}");

            // Cleanup
            if (File.Exists(pythonScriptPath))
                File.Delete(pythonScriptPath);

            return true;
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error converting text to speech: {ex.Message}");
        return false;
    }
}