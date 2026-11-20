using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Input;
using MyAutoDNS.Core;

namespace MyAutoDNS.UI;

/// <summary>
/// Fenêtre de configuration.
///
/// Elle écrit settings.json, crée ou répare les tâches planifiées, puis redémarre
/// l'agent pour que les nouveaux réglages prennent effet immédiatement. C'est un
/// choix assumé : relancer l'agent est plus prévisible que d'attendre qu'il relise
/// son fichier de configuration en fin de cycle.
/// </summary>
public partial class FenetrePrincipale : Window
{
    private const string NomProcessusService = "MyAutoDNS.Service";

    private static readonly Brush CouleurActive = new SolidColorBrush(Color.FromRgb(46, 160, 67));
    private static readonly Brush CouleurInactive = new SolidColorBrush(Color.FromRgb(217, 48, 37));
    private static readonly Brush CouleurNormale = new SolidColorBrush(Color.FromRgb(51, 51, 51));

    /// <summary>Rafraîchissement de l'état affiché (service, journal).</summary>
    private readonly DispatcherTimer _rafraichissement = new()
    {
        Interval = TimeSpan.FromSeconds(5),
    };

    public FenetrePrincipale()
    {
        InitializeComponent();

        _rafraichissement.Tick += (_, _) => RafraichirEtat();

        Loaded += (_, _) =>
        {
            Charger();
            RafraichirEtat();
            _rafraichissement.Start();
        };

        Closed += (_, _) => _rafraichissement.Stop();
    }

    // ------------------------------------------------------------------
    // Chargement et affichage
    // ------------------------------------------------------------------

    private void Charger()
    {
        Configuration configuration = Configuration.ChargerSansEchec(out string? probleme);

        ChampUrl.Text = configuration.Url;

        // Une fréquence exprimée en heures rondes est réaffichée en heures,
        // sinon en minutes : l'utilisateur retrouve ce qu'il a saisi.
        if (configuration.IntervalleMinutes >= 60 && configuration.IntervalleMinutes % 60 == 0)
        {
            ChampIntervalle.Text = (configuration.IntervalleMinutes / 60).ToString(CultureInfo.CurrentCulture);
            ChoixUnite.SelectedIndex = 1;
        }
        else
        {
            ChampIntervalle.Text = configuration.IntervalleMinutes.ToString(CultureInfo.CurrentCulture);
            ChoixUnite.SelectedIndex = 0;
        }

        CaseDemarrerAvecWindows.IsChecked = TachesPlanifiees.Existe(TachesPlanifiees.NomTray);

        MettreAJourResumeIntervalle();

        if (probleme is not null)
        {
            AfficherStatut(
                $"Le fichier de configuration est illisible ({probleme}). Les valeurs par défaut sont affichées.",
                erreur: true);
        }
    }

    private void RafraichirEtat()
    {
        bool actif = ServiceEnCours();

        TexteEtatService.Text = actif ? "● Service actif" : "● Service arrêté";
        TexteEtatService.Foreground = actif ? CouleurActive : CouleurInactive;

        EtatAgent etat = EtatAgent.Charger();

        // L'état est stocké qualifié par l'agent : l'utilisateur voit immédiatement
        // si son adresse a changé, et non un texte technique à décoder.
        TexteEtatSynchronisation.Text = $"Dernière synchronisation : {etat.DernierEtat.Libelle()}";
        TexteEtatSynchronisation.Foreground = etat.DernierEtat.EstUneAnomalie()
            ? CouleurInactive
            : etat.DernierEtat == EtatSynchronisation.MiseAJourEffectuee ? CouleurActive : CouleurNormale;

        List<string> precisions = new();

        if (etat.DerniereRequeteUtc is not null)
        {
            DateTime locale = etat.DerniereRequeteUtc.Value.LocalDateTime;
            precisions.Add($"Dernière requête : {locale:dd/MM/yyyy} à {locale:HH:mm:ss}");
        }

        if (!string.IsNullOrWhiteSpace(etat.DerniereReponse))
        {
            precisions.Add($"Réponse du fournisseur : {etat.DerniereReponse}");
        }

        TexteDerniereLigne.Text = precisions.Count == 0
            ? "Aucune requête enregistrée pour l'instant."
            : string.Join(Environment.NewLine, precisions);
    }

    // ------------------------------------------------------------------
    // Actions
    // ------------------------------------------------------------------

    private void SurEnregistrer(object sender, RoutedEventArgs e)
    {
        // L'arrêt de l'agent prend quelques secondes : le curseur d'attente évite
        // que la fenêtre paraisse figée pendant l'opération.
        Mouse.OverrideCursor = Cursors.Wait;

        try
        {
            EnregistrerEtAppliquer();
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }
    }

