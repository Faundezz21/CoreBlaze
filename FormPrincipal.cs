using System.Diagnostics;
using System.Media;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace ProbarCpu;

// ventana que aloja la interfaz html y la conecta con el motor de estres
public sealed class FormPrincipal : Form
{
    private const int SegundosParaDetener = 10;
    private const string Version = "3.0";

    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly WebView2 web;
    private readonly Estres estresCpu = new();
    private readonly EstresGpu estresGpu = new();
    private readonly MonitorHardware monitor;
    private readonly List<AdaptadorGpu> adaptadores;
    private readonly List<Sesion> historial;
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 1000 };
    private readonly Stopwatch reloj = Stopwatch.StartNew();

    private bool listo;
    private bool corriendo;
    private Sesion sesion;
    private DateTime inicio;
    private float limite = 100;
    private int minutos;
    private float tempMaxCpu, tempMaxGpu, potMaxCpu, potMaxGpu, picoFrecCpu;
    private int sobreLimiteCpu, sobreLimiteGpu;
    private long trabajoAnterior, lotesAnterior;
    private double segundosAnterior;

    public FormPrincipal()
    {
        float k = DeviceDpi / 96f;
        Text = "CoreBlaze";
        BackColor = Tema.Fondo;
        var icono = CargarIcono();
        if (icono != null) Icon = icono;

        var area = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1366, 768);
        MinimumSize = new Size(Math.Min((int)(1040 * k), area.Width), Math.Min((int)(680 * k), area.Height));
        Size = new Size(Math.Min((int)(1380 * k), area.Width - 20), Math.Min((int)(900 * k), area.Height - 20));
        StartPosition = FormStartPosition.CenterScreen;

        monitor = new MonitorHardware();
        adaptadores = EstresGpu.ListarAdaptadores();
        historial = Historial.Cargar();

        web = new WebView2 { Dock = DockStyle.Fill, DefaultBackgroundColor = Tema.Fondo };
        Controls.Add(web);

        timer.Tick += (_, _) => Actualizar();
        Load += async (_, _) => await IniciarWeb();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Tema.TituloOscuro(Handle);
    }

    private static Icon CargarIcono()
    {
        try
        {
            using var s = typeof(FormPrincipal).Assembly.GetManifestResourceStream("ProbarCpu.logo.ico");
            return s != null ? new Icon(s) : null;
        }
        catch
        {
            return null;
        }
    }

    private async Task IniciarWeb()
    {
        try
        {
            string datos = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CoreBlaze", "WebView2");
            var env = await CoreWebView2Environment.CreateAsync(null, datos);
            await web.EnsureCoreWebView2Async(env);

            var s = web.CoreWebView2.Settings;
            s.AreDefaultContextMenusEnabled = false;
            s.AreDevToolsEnabled = Debugger.IsAttached;
            s.IsZoomControlEnabled = false;
            s.IsStatusBarEnabled = false;
            s.AreBrowserAcceleratorKeysEnabled = false;
            s.IsPinchZoomEnabled = false;

            web.CoreWebView2.WebMessageReceived += Recibir;

            using var st = typeof(FormPrincipal).Assembly.GetManifestResourceStream("ProbarCpu.interfaz.html");
            using var lector = new StreamReader(st!);
            web.NavigateToString(lector.ReadToEnd());
        }
        catch (Exception ex)
        {
            Program.Log("error al iniciar webview2: " + ex);
            MessageBox.Show(this, "No se pudo iniciar la interfaz (WebView2).\n\n" + ex.Message +
                "\n\nInstala el runtime de Microsoft Edge WebView2 desde:\nhttps://developer.microsoft.com/microsoft-edge/webview2/",
                "CoreBlaze", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Close();
        }
    }

    private void Enviar(object mensaje)
    {
        if (!listo || web.CoreWebView2 == null) return;
        try
        {
            web.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(mensaje, Json));
        }
        catch (Exception ex)
        {
            Program.Log("error al enviar a la interfaz: " + ex.Message);
        }
    }

    private void Recibir(object sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var doc = JsonDocument.Parse(e.WebMessageAsJson);
            var m = doc.RootElement;
            switch (m.GetProperty("accion").GetString())
            {
                case "listo":
                    listo = true;
                    EnviarInicio();
                    timer.Start();
                    Actualizar();
                    break;
                case "iniciar":
                    Iniciar(m);
                    break;
                case "detener":
                    Detener("detenida por el usuario");
                    break;
                case "gpu":
                    SeleccionarGpu(m.GetProperty("indice").GetInt32());
                    break;
                case "encima":
                    TopMost = m.GetProperty("valor").GetBoolean();
                    break;
                case "exportar":
                    Exportar(m.GetProperty("id").GetString());
                    break;
                case "borrarHistorial":
                    Historial.Borrar();
                    historial.Clear();
                    Enviar(new { tipo = "historial", lista = Array.Empty<object>() });
                    break;
                case "config":
                    Directory.CreateDirectory(Path.GetDirectoryName(RutaConfig)!);
                    File.WriteAllText(RutaConfig, m.GetProperty("cfg").GetRawText());
                    break;
                case "carpeta":
                    Directory.CreateDirectory(Historial.Carpeta);
                    Process.Start("explorer.exe", Historial.Carpeta);
                    break;
            }
        }
        catch (Exception ex)
        {
            Program.Log("error al procesar mensaje: " + ex);
        }
    }

    private static string RutaConfig => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CoreBlaze", "config.json");

    // configuracion guardada de la interfaz, o null si no existe
    private static JsonElement? LeerConfig()
    {
        try
        {
            if (!File.Exists(RutaConfig)) return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(RutaConfig));
            return doc.RootElement.Clone();
        }
        catch
        {
            return null;
        }
    }

    private void EnviarInicio()
    {
        int gpuSel = adaptadores.Count > 0 ? adaptadores.IndexOf(adaptadores.OrderByDescending(a => a.Memoria).First()) : -1;
        if (gpuSel >= 0) monitor.SeleccionarGpu(adaptadores[gpuSel].Nombre);
        Enviar(new
        {
            tipo = "init",
            version = Version,
            cpu = monitor.NombreCpu,
            hilos = Environment.ProcessorCount,
            avx = System.Runtime.Intrinsics.X86.Fma.IsSupported,
            gpus = adaptadores.Select((a, i) => new { indice = i, nombre = a.Nombre, memoria = a.Memoria / 1024 / 1024 }).ToList(),
            gpuSel,
            config = LeerConfig(),
            modos = Estres.Nombres.Select((n, i) => new { indice = i, nombre = n, unidad = Estres.Unidad((ModoCpu)i) }).ToList(),
            historial = historial.Select(h => h.Resumen()).ToList()
        });
    }

    private void SeleccionarGpu(int indice)
    {
        if (indice >= 0 && indice < adaptadores.Count) monitor.SeleccionarGpu(adaptadores[indice].Nombre);
    }

    private void Iniciar(JsonElement m)
    {
        if (corriendo) return;
        bool conCpu = m.GetProperty("cpu").GetBoolean();
        bool conGpu = m.GetProperty("gpu").GetBoolean() && adaptadores.Count > 0;
        if (!conCpu && !conGpu)
        {
            Enviar(new { tipo = "aviso", nivel = "warn", texto = "Activa CPU, GPU o ambos para iniciar la prueba." });
            return;
        }

        var modo = (ModoCpu)Math.Clamp(m.GetProperty("modo").GetInt32(), 0, 2);
        int hilos = Math.Clamp(m.GetProperty("hilos").GetInt32(), 1, Environment.ProcessorCount);
        int gpuIdx = Math.Clamp(m.GetProperty("gpuIndice").GetInt32(), 0, Math.Max(0, adaptadores.Count - 1));
        minutos = Math.Max(0, m.GetProperty("minutos").GetInt32());
        limite = Math.Clamp(m.GetProperty("limite").GetSingle(), 60, 110);
        if (conGpu) SeleccionarGpu(gpuIdx);

        tempMaxCpu = tempMaxGpu = potMaxCpu = potMaxGpu = picoFrecCpu = 0;
        sobreLimiteCpu = sobreLimiteGpu = 0;
        trabajoAnterior = lotesAnterior = 0;
        segundosAnterior = reloj.Elapsed.TotalSeconds;
        inicio = DateTime.Now;
        sesion = new Sesion
        {
            Id = inicio.ToString("yyyyMMdd-HHmmss"), Inicio = inicio, ConCpu = conCpu, ConGpu = conGpu, Hilos = hilos,
            ModoCpu = Estres.Nombres[(int)modo], UnidadCpu = Estres.Unidad(modo),
            NombreCpu = monitor.NombreCpu, NombreGpu = conGpu ? adaptadores[gpuIdx].Nombre : ""
        };
        corriendo = true;

        if (conCpu) estresCpu.Iniciar(hilos, modo);
        if (conGpu) estresGpu.Iniciar(adaptadores[gpuIdx]);

        Enviar(new
        {
            tipo = "aviso", nivel = "info",
            texto = "Prueba iniciada" + (conCpu ? $" · CPU {Estres.Nombres[(int)modo]} ({hilos} hilos)" : "") + (conGpu ? " · GPU " + adaptadores[gpuIdx].Nombre : "")
        });
        Actualizar();
    }

    private void Detener(string motivo, bool alarma = false)
    {
        if (!corriendo) return;
        corriendo = false;
        estresCpu.Detener();
        estresGpu.Detener();

        sesion.Fin = DateTime.Now;
        sesion.Motivo = motivo;
        bool valida = sesion.Muestras.Count >= 3;
        if (valida)
        {
            historial.Insert(0, sesion);
            Historial.Guardar(sesion);
        }
        if (alarma) SystemSounds.Exclamation.Play();

        Enviar(new
        {
            tipo = "detenido",
            motivo,
            alarma,
            mostrar = valida && sesion.Duracion.TotalSeconds >= 15,
            resumen = valida ? sesion.Resumen() : null
        });
        Actualizar();
    }

    private void Exportar(string id)
    {
        var s = historial.FirstOrDefault(h => h.Id == id);
        if (s == null) return;
        using var dlg = new SaveFileDialog
        {
            Filter = "CSV (*.csv)|*.csv",
            FileName = "coreblaze-" + s.Id + ".csv",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            s.ExportarCsv(dlg.FileName);
            Enviar(new { tipo = "aviso", nivel = "ok", texto = "CSV guardado en " + dlg.FileName });
        }
        catch (Exception ex)
        {
            Enviar(new { tipo = "aviso", nivel = "bad", texto = "No se pudo guardar el CSV: " + ex.Message });
        }
    }

    private void Actualizar()
    {
        var l = monitor.Leer();

        // rendimiento medido desde la ultima lectura
        double ahora = reloj.Elapsed.TotalSeconds;
        double dt = Math.Max(0.2, ahora - segundosAnterior);
        segundosAnterior = ahora;
        double? rendCpu = null, rendGpu = null;
        if (estresCpu.Activo)
        {
            long t = estresCpu.Trabajo;
            rendCpu = (t - trabajoAnterior) / dt * Estres.Factor(estresCpu.Modo);
            trabajoAnterior = t;
        }
        if (estresGpu.Activo)
        {
            long t = estresGpu.Lotes;
            rendGpu = (t - lotesAnterior) / dt * EstresGpu.FlopsPorLote / 1e9;
            lotesAnterior = t;
        }

        if (l.Cpu.Temperatura is float tc && tc > tempMaxCpu) tempMaxCpu = tc;
        if (l.Gpu.Temperatura is float tg && tg > tempMaxGpu) tempMaxGpu = tg;
        if (l.Cpu.Potencia is float pc && pc > potMaxCpu) potMaxCpu = pc;
        if (l.Gpu.Potencia is float pg && pg > potMaxGpu) potMaxGpu = pg;
        if (estresCpu.Activo && l.Cpu.Frecuencia is float fc && fc > picoFrecCpu) picoFrecCpu = fc;

        bool throttling = estresCpu.Activo && l.Cpu.Temperatura is float tt && l.Cpu.Frecuencia is float ff &&
                          (tt >= 99 || (picoFrecCpu > 0 && ff < picoFrecCpu * 0.7f && tt >= 90));

        var dur = corriendo ? DateTime.Now - inicio : sesion?.Duracion ?? TimeSpan.Zero;

        if (corriendo)
        {
            sesion.Muestras.Add(new Muestra
            {
                Hora = DateTime.Now, CpuUso = l.Cpu.Uso, CpuTemp = l.Cpu.Temperatura, CpuFrec = l.Cpu.Frecuencia, CpuPot = l.Cpu.Potencia, CpuRend = rendCpu,
                GpuUso = l.Gpu.Uso, GpuTemp = l.Gpu.Temperatura, GpuHot = l.Gpu.Hotspot, GpuFrec = l.Gpu.Frecuencia, GpuPot = l.Gpu.Potencia, GpuRend = rendGpu
            });
        }

        Enviar(new
        {
            tipo = "estado",
            corriendo,
            tiempo = dur.ToString(@"hh\:mm\:ss"),
            segundos = (int)dur.TotalSeconds,
            restante = corriendo && minutos > 0 ? Math.Max(0, minutos * 60 - (int)dur.TotalSeconds) : -1,
            limite,
            cpu = new
            {
                uso = l.Cpu.Uso, temp = l.Cpu.Temperatura, frec = l.Cpu.Frecuencia, pot = l.Cpu.Potencia, rend = rendCpu,
                tempMax = Nulo(tempMaxCpu), potMax = Nulo(potMaxCpu), picoFrec = Nulo(picoFrecCpu), throttling,
                activo = estresCpu.Activo, unidad = Estres.Unidad(estresCpu.Modo), modo = Estres.Nombres[(int)estresCpu.Modo]
            },
            gpu = new
            {
                uso = l.Gpu.Uso, temp = l.Gpu.Temperatura, hot = l.Gpu.Hotspot, frec = l.Gpu.Frecuencia, pot = l.Gpu.Potencia, rend = rendGpu,
                tempMax = Nulo(tempMaxGpu), potMax = Nulo(potMaxGpu), activo = estresGpu.Activo, nombre = monitor.NombreGpu,
                sinSensores = monitor.GpuSinSensores, error = estresGpu.Error
            },
            sensores = l.Sensores.Select(s => new { d = s.Dispositivo, t = s.Tipo, n = s.Nombre, v = s.Valor, m = s.Maximo })
        });

        if (!corriendo) return;

        // solo se detiene si la temperatura se mantiene sobre el limite varios segundos seguidos
        sobreLimiteCpu = l.Cpu.Temperatura >= limite ? sobreLimiteCpu + 1 : 0;
        sobreLimiteGpu = l.Gpu.Temperatura >= limite ? sobreLimiteGpu + 1 : 0;

        if (minutos > 0 && dur.TotalMinutes >= minutos) Detener("finalizada: tiempo cumplido");
        else if (sobreLimiteCpu >= SegundosParaDetener) Detener($"detenida: CPU sobre {limite:0} °C por {SegundosParaDetener} s", true);
        else if (sobreLimiteGpu >= SegundosParaDetener) Detener($"detenida: GPU sobre {limite:0} °C por {SegundosParaDetener} s", true);
        else if (sesion.ConGpu && !estresGpu.Activo && estresGpu.Error != null && !estresCpu.Activo) Detener("detenida por error de GPU", true);
    }

    private static float? Nulo(float v) => v > 0 ? v : null;

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        timer.Stop();
        if (corriendo) Detener("detenida al cerrar la app");
        estresCpu.Detener();
        estresGpu.Detener();
        monitor.Dispose();
        base.OnFormClosing(e);
    }
}
