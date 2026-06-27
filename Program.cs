using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;

var config = new Configuration();
Directory.CreateDirectory("audio");

var speechService = new SpeechService(config);
var modelService = new ModelService(config.OllamaModel, "Reply shortly and answer in one short sentence.");

Console.WriteLine("You can speak, I am recording ...");

var pipelineStopwatch = Stopwatch.StartNew();

var interactionTimeout = 5; // seconds of silence before exiting loop
bool continueInteracting = true;
bool isFirstIteration = true;

while (continueInteracting)
{
    var recordingStopwatch = Stopwatch.StartNew();
    // Only apply timeout after the first interaction (after first TTS response)
    int? silenceTimeout = isFirstIteration ? null : interactionTimeout;
    var recordedPath = await speechService.RecordAudioAsync(config.RecordingAudioPath, config.MaxRecordingDurationInSeconds, silenceTimeout);
    recordingStopwatch.Stop();
    isFirstIteration = false;

    if (!string.IsNullOrWhiteSpace(recordedPath))
    {
        Console.WriteLine($"Recording completed in {recordingStopwatch.Elapsed.TotalSeconds:F2} seconds.");

        var transcriptionStopwatch = Stopwatch.StartNew();
        var transcription = await speechService.TranscribeAsync(recordedPath, config.WhisperLanguage, config.WhisperModelPath);
        transcriptionStopwatch.Stop();
        Console.WriteLine($"Transcribing audio completed in {transcriptionStopwatch.Elapsed.TotalSeconds:F2} seconds.");

        if (!string.IsNullOrWhiteSpace(transcription))
        {
            Console.WriteLine("\n=== Transcription Result ===");
            Console.WriteLine(transcription);

            modelService.SetUserPrompt(transcription);
            var requestBody = modelService.BuildRequestBody();

            var ollamaStopwatch = Stopwatch.StartNew();
            var ollamaResponse = await SendToOllamaAsync(requestBody, config.OllamaUrl);
            ollamaStopwatch.Stop();
            Console.WriteLine($"Getting Ollama response completed in {ollamaStopwatch.Elapsed.TotalSeconds:F2} seconds.");

            if (!string.IsNullOrWhiteSpace(ollamaResponse))
            {
                modelService.SetResponse(ollamaResponse);
                Console.WriteLine("\n=== Ollama Response ===");
                Console.WriteLine(modelService.Response);

                var ttsStopwatch = Stopwatch.StartNew();
                var ttsSuccess = await speechService.ConvertTextToSpeechAsync(ollamaResponse, config.PiperAudioOutput, config.PiperLanguage);
                ttsStopwatch.Stop();
                Console.WriteLine($"TTS completed in {ttsStopwatch.Elapsed.TotalSeconds:F2} seconds.");

                if (ttsSuccess)
                {
                    Console.WriteLine("Playing Ollama response...");
                    await speechService.PlayAudioAsync(config.PiperAudioOutput);
                }
                else
                {
                    Console.WriteLine("TTS generation failed, skipping audio playback.");
                }

                Console.WriteLine($"\nListening for response (will timeout after {interactionTimeout} seconds of silence)...");
            }
            else
            {
                Console.WriteLine("Failed to get response from Ollama.");
            }
        }
    }
    else
    {
        // Null returned means silence detected (no speech after timeout)
        Console.WriteLine("Silence detected. Exiting interaction loop.");
        continueInteracting = false;
    }
}

pipelineStopwatch.Stop();
Console.WriteLine($"Total pipeline time: {pipelineStopwatch.Elapsed.TotalSeconds:F2} seconds.");

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

        Console.WriteLine("Sending to Ollama...");
        var response = await httpClient.PostAsync($"{ollamaUrl}/api/chat", jsonContent);

        if (!response.IsSuccessStatusCode)
        {
            Console.WriteLine($"Error: HTTP {response.StatusCode}");
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
        Console.WriteLine($"Error sending to Ollama: {ex.Message}");
        return null;
    }
}
