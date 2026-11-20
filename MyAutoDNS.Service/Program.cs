using MyAutoDNS.Core;

namespace MyAutoDNS.Service;

/// <summary>
/// Agent de fond de MyAutoDNS.
///
/// Il n'est pas installé comme service Windows : il est lancé au démarrage de la
/// machine par une tâche planifiée qui s'exécute sous le compte SYSTEM. Il tourne
/// en permanence, envoie la requête de mise à jour à intervalle régulier, puis
/// attend.
///
/// Points d'attention :
///  - la configuration est relue dès que l'interface modifie settings.json, et
///    l'échéance est recalculée : passer de 6 heures à 10 minutes prend effet
///    immédiatement, sans redémarrer l'agent ;
///  - la date de la dernière requête est mémorisée, si bien qu'un redémarrage de
///    la tâche planifiée n'entraîne pas une requête supplémentaire ;
///  - un fichier « mise-a-jour-maintenant.flag » déposé par l'interface déclenche
///    une requête sans attendre l'échéance.
/// </summary>
internal static class Program
{
    /// <summary>
    /// Fréquence à laquelle l'agent vérifie l'heure, la configuration, la demande
    /// immédiate et la demande d'arrêt. 5 secondes : assez réactif pour que
    /// « Quitter » et « Mettre à jour maintenant » répondent sans délai perceptible,
    /// et assez rare pour ne pas solliciter le disque inutilement.
    /// </summary>
    private static readonly TimeSpan TrancheAttente = TimeSpan.FromSeconds(5);

    /// <summary>Nouvelle tentative après une requête en échec.</summary>
    private static readonly TimeSpan DelaiApresEchec = TimeSpan.FromMinutes(5);

    /// <summary>Délai avant de réessayer quand la configuration est inutilisable.</summary>
    private static readonly TimeSpan DelaiConfigurationInvalide = TimeSpan.FromMinutes(1);

    private static async Task<int> Main()
    {
        using IDisposable? verrou = InstanceUnique.Acquerir($"{Chemins.Produit}.Service", out bool dejaLance);

        if (dejaLance)
        {
            Console.WriteLine("MyAutoDNS Service : un autre exemplaire est déjà en cours d'exécution.");
            return 0;
        }

        Chemins.PreparerDossiers();

        // Chaque démarrage repart sur un journal propre : on ne relit que la
        // session en cours, sans mélange avec les précédentes.
        Journal.Reinitialiser();

        using CancellationTokenSource arret = new();

        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true; // arrêt propre plutôt que terminaison brutale
            arret.Cancel();
        };

        Configuration configuration = Configuration.ChargerSansEchec(out string? probleme);
        DateTime? signatureConfiguration = Configuration.DateDeModification();

        if (probleme is not null)
        {
            Journal.Ecrire($"Configuration illisible au démarrage : {probleme}");
        }

        Journal.Ecrire("Service démarré.");
        Console.WriteLine("MyAutoDNS Service démarré. Ctrl+C pour arrêter.");

        EtatAgent etat = EtatAgent.Charger();
        DateTimeOffset echeance = CalculerEcheance(configuration, etat);
        string? dernierAvertissement = null;

        // Le journal vient d'être recréé : sans ces lignes, un simple redémarrage
        // effacerait toute trace de la synchronisation précédente, et l'utilisateur
        // n'aurait plus aucun moyen de savoir où en était la mise à jour.
        TracerEtatEtEcheance(etat, echeance, rappelerDerniereRequete: true);

        // Date d'écriture de la dernière demande immédiate traitée. Elle empêche
        // de rejouer la même demande si le fichier n'a pas pu être supprimé.
        DateTime? demandeTraitee = null;

