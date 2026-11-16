using System.Management;
using LibreHardwareMonitor.Hardware;

namespace ProbarCpu;

public sealed class LecturaSensor
{
    public string Dispositivo;
    public string Tipo;
    public string Nombre;
    public float Valor;
    public float Maximo;
}

public sealed class Valores
{
    public float? Uso;
    public float? Temperatura;
    public float? Frecuencia;
    public float? Potencia;
    public float? Hotspot;
}

public sealed class Lectura
{
    public Valores Cpu = new();
    public Valores Gpu = new();
    public List<LecturaSensor> Sensores = new();
}

// lee uso, temperatura, frecuencia y potencia del cpu y la gpu
public sealed class MonitorHardware : IDisposable
{
    private readonly Computer computer;
    private readonly IHardware cpu;
    private readonly List<IHardware> gpus = new();
    private IHardware gpu;
    private readonly System.Diagnostics.PerformanceCounter contadorUso;

    public string NombreCpu { get; } = "cpu";
    public string NombreGpu => gpu?.Name ?? "GPU no detectada";
    public bool GpuSinSensores => gpu == null || gpu.HardwareType == HardwareType.GpuIntel;

    public MonitorHardware()
    {
        try
        {
            Program.Log("abriendo sensores");
            computer = new Computer { IsCpuEnabled = true, IsGpuEnabled = true };
            computer.Open();
            cpu = computer.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.Cpu);
            if (cpu != null) NombreCpu = cpu.Name;
            gpus.AddRange(computer.Hardware.Where(h => h.HardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel));
            // por defecto la gpu dedicada
            gpu = gpus.FirstOrDefault(h => h.HardwareType != HardwareType.GpuIntel) ?? gpus.FirstOrDefault();
            Program.Log("sensores abiertos: " + NombreCpu + " | " + string.Join(", ", gpus.Select(g => g.Name)));
        }
        catch (Exception ex)
        {
            Program.Log("error al abrir sensores: " + ex);
        }

        // respaldo para el uso total si la libreria falla
        try
        {
            contadorUso = new System.Diagnostics.PerformanceCounter("Processor", "% Processor Time", "_Total");
            contadorUso.NextValue();
        }
        catch { }
    }

    // elige la gpu a monitorear segun el nombre del adaptador
    public void SeleccionarGpu(string nombre)
    {
        if (string.IsNullOrEmpty(nombre)) return;
        var g = gpus.FirstOrDefault(h => h.Name.Contains(nombre, StringComparison.OrdinalIgnoreCase) || nombre.Contains(h.Name, StringComparison.OrdinalIgnoreCase));
        if (g != null) gpu = g;
    }

    public Lectura Leer()
    {
        var l = new Lectura();

        if (cpu != null)
        {
            try
            {
                cpu.Update();
                AgregarSensores(l, cpu, "cpu");
                l.Cpu.Uso = Valor(cpu, SensorType.Load, "CPU Total");
                l.Cpu.Temperatura = Valor(cpu, SensorType.Temperature, "CPU Package")
                    ?? Valor(cpu, SensorType.Temperature, "Core Max")
                    ?? Valor(cpu, SensorType.Temperature, "Core (Tctl/Tdie)")
                    ?? Maximo(cpu, SensorType.Temperature);
                l.Cpu.Frecuencia = Promedio(cpu, SensorType.Clock, "Bus");
                l.Cpu.Potencia = Valor(cpu, SensorType.Power, "CPU Package");
            }
            catch (Exception ex)
            {
                Program.Log("error leyendo cpu: " + ex.Message);
            }
        }

        if (l.Cpu.Uso == null && contadorUso != null)
        {
            try { l.Cpu.Uso = contadorUso.NextValue(); } catch { }
        }

        // respaldo de temperatura por wmi (zona termica acpi)
        if (l.Cpu.Temperatura == null) l.Cpu.Temperatura = TemperaturaWmi();

        // la telemetria de la gpu intel integrada puede cerrar el proceso con un error nativo, por eso no se lee
        if (gpu != null && gpu.HardwareType != HardwareType.GpuIntel)
        {
            try
            {
                gpu.Update();
                AgregarSensores(l, gpu, "gpu");
                l.Gpu.Uso = Valor(gpu, SensorType.Load, "GPU Core") ?? Maximo(gpu, SensorType.Load, "D3D 3D");
                l.Gpu.Temperatura = Valor(gpu, SensorType.Temperature, "GPU Core") ?? Maximo(gpu, SensorType.Temperature);
                l.Gpu.Hotspot = Valor(gpu, SensorType.Temperature, "GPU Hot Spot");
                l.Gpu.Frecuencia = Valor(gpu, SensorType.Clock, "GPU Core");
                l.Gpu.Potencia = Valor(gpu, SensorType.Power, "GPU Package") ?? Valor(gpu, SensorType.Power, "GPU Power") ?? Maximo(gpu, SensorType.Power);
            }
            catch (Exception ex)
            {
                Program.Log("error leyendo gpu: " + ex.Message);
            }
        }

        return l;
    }

    private static void AgregarSensores(Lectura l, IHardware h, string dispositivo)
    {
        foreach (var s in h.Sensors)
        {
            if (s.Value == null) continue;
            string tipo = s.SensorType switch
            {
                SensorType.Load => "uso %",
                SensorType.Temperature => "temp c",
                SensorType.Clock => "mhz",
                SensorType.Power => "watts",
                SensorType.Voltage => "volts",
                SensorType.SmallData => "mb",
                SensorType.Fan => "rpm",
                _ => null
            };
            if (tipo == null) continue;
            l.Sensores.Add(new LecturaSensor { Dispositivo = dispositivo, Tipo = tipo, Nombre = s.Name, Valor = s.Value.Value, Maximo = s.Max ?? s.Value.Value });
        }
    }

    private static float? Valor(IHardware h, SensorType tipo, string nombre)
    {
        var s = h.Sensors.FirstOrDefault(x => x.SensorType == tipo && x.Name == nombre);
        return s?.Value is float v && v > 0 ? v : null;
    }

    private static float? Maximo(IHardware h, SensorType tipo, string prefijo = null)
    {
        var vals = h.Sensors.Where(s => s.SensorType == tipo && s.Value > 0 && (prefijo == null || s.Name.StartsWith(prefijo)))
            .Select(s => s.Value.Value).ToList();
        return vals.Count > 0 ? vals.Max() : null;
    }

    private static float? Promedio(IHardware h, SensorType tipo, string excluir)
    {
        var vals = h.Sensors.Where(s => s.SensorType == tipo && s.Value > 0 && !s.Name.Contains(excluir))
            .Select(s => s.Value.Value).ToList();
        return vals.Count > 0 ? vals.Average() : null;
    }

    private static float? TemperaturaWmi()
    {
        try
        {
            using var buscador = new ManagementObjectSearcher(@"root\WMI", "SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature");
            float max = 0;
            foreach (ManagementObject o in buscador.Get())
            {
                float c = Convert.ToSingle(o["CurrentTemperature"]) / 10f - 273.15f;
                if (c > max) max = c;
            }
            return max > 0 ? max : null;
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        try { computer?.Close(); } catch { }
        contadorUso?.Dispose();
    }
}
