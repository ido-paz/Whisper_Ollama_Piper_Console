using System;
using System.Diagnostics;
using System.IO;
using System.Media;
using System.Text;
using System.Threading.Tasks;
using NAudio.Wave;
using Whisper.net;

public class SpeechService
{
    private readonly Configuration _config;
    private WhisperFactory? _cachedWhisperFactory;
    private string? _cachedModelPath;

    public SpeechService(Configuration config)
    {
        _config = config;
    }

    public async Task<string?> RecordAudioAsync(string outputPath, string? maxRecordingDurationInSeconds = null, int? silenceTimeoutSeconds = null)
    {
        string tempOutputPath = Path.Combine(
            Path.GetDirectoryName(outputPath) ?? Directory.GetCurrentDirectory(),
            $"{Path.GetFileNameWithoutExtension(outputPath)}-{Guid.NewGuid()}{Path.GetExtension(outputPath)}"
        );

        try
        {
            if (File.Exists(tempOutputPath))
                File.Delete(tempOutputPath);

            var durationSource = maxRecordingDurationInSeconds ?? _config.MaxRecordingDurationInSeconds;
            int? maxDurationSeconds = null;
            if (!string.IsNullOrWhiteSpace(durationSource))
            {
                if (int.TryParse(durationSource, out var durationValue) && durationValue > 0)
                {
                    maxDurationSeconds = durationValue;
                }
                else
                {
                    Console.WriteLine($"Warning: invalid MaxRecordingDurationInSeconds '{durationSource}'. Recording will stop only on silence.");
                }
            }

            var silenceDuration = TimeSpan.FromSeconds(_config.SilenceDetectionDurationSeconds);
            var silenceThresholdDb = _config.SilenceDetectionThresholdDb?.Trim().TrimEnd('d', 'B', 'b') ?? "-35";
            if (!double.TryParse(silenceThresholdDb, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var silenceDb))
            {
                silenceDb = -35.0;
            }

            var silenceThreshold = Math.Pow(10.0, silenceDb / 20.0);
            var hardSilenceTimeout = silenceTimeoutSeconds.HasValue ? TimeSpan.FromSeconds(silenceTimeoutSeconds.Value) : TimeSpan.MaxValue;

            using var waveIn = new WaveInEvent
            {
                WaveFormat = new WaveFormat(8000, 16, 1),
                DeviceNumber = 0
            };

            using var writer = new WaveFileWriter(tempOutputPath, waveIn.WaveFormat);
            var recordingStartTime = DateTime.UtcNow;
            var silenceTime = TimeSpan.Zero;
            bool hasHeardAudio = false;
            var stopRecording = false;
            var recordingStopped = new TaskCompletionSource<bool>();

            waveIn.DataAvailable += (sender, e) =>
            {
                if (stopRecording)
                    return;

                writer.Write(e.Buffer, 0, e.BytesRecorded);
                writer.Flush();

                int bytesPerSample = 2;
                double maxLevel = 0;
                for (int i = 0; i < e.BytesRecorded; i += bytesPerSample)
                {
                    short sample = (short)(e.Buffer[i] | (e.Buffer[i + 1] << 8));
                    double level = Math.Abs(sample / 32768.0);
                    if (level > maxLevel)
                        maxLevel = level;
                }

                bool isSilent = maxLevel <= silenceThreshold;
                if (!hasHeardAudio)
                {
                    if (!isSilent)
                    {
                        hasHeardAudio = true;
                    }
                }
                else
                {
                    if (isSilent)
                    {
                        silenceTime += TimeSpan.FromSeconds((double)e.BytesRecorded / waveIn.WaveFormat.AverageBytesPerSecond);
                    }
                    else
                    {
                        silenceTime = TimeSpan.Zero;
                    }
                }

                if (hasHeardAudio && silenceTime >= silenceDuration)
                {
                    stopRecording = true;
                    waveIn.StopRecording();
                }

                if (maxDurationSeconds.HasValue && DateTime.UtcNow - recordingStartTime >= TimeSpan.FromSeconds(maxDurationSeconds.Value))
                {
                    stopRecording = true;
                    waveIn.StopRecording();
                }

                // Hard silence timeout: if no audio is heard within the timeout, stop
                if (!hasHeardAudio && DateTime.UtcNow - recordingStartTime >= hardSilenceTimeout)
                {
                    stopRecording = true;
                    waveIn.StopRecording();
                }
            };

            waveIn.RecordingStopped += (sender, e) =>
            {
                writer.Dispose();

                if (e.Exception != null)
                {
                    recordingStopped.TrySetException(e.Exception);
                }
                else
                {
                    recordingStopped.TrySetResult(true);
                }
            };

            Console.WriteLine("Recording audio from the default microphone...");
            waveIn.StartRecording();
            await recordingStopped.Task;

            // If no audio was heard and timeout occurred, return null to signal silence
            if (!hasHeardAudio && DateTime.UtcNow - recordingStartTime >= hardSilenceTimeout)
            {
                if (File.Exists(tempOutputPath))
                {
                    try
                    {
                        File.Delete(tempOutputPath);
                    }
                    catch { }
                }
                Console.WriteLine("Silence detected (no speech within timeout).");
                return null;
            }

            try
            {
                if (!string.Equals(tempOutputPath, outputPath, StringComparison.OrdinalIgnoreCase))
                {
                    if (File.Exists(outputPath))
                    {
                        File.Delete(outputPath);
                    }
                    File.Move(tempOutputPath, outputPath, overwrite: true);
                }
            }
            catch (IOException)
            {
                Console.WriteLine($"Warning: could not replace {outputPath}. Using temporary recorded file {tempOutputPath} instead.");
                return tempOutputPath;
            }

            Console.WriteLine($"Audio recorded successfully to {outputPath}");
            return outputPath;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Exception occurred during recording: {ex.Message}");
            if (File.Exists(tempOutputPath))
            {
                try
                {
                    File.Delete(tempOutputPath);
                }
                catch { }
            }
            return null;
        }
    }

