using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Input;
using MyAutoDNS.Core;

namespace MyAutoDNS.UI;

/// <summary>
/// Fenêtre de déroulement du test SMTP, sur le modèle de MCEMonitor.
///
/// Elle montre chaque étape horodatée et recopie tout dans
/// `logs\smtp_test.log`, afin qu'une erreur reste consultable après la fermeture.
///
/// Une différence avec MCEMonitor : son test s'appuie sur MailKit, qui expose
/// connexion, authentification et envoi séparément. System.Net.Mail les regroupe
/// dans un seul appel, d'où une étape unique après le test de port.
/// </summary>
public partial class FenetreTestEmail : Window
{
    private static readonly TimeSpan DelaiPort = TimeSpan.FromSeconds(8);

    private readonly ConfigurationEmail _reglages;
    private bool _enCours;

    public FenetreTestEmail(ConfigurationEmail reglages)
    {
        InitializeComponent();

        _reglages = reglages;

        Loaded += async (_, _) => await ExecuterAsync();
    }

    private async Task ExecuterAsync()
    {
        if (_enCours)
        {
            return;
        }

        _enCours = true;
        BoutonRelancer.IsEnabled = false;
        ZoneLog.Clear();

        Journaliser("=== Nouveau test SMTP ===");
        Journaliser($"Paramètres : serveur={_reglages.Serveur}, port={_reglages.Port}, "
                    + $"chiffrement={(_reglages.ConnexionChiffree ? "STARTTLS" : "aucun")}");
        Journaliser($"Expéditeur : {_reglages.Expediteur}");
        Journaliser($"Destinataire : {_reglages.Destinataire}");
        Journaliser(string.Empty);

        if (!await TesterLePortAsync())
        {
            Terminer();
            return;
        }

        // System.Net.Mail ne sait faire que STARTTLS : sur le port 465, le serveur
        // attend une poignee de main TLS immediate alors que le client attend
        // d'abord la banniere en clair. Les deux se bloquent jusqu'au delai.
        if (_reglages.Port == 465)
        {
            Journaliser("ARRÊT : le port 465 utilise le SSL implicite, que System.Net.Mail ne sait pas négocier.");
            Journaliser("Utilise le port 587 (STARTTLS) : le serveur Orange accepte les deux.");
            Journaliser(string.Empty);
            Journaliser("Rien n'a été envoyé. La tentative aurait échoué après une attente de 30 secondes.");
            return;
        }

        await EnvoyerLeMessageAsync();

        Terminer();
    }

    /// <summary>
    /// Première étape : le serveur répond-il sur ce port ? Une erreur ici n'a rien
    /// à voir avec les identifiants, inutile de chercher de ce côté.
    /// </summary>
    private async Task<bool> TesterLePortAsync()
    {
        Journaliser($"Test du port {_reglages.Port} sur {_reglages.Serveur}…");

        try
        {
            await NotificationEmail.TesterPortAsync(_reglages.Serveur, _reglages.Port, DelaiPort);
            Journaliser("Port ouvert : le serveur répond.");
            return true;
        }
        catch (Exception ex)
        {
            Journaliser($"ÉCHEC : {NotificationEmail.DecrireException(ex)}");
            Journaliser(string.Empty);
            Journaliser("Rien n'a été envoyé : le serveur n'est pas joignable.");

            return false;
        }
    }

    private async Task EnvoyerLeMessageAsync()
    {
        Journaliser("Connexion, authentification et envoi…");
        Journaliser("(System.Net.Mail ne permet pas de détailler ces trois étapes)");
        Journaliser("Le serveur a jusqu'à 30 secondes pour répondre.");
        Journaliser(string.Empty);

        ResultatNotification resultat = await NotificationEmail.EnvoyerAsync(
            _reglages,
            "MyAutoDNS — test de configuration",
            "Ceci est un message de test envoyé par MyAutoDNS."
            + Environment.NewLine + Environment.NewLine
            + $"Machine : {Environment.MachineName}" + Environment.NewLine
            + "Si tu lis ce message, les réglages SMTP sont corrects.");

        Journaliser(resultat.Message);
        Journaliser(string.Empty);

        Journaliser(resultat.Envoye
            ? "Test réussi. Vérifie la boîte du destinataire, y compris le dossier des indésirables."
            : "Test en échec. Le message ci-dessus indique la cause.");
    }

    private void Terminer()
    {
        _enCours = false;
        BoutonRelancer.IsEnabled = true;
    }

    private async void SurRelancer(object sender, RoutedEventArgs e) => await ExecuterAsync();

    private void Journaliser(string message)
    {
        string ligne = string.IsNullOrEmpty(message)
            ? string.Empty
            : $"[{DateTime.Now:HH:mm:ss}] {message}";

        ZoneLog.AppendText(ligne + Environment.NewLine);
        ZoneLog.ScrollToEnd();

        try
        {
            Chemins.PreparerDossiers();
            File.AppendAllText(
                Path.Combine(Chemins.DossierJournaux, "smtp_test.log"),
                ligne + Environment.NewLine,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Écriture du journal impossible : l'affichage à l'écran suffit.
        }
    }

    private void SurFermer(object sender, RoutedEventArgs e) => Close();

    private void EnTete_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed)
        {
            return;
        }

        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            // Bouton relâché trop vite : sans conséquence.
        }
    }
}
