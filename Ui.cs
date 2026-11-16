using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace ProbarCpu;

// utilidades de dibujo y de ventana compartidas
public static class Tema
{
    public static readonly Color Fondo = Color.FromArgb(7, 11, 20);

    public static GraphicsPath Redondeado(RectangleF r, float radio)
    {
        var p = new GraphicsPath();
        float d = Math.Max(1f, Math.Min(radio * 2, Math.Min(r.Width, r.Height)));
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    // barra de titulo oscura y del mismo color que la interfaz en windows 11
    public static void TituloOscuro(IntPtr hwnd)
    {
        try
        {
            int oscuro = 1;
            DwmSetWindowAttribute(hwnd, 20, ref oscuro, sizeof(int));
            int fondo = Fondo.R | (Fondo.G << 8) | (Fondo.B << 16);
            DwmSetWindowAttribute(hwnd, 35, ref fondo, sizeof(int));
            DwmSetWindowAttribute(hwnd, 34, ref fondo, sizeof(int));
            int texto = 0xE9 | (0xEC << 8) | (0xF2 << 16);
            DwmSetWindowAttribute(hwnd, 36, ref texto, sizeof(int));
        }
        catch { }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int atributo, ref int valor, int tam);
}
