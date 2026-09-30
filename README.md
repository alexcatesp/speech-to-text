# Dictado por voz (speech-to-text)

Aplicación de Windows que vive en la bandeja del sistema (junto al reloj), arranca con Windows y transcribe lo que entra por el micrófono usando un **modelo Whisper local** (servidor compatible con la API de OpenAI, por defecto `http://127.0.0.1:8000`).

El texto se envía, a elección en la configuración (se pueden activar **ambos destinos a la vez**), a:
- la **ventana con el foco** (se teclea donde está el cursor, sin usar el portapapeles), y/o
- un **fichero de texto plano** (una frase por línea, con fecha y hora opcional). El nombre es una plantilla con marcadores de fecha .NET entre llaves, p. ej. `dictado_{yyyy-MM-dd_HH-mm}.txt` o `{yyyy}\{MM}\nota_{HH-mm}.txt`. Se calcula al iniciar el dictado: **un solo fichero por sesión** (desde que activas el micro hasta que lo paras).

## Uso
1. Pulsa el atajo global (por defecto `Ctrl+Alt+Espacio`) o haz doble clic en el icono para iniciar/parar el dictado.
2. Icono gris = parado · rojo = escuchando · verde = detectando voz.
3. Clic derecho en el icono: iniciar/parar, configuración, abrir fichero de salida, salir.

Las frases se cortan por silencios (VAD por energía) y se transcriben en orden casi en vivo.

## Configuración
Clic derecho → *Configuración…*: servidor y modelo, idioma (`es` por defecto, vacío = autodetectar), micrófono, atajo, destino (ventana/fichero), umbral de voz (dBFS) y silencio que corta frase, inicio con Windows. Se guarda en `%APPDATA%\SpeechToText\settings.json`; el log en `log.txt` de la misma carpeta.

Si no detecta tu voz, sube el umbral (p. ej. -45); si capta ruido, bájalo (p. ej. -32).

## Compilar
Requiere .NET 8 SDK.
```
cd src
dotnet build -c Release
dotnet publish -c Release -r win-x64 --self-contained false -o ../publish
```
Ejecutable: `src\bin\Release\net8.0-windows\SpeechToText.exe`. El autoarranque registra la ruta del exe que se ejecute en `HKCU\...\Run`; ejecuta una vez el exe definitivo (p. ej. el de `publish`) para fijarla.

## Limitaciones
- No puede teclear en ventanas ejecutadas como administrador (UIPI) salvo que la app también lo sea.
- Requiere que el servidor Whisper esté en marcha.
