using System.Diagnostics;
using System.Drawing;
using MyAutoDNS.Core;

namespace MyAutoDNS.Tray;

/// <summary>
/// Vie de l'icône dans la zone de notification.
///
/// Règles demandées :
///  - le Tray ne vit pas sans le Service : s'il ne le trouve pas au démarrage, il
///    patiente 30 secondes (le démarrage de Windows n'est pas instantané), puis
///    prévient et se ferme ; s'il l'a vu tourner puis qu'il disparaît, il se ferme ;
///  - clic gauche : ouvre l'interface de configuration ;
///  - clic droit : propose de quitter, ce qui ferme l'interface, arrête le Service
///    et, par conséquent, ferme l'icône elle-même.
/// </summary>
internal sealed class ContexteTray : ApplicationContext
{
    private const string NomProcessusService = "MyAutoDNS.Service";
    private const string NomProcessusUI = "MyAutoDNS.UI";

    /// <summary>Fréquence de vérification de la présence du Service.</summary>
    private static readonly TimeSpan IntervalleSurveillance = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Nombre de vérifications négatives avant de considérer le Service absent.
    /// Évite que l'icône clignote pendant un redémarrage de l'agent.
    /// </summary>
    private const int EchecsToleres = 2;

    /// <summary>Nombre de vérifications avant d'avertir que le Service n'est jamais venu.</summary>
    private const int TicksAvantAvertissement = 6;

    private readonly NotifyIcon _icone = new();
    private readonly System.Windows.Forms.Timer _minuteur = new();
    private readonly IconeEtat _etatActif = new(Color.FromArgb(46, 160, 67));
    private readonly IconeEtat _etatInactif = new(Color.FromArgb(217, 48, 37));

    private bool _serviceActif;
    private bool _serviceDejaVu;
    private bool _verificationEnCours;
    private int _echecsConsecutifs;
    private int _ticksSansService;

