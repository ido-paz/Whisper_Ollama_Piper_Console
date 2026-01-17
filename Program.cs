using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Whisper.net;
using Whisper.net.Ggml;
// Configuration
string latest_Recording_AudioPath = "latest_recording_output.wav";
int recordingDurationInSeconds = 3;
string whisper_Language = "en";//"he" for Hebrew, "es" for Spanish, etc.
string whisper_ModelPath = "ggml-base.bin";//"ggml-medium.bin" for better accuracy
string ollama_Model = "llama3.1";//"llama2", "neural-chat", etc.
string ollama_Url = "http://localhost:11434";
// Record X seconds of microphone audio

Console.WriteLine("You can speak, I am recording ...");
if(await Try_RecordAudio_Async(latest_Recording_AudioPath,recordingDurationInSeconds))
{
    Console.WriteLine("Recording completed.");
    // Transcribe the recorded audio
    string transcription = await Get_TranscribeAudio_Async(latest_Recording_AudioPath, whisper_Language, whisper_ModelPath);
    if (transcription != null)
    {
        Console.WriteLine("\n=== Transcription Result ===");
        Console.WriteLine(transcription);

        // Send the transcription to Ollama and get a response
        string ollamaResponse = await Send_TextToOllama_Async(transcription, ollama_Model, ollama_Url);
        if (ollamaResponse != null)
        {
            Console.WriteLine("\n=== Process Completed, Ollama response ===");
            Console.WriteLine(ollamaResponse);
        }
        else
            Console.WriteLine("Failed to get response from Ollama.");
    }
}
else
{
    Console.WriteLine("Recording failed.");
}

// Record audio from microphone using ffmpeg
static async Task<bool> Try_RecordAudio_Async(string outputPath, int durationInSeconds = 3)
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
static async Task<string> Get_TranscribeAudio_Async(string audioPath, string language = "en", string whisper_modelPath = "ggml-base.bin")
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
        
        // Download or use medium model - you can specify different model sizes
        // Available models: tiny, base, small, medium, large
        using (var whisperFactory = WhisperFactory.FromPath(whisper_modelPath))
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
// Send text to Ollama via HTTP and print the response
static async Task<string> Send_TextToOllama_Async(string text, string model = "llama2", string ollamaUrl = "http://localhost:11434")
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
                    string ollama_Response = responseProperty.GetString();
                    Console.WriteLine("\n=== Ollama Response ===");
                    Console.WriteLine(ollama_Response);
                    return ollama_Response;
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
