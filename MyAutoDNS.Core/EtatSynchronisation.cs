namespace MyAutoDNS.Core;

/// <summary>
/// Ce que la réponse du fournisseur DDNS signifie réellement.
///
/// afraid.org répond en texte libre, par exemple :
///   « Updated 1 host skypichat.mooo.com to 90.62.247.246 »
///   « No IP change detected for skypichat.mooo.com with IP 90.62.247.246, skipping update »
/// Ces deux réponses sont des succès HTTP 200, mais elles ne veulent pas dire la
/// même chose. On les distingue pour que l'utilisateur sache d'un coup d'œil si
/// son adresse a bougé ou non.
/// </summary>
public enum EtatSynchronisation
{
    /// <summary>Réponse que l'on ne sait pas interpréter.</summary>
    Inconnu = 0,

    /// <summary>Le fournisseur a répondu, mais l'adresse n'a pas changé.</summary>
    AucunChangement = 1,

    /// <summary>Le fournisseur a effectivement enregistré une nouvelle adresse.</summary>
    MiseAJourEffectuee = 2,

    /// <summary>Adresse de mise à jour refusée : jeton invalide, URL erronée.</summary>
    AdresseRefusee = 3,

    /// <summary>La requête n'a pas abouti : réseau, délai dépassé, erreur du serveur.</summary>
    Echec = 4,
}

public static class EtatSynchronisationExtensions
{
    /// <summary>Libellé court, tel qu'il apparaît dans le journal et dans l'interface.</summary>
    public static string Libelle(this EtatSynchronisation etat) => etat switch
    {
        EtatSynchronisation.MiseAJourEffectuee => "Mise à jour effectuée",
        EtatSynchronisation.AucunChangement => "Adresse déjà à jour",
        EtatSynchronisation.AdresseRefusee => "Adresse de mise à jour refusée",
        EtatSynchronisation.Echec => "Échec de la requête",
        _ => "Réponse non reconnue",
    };

    /// <summary>Vrai pour les états qui méritent l'attention de l'utilisateur.</summary>
    public static bool EstUneAnomalie(this EtatSynchronisation etat) =>
        etat is EtatSynchronisation.AdresseRefusee or EtatSynchronisation.Echec or EtatSynchronisation.Inconnu;
}
