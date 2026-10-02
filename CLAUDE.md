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
- Salida: `OutputToWindow` y `OutputToFile` son independientes (ambas posibles). `FilePath` es una plantilla; `Settings.ResolvePath` sustituye `{formato-fecha}` y `Dictation.Start` fija `SessionFile` (un fichero por sesión).
- Portable: `Settings.IsPortable` (existe `portable.txt` junto al exe) → datos en `.\data` y autoarranque desactivado por defecto. Publicar con `-p:PublishPortable=true` (single-file, autocontenido, comprimido, ~68 MB) y subir el zip como Release de GitHub.
- Pipeline en dos etapas (`Dictation`): etapa 1 Whisper → evento `Caption` (subtítulos, sin LLM); etapa 2 opcional LLM (`LlmCleaner`, Ollama `/api/chat`, `think:false`, fallback al texto bruto) → ventana/fichero. Con `OutputToCaptions` el VAD usa silencio ≤ 450 ms y máx. 8 s, y al llegar al máximo corta en el punto más silencioso de los últimos 2 s (`SplitAtQuietPoint`).
- La app no supone que Ollama exista: `LlmUrl`/`LlmModel` vacíos por defecto y todo el código usa `Settings.LlmEnabled` (= `UseLlm` + URL + modelo rellenos). Si falla el endpoint, `Dictation` usa el texto bruto y no reintenta 60 s (`llmDownUntil`).
- VRAM del LLM: solo se toca Ollama si `LlmEnabled` (precarga en `Dictation.Start`, `CleanAsync`). `LlmCleaner` recuerda el último modelo usado y `UnloadAsync` lo descarga (`keep_alive: 0`) al parar el dictado (`StopAsync(releaseLlm)`; false al reiniciar por cambio de ajustes) o cerrar `FileTranscribeForm`, si `LlmUnloadOnStop` (true por defecto). Verificado: 16 s en cargar, 1 s en descargar.
- Fragmentos cortos: con subtítulos el silencio de corte es `CaptionSilenceMs` (300 por defecto; test sintético: WER 10 % a 150 ms vs 18 % a 700 ms). La etapa 2 agrupa fragmentos en frases (`SentencePauseMs`, 1000) cuando hay LLM o subtítulos, para que el LLM reciba frases completas. `LlmCleaner` descarta salidas con <85 % de palabras presentes en la entrada. `CaptionsForm.AddLine` une fragmentos seguidos (quita el punto final de Whisper si la pausa < 1 s; párrafo nuevo si ≥ 2 s).
- `CaptionsForm` (subtítulos, scroll automático salvo que el lector suba), `FileTranscribeForm` + `AudioFile` (Media Foundation vía paquete NAudio completo → PCM 16 kHz mono, troceado ≤ 28 s por silencios, umbral = ThresholdDb − 8).
- Medidas (RTX 5070 Ti): Whisper ~0,5 s por 10 s de audio; gemma4-aula ~0,6 s/frase en caliente, ~85 s de carga en frío, 8,4 GB VRAM; servidor Whisper ~1,3 GB VRAM. Ollama debe estar en marcha (`ollama serve`; no se arranca solo).
- Los equipos del aula no tienen permisos de administrador: nada de reglas de cortafuegos → subtítulos solo en ventana local (sin servidor web).
- Mantener README.md y este fichero al día con cada cambio importante.
- Cierre robusto: `StopAsync` usa `ConfigureAwait(false)` (antes podía interbloquear el hilo de UI al reiniciar desde Configuración con `GetResult()`; ahora `TrayApp.RestartAsync`). `ExitAsync` espera como máximo 5 s a que pare el dictado y luego fuerza `Environment.Exit(0)` (una petición colgada a Whisper/Ollama ya no impide salir).
