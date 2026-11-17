<p align="center">
  <img src="recursos/logo.png" width="120" alt="CoreBlaze">
</p>

<h1 align="center">CoreBlaze</h1>

<p align="center">
  Stress test, benchmark y monitor térmico de CPU y GPU para Windows.<br>
  Pone tu equipo al 100 %, mide temperaturas, consumo y rendimiento en vivo, y te dice si la refrigeración está bien.
</p>

<p align="center">
  Desarrollado por <a href="https://github.com/Faundezz21"><b>Faundezz21</b></a>
</p>

---

## Descarga

Descarga la última versión desde **[Releases](https://github.com/Faundezz21/CoreBlaze/releases/latest)**:

| Archivo | Para qué |
|---|---|
| `CoreBlaze-x.y.z-setup.exe` | Instalador: crea accesos directos en el menú inicio y el escritorio, y se desinstala desde Configuración de Windows |
| `CoreBlaze-x.y.z-portable.exe` | Versión portable: un solo archivo, se ejecuta sin instalar |

Ambos incluyen todo lo necesario, no hace falta instalar .NET. La app avisa sola cuando hay una versión nueva.

## Características

- **Estrés de CPU** con un hilo por núcleo lógico y tres modos de carga:
  | Modo | Qué hace | Mide |
  |---|---|---|
  | Extremo (AVX2 + FMA) | Cadenas FMA de 256 bits en todos los hilos; es la carga que más calienta | GFLOPS |
  | Estándar (FPU) | Mezcla de punto flotante y operaciones enteras | Mop/s |
  | Memoria RAM | Lectura y escritura continua de bloques grandes | GB/s |
- **Estrés de GPU** con un compute shader de DirectX 11 (FP32), enviado en lotes cortos para no gatillar el reinicio del driver (TDR). Mide GFLOPS reales.
- **Puntaje CoreBlaze**: benchmark fijo de 60 segundos (30 s de CPU y 30 s de GPU) que entrega un solo número para comparar equipos o ver si una mejora funcionó. Guarda tus resultados y marca tu récord personal.
- **Monitoreo en vivo** de uso, temperatura, hot spot, frecuencia y potencia de CPU y GPU.
- **Protección térmica**: la prueba se detiene sola si el CPU o la GPU superan el límite configurado durante 10 segundos seguidos (los picos breves del turbo no la cortan).
- **Aviso de batería**: si el laptop no está conectado al cargador, lo advierte y marca los resultados como no comparables.
- **Detección de throttling térmico** del CPU.
- **Resumen y veredicto** al terminar cada prueba (excelente, bueno, aceptable o alto), basado en el promedio del último minuto.
- **Historial** de las últimas 30 pruebas, con gráfico y exportación a **CSV** compatible con Excel.
- **Interfaz en español e inglés**, con medidores, gráficos en tiempo real y tabla de sensores con buscador y filtros.
- **Aviso de nuevas versiones** consultando los releases de este repositorio.

## Requisitos

- Windows 10 u 11 de 64 bits.
- [Microsoft Edge WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/) (viene incluido en Windows 11; el instalador avisa si falta).
- **Permisos de administrador**: la app los pide al abrirse, porque sin ellos no se pueden leer las temperaturas del CPU.
- En builds recientes de Windows, el acceso a los sensores del CPU usa el driver **PawnIO**. Si la temperatura aparece como "sin lectura", instálalo con:
  ```
  winget install namazso.PawnIO
  ```

## Uso

1. Abre CoreBlaze y acepta el permiso de administrador.
2. En **Prueba de estrés** elige qué estresar (CPU, GPU o ambos), el modo de carga, los hilos, la GPU, la duración y el límite térmico.
3. Pulsa **Iniciar prueba** (o `F9`) y sigue los valores en el **Panel**.
4. Al detenerla verás el resumen con el veredicto; queda guardado en **Historial**.
5. Para obtener tu puntaje, ve a **Benchmark** y pulsa **Iniciar benchmark**. Para que sea comparable, cierra otros programas y conecta el cargador.

| Atajo | Acción |
|---|---|
| `F9` | Iniciar o detener la prueba |
| `Ctrl + 1..5` | Cambiar de página |
| `Esc` | Cerrar ventanas emergentes |

### Puntaje CoreBlaze

| Fase | Duración | Carga |
|---|---|---|
| 1 | 30 s | CPU en modo Extremo (AVX2 + FMA) con todos los hilos |
| 2 | 30 s | GPU con compute shader de DirectX 11 |

Se descartan los primeros 6 segundos de cada fase mientras sube el turbo, y se promedia el resto:

```
CPU   = GFLOPS del CPU × 5
GPU   = GFLOPS de la GPU × 0,2
Total = CPU + GPU
```

Los pesos están pensados para que CPU y GPU aporten de forma parecida en un laptop gamer actual. Los resultados hechos con batería no cuentan para el récord.

### Criterios del veredicto

Pensados para laptops con CPU serie H y GPU dedicada, usando la temperatura estable (promedio del último minuto):

| Componente | Excelente | Bueno | Aceptable | Alto |
|---|---|---|---|---|
| CPU | < 85 °C | < 92 °C | < 97 °C | ≥ 97 °C |
| GPU | < 75 °C | < 83 °C | < 87 °C | ≥ 87 °C |

Si el hot spot de la GPU supera en más de 20 °C a la temperatura del núcleo, se marca como posible pasta térmica seca o mal contacto.

## Compilar

Requiere el [SDK de .NET 8](https://dotnet.microsoft.com/download/dotnet/8.0).

```
git clone https://github.com/Faundezz21/CoreBlaze.git
cd CoreBlaze
dotnet build -c Release
```

Para generar el exe portable y el instalador (requiere [Inno Setup 6](https://jrsoftware.org/isinfo.php): `winget install JRSoftware.InnoSetup`):

```
powershell -ExecutionPolicy Bypass -File instalador\compilar.ps1
```

Los archivos quedan en la carpeta `ejecutable`. La versión se toma de `<Version>` en `CoreBlaze.csproj`; para publicar una nueva, súbela ahí y crea un release en GitHub con el tag `vX.Y.Z`.

## Estructura

| Archivo | Contenido |
|---|---|
| `Program.cs` | Punto de entrada, registro de errores, uso de todos los núcleos y desactivación de EcoQoS para que Windows 11 no mande los hilos a núcleos lentos |
| `FormPrincipal.cs` | Ventana que aloja la interfaz WebView2 y la conecta con el motor: pruebas, benchmark, batería y actualizaciones |
| `Estres.cs` | Estrés de CPU (AVX2 + FMA, FPU y memoria) |
| `EstresGpu.cs` | Estrés de GPU con compute shader de DirectX 11 |
| `Monitor.cs` | Lectura de sensores con LibreHardwareMonitor y respaldo por WMI |
| `Reporte.cs` | Sesiones, benchmarks, veredictos, historial y exportación CSV |
| `Logo.cs`, `Ui.cs` | Dibujo del logo e integración con la barra de título de Windows |
| `recursos/interfaz.html` | Interfaz completa (HTML, CSS y JS) con textos en español e inglés |
| `recursos/logo.ico`, `recursos/logo.png` | Icono y logo |
| `instalador/CoreBlaze.iss` | Script del instalador de Inno Setup |
| `instalador/compilar.ps1` | Genera el exe portable y el instalador |

La configuración, el historial y los resultados del benchmark se guardan en `%LOCALAPPDATA%\CoreBlaze`. Si la app falla, el detalle queda en `%TEMP%\coreblaze-error.txt`.

## Tecnologías

- C# / .NET 8 con Windows Forms y [WebView2](https://learn.microsoft.com/microsoft-edge/webview2/)
- [LibreHardwareMonitorLib](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) para los sensores
- [Vortice.Windows](https://github.com/amerkoleci/Vortice.Windows) para DirectX 11
- [Inno Setup](https://jrsoftware.org/isinfo.php) para el instalador

## Autor

**CoreBlaze** fue diseñado y desarrollado por **[Faundezz21](https://github.com/Faundezz21)**.

## Aviso

Una prueba de estrés lleva el hardware a su máximo de temperatura y consumo. Usa el equipo conectado a la corriente, con buena ventilación, y vigila las temperaturas. El uso es bajo tu propia responsabilidad.
