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

        ChampDelai.Text = configuration.DelaiMaximalSecondes.ToString(CultureInfo.CurrentCulture);
        ChampRetention.Text = configuration.JoursDeRetention.ToString(CultureInfo.CurrentCulture);
        CaseIgnorerCertificat.IsChecked = configuration.IgnorerErreurCertificat;

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
        TexteEtatService.Foreground = actif ? Couleur("Succes") : Couleur("Erreur");

        EtatAgent etat = EtatAgent.Charger();

        // L'état est stocké qualifié par l'agent : l'utilisateur voit immédiatement
        // si son adresse a changé, et non un texte technique à décoder.
        TexteEtatSynchronisation.Text = $"Dernière synchronisation : {etat.DernierEtat.Libelle()}";
        TexteEtatSynchronisation.Foreground = etat.DernierEtat.EstUneAnomalie()
            ? Couleur("Erreur")
            : etat.DernierEtat == EtatSynchronisation.MiseAJourEffectuee ? Couleur("Succes") : Couleur("Texte");

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

        // Les boutons de service ne doivent jamais proposer une action sans objet.
        BoutonDemarrer.IsEnabled = !actif;
        BoutonArreter.IsEnabled = actif;
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
            EnregistrerConfiguration();
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }
    }

    private void EnregistrerConfiguration()
    {
        if (!LireIntervalle(out int minutes, out string erreur))
        {
            AfficherStatut(erreur, erreur: true);
            return;
        }

        if (!LireReglagesAvances(out int delai, out int retention, out bool ignorerCertificat, out string erreurAvancee))
        {
            AfficherStatut(erreurAvancee, erreur: true);
            return;
        }

        // On repart du fichier existant : une clé ajoutée à la main, ou par une
        // version future, n'est ainsi jamais perdue à l'enregistrement.
        Configuration configuration = Configuration.ChargerSansEchec(out _);
        configuration.Url = ChampUrl.Text.Trim();
        configuration.IntervalleMinutes = minutes;
        configuration.DelaiMaximalSecondes = delai;
        configuration.JoursDeRetention = retention;
        configuration.IgnorerErreurCertificat = ignorerCertificat;

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

        if (!TachesPlanifiees.Existe(TachesPlanifiees.NomService))
        {
            etapes.Add(TachesPlanifiees.CreerService().Message);
        }

        bool avecWindows = CaseDemarrerAvecWindows.IsChecked == true;

        if (avecWindows)
        {
            etapes.Add(TachesPlanifiees.CreerTray().Message);
        }
        else if (TachesPlanifiees.Existe(TachesPlanifiees.NomTray))
        {
            etapes.Add(TachesPlanifiees.Supprimer(TachesPlanifiees.NomTray).Message);
        }

        etapes.Add(ServiceEnCours()
            ? "Le service tourne : il relira la configuration dans les secondes qui viennent."
            : "Le service n'est pas lancé : utilise le bouton « Démarrer le service ».");
        AfficherStatut(string.Join(Environment.NewLine, etapes));
        RafraichirEtat();
    }

    /// <summary>Lance l'agent, et l'icône si le démarrage automatique est actif.</summary>
    private void SurDemarrerService(object sender, RoutedEventArgs e)
    {
        Mouse.OverrideCursor = Cursors.Wait;

        try
        {
            ResultatTache resultat = TachesPlanifiees.Demarrer(TachesPlanifiees.NomService);

            if (!resultat.Ok)
            {
                AfficherStatut(resultat.Message, erreur: true);
                return;
            }

            if (CaseDemarrerAvecWindows.IsChecked == true)
            {
                TachesPlanifiees.Demarrer(TachesPlanifiees.NomTray);
            }

            AfficherStatut("Service démarré.");
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }

        RafraichirEtat();
    }

    /// <summary>Arrête l'agent proprement, sans relance automatique.</summary>
    private void SurArreterService(object sender, RoutedEventArgs e)
    {
        Mouse.OverrideCursor = Cursors.Wait;

        try
        {
            ResultatTache resultat = TachesPlanifiees.ArreterService();
            AfficherStatut(resultat.Message, erreur: !resultat.Ok);
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }

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

    /// <summary>
    /// Ouvre la configuration des notifications par courriel.
    /// </summary>
    private void SurOuvrirNotifications(object sender, RoutedEventArgs e)
    {
        FenetreEmail fenetre = new() { Owner = this };
        fenetre.ShowDialog();
    }

    private void SurReduire(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    /// <summary>
    /// Déplace la fenêtre : l'en-tête fait office de barre de titre.
    /// </summary>
    private void EnTete_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed)
        {
            return;
        }

        try
        {
            // DragMove exige que le bouton soit encore enfoncé : un double-clic
            // rapide peut le relâcher entre-temps, d'où le garde-fou.
            DragMove();
        }
        catch (InvalidOperationException)
        {
            // Sans conséquence : la fenêtre reste simplement en place.
        }
    }

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

    /// <summary>
    /// Lit les réglages de la section « Réglages avancés ».
    /// </summary>
    private bool LireReglagesAvances(
        out int delai,
        out int retention,
        out bool ignorerCertificat,
        out string erreur)
    {
        delai = 0;
        retention = 0;
        ignorerCertificat = CaseIgnorerCertificat.IsChecked == true;
        erreur = string.Empty;

        if (!int.TryParse(ChampDelai.Text.Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out delai)
            || delai is < 1 or > 600)
        {
            erreur = "Le délai maximal d'attente doit être un nombre entier compris entre 1 et 600 secondes.";
            return false;
        }

        if (!int.TryParse(ChampRetention.Text.Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out retention)
            || retention < 0)
        {
            erreur = "Le nombre de jours de journaux conservés doit être un nombre entier positif ou nul "
                   + "(0 = conservation illimitée).";
            return false;
        }

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

    /// <summary>Récupère une couleur de la palette définie dans App.xaml.</summary>
    private Brush Couleur(string cle) => (Brush)FindResource(cle);

    private void AfficherStatut(string message, bool erreur = false)
    {
        TexteStatut.Text = message;
        TexteStatut.Foreground = erreur ? Couleur("Erreur") : Couleur("TexteSecondaire");
    }
}
