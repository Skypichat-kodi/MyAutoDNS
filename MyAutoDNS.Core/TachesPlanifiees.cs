using System.Diagnostics;
using System.Text;

namespace MyAutoDNS.Core;

/// <summary>Résultat d'une opération sur une tâche planifiée.</summary>
public sealed record ResultatTache(bool Ok, string Message);

/// <summary>
/// Création, suppression et pilotage des deux tâches planifiées de MyAutoDNS.
///
/// Les tâches sont créées par IMPORT DE XML, et non par les options de la ligne de
/// commande, pour trois raisons qui tiennent toutes à des pièges de Windows :
///
///  1. `schtasks /Create` sans XML impose une durée d'exécution maximale de
///     72 heures. Un agent permanent serait donc arrêté par Windows tous les
///     trois jours, sans le moindre message. Le XML fixe `ExecutionTimeLimit`
///     à `PT0S`, c'est-à-dire aucune limite.
///  2. Par défaut, Windows ne démarre pas une tâche si l'ordinateur fonctionne
///     sur batterie, et l'arrête dès qu'on débranche. Inacceptable pour un agent
///     qui doit tourner en permanence.
///  3. Le XML est le seul moyen de demander une relance automatique en cas
///     d'échec, et de fixer proprement le compte d'exécution.
///
/// Encodage : `schtasks /Create /XML` exige un fichier Unicode, d'où l'écriture
/// en UTF-16 avec BOM.
/// </summary>
public static class TachesPlanifiees
{
    /// <summary>Nom de la tâche de l'agent de fond.</summary>
    public const string NomService = "MyAutoDNS Service";

    /// <summary>Nom de la tâche de l'icône de notification.</summary>
    public const string NomTray = "MyAutoDNS Tray";

    public const string FichierService = "MyAutoDNS.Service.exe";
    public const string FichierTray = "MyAutoDNS.Tray.exe";
    public const string FichierUI = "MyAutoDNS.UI.exe";

    /// <summary>SID bien connu du compte SYSTEM.</summary>
    private const string SidSystem = "S-1-5-18";

    // ------------------------------------------------------------------
    // Localisation des programmes
    // ------------------------------------------------------------------

    /// <summary>Chemin de l'agent, ou null s'il est introuvable.</summary>
    public static string? LocaliserService() => Localiser(FichierService, Chemins.DossierDonnees);

    /// <summary>Chemin de l'icône de notification, ou null si elle est introuvable.</summary>
    public static string? LocaliserTray() =>
        Localiser(FichierTray, Chemins.DossierProgrammes, AppContext.BaseDirectory);

    /// <summary>Chemin de l'interface de configuration, ou null si elle est introuvable.</summary>
    public static string? LocaliserUI() =>
        Localiser(FichierUI, Chemins.DossierProgrammes, AppContext.BaseDirectory);

    private static string? Localiser(string nomFichier, params string[] dossiers)
    {
        foreach (string dossier in dossiers)
        {
            if (string.IsNullOrWhiteSpace(dossier))
            {
                continue;
            }

            string candidat = Path.Combine(dossier, nomFichier);

            if (File.Exists(candidat))
            {
                return candidat;
            }
        }

        return null;
    }

    // ------------------------------------------------------------------
    // Etat et création
    // ------------------------------------------------------------------

    /// <summary>Vrai si la tâche existe déjà.</summary>
    public static bool Existe(string nom) => Executer("/Query", "/TN", nom).Code == 0;

    /// <summary>
    /// Crée (ou remplace) la tâche de l'agent : au démarrage de la machine, sous
    /// le compte SYSTEM, sans limite de durée, avec relance en cas d'échec.
    /// </summary>
    public static ResultatTache CreerService()
    {
        string? chemin = LocaliserService();

        if (chemin is null)
        {
            return new ResultatTache(false,
                $"{FichierService} est introuvable dans « {Chemins.DossierDonnees} ». "
                + "L'installation est incomplète.");
        }

        return Installer(NomService, XmlService(chemin));
    }

