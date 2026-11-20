using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MyAutoDNS.Core;

/// <summary>
/// Réglages d'envoi des notifications par courriel.
///
/// Le principe reprend celui de MCEMonitor (onglet Email) : expéditeur, mot de
/// passe, destinataire, serveur, port et sécurité. Deux différences assumées :
///
///  - le mot de passe est chiffré avec DPAPI en portée MACHINE. La portée
///    « utilisateur » ne conviendrait pas : le service tourne sous SYSTEM et
///    l'interface sous le compte administrateur, ce sont deux comptes distincts.
///    Un fichier recopié sur un autre poste devient donc inutilisable ;
///  - le serveur SMTP est joignable par System.Net.Mail, qui ne gère que
///    STARTTLS : d'où le port 587 par défaut, et non 465.
/// </summary>
public sealed class ConfigurationEmail
{
    private static readonly JsonSerializerOptions OptionsLecture = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static readonly JsonSerializerOptions OptionsEcriture = new()
    {
        WriteIndented = true,
    };

    /// <summary>Envoi des notifications activé.</summary>
    public bool Actif { get; set; }

    /// <summary>Adresse d'expédition, qui sert aussi d'identifiant SMTP.</summary>
    public string Expediteur { get; set; } = string.Empty;

    /// <summary>Adresse qui reçoit l'alerte.</summary>
    public string Destinataire { get; set; } = string.Empty;

    /// <summary>Nom d'hôte du serveur SMTP, par exemple smtp.gmail.com.</summary>
    public string Serveur { get; set; } = string.Empty;

    /// <summary>Port du serveur. 587 = STARTTLS, 25 = non chiffré.</summary>
    public int Port { get; set; } = 587;

    /// <summary>
    /// Mode de chiffrement de la connexion :
    /// Ssl = TLS immédiat (port 465), StartTls = TLS après négociation (port 587),
    /// Aucun = trafic en clair (port 25, serveur local uniquement).
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ModeSecurite Securite { get; set; } = ModeSecurite.StartTls;

    /// <summary>Mot de passe chiffré, tel qu'il est écrit dans le fichier. Jamais affiché.</summary>
    public string MotDePasseChiffre { get; set; } = string.Empty;

    /// <summary>Mot de passe en clair, en mémoire uniquement : il n'est jamais sérialisé.</summary>
    [JsonIgnore]
    public string MotDePasse { get; set; } = string.Empty;

    /// <summary>
    /// Vrai si le mot de passe stocké n'a pas pu être déchiffré (fichier recopié
    /// depuis un autre poste, ou compte différent). L'interface doit alors le
    /// demander de nouveau.
    /// </summary>
    [JsonIgnore]
    public bool MotDePasseIllisible { get; private set; }

    /// <summary>Charge email.json, ou renvoie une configuration vide s'il est absent.</summary>
    public static ConfigurationEmail Charger()
    {
        var configuration = new ConfigurationEmail();

        try
        {
            if (!File.Exists(Chemins.FichierEmail))
            {
                return configuration;
            }

            string json = File.ReadAllText(Chemins.FichierEmail, Encoding.UTF8);
            var lu = JsonSerializer.Deserialize<ConfigurationEmail>(json, OptionsLecture);

            if (lu is not null)
            {
                configuration = lu;
            }
        }
        catch (Exception ex)
        {
            Journal.Ecrire($"Configuration des notifications illisible : {ex.Message}");
            return new ConfigurationEmail();
        }

        configuration.MotDePasse = ProtectionDonnees.Dechiffrer(configuration.MotDePasseChiffre) ?? string.Empty;
        configuration.MotDePasseIllisible =
            !string.IsNullOrEmpty(configuration.MotDePasseChiffre) && configuration.MotDePasse.Length == 0;

        return configuration;
    }

