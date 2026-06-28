using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;

var config = new Configuration();
Directory.CreateDirectory("audio");
Directory.CreateDirectory("log");

// Create log file with session timestamp
var now = DateTime.Now;
var logFileName = $"session-{now:yyyy-MM-dd-HH-mm}.log";
var logFilePath = Path.Combine(Directory.GetCurrentDirectory(), "log", logFileName);

// Function to log console output with timestamp
void Log(string message)
{
    var timestamp = DateTime.Now.ToString("HH:mm:ss");
    Console.WriteLine($"[{timestamp}] : {message}");
}

// Function to log conversation
void LogMessage(string role, string message)
{
    var timestamp = DateTime.Now.ToString("HH:mm:ss");
    var logLine = $"[{timestamp}]:[{role}], {message}";
    
    try
    {
        File.AppendAllText(logFilePath, logLine + Environment.NewLine);
    }
    catch (Exception ex)
    {
        Log($"Failed to write to log file: {ex.Message}");
    }
}

// Clean audio folder before starting session
var audioDir = new DirectoryInfo("audio");
foreach (var file in audioDir.GetFiles())
{
    try
    {
        file.Delete();
        Log($"Deleted: {file.Name}");
    }
    catch (Exception ex)
    {
        Log($"Failed to delete {file.Name}: {ex.Message}");
    }
}

var speechService = new SpeechService(config);
var modelService = new ModelService(config.OllamaModel, config.SystemPrompt);

Log("You can speak, I am recording ...");

var pipelineStopwatch = Stopwatch.StartNew();

bool continueInteracting = true;
bool isFirstIteration = true;

while (continueInteracting)
{
    var recordingStopwatch = Stopwatch.StartNew();
    // Only apply timeout after the first interaction (after first TTS response)
    int? silenceTimeout = isFirstIteration ? null : config.InteractionTimeoutSeconds;
    var recordedPath = await speechService.RecordAudioAsync(config.RecordingAudioPath, config.MaxRecordingDurationInSeconds, silenceTimeout);
    recordingStopwatch.Stop();
    isFirstIteration = false;

    if (!string.IsNullOrWhiteSpace(recordedPath))
    {
        Log($"Recording completed in {recordingStopwatch.Elapsed.TotalSeconds:F2} seconds.");

        var transcriptionStopwatch = Stopwatch.StartNew();
        var transcription = await speechService.TranscribeAsync(recordedPath, config.WhisperLanguage, config.WhisperModelPath);
        transcriptionStopwatch.Stop();
        Log($"Transcribing audio completed in {transcriptionStopwatch.Elapsed.TotalSeconds:F2} seconds.");

        if (!string.IsNullOrWhiteSpace(transcription))
        {
            Log("\n=== Transcription Result ===");
            Log(transcription);

            modelService.SetUserPrompt(transcription);
            // Store user prompt in session memory
            modelService.AddSessionMemory("user", transcription);
            // Log user message
            LogMessage("User", transcription);
            
            var requestBody = modelService.BuildRequestBody();

            var ollamaStopwatch = Stopwatch.StartNew();
            var ollamaResponse = await SendToOllamaAsync(requestBody, config.OllamaUrl);
            ollamaStopwatch.Stop();
            Log($"Getting Ollama response completed in {ollamaStopwatch.Elapsed.TotalSeconds:F2} seconds.");

            if (!string.IsNullOrWhiteSpace(ollamaResponse))
            {
                modelService.SetResponse(ollamaResponse);
                // Store Ollama response in session memory
                modelService.AddSessionMemory("assistant", ollamaResponse);
                // Log system response
                LogMessage("System", ollamaResponse);
                
                Log("\n=== Ollama Response ===");
                Log(modelService.Response);

                var ttsStopwatch = Stopwatch.StartNew();
                var ttsSuccess = await speechService.ConvertTextToSpeechAsync(ollamaResponse, config.PiperAudioOutput, config.PiperLanguage);
                ttsStopwatch.Stop();
                Log($"TTS completed in {ttsStopwatch.Elapsed.TotalSeconds:F2} seconds.");

                if (ttsSuccess)
                {
                    Log("Playing Ollama response...");
                    await speechService.PlayAudioAsync(config.PiperAudioOutput);
                }
                else
                {
                    Log("TTS generation failed, skipping audio playback.");
                }

                Log($"\nListening for response (will timeout after {config.InteractionTimeoutSeconds} seconds of silence)...");
            }
            else
            {
                Log("Failed to get response from Ollama.");
            }
        }
    }
    else
    {
        // Null returned means silence detected (no speech after timeout)
        Log("Silence detected. Exiting interaction loop.");
        continueInteracting = false;
    }
}

pipelineStopwatch.Stop();
Log($"Total pipeline time: {pipelineStopwatch.Elapsed.TotalSeconds:F2} seconds.");

async Task<string?> SendToOllamaAsync(object requestBody, string ollamaUrl)
{
    try
    {
        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        var jsonContent = new StringContent(
            JsonSerializer.Serialize(requestBody),
            Encoding.UTF8,
            "application/json"
        );

        Log("Sending to Ollama...");
        var response = await httpClient.PostAsync($"{ollamaUrl}/api/chat", jsonContent);

        if (!response.IsSuccessStatusCode)
        {
            Log($"Error: HTTP {response.StatusCode}");
            return null;
        }

        var responseContent = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(responseContent);
        var root = doc.RootElement;

        if (root.TryGetProperty("message", out var messageElement) &&
            messageElement.TryGetProperty("content", out var contentElement))
        {
            return contentElement.GetString()?.Trim();
        }

        return null;
    }
    catch (Exception ex)
    {
        Log($"Error sending to Ollama: {ex.Message}");
        return null;
    }
}