        try
        {
            while (!arret.IsCancellationRequested)
            {
                // ---- 0. Arrêt demandé par l'interface ou par l'icône ? ----
                // On sort avec le code 0 : Windows ne voit pas d'échec et ne relance
                // donc pas la tâche planifiée.
                if (ArretAgent.DemandeEnAttente())
                {
                    ArretAgent.Reinitialiser();
                    Journal.Ecrire("Arrêt demandé, sortie propre.");
                    break;
                }

                // ---- 1. L'interface a-t-elle enregistré de nouveaux réglages ? ----
                DateTime? signatureCourante = Configuration.DateDeModification();

                if (signatureCourante != signatureConfiguration)
                {
                    signatureConfiguration = signatureCourante;

                    configuration.Adopter(Configuration.ChargerSansEchec(out string? souci));

                    if (souci is null)
                    {
                        Journal.Ecrire("Configuration rechargée depuis l'interface.");
                    }
                    else
                    {
                        Journal.Ecrire($"Configuration rechargée mais illisible : {souci}");
                    }

                    echeance = CalculerEcheance(configuration, etat);
                    dernierAvertissement = null;

                    // La configuration a changé : l'échéance aussi.
                    TracerEtatEtEcheance(etat, echeance, rappelerDerniereRequete: false);
                }

                // ---- 2. Demande de mise à jour immédiate ? ----
                DateTime? demande = LireDemandeImmediate();

                if (demande is not null && demande != demandeTraitee)
                {
                    demandeTraitee = demande;
                    SupprimerDemandeImmediate();
                    echeance = DateTimeOffset.UtcNow;
                }

                // ---- 3. Échéance atteinte : on envoie la requête ----
                if (DateTimeOffset.UtcNow >= echeance)
                {
                    (echeance, dernierAvertissement) =
                        await ExecuterAsync(configuration, etat, dernierAvertissement, arret.Token);
                }

                // ---- 4. Attente par tranches ----
                TimeSpan restant = echeance - DateTimeOffset.UtcNow;
                TimeSpan attente = restant < TrancheAttente ? restant : TrancheAttente;

                if (attente < TimeSpan.FromSeconds(1))
                {
                    attente = TimeSpan.FromSeconds(1);
                }

                await Task.Delay(attente, arret.Token);
            }
        }
        catch (OperationCanceledException)
        {
            // Arrêt demandé : sortie normale.
        }

