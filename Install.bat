@echo off
REM =====================================================
REM Installation and Configuration Script
REM Whisper + Ollama + Piper Console Application
REM =====================================================

setlocal enabledelayedexpansion

echo.
echo =====================================================
echo Installation Script for Whisper-Ollama-Piper Console
echo =====================================================
echo.

REM Check if running as Administrator
net session >nul 2>&1
if %errorlevel% neq 0 (
    echo ERROR: This script must be run as Administrator
    echo Please right-click and select "Run as administrator"
    pause
    exit /b 1
)

REM =====================================================
REM 1. Check and Install Python
REM =====================================================
echo [1/6] Checking Python installation...
python --version >nul 2>&1
if %errorlevel% neq 0 (
    echo Python not found. Downloading Python 3.11...
    powershell -Command "Invoke-WebRequest -Uri 'https://www.python.org/ftp/python/3.11.7/python-3.11.7-amd64.exe' -OutFile '%TEMP%\python-installer.exe'; & '%TEMP%\python-installer.exe' /quiet InstallAllUsers=1 PrependPath=1"
    
    if %errorlevel% neq 0 (
        echo ERROR: Failed to install Python
        pause
        exit /b 1
    )
    echo Python installed successfully
) else (
    for /f "tokens=*" %%A in ('python --version') do set PYTHON_VERSION=%%A
    echo Python found: !PYTHON_VERSION!
)

REM =====================================================
REM 2. Install Piper TTS
REM =====================================================
echo.
echo [2/6] Installing Piper TTS...
python -m pip install --upgrade pip >nul 2>&1
python -m pip install piper-tts >nul 2>&1

if %errorlevel% neq 0 (
    echo ERROR: Failed to install Piper TTS
    pause
    exit /b 1
)
echo Piper TTS installed successfully

REM =====================================================
REM 3. Download Piper Voice Models
REM =====================================================
echo.
echo [3/6] Downloading Piper voice models...

set VOICES_DIR=%USERPROFILE%\.local\share\piper\voices
if not exist "!VOICES_DIR!" mkdir "!VOICES_DIR!"

REM English Model
if not exist "!VOICES_DIR!\en_US-lessac-medium.onnx" (
    echo Downloading English (en_US-lessac-medium)...
    powershell -Command "Invoke-WebRequest -Uri 'https://huggingface.co/rhasspy/piper-voices/resolve/main/en/en_US/lessac/medium/en_US-lessac-medium.onnx' -OutFile '!VOICES_DIR!\en_US-lessac-medium.onnx'"
    if !errorlevel! equ 0 (
        echo English model downloaded successfully
    ) else (
        echo WARNING: Failed to download English model
    )
) else (
    echo English model already exists
)

REM English Config
if not exist "!VOICES_DIR!\en_US-lessac-medium.onnx.json" (
    echo Downloading English config...
    powershell -Command "Invoke-WebRequest -Uri 'https://huggingface.co/rhasspy/piper-voices/resolve/main/en/en_US/lessac/medium/en_US-lessac-medium.onnx.json' -OutFile '!VOICES_DIR!\en_US-lessac-medium.onnx.json'"
)

REM Copy JSON to expected name
if not exist "!VOICES_DIR!\en_US-lessac-medium.json" (
    if exist "!VOICES_DIR!\en_US-lessac-medium.onnx.json" (
        copy "!VOICES_DIR!\en_US-lessac-medium.onnx.json" "!VOICES_DIR!\en_US-lessac-medium.json" >nul
    )
)

REM Hebrew Model (Optional)
echo.
echo Download Hebrew model? (y/n)
set /p HEBREW_CHOICE=
if /i "!HEBREW_CHOICE!"=="y" (
    if not exist "!VOICES_DIR!\he_IL-kalpak-medium.onnx" (
        echo Downloading Hebrew (he_IL-kalpak-medium)...
        powershell -Command "Invoke-WebRequest -Uri 'https://huggingface.co/rhasspy/piper-voices/resolve/main/he/he_IL/kalpak/medium/he_IL-kalpak-medium.onnx' -OutFile '!VOICES_DIR!\he_IL-kalpak-medium.onnx'"
        echo Downloading Hebrew config...
        powershell -Command "Invoke-WebRequest -Uri 'https://huggingface.co/rhasspy/piper-voices/resolve/main/he/he_IL/kalpak/medium/he_IL-kalpak-medium.onnx.json' -OutFile '!VOICES_DIR!\he_IL-kalpak-medium.onnx.json'"
        if exist "!VOICES_DIR!\he_IL-kalpak-medium.onnx.json" (
            copy "!VOICES_DIR!\he_IL-kalpak-medium.onnx.json" "!VOICES_DIR!\he_IL-kalpak-medium.json" >nul
        )
        echo Hebrew model downloaded successfully
    ) else (
        echo Hebrew model already exists
    )
)

REM =====================================================
REM 4. Download Whisper Model
REM =====================================================
echo.
echo [4/6] Downloading Whisper model...

cd /d "%~dp0"

set WHISPER_DIR=%~dp0whisper
if not exist "!WHISPER_DIR!" mkdir "!WHISPER_DIR!"

if not exist "!WHISPER_DIR!\ggml-base.bin" (
    echo Downloading ggml-base.bin (141 MB) to whisper folder...
    powershell -Command "Invoke-WebRequest -Uri 'https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.bin' -OutFile '!WHISPER_DIR!\ggml-base.bin'"
    
    if !errorlevel! equ 0 (
        echo Whisper model downloaded successfully
    ) else (
        echo WARNING: Failed to download Whisper model
    )
) else (
    echo Whisper model already exists
)

REM =====================================================
REM 5. Check FFmpeg
REM =====================================================
echo.
echo [5/6] Checking FFmpeg installation...

if exist "C:\ffmpeg\ffmpeg-master-latest-win64-gpl\bin\ffmpeg.exe" (
    echo FFmpeg found
) else (
    echo WARNING: FFmpeg not found at C:\ffmpeg\ffmpeg-master-latest-win64-gpl\bin\ffmpeg.exe
    echo Please install FFmpeg manually from: https://ffmpeg.org/download.html
    echo Or extract to: C:\ffmpeg\ffmpeg-master-latest-win64-gpl\
)

REM =====================================================
REM 6. Check Ollama
REM =====================================================
echo.
echo [6/6] Checking Ollama installation...

ollama --version >nul 2>&1
if !errorlevel! equ 0 (
    echo Ollama found
    echo.
    echo Make sure to run: ollama serve
    echo And pull a model: ollama pull llama3.1
) else (
    echo WARNING: Ollama not found
    echo Please install Ollama from: https://ollama.ai
    echo Then run: ollama serve
    echo And pull a model: ollama pull llama3.1
)

REM =====================================================
REM Summary
REM =====================================================
echo.
echo =====================================================
echo Installation Complete!
echo =====================================================
echo.
echo Next steps:
echo 1. Make sure FFmpeg is installed and in the correct path
echo 2. Start Ollama: ollama serve
echo 3. Pull a model: ollama pull llama3.1
echo 4. Run the application: dotnet run
echo.
echo Voice models location: !VOICES_DIR!
echo Whisper model location: !WHISPER_DIR!\ggml-base.bin
echo.
pause
