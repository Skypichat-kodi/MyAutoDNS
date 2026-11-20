namespace MyAutoDNS.Core;

/// <summary>
/// Tous les chemins du logiciel, en un seul endroit.
///
/// Répartition, identique aux autres logiciels de la machine :
///   C:\ProgramData\MyAutoDNS          le Service, sa configuration, son journal
///   C:\Program Files\MyAutoDNS        le Tray et l'interface de configuration
///
/// Le Service tourne sous le compte SYSTEM : c'est lui qui écrit dans ProgramData.
/// L'interface, elle, est élevée par un manifeste requireAdministrator, ce qui lui
/// permet d'y écrire aussi.
/// </summary>
public static class Chemins
{
    /// <summary>Nom du produit, utilisé pour les dossiers et les tâches planifiées.</summary>
    public const string Produit = "MyAutoDNS";

    /// <summary>Dossier de travail du Service : C:\ProgramData\MyAutoDNS</summary>
    public static string DossierDonnees { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        Produit);

    /// <summary>Dossier d'installation de l'interface et du Tray : C:\Program Files\MyAutoDNS</summary>
    public static string DossierProgrammes { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        Produit);

    /// <summary>Réglages saisis dans l'interface, relus par le Service.</summary>
    public static string FichierConfiguration => Path.Combine(DossierDonnees, "settings.json");

    /// <summary>
    /// Mémoire du Service : date de la dernière requête et de la dernière réussite.
    /// Conservée sur disque pour qu'un redémarrage de l'agent ne déclenche pas
    /// une requête supplémentaire.
    /// </summary>
    public static string FichierEtat => Path.Combine(DossierDonnees, "etat.json");

    /// <summary>Journaux, un fichier par jour.</summary>
    public static string DossierJournaux => Path.Combine(DossierDonnees, "logs");

    /// <summary>
    /// Déposé par l'interface quand on clique « Mettre à jour maintenant ».
    /// Le Service le voit, effectue la requête sans attendre l'échéance, puis le supprime.
    /// </summary>
    public static string FichierDemandeImmediate =>
        Path.Combine(DossierDonnees, "mise-a-jour-maintenant.flag");

    /// <summary>
    /// Déposé par l'interface ou par l'icône pour demander l'arrêt de l'agent.
    /// Il existe parce que tuer le processus (schtasks /End) est vu par Windows
    /// comme un échec, ce qui déclenche la relance automatique de la tâche.
    /// </summary>
    public static string FichierArretDemande => Path.Combine(DossierDonnees, "arret.flag");

    /// <summary>Chemin du journal du jour : logs\myautodns_2026-11-20.log</summary>
    public static string JournalDuJour() => JournalDe(DateTime.Now);

    /// <summary>Chemin du journal d'une date donnée.</summary>
    public static string JournalDe(DateTime date) => Path.Combine(
        DossierJournaux,
        $"myautodns_{date:yyyy-MM-dd}.log");

    /// <summary>Crée le dossier de données et celui des journaux si nécessaire.</summary>
    public static void PreparerDossiers()
    {
        Directory.CreateDirectory(DossierDonnees);
        Directory.CreateDirectory(DossierJournaux);
    }
}
