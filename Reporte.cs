using System.Globalization;
using System.Text;
using System.Text.Json;

namespace ProbarCpu;

// una muestra por segundo de la sesion de estres
public sealed class Muestra
{
    public DateTime Hora;
    public float? CpuUso, CpuTemp, CpuFrec, CpuPot;
    public float? GpuUso, GpuTemp, GpuHot, GpuFrec, GpuPot;
    public double? CpuRend, GpuRend;
}

public sealed class Sesion
{
    public string Id;
    public DateTime Inicio;
    public DateTime Fin;
    public string ModoCpu;
    public int ModoIndice = -1;
    public string UnidadCpu;
    public int Hilos;
    public bool ConCpu, ConGpu;
    public bool ConBateria;
    public string NombreCpu, NombreGpu;
    public string Motivo;
    public List<Muestra> Muestras = new();

    public TimeSpan Duracion => Fin - Inicio;

    // muestras del tramo final, cuando la temperatura ya se estabilizo
    public List<Muestra> TramoFinal()
    {
        int n = Math.Min(60, Math.Max(1, Muestras.Count / 2));
        return Muestras.Skip(Math.Max(0, Muestras.Count - n)).ToList();
    }

    public object Resumen()
    {
        var f = TramoFinal();
        float? cpuTemp = Prom(f.Select(m => m.CpuTemp));
        float? gpuTemp = Prom(f.Select(m => m.GpuTemp));
        float? gpuHot = Prom(f.Select(m => m.GpuHot));
        var (vc, nc) = Veredicto.Cpu(cpuTemp);
        var (vg, ng) = Veredicto.Gpu(gpuTemp, gpuHot);
        return new
        {
            id = Id,
            fecha = Inicio.ToString("yyyy-MM-ddTHH:mm:ss"),
            duracion = Duracion.ToString(@"hh\:mm\:ss"),
            segundos = (int)Duracion.TotalSeconds,
            motivo = Motivo,
            conCpu = ConCpu,
            conGpu = ConGpu,
            conBateria = ConBateria,
            modo = ModoCpu,
            modoIndice = ModoIndice,
            unidad = UnidadCpu,
            hilos = Hilos,
            nombreCpu = NombreCpu,
            nombreGpu = NombreGpu,
            cpu = new
            {
                temp = cpuTemp,
                tempMax = Max(Muestras.Select(m => m.CpuTemp)),
                frec = Prom(f.Select(m => m.CpuFrec)),
                pot = Prom(f.Select(m => m.CpuPot)),
                rend = PromD(f.Select(m => m.CpuRend)),
                veredicto = vc,
                nivel = nc
            },
            gpu = new
            {
                temp = gpuTemp,
                tempMax = Max(Muestras.Select(m => m.GpuTemp)),
                hot = gpuHot,
                frec = Prom(f.Select(m => m.GpuFrec)),
                pot = Prom(f.Select(m => m.GpuPot)),
                rend = PromD(f.Select(m => m.GpuRend)),
                veredicto = vg,
                nivel = ng
            },
            serie = new
            {
                cpuTemp = Reducir(Muestras.Select(m => m.CpuTemp)),
                gpuTemp = Reducir(Muestras.Select(m => m.GpuTemp)),
                cpuUso = Reducir(Muestras.Select(m => m.CpuUso)),
                gpuUso = Reducir(Muestras.Select(m => m.GpuUso))
            }
        };
    }

    public void ExportarCsv(string ruta)
    {
        var c = CultureInfo.CurrentCulture;
        string sep = c.NumberFormat.NumberDecimalSeparator == "," ? ";" : ",";
        string F(float? v) => v?.ToString("0.0", c) ?? "";
        string D(double? v) => v?.ToString("0.0", c) ?? "";

        var sb = new StringBuilder();
        sb.AppendLine(string.Join(sep, "time", "cpu_load_%", "cpu_temp_c", "cpu_mhz", "cpu_w", "cpu_perf_" + UnidadCpu,
            "gpu_load_%", "gpu_temp_c", "gpu_hotspot_c", "gpu_mhz", "gpu_w", "gpu_gflops"));
        foreach (var m in Muestras)
        {
            sb.AppendLine(string.Join(sep, m.Hora.ToString("HH:mm:ss"), F(m.CpuUso), F(m.CpuTemp), F(m.CpuFrec), F(m.CpuPot), D(m.CpuRend),
                F(m.GpuUso), F(m.GpuTemp), F(m.GpuHot), F(m.GpuFrec), F(m.GpuPot), D(m.GpuRend)));
        }
        File.WriteAllText(ruta, sb.ToString(), new UTF8Encoding(true));
    }

    // reduce una serie a un maximo de puntos para graficarla
    public static float[] Reducir(IEnumerable<float?> v, int max = 150)
    {
        var l = v.Select(x => x ?? 0).ToArray();
        if (l.Length <= max) return l;
        var r = new float[max];
        double paso = l.Length / (double)max;
        for (int i = 0; i < max; i++)
        {
            int a = (int)(i * paso), b = Math.Max(a + 1, (int)((i + 1) * paso));
            r[i] = l.Skip(a).Take(b - a).Average();
        }
        return r;
    }

    public static float? Prom(IEnumerable<float?> v)
    {
        var l = v.Where(x => x is > 0).Select(x => x.Value).ToList();
        return l.Count > 0 ? l.Average() : null;
    }