    private void EnregistrerEtAppliquer()
    {
        if (!LireIntervalle(out int minutes, out string erreur))
        {
            AfficherStatut(erreur, erreur: true);
            return;
        }

        // On repart du fichier existant pour conserver les réglages que cette
        // fenêtre n'expose pas encore (délai maximal, rétention des journaux).
        Configuration configuration = Configuration.ChargerSansEchec(out _);
        configuration.Url = ChampUrl.Text.Trim();
        configuration.IntervalleMinutes = minutes;

        string? invalide = configuration.Valider();

        if (invalide is not null)
        {
            AfficherStatut(invalide, erreur: true);
            return;
        }

        try
        {
            configuration.Enregistrer();
        }
        catch (Exception ex)
        {
            AfficherStatut($"Enregistrement impossible : {ex.Message}", erreur: true);
            return;
        }

        List<string> etapes = new() { "Configuration enregistrée." };

        ResultatTache creationService = TachesPlanifiees.CreerService();
        etapes.Add(creationService.Message);

        if (!creationService.Ok)
        {
            AfficherStatut(string.Join(Environment.NewLine, etapes), erreur: true);
            return;
        }

        bool avecWindows = CaseDemarrerAvecWindows.IsChecked == true;

        if (avecWindows)
        {
            etapes.Add(TachesPlanifiees.CreerTray().Message);
        }
        else
        {
            TachesPlanifiees.Supprimer(TachesPlanifiees.NomTray);
            etapes.Add("Démarrage automatique de l'icône désactivé.");
        }
        // Redémarrage de l'agent : il relit settings.json et recalcule son échéance.
        // L'arrêt est propre, donc la tâche planifiée ne le relance pas en double.
        etapes.Add(TachesPlanifiees.RedemarrerService().Message);

        if (avecWindows)
        {
            // IgnoreNew : si l'icône tourne déjà, rien ne se passe. L'échec
            // éventuel n'est donc pas une erreur à signaler.
            if (TachesPlanifiees.Demarrer(TachesPlanifiees.NomTray).Ok)
            {
                etapes.Add("Icône lancée.");
            }
        }

        AfficherStatut(string.Join(Environment.NewLine, etapes));
        RafraichirEtat();
    }

    private void SurMiseAJourImmediatee(object sender, RoutedEventArgs e)
    {
        try
        {
            Chemins.PreparerDossiers();
            File.WriteAllText(
                Chemins.FichierDemandeImmediate,
                DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        }
        catch (Exception ex)
        {
            AfficherStatut($"Impossible de déposer la demande : {ex.Message}", erreur: true);
            return;
        }

        if (!ServiceEnCours())
        {
            TachesPlanifiees.Demarrer(TachesPlanifiees.NomService);
        }

        AfficherStatut(
            "Demande envoyée : la requête part dans les 5 secondes. "
            + "Le résultat apparaîtra dans le journal.");

        RafraichirEtat();
    }

    private void SurOuvrirJournal(object sender, RoutedEventArgs e)
    {
        try
        {
            Chemins.PreparerDossiers();

            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{Chemins.DossierJournaux}\"")
            {
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            AfficherStatut($"Impossible d'ouvrir le journal : {ex.Message}", erreur: true);
        }
    }

    private void SurFermer(object sender, RoutedEventArgs e) => Close();

    // ------------------------------------------------------------------
    // Saisie de la fréquence
    // ------------------------------------------------------------------

    private void SurIntervalleModifie(object sender, TextChangedEventArgs e) => MettreAJourResumeIntervalle();

    private void SurUniteModifiee(object sender, SelectionChangedEventArgs e) => MettreAJourResumeIntervalle();

    private void MettreAJourResumeIntervalle()
    {
        // Cet événement peut se déclencher pendant l'initialisation de la fenêtre,
        // avant que le bloc de texte existe.
        if (TexteResumeIntervalle is null)
        {
            return;
        }

        TexteResumeIntervalle.Text = LireIntervalle(out int minutes, out string erreur)
            ? DecrireFrequence(minutes)
            : erreur;
    }

    /// <summary>
    /// Lit la fréquence saisie et la convertit en minutes, unité de stockage.
    /// </summary>
    private bool LireIntervalle(out int minutes, out string erreur)
    {
        minutes = 0;
        erreur = string.Empty;

        if (!int.TryParse(ChampIntervalle.Text.Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out int valeur)
            || valeur <= 0)
        {
            erreur = "La fréquence doit être un nombre entier supérieur à zéro.";
            return false;
        }

        bool enHeures = ChoixUnite.SelectedIndex == 1;

        // Calcul en long : une saisie absurde ne doit pas déborder silencieusement.
        long total = enHeures ? (long)valeur * 60 : valeur;

        if (total < Configuration.IntervalleMinimumMinutes || total > Configuration.IntervalleMaximumMinutes)
        {
            erreur = $"La fréquence doit être comprise entre {Configuration.IntervalleMinimumMinutes} minutes "
                   + $"et {Configuration.IntervalleMaximumMinutes / 1440} jours.";
            return false;
        }

        minutes = (int)total;
        return true;
    }

    private static string DecrireFrequence(int minutes)
    {
        if (minutes == 60)
        {
            return "Une requête par heure.";
        }

        if (minutes < 60)
        {
            return $"Une requête toutes les {minutes} minutes.";
        }

        if (minutes % 1440 == 0)
        {
            int jours = minutes / 1440;
            return jours == 1 ? "Une requête par jour." : $"Une requête tous les {jours} jours.";
        }

        if (minutes % 60 == 0)
        {
            return $"Une requête toutes les {minutes / 60} heures.";
        }

        return $"Une requête toutes les {minutes} minutes.";
    }

    // ------------------------------------------------------------------
    // Outils internes
    // ------------------------------------------------------------------

    private static bool ServiceEnCours()
    {
        Process[] processus = Process.GetProcessesByName(NomProcessusService);

        try
        {
            return processus.Length > 0;
        }
        finally
        {
            foreach (Process instance in processus)
            {
                instance.Dispose();
            }
        }
    }

    private void AfficherStatut(string message, bool erreur = false)
    {
        TexteStatut.Text = message;
        TexteStatut.Foreground = erreur ? CouleurInactive : CouleurNormale;
    }
}
