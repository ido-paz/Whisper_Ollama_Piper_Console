using System;
using System.IO;

public class Configuration
{
    public string RecordingAudioPath { get; set; }
    public int RecordingDurationInSeconds { get; set; }
    public string WhisperLanguage { get; set; }
    public string WhisperModelPath { get; set; }
    public string OllamaModel { get; set; }
    public string OllamaUrl { get; set; }
    public string PiperAudioOutput { get; set; }
    public string PiperLanguage { get; set; }
    public int SilenceDetectionDurationSeconds { get; set; }
    public string SilenceDetectionThresholdDb { get; set; }
    public string FFmpegPath { get; set; }

    public Configuration()
    {
        RecordingAudioPath = Path.Combine("audio", "latest_recording_output.wav");
        RecordingDurationInSeconds = 5;
        WhisperLanguage = "en";
        WhisperModelPath = Path.Combine("whisper", "ggml-base.bin");
        OllamaModel = "qwen2.5:3b-instruct";
        OllamaUrl = "http://localhost:11434";
        PiperAudioOutput = Path.Combine("audio", "ollama_response_output.wav");
        PiperLanguage = "en_US-lessac-medium";
        SilenceDetectionDurationSeconds = 1;
        SilenceDetectionThresholdDb = "-35dB";
        FFmpegPath = Path.Combine("C:", "ffmpeg", "ffmpeg-master-latest-win64-gpl", "bin", "ffmpeg.exe");
    }

    public T Get<T>(Func<Configuration, T> selector) => selector(this);

    public void Set(Action<Configuration> setter) => setter(this);
}