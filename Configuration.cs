using System;
using System.IO;
using Microsoft.Extensions.Configuration;

public class Configuration
{
    public string RecordingAudioPath { get; set; }
    public string MaxRecordingDurationInSeconds { get; set; }
    public string WhisperLanguage { get; set; }
    public string WhisperModelPath { get; set; }
    public string OllamaModel { get; set; }
    public string OllamaUrl { get; set; }
    public string PiperAudioOutput { get; set; }
    public string PiperLanguage { get; set; }
    public int SilenceDetectionDurationSeconds { get; set; }
    public string SilenceDetectionThresholdDb { get; set; }
    public string FFmpegPath { get; set; }
    public int InteractionTimeoutSeconds { get; set; }

    public Configuration()
    {
        // Defaults
        RecordingAudioPath = Path.Combine("audio", "latest_recording_output.wav");
        MaxRecordingDurationInSeconds = "5";
        WhisperLanguage = "en";
        WhisperModelPath = Path.Combine("whisper", "ggml-base.bin");
        OllamaModel = "qwen2.5:3b-instruct";
        OllamaUrl = "http://localhost:11434";
        PiperAudioOutput = Path.Combine("audio", "ollama_response_output.wav");
        PiperLanguage = "en_US-lessac-medium";
        SilenceDetectionDurationSeconds = 1;
        SilenceDetectionThresholdDb = "-35dB";
        FFmpegPath = Path.Combine("C:", "ffmpeg", "ffmpeg-master-latest-win64-gpl", "bin", "ffmpeg.exe");
        InteractionTimeoutSeconds = 5;

        // Attempt to override from appsettings.json in the current working directory using IConfiguration
        try
        {
            var builder = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false);

            var cfg = builder.Build();

            var sRecordingAudioPath = cfg.GetValue<string>("RecordingAudioPath");
            if (!string.IsNullOrWhiteSpace(sRecordingAudioPath)) RecordingAudioPath = sRecordingAudioPath;

            var sRecordingDuration = cfg.GetValue<string>("MaxRecordingDurationInSeconds");
            if (sRecordingDuration != null) MaxRecordingDurationInSeconds = sRecordingDuration;

            var sWhisperLanguage = cfg.GetValue<string>("WhisperLanguage");
            if (!string.IsNullOrWhiteSpace(sWhisperLanguage)) WhisperLanguage = sWhisperLanguage;

            var sWhisperModelPath = cfg.GetValue<string>("WhisperModelPath");
            if (!string.IsNullOrWhiteSpace(sWhisperModelPath)) WhisperModelPath = sWhisperModelPath;

            var sOllamaModel = cfg.GetValue<string>("OllamaModel");
            if (!string.IsNullOrWhiteSpace(sOllamaModel)) OllamaModel = sOllamaModel;

            var sOllamaUrl = cfg.GetValue<string>("OllamaUrl");
            if (!string.IsNullOrWhiteSpace(sOllamaUrl)) OllamaUrl = sOllamaUrl;

            var sPiperAudioOutput = cfg.GetValue<string>("PiperAudioOutput");
            if (!string.IsNullOrWhiteSpace(sPiperAudioOutput)) PiperAudioOutput = sPiperAudioOutput;

            var sPiperLanguage = cfg.GetValue<string>("PiperLanguage");
            if (!string.IsNullOrWhiteSpace(sPiperLanguage)) PiperLanguage = sPiperLanguage;

            var silenceDuration = cfg.GetValue<int?>("SilenceDetectionDurationSeconds");
            if (silenceDuration.HasValue) SilenceDetectionDurationSeconds = silenceDuration.Value;

            var sSilenceThreshold = cfg.GetValue<string>("SilenceDetectionThresholdDb");
            if (!string.IsNullOrWhiteSpace(sSilenceThreshold)) SilenceDetectionThresholdDb = sSilenceThreshold;

            var sFFmpegPath = cfg.GetValue<string>("FFmpegPath");
            if (!string.IsNullOrWhiteSpace(sFFmpegPath)) FFmpegPath = sFFmpegPath;

            var sInteractionTimeout = cfg.GetValue<int?>("InteractionTimeoutSeconds");
            if (sInteractionTimeout.HasValue) InteractionTimeoutSeconds = sInteractionTimeout.Value;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: failed to read appsettings.json: {ex.Message}");
        }
    }

    public T Get<T>(Func<Configuration, T> selector) => selector(this);

    public void Set(Action<Configuration> setter) => setter(this);
}