using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MyAutoDNS.Core;

namespace MyAutoDNS.UI;

/// <summary>
/// Fenêtre de configuration des notifications par courriel.
///
/// Elle reprend les champs de l'onglet Email de MCEMonitor, dans le style sans
/// bordure de MyAutoDNS. Le mot de passe est chiffré à l'enregistrement, et le
/// bouton de test envoie un vrai message pour vérifier les réglages tout de suite,
/// sans attendre qu'une adresse change réellement.
/// </summary>
public partial class FenetreEmail : Window
{
    public FenetreEmail()
    {
        InitializeComponent();
        Loaded += (_, _) => Charger();
    }

    private void Charger()
    {
        ConfigurationEmail configuration = ConfigurationEmail.Charger();

        CaseActif.IsChecked = configuration.Actif;
        ChampExpediteur.Text = configuration.Expediteur;
        ChampMotDePasse.Password = configuration.MotDePasse;
        ChampDestinataire.Text = configuration.Destinataire;
        ChampServeur.Text = configuration.Serveur;
        ChampPort.Text = configuration.Port.ToString(CultureInfo.CurrentCulture);

        // Après le port : la saisie du port peut avoir ajusté le mode tout seul,
        // la valeur enregistrée doit l'emporter.
        ChoixSecurite.SelectedIndex = configuration.Securite switch
        {
            ModeSecurite.Ssl => 0,
            ModeSecurite.Aucun => 2,
            _ => 1,
        };

        if (configuration.MotDePasseIllisible)
        {
            Afficher(
                "Le mot de passe enregistré n'a pas pu être déchiffré sur ce poste. Saisis-le de nouveau.",
                erreur: true);
        }
    }

    /// <summary>
    /// Lit les champs et vérifie leur cohérence.
    /// </summary>
    private bool Construire(out ConfigurationEmail reglages, out string erreur)
    {
        reglages = new ConfigurationEmail
        {
            Actif = CaseActif.IsChecked == true,
            Expediteur = ChampExpediteur.Text.Trim(),
            Destinataire = ChampDestinataire.Text.Trim(),
            Serveur = ChampServeur.Text.Trim(),
            MotDePasse = ChampMotDePasse.Password,
            Securite = ChoixSecurite.SelectedIndex switch
            {
                0 => ModeSecurite.Ssl,
                2 => ModeSecurite.Aucun,
                _ => ModeSecurite.StartTls,
            },
        };

        erreur = string.Empty;

        if (!int.TryParse(ChampPort.Text.Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out int port)
            || port is < 1 or > 65535)
        {
            erreur = "Le port du serveur SMTP doit être un nombre entier compris entre 1 et 65535.";
            return false;
        }

        reglages.Port = port;

        // L'enregistrement comme le test doivent vérifier tous les champs, même
        // quand l'envoi est désactivé : on force Actif le temps du contrôle.
        bool actif = reglages.Actif;
        reglages.Actif = true;
        string? invalide = reglages.Valider();
        reglages.Actif = actif;

        if (invalide is not null)
        {
            erreur = invalide;
            return false;
        }

        return true;
    }

    private void SurEnregistrer(object sender, RoutedEventArgs e)
    {
        if (!Construire(out ConfigurationEmail reglages, out string erreur))
        {
            Afficher(erreur, erreur: true);
            return;
        }

        try
        {
            reglages.Enregistrer();
        }
        catch (Exception ex)
        {
            Afficher($"Enregistrement impossible : {ex.Message}", erreur: true);
            return;
        }

        Afficher(reglages.Actif
            ? "Réglages enregistrés. Un courriel partira à la prochaine mise à jour du domaine."
            : "Réglages enregistrés. Les notifications sont désactivées.");
    }

    private void SurTester(object sender, RoutedEventArgs e)
    {
        if (!Construire(out ConfigurationEmail reglages, out string erreur))
        {
            Afficher(erreur, erreur: true);
            return;
        }

        // Le test s'ouvre dans sa propre fenêtre : chaque étape y est horodatée, et
        // tout est recopié dans logs\smtp_test.log pour rester consultable ensuite.
        FenetreTestEmail fenetre = new(reglages) { Owner = this };
        fenetre.ShowDialog();
    }

    private void SurFermer(object sender, RoutedEventArgs e) => Close();

    /// <summary>
    /// Le port et le mode de sécurité vont de pair : 465 impose le SSL immédiat,
    /// 587 le STARTTLS, 25 le trafic en clair. On aligne le mode automatiquement,
    /// l'utilisateur reste libre de le changer ensuite.
    /// </summary>
    private void SurPortModifie(object sender, TextChangedEventArgs e)
    {
        if (ChoixSecurite is null)
        {
            return;
        }

        if (!int.TryParse(ChampPort.Text.Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out int port))
        {
            return;
        }

        int attendu = port switch
        {
            465 => 0,
            25 => 2,
            _ => 1,
        };

        if (port is 465 or 587 or 25 && ChoixSecurite.SelectedIndex != attendu)
        {
            ChoixSecurite.SelectedIndex = attendu;
        }
    }

    /// <summary>L'en-tête fait office de barre de titre.</summary>
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

    private void Afficher(string message, bool erreur = false)
    {
        TexteStatut.Text = message;
        TexteStatut.Foreground = erreur ? (Brush)FindResource("Erreur") : (Brush)FindResource("TexteSecondaire");
    }
}