    public async Task<string?> TranscribeAsync(string audioPath, string language, string modelPath)
    {
        try
        {
            if (!File.Exists(audioPath))
            {
                Console.WriteLine($"Audio file not found: {audioPath}");
                return null;
            }

            string resolvedModelPath = modelPath;
            if (!File.Exists(resolvedModelPath))
            {
                string fallbackModelPath = Path.Combine("whisper", "ggml-base.bin");
                if (File.Exists(fallbackModelPath))
                {
                    resolvedModelPath = fallbackModelPath;
                    Console.WriteLine($"Whisper model not found at {modelPath}; using {resolvedModelPath}");
                }
            }

            var modelLoadStopwatch = Stopwatch.StartNew();
            Console.WriteLine($"Preparing Whisper model from {resolvedModelPath}...");

            if (_cachedWhisperFactory == null || _cachedModelPath != resolvedModelPath)
            {
                _cachedWhisperFactory = WhisperFactory.FromPath(resolvedModelPath);
                _cachedModelPath = resolvedModelPath;
            }

            modelLoadStopwatch.Stop();
            Console.WriteLine($"Whisper model ready in {modelLoadStopwatch.Elapsed.TotalSeconds:F2} seconds.");

            var stringBuilder = new StringBuilder();
            var transcriptionStopwatch = Stopwatch.StartNew();
            string? tempResampledFilePath = null;
            string audioPathToUse = audioPath;

            try
            {
                using var reader = new WaveFileReader(audioPath);
                if (reader.WaveFormat.SampleRate != 16000 || reader.WaveFormat.Channels != 1 || reader.WaveFormat.BitsPerSample != 16)
                {
                    tempResampledFilePath = Path.Combine(Path.GetTempPath(), $"whisper_resampled_{Guid.NewGuid()}.wav");
                    var targetFormat = new WaveFormat(16000, 16, 1);
                    using var resampler = new MediaFoundationResampler(reader, targetFormat)
                    {
                        ResamplerQuality = 60
                    };
                    WaveFileWriter.CreateWaveFile(tempResampledFilePath, resampler);
                    audioPathToUse = tempResampledFilePath;
                }

            using (var processor = _cachedWhisperFactory.CreateBuilder()
                .WithLanguage(language)
                .Build())
            {
                Console.WriteLine($"Transcribing {audioPathToUse}...");

                using var fileStream = File.OpenRead(audioPathToUse);
                await foreach (var segment in processor.ProcessAsync(fileStream))
                {
                    stringBuilder.AppendLine($"[{segment.Start:hh\\:mm\\:ss} --> {segment.End:hh\\:mm\\:ss}] {segment.Text}");
                }
            }

            transcriptionStopwatch.Stop();
            Console.WriteLine($"Transcribing audio completed in {transcriptionStopwatch.Elapsed.TotalSeconds:F2} seconds.");

            return stringBuilder.ToString();
        }
        finally
        {
            if (tempResampledFilePath != null && File.Exists(tempResampledFilePath))
            {
                try
                {
                    File.Delete(tempResampledFilePath);
                }
                catch { }
            }
        }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error transcribing audio: {ex.Message}");
            return null;
        }
    }

    public async Task<bool> ConvertTextToSpeechAsync(string text, string outputPath = "output.wav", string language = "en_US-lessac-medium")
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

            using var process = Process.Start(startInfo);
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

            if (File.Exists(pythonScriptPath))
                File.Delete(pythonScriptPath);

            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error converting text to speech: {ex.Message}");
            return false;
        }
    }
    public Task<bool> PlayAudioAsync(string audioPath)
    {
        if (!OperatingSystem.IsWindows())
        {
            Console.WriteLine("Audio playback is only supported on Windows.");
            return Task.FromResult(false);
        }

#pragma warning disable CA1416
        try
        {
            if (!File.Exists(audioPath))
            {
                Console.WriteLine($"Audio file not found for playback: {audioPath}");
                return Task.FromResult(false);
            }

            using var player = new SoundPlayer(audioPath);
            player.PlaySync();
            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error playing audio: {ex.Message}");
            return Task.FromResult(false);
        }
#pragma warning restore CA1416
    }
}