    /// <summary>Écrit email.json, mot de passe chiffré.</summary>
    public void Enregistrer()
    {
        Chemins.PreparerDossiers();

        MotDePasseChiffre = ProtectionDonnees.Chiffrer(MotDePasse);

        string json = JsonSerializer.Serialize(this, OptionsEcriture);
        string temporaire = Chemins.FichierEmail + ".tmp";

        File.WriteAllText(temporaire, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(temporaire, Chemins.FichierEmail, overwrite: true);
    }

    /// <summary>Vérifie la cohérence des réglages avant enregistrement ou envoi.</summary>
    /// <returns>null si tout est correct, sinon le message à afficher.</returns>
    public string? Valider()
    {
        if (!Actif)
        {
            return null;
        }

        if (!EstAdresseValide(Expediteur))
        {
            return "L'adresse d'expédition n'est pas une adresse électronique valide.";
        }

        if (!EstAdresseValide(Destinataire))
        {
            return "L'adresse du destinataire n'est pas une adresse électronique valide.";
        }

        if (string.IsNullOrWhiteSpace(Serveur))
        {
            return "Le nom du serveur SMTP n'est pas renseigné.";
        }

        if (Port is < 1 or > 65535)
        {
            return "Le port du serveur SMTP doit être compris entre 1 et 65535.";
        }

        if (string.IsNullOrEmpty(MotDePasse))
        {
            return MotDePasseIllisible
                ? "Le mot de passe enregistré n'a pas pu être déchiffré sur ce poste. Saisis-le de nouveau."
                : "Le mot de passe SMTP n'est pas renseigné.";
        }

        return null;
    }

    private static bool EstAdresseValide(string adresse)
    {
        if (string.IsNullOrWhiteSpace(adresse) || !adresse.Contains('@', StringComparison.Ordinal))
        {
            return false;
        }

        return adresse.IndexOf('@', StringComparison.Ordinal) > 0
            && adresse.LastIndexOf('@') < adresse.Length - 1
            && !adresse.Contains(' ', StringComparison.Ordinal);
    }
}

/// <summary>Mode de chiffrement retenu pour la connexion au serveur SMTP.</summary>
public enum ModeSecurite
{
    /// <summary>TLS immédiat à la connexion. Port 465.</summary>
    Ssl = 0,

    /// <summary>TLS négocié après la bannière du serveur. Port 587.</summary>
    StartTls = 1,

    /// <summary>Trafic en clair. Port 25, réservé à un serveur local.</summary>
    Aucun = 2,
}

public static class ModeSecuriteExtensions
{
    public static string Libelle(this ModeSecurite mode) => mode switch
    {
        ModeSecurite.Ssl => "SSL (port 465)",
        ModeSecurite.Aucun => "Aucun chiffrement (port 25)",
        _ => "STARTTLS (port 587)",
    };
}

/// <summary>
/// Chiffrement du mot de passe avec DPAPI.
///
/// La portée est LocalMachine : le service (SYSTEM) et l'interface
/// (administrateur) doivent tous deux pouvoir déchiffrer la même valeur.
/// </summary>
internal static class ProtectionDonnees
{
    /// <summary>Sel applicatif : une valeur différente produit un chiffré différent.</summary>
    private static readonly byte[] Entropie = Encoding.UTF8.GetBytes("MyAutoDNS.notifications.v1");

    public static string Chiffrer(string clair)
    {
        if (string.IsNullOrEmpty(clair))
        {
            return string.Empty;
        }

        try
        {
            byte[] octets = Encoding.UTF8.GetBytes(clair);
            byte[] proteges = ProtectedData.Protect(octets, Entropie, DataProtectionScope.LocalMachine);

            return Convert.ToBase64String(proteges);
        }
        catch (Exception ex)
        {
            Journal.Ecrire($"Chiffrement du mot de passe impossible : {ex.Message}");
            return string.Empty;
        }
    }

    /// <summary>Renvoie null si la valeur est vide ou indéchiffrable.</summary>
    public static string? Dechiffrer(string chiffre)
    {
        if (string.IsNullOrWhiteSpace(chiffre))
        {
            return null;
        }

        try
        {
            byte[] proteges = Convert.FromBase64String(chiffre);
            byte[] octets = ProtectedData.Unprotect(proteges, Entropie, DataProtectionScope.LocalMachine);

            return Encoding.UTF8.GetString(octets);
        }
        catch (Exception)
        {
            // Fichier venu d'un autre poste, ou compte différent : illisible.
            return null;
        }
    }
}
