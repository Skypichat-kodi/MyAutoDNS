using System.Windows;
using MyAutoDNS.Core;

namespace MyAutoDNS.UI;

/// <summary>
/// Application WPF. La fenêtre de configuration est ouverte par StartupUri
/// (voir App.xaml) ; la seule chose à faire ici est d'empêcher l'ouverture de
/// plusieurs fenêtres.
/// </summary>
public partial class App : Application
{
    private IDisposable? _verrou;

    protected override void OnStartup(StartupEventArgs e)
    {
        // Une seule fenêtre de configuration à la fois : un second clic gauche sur
        // l'icône de notification, ou un double lancement depuis le menu Démarrer,
        // s'arrête silencieusement.
        _verrou = InstanceUnique.Acquerir($"{Chemins.Produit}.UI", out bool dejaLance);

        if (dejaLance)
        {
            Shutdown();
            return;
        }

        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _verrou?.Dispose();
        base.OnExit(e);
    }
}