    /// <summary>
    /// Crée (ou remplace) la tâche de l'icône : à l'ouverture de session de
    /// l'utilisateur courant, avec les droits maximaux afin qu'elle puisse
    /// arrêter l'agent et fermer l'interface sans invite UAC.
    /// </summary>
    public static ResultatTache CreerTray()
    {
        string? chemin = LocaliserTray();

        if (chemin is null)
        {
            return new ResultatTache(false,
                $"{FichierTray} est introuvable dans « {Chemins.DossierProgrammes} ». "
                + "L'installation est incomplète.");
        }

        return Installer(NomTray, XmlTray(chemin));
    }

    /// <summary>Supprime la tâche.</summary>
    public static ResultatTache Supprimer(string nom)
    {
        (int code, string sortie) = Executer("/Delete", "/TN", nom, "/F");

        if (code == 0)
        {
            return new ResultatTache(true, $"Tâche « {nom} » supprimée.");
        }

        return new ResultatTache(false, $"Suppression de « {nom} » impossible : {sortie}");
    }

    // ------------------------------------------------------------------
    // Pilotage
    // ------------------------------------------------------------------

    /// <summary>Démarre la tâche immédiatement.</summary>
    public static ResultatTache Demarrer(string nom)
    {
        (int code, string sortie) = Executer("/Run", "/TN", nom);

        return code == 0
            ? new ResultatTache(true, $"Tâche « {nom} » démarrée.")
            : new ResultatTache(false, $"Démarrage de « {nom} » impossible : {sortie}");
    }

    /// <summary>Arrête la tâche, donc le programme qu'elle a lancé.</summary>
    public static ResultatTache Arreter(string nom)
    {
        if (!Existe(nom))
        {
            return new ResultatTache(true, $"Tâche « {nom} » absente, rien à arrêter.");
        }

        (int code, string sortie) = Executer("/End", "/TN", nom);

        return code == 0
            ? new ResultatTache(true, $"Tâche « {nom} » arrêtée.")
            : new ResultatTache(false, $"Arrêt de « {nom} » impossible : {sortie}");
    }

    /// <summary>
    /// Arrête puis redémarre la tâche. Sert à faire relire la configuration par
    /// l'agent sans attendre la fin de son cycle en cours.
    /// </summary>
    public static ResultatTache Redemarrer(string nom)
    {
        if (!Existe(nom))
        {
            return new ResultatTache(false, $"La tâche « {nom} » n'existe pas.");
        }

        Executer("/End", "/TN", nom);

        // Laisse au Planificateur le temps de constater l'arrêt : enchaîner
        // immédiatement peut faire échouer le /Run sur « tâche déjà en cours ».
        Thread.Sleep(700);

        ResultatTache resultat = Demarrer(nom);

        return resultat.Ok
            ? new ResultatTache(true, $"Tâche « {nom} » redémarrée.")
            : resultat;
    }

    // ------------------------------------------------------------------
    // Fabrique XML
    // ------------------------------------------------------------------

