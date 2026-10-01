# Dictado por voz (speech-to-text)

Aplicación de Windows que vive en la bandeja del sistema (junto al reloj), arranca con Windows y transcribe lo que entra por el micrófono usando un **modelo Whisper local**: nada sale de tu equipo. Pensada para el aula, pero útil en cualquier sitio.

**Qué hace**
- **Dictado** con un atajo global: el texto se teclea donde esté el cursor y/o se guarda en un fichero de texto.
- **Subtítulos en directo** en una ventana grande con desplazamiento automático, para alumnado sordo o con dificultades de audición.
- **Transcripción de ficheros** de audio o vídeo (clases grabadas, reuniones…).
- **Depuración opcional con un LLM local** (Ollama) que quita muletillas, repeticiones y dudas.

## Requisitos
- Windows 10/11 de 64 bits.
- Un **servidor Whisper local compatible con la API de OpenAI** (por defecto `http://127.0.0.1:8000`). La app usa:
  - `POST /v1/audio/transcriptions` (multipart: `file`, `model_name`, `language`, `response_format=json`) → `{"text": "..."}`
  - `GET /v1/models` (solo para el botón *Probar servidor*)

  Ha sido probada con faster-whisper large-v3-turbo (~1,3 GB de VRAM).
- Opcional: [Ollama](https://ollama.com) con un modelo para la depuración (por defecto `gemma4-aula`). Ollama debe estar en marcha antes de usarla (`ollama serve`); la app no lo arranca.
- No necesitas instalar .NET si usas la versión portable.

## Instalación y primer uso
1. Descarga `SpeechToText-portable.zip` de [Releases](https://github.com/alexcatesp/speech-to-text/releases), descomprime y ejecuta `SpeechToText.exe` (no necesita instalación ni .NET).
2. Aparece un icono de micrófono gris junto al reloj (puede estar en el menú de iconos ocultos `^`).
3. Clic derecho → *Configuración…*: comprueba el servidor con **Probar servidor**, elige el micrófono y los destinos.
4. Pulsa el atajo (por defecto `Ctrl+Alt+Espacio`, configurable) o haz doble clic en el icono. Habla. Vuelve a pulsar para parar.

Icono: gris = parado · rojo = escuchando · verde = detectando voz. Menú del icono: iniciar/parar, subtítulos, transcribir fichero, configuración, abrir fichero de salida, salir.

La versión portable guarda su configuración en la carpeta `data\` junto al exe (mientras exista `portable.txt`) y tiene desactivado el inicio con Windows; puedes activarlo en Configuración, pero registra la ruta actual del exe, así que si lo mueves hay que reactivarlo.

## Destinos del texto
Se pueden activar varios a la vez (Configuración → Destinos):
- **Ventana con el foco**: teclea en la aplicación donde esté el cursor, sin tocar el portapapeles.
- **Fichero de texto**: una frase por línea, con fecha y hora opcional. El nombre es una plantilla con marcadores de fecha .NET entre llaves, p. ej. `dictado_{yyyy-MM-dd_HH-mm}.txt` o `{yyyy}\{MM}\nota_{HH-mm}.txt`. Se calcula al iniciar el dictado: **un solo fichero por sesión**, desde que activas el micro hasta que lo paras.
- **Subtítulos en directo**: ver abajo.

## Subtítulos en directo (accesibilidad)
Activa *Mostrar subtítulos en directo*, elige el micrófono (p. ej. el micrófono Bluetooth del profesor) y pulsa el atajo. Se abre una ventana dimensionable con letra grande y **desplazamiento automático** (si la alumna sube a releer, no baja sola hasta que vuelva al final).
- `Ctrl +` / `Ctrl -` o `Ctrl` + rueda: tamaño de letra. Clic derecho: tema claro/oscuro, siempre visible, limpiar. Tamaño, posición y tema se recuerdan.
- Es la ruta de **mínima latencia** y nunca pasa por el LLM. Con subtítulos se usa un silencio corto para cortar (300 ms por defecto, configurable de 150 a 1000 ms) y los fragmentos se acotan a 8 s, cortando en el punto más silencioso. Los fragmentos seguidos se unen en el mismo párrafo; una pausa de más de 2 s abre párrafo nuevo.
- Latencia típica: lo que dura el fragmento más ~0,2–0,5 s de Whisper (medido: ~180 ms para un clip de 1–2 s, ~250 ms para 4 s).
- Silencio más corto = texto antes, casi palabra a palabra. En una prueba con voz sintética (34 s, un solo audio) el error de palabras fue del 10 % con 150 ms y del 18 % con 700 ms, porque los fragmentos largos hacían que Whisper repitiera o se comiera frases. Es una muestra pequeña: ajusta con tu voz y tu micrófono.
- Solo ventana local: no abre puertos ni requiere reglas de cortafuegos ni permisos de administrador.

## Transcribir un fichero de audio
Menú del icono → *Transcribir fichero de audio…* (o arrastra el fichero a la ventana). Acepta wav, mp3, m4a/aac, wma, flac y vídeo mp4/mkv/mov (lo que decodifique Windows; ogg/opus no). Se trocea por silencios en bloques de ≤ 28 s, con progreso, cancelación y marcas de tiempo opcionales, y botones *Copiar* y *Guardar .txt*. Rendimiento medido: ~4× tiempo real incluyendo el LLM.

## Depuración opcional con LLM (Ollama)
En **Configuración → Depuración (LLM)**: activa la casilla, indica la URL (`http://127.0.0.1:11434`), el modelo y, si quieres, edita el prompt. Cada frase pasa por el modelo antes de escribirse en la ventana o el fichero (y en la transcripción de ficheros). Los subtítulos no se tocan.
- **Frases completas**: cuando hay LLM o subtítulos los fragmentos son cortos, así que se agrupan hasta una pausa de 1 s (configurable) y el LLM recibe frases enteras, no trozos.
- **Latencia** medida con `gemma4-aula` (Q4_K_M, GPU): ~0,6 s por frase con el modelo cargado, ~1,1 s en fragmentos de 27 s. La primera carga en VRAM tarda ~85 s y ocupa ~8,4 GB; la app lo precarga al iniciar el dictado y lo mantiene 30 min.
- **Salvaguardas**: si Ollama no responde (30 s) se usa el texto sin depurar y se avisa. Si el modelo devuelve algo vacío, desproporcionado o con palabras que no estaban en la entrada (reescribe o inventa), también se descarta.
- Cuidado: el prompt por defecto manda eliminar repeticiones; un texto con repeticiones legítimas puede salir recortado.

## Configuración
Pestañas **General** (servidor, modelo, idioma —`es` por defecto, vacío = autodetectar—, micrófono, atajo, umbral de voz, silencio, inicio con Windows), **Destinos** y **Depuración (LLM)**. Se guarda en `%APPDATA%\SpeechToText\settings.json` (o `data\` en modo portable); el log, en `log.txt` de la misma carpeta.

## Solución de problemas
| Síntoma | Qué hacer |
|---|---|
| No detecta mi voz | Sube el umbral de voz (p. ej. −45 dBFS); si capta ruido, bájalo (p. ej. −32). |
| «No se pudo registrar el atajo» | Otra aplicación usa ese atajo: cámbialo en Configuración. |
| Error al transcribir | Pulsa *Probar servidor*; comprueba que el servidor Whisper está en marcha. |
| Texto sin depurar aunque el LLM está activado | Ollama parado o modelo sin cargar (la primera carga tarda ~85 s); mira `log.txt`. |
| No escribe en cierta ventana | Las ventanas ejecutadas como administrador no aceptan texto de una app normal. |
| Formato de fichero no compatible | Conviértelo a mp3 o wav. |

## Compilar
Requiere el SDK de .NET 8.
```
dotnet build src -c Release          # src\bin\Release\net8.0-windows\SpeechToText.exe
dotnet publish src -c Release -r win-x64 -p:PublishPortable=true -o publish/SpeechToText-portable
```
Para la versión portable, añade un `portable.txt` a la carpeta resultante y comprime `SpeechToText.exe` y `portable.txt`. Detalles de arquitectura en [CLAUDE.md](CLAUDE.md).

## Limitaciones
- No puede teclear en ventanas ejecutadas como administrador (UIPI) salvo que la app también lo sea.
- Requiere que el servidor Whisper esté en marcha.
- Whisper no es un motor de streaming: el texto llega por fragmentos tras cada pausa, no letra a letra.
