using System.Text;
using System.Text.Json;

namespace MyAutoDNS.Core;

/// <summary>
/// Mémoire du Service entre deux démarrages.
///
/// Sans ce fichier, chaque redémarrage de l'agent (relance par le Planificateur,
/// ou « Appliquer » dans l'interface) enverrait une requête supplémentaire au
/// fournisseur. On conserve donc la date de la dernière tentative et celle de la
/// dernière réussite.
/// </summary>
public sealed class EtatAgent
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    /// <summary>Date de la dernière requête tentée, quel qu'en soit le résultat.</summary>
    public DateTimeOffset? DerniereRequeteUtc { get; set; }

    /// <summary>Date de la dernière requête réussie.</summary>
    public DateTimeOffset? DerniereReussiteUtc { get; set; }

    /// <summary>Résumé lisible du dernier résultat.</summary>
    public string? DernierResultat { get; set; }

    /// <summary>Charge l'état, ou renvoie un état vide si le fichier est absent ou illisible.</summary>
    public static EtatAgent Charger()
    {
        try
        {
            if (!File.Exists(Chemins.FichierEtat))
            {
                return new EtatAgent();
            }

            string json = File.ReadAllText(Chemins.FichierEtat, Encoding.UTF8);
            return JsonSerializer.Deserialize<EtatAgent>(json, Options) ?? new EtatAgent();
        }
        catch (Exception ex)
        {
            // État illisible : on repart de zéro, au pire une requête de plus.
            Journal.Ecrire($"État illisible, réinitialisation ({ex.Message}).");
            return new EtatAgent();
        }
    }

    /// <summary>Enregistre la date de la tentative, et celle de la réussite le cas échéant.</summary>
    public void EnregistrerRequete(bool reussie)
    {
        DateTimeOffset maintenant = DateTimeOffset.UtcNow;

        DerniereRequeteUtc = maintenant;

        if (reussie)
        {
            DerniereReussiteUtc = maintenant;
        }

        DernierResultat = reussie ? "réussie" : "échouée";

        Enregistrer();
    }

    /// <summary>Écrit l'état sur disque, sans lever d'exception.</summary>
    public void Enregistrer()
    {
        try
        {
            Chemins.PreparerDossiers();

            string json = JsonSerializer.Serialize(this, Options);
            string temporaire = Chemins.FichierEtat + ".tmp";

            File.WriteAllText(temporaire, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(temporaire, Chemins.FichierEtat, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Enregistrement de l'état impossible : {ex.Message}");
        }
    }
}