    public ContexteTray()
    {
        ContextMenuStrip menu = new();

        ToolStripMenuItem ouvrir = new("Ouvrir la configuration");
        ouvrir.Font = new Font(ouvrir.Font, FontStyle.Bold);
        ouvrir.Click += (_, _) => OuvrirConfiguration();

        ToolStripMenuItem quitter = new("Quitter MyAutoDNS");
        quitter.Click += (_, _) => DemanderArret();

        menu.Items.Add(ouvrir);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(quitter);

        _icone.ContextMenuStrip = menu;
        _icone.Icon = _etatInactif.Icone;
        _icone.Text = "MyAutoDNS — recherche du service…";
        _icone.Visible = true;

        _icone.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                OuvrirConfiguration();
            }
        };

        _minuteur.Interval = (int)IntervalleSurveillance.TotalMilliseconds;
        _minuteur.Tick += (_, _) => VerifierService();
        _minuteur.Start();

        // Première vérification une fois la boucle de messages installée : sans
        // cela, une fermeture immédiate s'exécuterait hors du thread de l'interface.
        Application.Idle += SurPremierIdle;
    }

    private void SurPremierIdle(object? sender, EventArgs e)
    {
        Application.Idle -= SurPremierIdle;
        VerifierService();
    }

    // ------------------------------------------------------------------
    // Surveillance du Service
    // ------------------------------------------------------------------

    private void VerifierService()
    {
        if (_verificationEnCours)
        {
            return;
        }

        _verificationEnCours = true;

        try
        {
            if (ServiceEnCours())
            {
                _serviceDejaVu = true;
                _echecsConsecutifs = 0;
                _ticksSansService = 0;
                AppliquerEtat(true);
                return;
            }

            _echecsConsecutifs++;

            if (_echecsConsecutifs < EchecsToleres)
            {
                return;
            }

            AppliquerEtat(false);

            // Le Service a existé puis a disparu : le Tray n'a plus de raison d'être.
            if (_serviceDejaVu)
            {
                Fermer();
                return;
            }

            // Service absent depuis le démarrage : on patiente, puis on prévient.
            _ticksSansService++;

            if (_ticksSansService >= TicksAvantAvertissement)
            {
                MessageBox.Show(
                    "Le service MyAutoDNS ne répond pas." + Environment.NewLine
                    + "L'icône va se fermer." + Environment.NewLine + Environment.NewLine
                    + "Vérifiez que la tâche planifiée « " + TachesPlanifiees.NomService
                    + " » est active, ou ouvrez la configuration pour le redémarrer.",
                    "MyAutoDNS",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);

                Fermer();
            }
        }
        finally
        {
            _verificationEnCours = false;
        }
    }

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

    // ------------------------------------------------------------------
    // Etat de l'icône
    // ------------------------------------------------------------------

    private void AppliquerEtat(bool actif)
    {
        if (actif != _serviceActif)
        {
            _serviceActif = actif;
            _icone.Icon = actif ? _etatActif.Icone : _etatInactif.Icone;
        }

        MettreAJourBulle();
    }

    private void MettreAJourBulle()
    {
        string texte;

        if (_serviceActif)
        {
            // La dernière ligne du journal donne l'état réel de la synchronisation :
            // c'est plus utile qu'un simple « actif ».
            string? derniere = Journal.DerniereLigne();
            texte = derniere is null ? "MyAutoDNS — service actif" : $"MyAutoDNS — {derniere}";
        }
        else
        {
            texte = "MyAutoDNS — service arrêté";
        }

        // NotifyIcon refuse les info-bulles de plus de 63 caractères.
        _icone.Text = texte.Length > 63 ? texte[..63] : texte;
    }

    // ------------------------------------------------------------------
    // Actions
    // ------------------------------------------------------------------

    private void OuvrirConfiguration()
    {
        string? chemin = TachesPlanifiees.LocaliserUI();

        if (chemin is null)
        {
            MessageBox.Show(
                $"{TachesPlanifiees.FichierUI} est introuvable." + Environment.NewLine
                + $"Il doit se trouver dans « {Chemins.DossierProgrammes} ».",
                "MyAutoDNS",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        try
        {
            // UseShellExecute déclenche l'invite UAC : l'interface porte un
            // manifeste requireAdministrator, elle en a besoin pour écrire dans
            // ProgramData et piloter les tâches planifiées.
            Process.Start(new ProcessStartInfo(chemin)
            {
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(chemin) ?? Chemins.DossierProgrammes,
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Impossible d'ouvrir la configuration :{Environment.NewLine}{ex.Message}",
                "MyAutoDNS",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private void DemanderArret()
    {
        DialogResult confirmation = MessageBox.Show(
            "Arrêter MyAutoDNS ?" + Environment.NewLine + Environment.NewLine
            + "La configuration va se fermer, le service va s'arrêter et cette icône disparaîtra."
            + Environment.NewLine
            + "Les mises à jour reprendront au prochain démarrage de Windows.",
            "MyAutoDNS",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button2);

        if (confirmation != DialogResult.Yes)
        {
            return;
        }

        // 1. Fermer l'interface d'abord : sinon elle pourrait relancer le service
        //    dans la foulée et annuler l'arrêt demandé.
        FermerInterface();

        // 2. Arrêter le service : par la tâche planifiée, puis en dernier recours
        //    par le processus lui-même si la tâche n'existe pas ou a échoué.
        TachesPlanifiees.Arreter(TachesPlanifiees.NomService);
        ArreterProcessus(NomProcessusService);

        // 3. Et, par conséquent, se fermer.
        Fermer();
    }

    private static void FermerInterface()
    {
        Process[] processus = Process.GetProcessesByName(NomProcessusUI);

        foreach (Process instance in processus)
        {
            try
            {
                if (!instance.HasExited)
                {
                    instance.Kill();
                    instance.WaitForExit(3000);
                }
            }
            catch (Exception)
            {
                // Interface déjà fermée, ou droits insuffisants : sans gravité.
            }
            finally
            {
                instance.Dispose();
            }
        }
    }

    private static void ArreterProcessus(string nomProcessus)
    {
        Process[] processus = Process.GetProcessesByName(nomProcessus);

        foreach (Process instance in processus)
        {
            try
            {
                if (!instance.HasExited)
                {
                    instance.Kill();
                    instance.WaitForExit(3000);
                }
            }
            catch (Exception)
            {
                // Processus déjà arrêté par la tâche planifiée : c'est le cas normal.
            }
            finally
            {
                instance.Dispose();
            }
        }
    }

    private void Fermer()
    {
        _minuteur.Stop();
        _icone.Visible = false;
        ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _minuteur.Dispose();
            _icone.Dispose();
            _etatActif.Dispose();
            _etatInactif.Dispose();
        }

        base.Dispose(disposing);
    }
}
