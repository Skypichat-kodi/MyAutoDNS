using System.Diagnostics;
using System.Globalization;

namespace MyAutoDNS.Core;

/// <summary>
/// Arrêt propre de l'agent de fond.
///
/// La tâche planifiée de l'agent porte RestartOnFailure : si le processus est tué
/// brutalement par « schtasks /End », Windows considère que l'exécution a échoué et
/// relance la tâche une minute plus tard. Un utilisateur qui clique « Quitter »
/// verrait alors l'icône et le service revenir, ce qui n'est pas acceptable.
///
/// La séquence correcte est donc :
///   1. déposer le fichier d'arrêt ;
///   2. laisser l'agent terminer son cycle et sortir avec le code 0 ;
///   3. supprimer le fichier — même s'il n'a pas servi, sinon le prochain
///      démarrage s'arrêterait aussitôt.
/// </summary>
public static class ArretAgent
{
    /// <summary>Nom du processus de l'agent, sans l'extension.</summary>
    public const string NomProcessus = "MyAutoDNS.Service";

    /// <summary>Dépose la demande d'arrêt.</summary>
    public static void Demander()
    {
        try
        {
            Chemins.PreparerDossiers();
            File.WriteAllText(
                Chemins.FichierArretDemande,
                DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // L'appelant retombera sur l'arrêt forcé.
        }
    }

    /// <summary>Supprime un fichier d'arrêt resté en place. À faire avant tout démarrage.</summary>
    public static void Reinitialiser()
    {
        try
        {
            File.Delete(Chemins.FichierArretDemande);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Absent, ou verrouillé : sans conséquence ici.
        }
    }

    /// <summary>Vrai si le drapeau d'arrêt attend encore d'être traité.</summary>
    public static bool DemandeEnAttente() => File.Exists(Chemins.FichierArretDemande);

    /// <summary>L'agent tourne-t-il en ce moment ?</summary>
    public static bool EstEnCours()
    {
        Process[] processus = Process.GetProcessesByName(NomProcessus);

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

    /// <summary>
    /// Attend la disparition du processus de l'agent.
    /// </summary>
    /// <returns>Vrai si l'agent s'est arrêté dans le délai imparti.</returns>
    public static bool AttendreArret(TimeSpan delai)
    {
        DateTime limite = DateTime.UtcNow + delai;

        while (DateTime.UtcNow < limite)
        {
            if (!EstEnCours())
            {
                return true;
            }

            Thread.Sleep(250);
        }

        return !EstEnCours();
    }
}
