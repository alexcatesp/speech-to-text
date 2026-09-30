# CLAUDE.md

App de bandeja de Windows (C# .NET 8 WinForms, NAudio.WinMM) para dictado con Whisper local.

## Arquitectura (`src/`)
- `Program.cs`: instancia única (mutex) y arranque de `TrayApp`.
- `TrayApp.cs`: NotifyIcon, menú, atajo global, iconos (gris/rojo/verde) generados en código.
- `Dictation.cs`: captura WaveIn 16 kHz mono, VAD por energía (umbral dBFS, silencio, pre-roll 300 ms, máx. segmento), cola ordenada (Channel) → `Transcriber` → salida (ventana/fichero).
- `Transcriber.cs`: POST multipart a `{ServerUrl}/v1/audio/transcriptions` (campos `file`, `model_name`, `language`, `response_format`); filtra alucinaciones típicas de Whisper.
- `TextInjector.cs`: `SendInput` con `KEYEVENTF_UNICODE`. `HotkeyWindow.cs`: `RegisterHotKey`.
- `Settings.cs`: JSON en `%APPDATA%\SpeechToText`; gestiona autoarranque en `HKCU\...\Run`.
- `SettingsForm.cs`: diálogo de configuración construido en código.

## Notas
- Servidor Whisper del usuario: `http://127.0.0.1:8000` (faster-whisper-large-v3-turbo, app "Whisper del aula").
- Compilar con `dotnet build -c Release` dentro de `src/` (el SDK está en `C:\Program Files\dotnet`; puede no estar en el PATH de la sesión).
- Mantener README.md y este fichero al día con cada cambio importante.
