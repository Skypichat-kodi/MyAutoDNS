using System.Globalization;
using System.Text;

namespace MyAutoDNS.Core;

/// <summary>
/// Journal du Service : un fichier par jour, une ligne par événement.
///
/// Le format reprend celui du script d'origine (horodatage puis résultat) pour
/// que rien ne change dans les habitudes de lecture.
/// </summary>
public static class Journal
{
    /// <summary>Longueur maximale d'un message, pour qu'une page d'erreur HTML ne noie pas le fichier.</summary>
    private const int LongueurMaximale = 400;

    private static readonly UTF8Encoding EncodageAvecBom = new(encoderShouldEmitUTF8Identifier: true);

    /// <summary>
    /// Vide le journal du jour et le recrée.
    ///
    /// Appelé au démarrage de l'agent : chaque session repart sur un journal
    /// propre, sans se mélanger avec les démarrages précédents.
    /// </summary>
    public static void Reinitialiser()
    {
        try
        {
            Chemins.PreparerDossiers();

            // WriteAllText sur une chaîne vide crée un fichier neuf : le BOM est
            // écrit, et les lignes suivantes s'ajoutent derrière.
            File.WriteAllText(Chemins.JournalDuJour(), string.Empty, EncodageAvecBom);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Remise à zéro du journal impossible : {ex.Message}");
        }
    }

    /// <summary>Ajoute une ligne horodatée au journal du jour.</summary>
    public static void Ecrire(string message)
    {
        string horodatage = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        string ligne = $"{horodatage}  {Reduire(message)}{Environment.NewLine}";

        try
        {
            Chemins.PreparerDossiers();
            File.AppendAllText(Chemins.JournalDuJour(), ligne, EncodageAvecBom);
        }
        catch (Exception ex)
        {
            // Un journal inaccessible ne doit jamais interrompre le service.
            Console.Error.WriteLine($"Journal inscriptible ? {ex.Message}");
        }
    }

    /// <summary>
    /// Renvoie la dernière ligne non vide du journal du jour, ou null s'il n'y en a pas.
    /// Sert à l'interface et à l'info-bulle du Tray.
    /// </summary>
    public static string? DerniereLigne()
    {
        try
        {
            string chemin = Chemins.JournalDuJour();

            if (!File.Exists(chemin))
            {
                return null;
            }

            // Le fichier reste petit (une ligne par requête) : une lecture complète
            // est plus simple et plus sûre qu'un parcours en sens inverse.
            string[] lignes = File.ReadAllLines(chemin, Encoding.UTF8);

            for (int i = lignes.Length - 1; i >= 0; i--)
            {
                if (!string.IsNullOrWhiteSpace(lignes[i]))
                {
                    return lignes[i].Trim();
                }
            }

            return null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Renvoie les n dernières lignes du journal, les plus récentes en dernier.</summary>
    public static IReadOnlyList<string> DernieresLignes(int nombre)
    {
        try
        {
            string chemin = Chemins.JournalDuJour();

            if (!File.Exists(chemin) || nombre <= 0)
            {
                return Array.Empty<string>();
            }

            string[] lignes = File.ReadAllLines(chemin, Encoding.UTF8);
            return lignes.Where(l => !string.IsNullOrWhiteSpace(l)).TakeLast(nombre).ToArray();
        }
        catch (Exception)
        {
            return Array.Empty<string>();
        }
    }

    /// <summary>Supprime les journaux plus anciens que le nombre de jours indiqué.</summary>
    public static void PurgerAnciens(int joursDeRetention)
    {
        if (joursDeRetention <= 0)
        {
            return;
        }

        DateTime limite = DateTime.UtcNow.AddDays(-joursDeRetention);

        try
        {
            foreach (string fichier in Directory.EnumerateFiles(Chemins.DossierJournaux, "myautodns_*.log"))
            {
                try
                {
                    if (File.GetLastWriteTimeUtc(fichier) < limite)
                    {
                        File.Delete(fichier);
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Fichier encore ouvert : il repartira au prochain passage.
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Purge des journaux impossible : {ex.Message}");
        }
    }

    /// <summary>Ramène un message sur une seule ligne et le tronque.</summary>
    public static string Reduire(string message)
    {
        string aplati = message
            .Replace("\r\n", " ", StringComparison.Ordinal)
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();

        if (aplati.Length == 0)
        {
            return "(vide)";
        }

        return aplati.Length > LongueurMaximale
            ? string.Concat(aplati.AsSpan(0, LongueurMaximale), " […]")
            : aplati;
    }
}
