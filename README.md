# Dictado por voz (speech-to-text)

Aplicación de Windows que vive en la bandeja del sistema (junto al reloj), arranca con Windows y transcribe lo que entra por el micrófono usando un **modelo Whisper local** (servidor compatible con la API de OpenAI, por defecto `http://127.0.0.1:8000`).

El texto se envía, a elección en la configuración (se pueden activar **varios destinos a la vez**), a:
- una **ventana de subtítulos en directo** pensada para alumnado sordo (ver más abajo), y/o
- la **ventana con el foco** (se teclea donde está el cursor, sin usar el portapapeles), y/o
- un **fichero de texto plano** (una frase por línea, con fecha y hora opcional). El nombre es una plantilla con marcadores de fecha .NET entre llaves, p. ej. `dictado_{yyyy-MM-dd_HH-mm}.txt` o `{yyyy}\{MM}\nota_{HH-mm}.txt`. Se calcula al iniciar el dictado: **un solo fichero por sesión** (desde que activas el micro hasta que lo paras).

## Uso
1. Pulsa el atajo global (por defecto `Ctrl+Alt+Espacio`) o haz doble clic en el icono para iniciar/parar el dictado.
2. Icono gris = parado · rojo = escuchando · verde = detectando voz.
3. Clic derecho en el icono: iniciar/parar, configuración, abrir fichero de salida, salir.

Las frases se cortan por silencios (VAD por energía) y se transcriben en orden casi en vivo.

## Subtítulos en directo (accesibilidad)
Activa *Mostrar subtítulos en directo* en **Configuración → Destinos**, elige el micrófono (p. ej. un micrófono Bluetooth del profesor) y pulsa el atajo. Se abre una ventana dimensionable con letra grande y **desplazamiento automático** (si la alumna sube a releer, no se desplaza hasta que vuelva al final).
- Ctrl `+` / Ctrl `-` o Ctrl + rueda: tamaño de letra. Clic derecho: tema claro/oscuro, siempre visible, limpiar. Tamaño, posición y tema se recuerdan.
- Es la ruta de **mínima latencia**: nunca pasa por el LLM. Con subtítulos activos las frases se cortan antes (silencio ≤ 450 ms, fragmentos ≤ 8 s, cortando en el punto más silencioso). Latencia típica: lo que dura la frase + ~0,5 s de Whisper.
- Solo ventana local: no abre puertos ni requiere reglas de cortafuegos. Menú de la bandeja → *Subtítulos en directo* la vuelve a mostrar.

## Transcribir un fichero de audio
Menú de la bandeja → *Transcribir fichero de audio…* (o arrastra el fichero a la ventana). Acepta wav, mp3, m4a/aac, wma, flac y vídeo mp4/mkv/mov (lo que decodifique Windows; ogg/opus no). Se trocea por silencios en bloques de ≤ 28 s, con progreso y cancelación, marcas de tiempo opcionales, y botones *Copiar* y *Guardar .txt*. Rendimiento medido: ~4× tiempo real incluyendo el LLM.

## Depuración opcional con LLM (Ollama)
En **Configuración → Depuración (LLM)**: activa la casilla, indica URL (`http://127.0.0.1:11434`), modelo (por defecto `gemma4-aula`) y edita el prompt si quieres. Cada frase pasa por el modelo para quitar muletillas, repeticiones y dudas antes de escribirse en la ventana/fichero (y en la transcripción de ficheros). Los subtítulos no se tocan.
- Latencia medida con `gemma4-aula` (Q4_K_M, GPU): **~0,6 s por frase** con el modelo cargado, ~1,1 s en fragmentos de 27 s. La primera carga en VRAM tarda **~85 s** y ocupa ~8,4 GB: la app lo precarga al iniciar el dictado y lo mantiene 30 min (`keep_alive`).
- Si Ollama no responde (30 s), se escribe el texto sin depurar y se avisa. Una salvaguarda descarta respuestas vacías o desproporcionadas.
- Cuidado: el prompt manda eliminar repeticiones; un texto con repeticiones legítimas puede salir recortado.

## Configuración
Clic derecho → *Configuración…* (pestañas General, Destinos, Depuración): servidor y modelo, idioma (`es` por defecto, vacío = autodetectar), micrófono, atajo, destinos, umbral de voz (dBFS) y silencio que corta frase, inicio con Windows. Se guarda en `%APPDATA%\SpeechToText\settings.json`; el log en `log.txt` de la misma carpeta.

Si no detecta tu voz, sube el umbral (p. ej. -45); si capta ruido, bájalo (p. ej. -32).

## Compilar
Requiere .NET 8 SDK.
```
cd src
dotnet build -c Release
dotnet publish -c Release -r win-x64 --self-contained false -o ../publish
```
Ejecutable: `src\bin\Release\net8.0-windows\SpeechToText.exe`. El autoarranque registra la ruta del exe que se ejecute en `HKCU\...\Run`; ejecuta una vez el exe definitivo (p. ej. el de `publish`) para fijarla.

## Versión portable
Descarga `SpeechToText-portable.zip` de [Releases](https://github.com/alexcatesp/speech-to-text/releases), descomprime y ejecuta `SpeechToText.exe`. Es un único exe autocontenido (**no necesita instalar .NET**). Mientras exista `portable.txt` junto al exe, la configuración y el log se guardan en la carpeta `data\` de al lado, y el inicio con Windows viene desactivado (puedes activarlo en Configuración; registra la ruta actual del exe en el registro). Para generarla:
```
dotnet publish src -c Release -r win-x64 -p:PublishPortable=true -o publish/SpeechToText-portable
```
y añade un `portable.txt` a la carpeta resultante.

## Limitaciones
- No puede teclear en ventanas ejecutadas como administrador (UIPI) salvo que la app también lo sea.
- Requiere que el servidor Whisper esté en marcha.
