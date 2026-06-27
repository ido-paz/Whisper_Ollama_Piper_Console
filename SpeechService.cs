using System.Diagnostics;
using System.IO;
using System.Text;
using Whisper.net;
using Whisper.net.Ggml;

public class SpeechService
{
    private readonly Configuration _config;
    private WhisperFactory? _cachedWhisperFactory;
    private string? _cachedModelPath;

    public SpeechService(Configuration config)
    {
        _config = config;
    }

    public async Task<bool> RecordAudioAsync(string outputPath, int durationInSeconds = 3)
    {
        try
        {
            if (File.Exists(outputPath))
                File.Delete(outputPath);

            var startInfo = new ProcessStartInfo
            {
                FileName = _config.FFmpegPath,
                Arguments = $"-f dshow -i audio=\"Microphone Array (Intel® Smart Sound Technology for Digital Microphones)\" -t {durationInSeconds} -af silencedetect=noise={_config.SilenceDetectionThresholdDb}:d={_config.SilenceDetectionDurationSeconds} -acodec pcm_s16le -ar 16000 \"{outputPath}\"",
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process == null)
                throw new Exception("Failed to start ffmpeg process.");

            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorBuilder = new StringBuilder();
            var expectedSilenceDuration = TimeSpan.FromSeconds(_config.SilenceDetectionDurationSeconds);
            DateTime? silenceStartTime = null;
            bool isInSilence = false;
            object silenceLock = new object();

            // Setup async stderr reader via event to avoid concurrent stream reads
            process.EnableRaisingEvents = true;
            process.ErrorDataReceived += (s, e) =>
            {
                if (e.Data == null) return;
                lock (errorBuilder)
                {
                    errorBuilder.AppendLine(e.Data);
                }

                if (e.Data.Contains("silence_start:"))
                {
                    lock (silenceLock)
                    {
                        if (!isInSilence)
                        {
                            isInSilence = true;
                            silenceStartTime = DateTime.UtcNow;
                        }
                    }
                }
                else if (e.Data.Contains("silence_end:"))
                {
                    lock (silenceLock)
                    {
                        isInSilence = false;
                        silenceStartTime = null;
                    }
                }
            };
            process.BeginErrorReadLine();

            // Poll for silence or process exit
            while (!process.HasExited)
            {
                await Task.Delay(100);
                bool stop = false;
                lock (silenceLock)
                {
                    if (isInSilence && silenceStartTime.HasValue && DateTime.UtcNow - silenceStartTime.Value >= expectedSilenceDuration)
                    {
                        stop = true;
                    }
                }

                if (stop)
                {
                    try
                    {
                        if (!process.HasExited)
                            await process.StandardInput.WriteLineAsync("q");
                    }
                    catch { }
                    break;
                }
            }

            await process.WaitForExitAsync();
            var output = await outputTask;
            string error = null;
            lock (errorBuilder)
            {
                error = errorBuilder.ToString();
            }

            if (process.ExitCode != 0)
                throw new Exception($"Error recording audio: {error}");

            Console.WriteLine($"Audio recorded successfully to {outputPath}");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Exception occurred during recording: {ex.Message}");
            return false;
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

            using (var processor = _cachedWhisperFactory.CreateBuilder()
                .WithLanguage(language)
                .Build())
            {
                Console.WriteLine($"Transcribing {audioPath}...");

                using var fileStream = File.OpenRead(audioPath);
                await foreach (var segment in processor.ProcessAsync(fileStream))
                {
                    stringBuilder.AppendLine($"[{segment.Start:hh\\:mm\\:ss} --> {segment.End:hh\\:mm\\:ss}] {segment.Text}");
                }
            }

            transcriptionStopwatch.Stop();
            Console.WriteLine($"Transcribing audio completed in {transcriptionStopwatch.Elapsed.TotalSeconds:F2} seconds.");

            return stringBuilder.ToString();
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
}