    public static double? PromD(IEnumerable<double?> v)
    {
        var l = v.Where(x => x is > 0).Select(x => x.Value).ToList();
        return l.Count > 0 ? l.Average() : null;
    }

    public static float? Max(IEnumerable<float?> v)
    {
        var l = v.Where(x => x is > 0).Select(x => x.Value).ToList();
        return l.Count > 0 ? l.Max() : null;
    }
}

// criterios de evaluacion termica pensados para laptops serie h, devuelve un codigo que la interfaz traduce
public static class Veredicto
{
    public static (string, string) Cpu(float? t) => t switch
    {
        null => ("cpu.na", "na"),
        < 85 => ("cpu.exc", "ok"),
        < 92 => ("cpu.bueno", "ok"),
        < 97 => ("cpu.acep", "warn"),
        _ => ("cpu.alto", "bad")
    };

    public static (string, string) Gpu(float? t, float? hot)
    {
        if (t == null) return ("gpu.na", "na");
        if (hot is float h && h - t.Value > 20) return ("gpu.hot", "bad");
        return t.Value switch
        {
            < 75 => ("gpu.exc", "ok"),
            < 83 => ("gpu.bueno", "ok"),
            < 87 => ("gpu.acep", "warn"),
            _ => ("gpu.alto", "bad")
        };
    }
}

// resultado de la prueba fija de 60 segundos
public sealed class ResultadoBench
{
    public string Id;
    public DateTime Fecha;
    public string Version;
    public string NombreCpu, NombreGpu;
    public int Hilos;
    public double CpuGflops, GpuGflops;
    public int PuntajeCpu, PuntajeGpu, Total;
    public float? CpuTempMax, GpuTempMax, CpuPot, GpuPot;
    public bool ConBateria;
    public bool ConGpu;
    public float[] SerieCpu, SerieGpu;

    // pesos elegidos para que cpu y gpu aporten parecido en un laptop gamer actual
    public const double PesoCpu = 5.0;
    public const double PesoGpu = 0.2;

    public object Dto() => new
    {
        id = Id,
        fecha = Fecha.ToString("yyyy-MM-ddTHH:mm:ss"),
        version = Version,
        nombreCpu = NombreCpu,
        nombreGpu = NombreGpu,
        hilos = Hilos,
        cpuGflops = CpuGflops,
        gpuGflops = GpuGflops,
        puntajeCpu = PuntajeCpu,
        puntajeGpu = PuntajeGpu,
        total = Total,
        cpuTempMax = CpuTempMax,
        gpuTempMax = GpuTempMax,
        cpuPot = CpuPot,
        gpuPot = GpuPot,
        conBateria = ConBateria,
        conGpu = ConGpu,
        serieCpu = SerieCpu,
        serieGpu = SerieGpu
    };
}

// guarda sesiones y benchmarks en la carpeta de datos local del usuario
public static class Historial
{
    private const int Maximo = 30;
    private const int MaximoBench = 50;
    private static readonly JsonSerializerOptions Opciones = new() { IncludeFields = true };

    public static string Base => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CoreBlaze");
    public static string Carpeta => Path.Combine(Base, "historial");
    private static string RutaBench => Path.Combine(Base, "benchmarks.json");

    public static void Guardar(Sesion s)
    {
        try
        {
            Directory.CreateDirectory(Carpeta);
            File.WriteAllText(Path.Combine(Carpeta, s.Id + ".json"), JsonSerializer.Serialize(s, Opciones));
            // conserva solo las sesiones mas recientes
            foreach (var viejo in new DirectoryInfo(Carpeta).GetFiles("*.json").OrderByDescending(f => f.Name).Skip(Maximo))
                viejo.Delete();
        }
        catch (Exception ex)
        {
            Program.Log("error al guardar historial: " + ex.Message);
        }
    }

    public static List<Sesion> Cargar()
    {
        var lista = new List<Sesion>();
        try
        {
            if (!Directory.Exists(Carpeta)) return lista;
            foreach (var f in new DirectoryInfo(Carpeta).GetFiles("*.json").OrderByDescending(f => f.Name))
            {
                try
                {
                    var s = JsonSerializer.Deserialize<Sesion>(File.ReadAllText(f.FullName), Opciones);
                    if (s != null) lista.Add(s);
                }
                catch { }
            }
        }
        catch (Exception ex)
        {
            Program.Log("error al cargar historial: " + ex.Message);
        }
        return lista;
    }

    public static void Borrar()
    {
        try
        {
            if (Directory.Exists(Carpeta))
                foreach (var f in Directory.GetFiles(Carpeta, "*.json")) File.Delete(f);
        }
        catch { }
    }

    public static List<ResultadoBench> CargarBench()
    {
        try
        {
            if (File.Exists(RutaBench))
                return JsonSerializer.Deserialize<List<ResultadoBench>>(File.ReadAllText(RutaBench), Opciones) ?? new();
        }
        catch (Exception ex)
        {
            Program.Log("error al cargar benchmarks: " + ex.Message);
        }
        return new();
    }

    public static void GuardarBench(List<ResultadoBench> lista)
    {
        try
        {
            Directory.CreateDirectory(Base);
            File.WriteAllText(RutaBench, JsonSerializer.Serialize(lista.Take(MaximoBench).ToList(), Opciones));
        }
        catch (Exception ex)
        {
            Program.Log("error al guardar benchmarks: " + ex.Message);
        }
    }
}