        Journal.Ecrire("Service arrêté.");
        Console.WriteLine("MyAutoDNS Service arrêté.");
        return 0;
    }

    /// <summary>
    /// Journalise la prochaine échéance, et éventuellement la dernière
    /// synchronisation connue.
    ///
    /// Le journal étant recréé à chaque démarrage, ces lignes sont la seule
    /// mémoire de ce qui s'est passé avant : sans elles, un redémarrage du
    /// service effacerait le résultat de la dernière requête, notamment le cas
    /// « adresse déjà à jour » où rien ne change et où rien ne se remarque.
    /// </summary>
    private static void TracerEtatEtEcheance(
        EtatAgent etat,
        DateTimeOffset echeance,
        bool rappelerDerniereRequete)
    {
        if (rappelerDerniereRequete)
        {
            if (etat.DerniereRequeteUtc is null)
            {
                Journal.Ecrire("Aucune requête enregistrée pour l'instant.");
            }
            else
            {
                DateTime quand = etat.DerniereRequeteUtc.Value.LocalDateTime;

                string detail = string.IsNullOrWhiteSpace(etat.DerniereReponse)
                    ? string.Empty
                    : $" : {etat.DerniereReponse}";

                Journal.Ecrire(
                    $"Dernière requête le {quand:dd/MM/yyyy} à {quand:HH:mm:ss} — "
                    + $"{etat.DernierEtat.Libelle()}{detail}");
            }
        }

        DateTime prochaine = echeance.LocalDateTime;
        Journal.Ecrire($"Prochaine mise à jour prévue le {prochaine:dd/MM/yyyy} à {prochaine:HH:mm:ss}.");
    }

    /// <summary>
    /// Envoie une requête et renvoie la nouvelle échéance.
    /// </summary>
    private static async Task<(DateTimeOffset Echeance, string? Avertissement)> ExecuterAsync(
        Configuration configuration,
        EtatAgent etat,
        string? avertissementPrecedent,
        CancellationToken jeton)
    {
        string? invalide = configuration.Valider();

        if (invalide is not null)
        {
            // On ne journalise qu'au premier passage : sinon le journal recevrait une
            // ligne par minute tant que l'interface n'a rien enregistré de valable.
            if (!string.Equals(avertissementPrecedent, invalide, StringComparison.Ordinal))
            {
                Journal.Ecrire($"Aucune requête envoyée : {invalide}");
            }

            return (DateTimeOffset.UtcNow + DelaiConfigurationInvalide, invalide);
        }

        ResultatRequete resultat = await MiseAJourDdns.ExecuterAsync(configuration, jeton);

        Journal.Ecrire(resultat.Message);
        etat.EnregistrerRequete(resultat);

        // Courriel uniquement lorsqu'une nouvelle adresse a réellement été
        // enregistrée chez le fournisseur. Le cas « adresse déjà à jour » — de
        // loin le plus fréquent — n'envoie rien : sinon la boîte recevrait un
        // message à chaque cycle, pour ne rien dire.
        if (resultat.Etat == EtatSynchronisation.MiseAJourEffectuee)
        {
            await NotifierMiseAJourAsync(resultat, jeton);
        }

        if (!resultat.Reussie)
        {
            // Une coupure réseau ne doit pas faire attendre une heure entière.
            return (DateTimeOffset.UtcNow + DelaiApresEchec, null);
        }

        Journal.PurgerAnciens(configuration.JoursDeRetention);
        return (DateTimeOffset.UtcNow + configuration.Intervalle, null);
    }

    /// <summary>
    /// Prévient par courriel qu'une nouvelle adresse vient d'être enregistrée.
    /// Un échec d'envoi est journalisé mais ne remet jamais en cause la
    /// synchronisation, qui a déjà réussi.
    /// </summary>
    private static async Task NotifierMiseAJourAsync(ResultatRequete resultat, CancellationToken jeton)
    {
        ConfigurationEmail configuration = ConfigurationEmail.Charger();

        if (!configuration.Actif)
        {
            return;
        }

        ResultatNotification notification = await NotificationEmail.EnvoyerAsync(
            configuration,
            NotificationEmail.ConstruireSujet(),
            NotificationEmail.ConstruireCorps(resultat.Reponse, Chemins.JournalDuJour(), DateTime.Now),
            jeton);

        Journal.Ecrire(notification.Message);
    }

    /// <summary>
    /// Prochaine date d'envoi : dernière tentative plus l'intervalle configuré.
    /// Au tout premier démarrage, la requête part immédiatement, comme le faisait
    /// le script d'origine.
    /// </summary>
    private static DateTimeOffset CalculerEcheance(Configuration configuration, EtatAgent etat)
    {
        if (etat.DerniereRequeteUtc is null)
        {
            return DateTimeOffset.UtcNow;
        }

        int minutes = Math.Max(configuration.IntervalleMinutes, Configuration.IntervalleMinimumMinutes);
        DateTimeOffset echeance = etat.DerniereRequeteUtc.Value + TimeSpan.FromMinutes(minutes);

        return echeance < DateTimeOffset.UtcNow ? DateTimeOffset.UtcNow : echeance;
    }

    /// <summary>Date d'écriture du fichier de demande immédiate, ou null s'il n'existe pas.</summary>
    private static DateTime? LireDemandeImmediate()
    {
        try
        {
            return File.Exists(Chemins.FichierDemandeImmediate)
                ? File.GetLastWriteTimeUtc(Chemins.FichierDemandeImmediate)
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static void SupprimerDemandeImmediate()
    {
        try
        {
            File.Delete(Chemins.FichierDemandeImmediate);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Journal.Ecrire($"Demande immédiate traitée mais fichier non supprimable : {ex.Message}");
        }
    }
}
