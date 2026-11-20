using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace MyAutoDNS.Tray;

/// <summary>
/// Icône de la zone de notification, dessinée par le programme.
///
/// Aucun fichier .ico à embarquer : un simple disque de couleur suffit et évite
/// d'ajouter des ressources binaires au dépôt. Le handle natif doit être libéré
/// explicitement, sinon l'icône fuit à chaque changement d'état.
/// </summary>
internal sealed class IconeEtat : IDisposable
{
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr handle);

    private IntPtr _handle;
    private bool _libere;

    public IconeEtat(Color couleur)
    {
        using Bitmap dessin = new(32, 32);

        using (Graphics pinceau = Graphics.FromImage(dessin))
        {
            pinceau.SmoothingMode = SmoothingMode.AntiAlias;
            pinceau.Clear(Color.Transparent);

            using SolidBrush remplissage = new(couleur);

            // Contour sombre : l'icône reste lisible sur un fond clair comme sur
            // un fond sombre, ce qui n'est pas garanti avec un simple aplat.
            pinceau.FillEllipse(remplissage, 3, 3, 26, 26);
            pinceau.DrawEllipse(Pens.Black, 2, 2, 28, 28);
        }

        _handle = dessin.GetHicon();
        Icone = Icon.FromHandle(_handle);
    }

    public Icon Icone { get; }

    public void Dispose()
    {
        if (_libere)
        {
            return;
        }

        _libere = true;
        Icone.Dispose();

        if (_handle != IntPtr.Zero)
        {
            DestroyIcon(_handle);
            _handle = IntPtr.Zero;
        }
    }
}
