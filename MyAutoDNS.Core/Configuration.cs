using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MyAutoDNS.Core;

/// <summary>
/// Réglages saisis dans l'interface et relus par le Service.
///
/// Le fichier est réécrit en entier à chaque enregistrement : il ne doit donc
/// contenir aucun commentaire, ils seraient perdus. La documentation des réglages
/// vit dans l'interface et dans le README.
/// </summary>
public sealed class Configuration
{
    /// <summary>Intervalle le plus court accepté, en minutes.</summary>
    public const int IntervalleMinimumMinutes = 5;

    /// <summary>Intervalle le plus long accepté, en minutes (une semaine).</summary>
    public const int IntervalleMaximumMinutes = 7 * 24 * 60;

    private static readonly JsonSerializerOptions OptionsLecture = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static readonly JsonSerializerOptions OptionsEcriture = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    /// <summary>
    /// Adresse de mise à jour fournie par le fournisseur de nom de domaine.
    /// Pour afraid.org : menu « Direct URL ».
    /// </summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// Fréquence des requêtes, en minutes. L'interface propose des minutes ou des
    /// heures, mais tout est stocké en minutes : une seule valeur, aucune ambiguïté.
    /// </summary>
    public int IntervalleMinutes { get; set; } = 60;

    /// <summary>Délai maximal accordé à une requête avant abandon, en secondes.</summary>
    public int DelaiMaximalSecondes { get; set; } = 30;

    /// <summary>
    /// Équivalent de l'option « -k » de curl : ignore les erreurs de certificat TLS.
    /// </summary>
    public bool IgnorerErreurCertificat { get; set; } = true;

    /// <summary>Nombre de jours de journaux conservés. 0 = conservation illimitée.</summary>
    public int JoursDeRetention { get; set; } = 30;

    /// <summary>Intervalle sous forme de durée.</summary>
    [JsonIgnore]
    public TimeSpan Intervalle => TimeSpan.FromMinutes(IntervalleMinutes);

    /// <summary>Configuration par défaut, utilisée tant que rien n'a été enregistré.</summary>
    public static Configuration ParDefaut() => new();

    /// <summary>
    /// Complète l'adresse par le slash final attendu par certains fournisseurs.
    ///
    /// afraid.org répond « Error 404 : Page not found » à une adresse de
    /// synchronisation privée du slash final, alors même que le jeton est correct :
    /// c'est une faute de saisie facile à commettre et difficile à diagnostiquer.
    ///
    /// Les adresses portant des paramètres de requête ne sont pas touchées : un
    /// slash ajouté après le « ? » rendrait l'adresse invalide.
    /// </summary>
    public static string NormaliserUrl(string url)
    {
        string adresse = (url ?? string.Empty).Trim();

        if (adresse.Length == 0
            || adresse.Contains('?', StringComparison.Ordinal)
            || adresse.Contains('#', StringComparison.Ordinal))
        {
            return adresse;
        }

        return adresse.EndsWith('/') ? adresse : adresse + "/";
    }

    /// <summary>
    /// Charge settings.json. Si le fichier est absent, renvoie la configuration par
    /// défaut (Url vide, donc invalide : le Service attendra une saisie).
    /// </summary>
    /// <exception cref="JsonException">Le fichier existe mais est mal formé.</exception>
    public static Configuration Charger()
    {
        if (!File.Exists(Chemins.FichierConfiguration))
        {
            return ParDefaut();
        }

        string json = File.ReadAllText(Chemins.FichierConfiguration, Encoding.UTF8);
        Configuration? configuration = JsonSerializer.Deserialize<Configuration>(json, OptionsLecture);

        // Une adresse éditée à la main dans le fichier bénéficie de la même
        // correction qu'une adresse saisie dans l'interface.
        Configuration resultat = configuration ?? ParDefaut();
        resultat.Url = NormaliserUrl(resultat.Url);

        return resultat;
    }

    /// <summary>Charge sans lever d'exception : renvoie la configuration par défaut en cas de problème.</summary>
    public static Configuration ChargerSansEchec(out string? probleme)
    {
        try
        {
            probleme = null;
            return Charger();
        }
        catch (Exception ex)
        {
            probleme = ex.Message;
            return ParDefaut();
        }
    }

    /// <summary>Écrit le fichier settings.json, en créant le dossier au besoin.</summary>
    public void Enregistrer()
    {
        Chemins.PreparerDossiers();

        // Le slash final manquant provoque un 404 chez afraid.org : on le complète
        // avant d'écrire, quelle que soit l'origine de la saisie.
        Url = NormaliserUrl(Url);

        string json = JsonSerializer.Serialize(this, OptionsEcriture);

        // Écriture par fichier temporaire puis remplacement : si l'agent lit le
        // fichier pile au mauvais moment, il ne tombe jamais sur un JSON tronqué.
        string temporaire = Chemins.FichierConfiguration + ".tmp";
        File.WriteAllText(temporaire, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(temporaire, Chemins.FichierConfiguration, overwrite: true);
    }

    /// <summary>Date de dernière modification du fichier, ou null s'il n'existe pas.</summary>
    public static DateTime? DateDeModification()
    {
        return File.Exists(Chemins.FichierConfiguration)
            ? File.GetLastWriteTimeUtc(Chemins.FichierConfiguration)
            : null;
    }

    /// <summary>
    /// Vérifie la cohérence des réglages.
    /// </summary>
    /// <returns>null si tout est correct, sinon le message à afficher.</returns>
    public string? Valider()
    {
        if (string.IsNullOrWhiteSpace(Url))
        {
            return "L'adresse de mise à jour n'est pas renseignée.";
        }

        if (!Uri.TryCreate(Url.Trim(), UriKind.Absolute, out Uri? uri))
        {
            return "L'adresse de mise à jour n'est pas une adresse complète. "
                 + "Elle doit commencer par http:// ou https://.";
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            return $"L'adresse doit commencer par http:// ou https:// (détecté : « {uri.Scheme} »).";
        }

        if (IntervalleMinutes < IntervalleMinimumMinutes || IntervalleMinutes > IntervalleMaximumMinutes)
        {
            return $"L'intervalle doit être compris entre {IntervalleMinimumMinutes} minutes "
                 + $"et {IntervalleMaximumMinutes / 60 / 24} jours.";
        }

        if (DelaiMaximalSecondes is < 1 or > 600)
        {
            return "Le délai maximal d'attente doit être compris entre 1 et 600 secondes.";
        }

        if (JoursDeRetention < 0)
        {
            return "Le nombre de jours de journaux conservés ne peut pas être négatif.";
        }

        return null;
    }

    /// <summary>Copie superficielle, pour comparer deux états de la configuration.</summary>
    public Configuration Copier() => (Configuration)MemberwiseClone();

    /// <summary>
    /// Reprend les valeurs d'une autre configuration, sans changer l'instance :
    /// le Service garde ainsi une seule référence pendant toute sa vie.
    /// </summary>
    public void Adopter(Configuration autre)
    {
        Url = autre.Url;
        IntervalleMinutes = autre.IntervalleMinutes;
        DelaiMaximalSecondes = autre.DelaiMaximalSecondes;
        IgnorerErreurCertificat = autre.IgnorerErreurCertificat;
        JoursDeRetention = autre.JoursDeRetention;
    }
}