    private static string XmlService(string cheminExecutable)
    {
        string dossier = Path.GetDirectoryName(cheminExecutable) ?? Chemins.DossierDonnees;

        return $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo>
                <Author>MyAutoDNS</Author>
                <Description>Envoie la requete de mise a jour DDNS a intervalle regulier.</Description>
              </RegistrationInfo>
              <Triggers>
                <BootTrigger>
                  <Enabled>true</Enabled>
                  <Delay>PT30S</Delay>
                </BootTrigger>
              </Triggers>
              <Principals>
                <Principal id="Author">
                  <UserId>{SidSystem}</UserId>
                  <RunLevel>HighestAvailable</RunLevel>
                </Principal>
              </Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <AllowHardTerminate>true</AllowHardTerminate>
                <StartWhenAvailable>true</StartWhenAvailable>
                <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
                <IdleSettings>
                  <StopOnIdleEnd>false</StopOnIdleEnd>
                  <RestartOnIdle>false</RestartOnIdle>
                </IdleSettings>
                <AllowStartOnDemand>true</AllowStartOnDemand>
                <Enabled>true</Enabled>
                <Hidden>false</Hidden>
                <RunOnlyIfIdle>false</RunOnlyIfIdle>
                <WakeToRun>false</WakeToRun>
                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                <Priority>7</Priority>
                <RestartOnFailure>
                  <Interval>PT1M</Interval>
                  <Count>3</Count>
                </RestartOnFailure>
              </Settings>
              <Actions Context="Author">
                <Exec>
                  <Command>{Echapper(cheminExecutable)}</Command>
                  <WorkingDirectory>{Echapper(dossier)}</WorkingDirectory>
                </Exec>
              </Actions>
            </Task>
            """;
    }

    private static string XmlTray(string cheminExecutable)
    {
        string dossier = Path.GetDirectoryName(cheminExecutable) ?? Chemins.DossierProgrammes;
        string utilisateur = $"{Environment.UserDomainName}\\{Environment.UserName}";

        return $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo>
                <Author>MyAutoDNS</Author>
                <Description>Icone de notification MyAutoDNS, ouverte a l'ouverture de session.</Description>
              </RegistrationInfo>
              <Triggers>
                <LogonTrigger>
                  <Enabled>true</Enabled>
                  <UserId>{Echapper(utilisateur)}</UserId>
                </LogonTrigger>
              </Triggers>
              <Principals>
                <Principal id="Author">
                  <UserId>{Echapper(utilisateur)}</UserId>
                  <LogonType>InteractiveToken</LogonType>
                  <RunLevel>HighestAvailable</RunLevel>
                </Principal>
              </Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <AllowHardTerminate>true</AllowHardTerminate>
                <StartWhenAvailable>true</StartWhenAvailable>
                <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
                <IdleSettings>
                  <StopOnIdleEnd>false</StopOnIdleEnd>
                  <RestartOnIdle>false</RestartOnIdle>
                </IdleSettings>
                <AllowStartOnDemand>true</AllowStartOnDemand>
                <Enabled>true</Enabled>
                <Hidden>false</Hidden>
                <RunOnlyIfIdle>false</RunOnlyIfIdle>
                <WakeToRun>false</WakeToRun>
                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                <Priority>7</Priority>
              </Settings>
              <Actions Context="Author">
                <Exec>
                  <Command>{Echapper(cheminExecutable)}</Command>
                  <WorkingDirectory>{Echapper(dossier)}</WorkingDirectory>
                </Exec>
              </Actions>
            </Task>
            """;
    }

    // ------------------------------------------------------------------
    // Outils internes
    // ------------------------------------------------------------------

    private static ResultatTache Installer(string nom, string xml)
    {
        string fichierTemporaire = Path.Combine(Path.GetTempPath(), $"myautodns-{Guid.NewGuid():N}.xml");

        try
        {
            // Unicode obligatoire : schtasks refuse un XML UTF-8.
            File.WriteAllText(fichierTemporaire, xml, new UnicodeEncoding(false, true));

            (int code, string sortie) = Executer("/Create", "/TN", nom, "/XML", fichierTemporaire, "/F");

            return code == 0
                ? new ResultatTache(true, $"Tâche « {nom} » créée.")
                : new ResultatTache(false, $"Création de « {nom} » impossible : {sortie}");
        }
        catch (Exception ex)
        {
            return new ResultatTache(false, $"Création de « {nom} » impossible : {ex.Message}");
        }
        finally
        {
            try
            {
                File.Delete(fichierTemporaire);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Fichier temporaire : Windows le nettoiera.
            }
        }
    }

    /// <summary>Échappe le texte pour insertion dans un attribut ou un élément XML.</summary>
    private static string Echapper(string valeur) => valeur
        .Replace("&", "&amp;", StringComparison.Ordinal)
        .Replace("<", "&lt;", StringComparison.Ordinal)
        .Replace(">", "&gt;", StringComparison.Ordinal)
        .Replace("\"", "&quot;", StringComparison.Ordinal)
        .Replace("'", "&apos;", StringComparison.Ordinal);

    /// <summary>Lance schtasks.exe et renvoie son code de sortie et sa sortie texte.</summary>
    private static (int Code, string Sortie) Executer(params string[] arguments)
    {
        try
        {
            ProcessStartInfo info = new("schtasks.exe")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            foreach (string argument in arguments)
            {
                info.ArgumentList.Add(argument);
            }

            using Process? processus = Process.Start(info);

            if (processus is null)
            {
                return (1, "schtasks.exe n'a pas pu être lancé.");
            }

            string sortie = processus.StandardOutput.ReadToEnd() + processus.StandardError.ReadToEnd();
            processus.WaitForExit();

            return (processus.ExitCode, sortie.Trim());
        }
        catch (Exception ex)
        {
            return (1, ex.Message);
        }
    }
}
