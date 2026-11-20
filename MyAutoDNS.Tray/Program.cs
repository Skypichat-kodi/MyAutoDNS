using MyAutoDNS.Core;

namespace MyAutoDNS.Tray;

/// <summary>
/// Point d'entrée de l'icône de notification. Aucune fenêtre : le programme ne
/// vit que par son icône dans la zone de notification.
/// </summary>
internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // Une seule icône : un second lancement (double clic sur la tâche, par
        // exemple) s'arrête immédiatement sans rien afficher.
        using IDisposable? verrou = InstanceUnique.Acquerir($"{Chemins.Produit}.Tray", out bool dejaLance);

        if (dejaLance)
        {
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new ContexteTray());
    }
}
