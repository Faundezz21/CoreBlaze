namespace ProbarCpu;

internal static class Program
{
    // archivo de errores en la carpeta temporal del usuario
    public static readonly string RutaLog = Path.Combine(Path.GetTempPath(), "coreblaze-error.txt");

    public static void Log(string texto)
    {
        try { File.AppendAllText(RutaLog, DateTime.Now.ToString("HH:mm:ss") + "  " + texto + Environment.NewLine); } catch { }
    }

    [STAThread]
    private static void Main()
    {
        try { File.Delete(RutaLog); } catch { }
        Log("inicio");

        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => Fallo(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Fallo(e.ExceptionObject as Exception);

        try
        {
            ApplicationConfiguration.Initialize();
            // prioridad alta para que la interfaz siga respondiendo durante el estres
            try { System.Diagnostics.Process.GetCurrentProcess().PriorityClass = System.Diagnostics.ProcessPriorityClass.AboveNormal; } catch { }
            DesactivarEcoQos();
            UsarTodosLosNucleos();
            Application.Run(new FormPrincipal());
            Log("cierre normal");
        }
        catch (Exception ex)
        {
            Fallo(ex);
        }
    }

    // cantidad real de nucleos logicos del equipo, sin importar la afinidad heredada
    public static int Hilos { get; private set; } = Environment.ProcessorCount;

    // si el proceso hereda una afinidad limitada se restablece a todos los nucleos
    private static void UsarTodosLosNucleos()
    {
        try
        {
            int total = (int)GetActiveProcessorCount(0xFFFF);
            if (total <= 0) return;
            Hilos = total;
            if (total <= 64)
            {
                long mascara = total == 64 ? -1L : (1L << total) - 1;
                System.Diagnostics.Process.GetCurrentProcess().ProcessorAffinity = (IntPtr)mascara;
            }
        }
        catch { }
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern uint GetActiveProcessorCount(ushort grupo);

    // evita que windows 11 mande los hilos a nucleos lentos cuando la ventana no esta al frente
    private static void DesactivarEcoQos()
    {
        try
        {
            var estado = new EstadoEnergia { Version = 1, ControlMask = 1, StateMask = 0 };
            SetProcessInformation(GetCurrentProcess(), 4, ref estado, (uint)System.Runtime.InteropServices.Marshal.SizeOf<EstadoEnergia>());
        }
        catch { }
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct EstadoEnergia
    {
        public uint Version;
        public uint ControlMask;
        public uint StateMask;
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern bool SetProcessInformation(IntPtr proceso, int clase, ref EstadoEnergia info, uint tam);

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    private static void Fallo(Exception ex)
    {
        Log("error: " + ex);
        MessageBox.Show("ocurrio un error:\n\n" + ex?.Message + "\n\ndetalle guardado en:\n" + RutaLog, "CoreBlaze", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}
