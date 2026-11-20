using System.Diagnostics;
using System.Net.Http;

namespace MyAutoDNS.Core;

/// <summary>
/// Résultat d'une tentative de mise à jour, prêt à être journalisé et affiché.
/// </summary>
/// <param name="Reussie">Vrai si le fournisseur a répondu et accepté la requête (HTTP 2xx).</param>
/// <param name="Etat">Interprétation de la réponse.</param>
/// <param name="Reponse">Corps de la réponse, ramené sur une ligne et tronqué.</param>
/// <param name="Message">Ligne complète destinée au journal.</param>
public sealed record ResultatRequete(
    bool Reussie,
    EtatSynchronisation Etat,
    string Reponse,
    string Message);

/// <summary>
/// Envoi de la requête de mise à jour au fournisseur de nom de domaine.
///
/// Reprend exactement le comportement du script d'origine :
///     curl.exe -k -s http://sync.afraid.org/u/xxxxx/
/// c'est-à-dire une requête GET, sans vérification stricte du certificat.
/// </summary>
public static class MiseAJourDdns
{
    public static async Task<ResultatRequete> ExecuterAsync(
        Configuration configuration,
        CancellationToken jeton)
    {
        TimeSpan delaiMaximal = TimeSpan.FromSeconds(configuration.DelaiMaximalSecondes);

        using SocketsHttpHandler gestionnaire = new()
        {
            // curl sans -L ne suit pas les redirections. On les suit malgré tout :
            // une redirection ignorée donnerait un domaine jamais mis à jour,
            // sans que rien ne le signale dans le journal.
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 5,
            ConnectTimeout = delaiMaximal,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        };

        if (configuration.IgnorerErreurCertificat)
        {
            // Équivalent exact de l'option « -k » de curl.
            gestionnaire.SslOptions.RemoteCertificateValidationCallback = static (_, _, _, _) => true;
        }

        using HttpClient client = new(gestionnaire, disposeHandler: false)
        {
            Timeout = delaiMaximal,
        };

        client.DefaultRequestHeaders.UserAgent.ParseAdd("MyAutoDNS/1.0");

        Stopwatch chronometre = Stopwatch.StartNew();

        try
        {
            using HttpResponseMessage reponse = await client.GetAsync(configuration.Url.Trim(), jeton);

            string corps = Journal.Reduire(await reponse.Content.ReadAsStringAsync(jeton));
            chronometre.Stop();

            int code = (int)reponse.StatusCode;
            EtatSynchronisation etat = Interpreter(code, corps);

            string message = reponse.IsSuccessStatusCode
                ? $"{etat.Libelle()} — HTTP {code} en {chronometre.ElapsedMilliseconds} ms : {corps}"
                : $"ÉCHEC ({etat.Libelle()}) — HTTP {code} {reponse.ReasonPhrase} "
                  + $"en {chronometre.ElapsedMilliseconds} ms : {corps}";

            return new ResultatRequete(reponse.IsSuccessStatusCode, etat, corps, message);
        }
        catch (TaskCanceledException) when (!jeton.IsCancellationRequested)
        {
            // HttpClient signale ainsi le dépassement de son propre délai.
            const string message = "ÉCHEC : aucune réponse du fournisseur dans le délai imparti";
            return new ResultatRequete(false, EtatSynchronisation.Echec, string.Empty, message);
        }
        catch (OperationCanceledException)
        {
            return new ResultatRequete(
                false,
                EtatSynchronisation.Echec,
                string.Empty,
                "ÉCHEC : requête interrompue (arrêt du service)");
        }
        catch (HttpRequestException ex)
        {
            return new ResultatRequete(false, EtatSynchronisation.Echec, string.Empty, $"ÉCHEC : {ex.Message}");
        }
        catch (Exception ex)
        {
            return new ResultatRequete(
                false,
                EtatSynchronisation.Echec,
                string.Empty,
                $"ÉCHEC : {ex.GetType().Name} : {ex.Message}");
        }
    }

    /// <summary>
    /// Traduit la réponse texte du fournisseur en état compréhensible.
    ///
    /// L'ordre des tests compte : « No IP change detected ... skipping update » ne
    /// contient pas « Updated », mais mieux vaut vérifier l'absence de changement
    /// avant de chercher une mise à jour, pour ne jamais annoncer une mise à jour
    /// qui n'a pas eu lieu.
    /// </summary>
    public static EtatSynchronisation Interpreter(int codeHttp, string corps)
    {
        if (codeHttp is 401 or 403)
        {
            return EtatSynchronisation.AdresseRefusee;
        }

        if (codeHttp >= 500)
        {
            return EtatSynchronisation.Echec;
        }

        string texte = corps ?? string.Empty;

        if (ContientUnDes(texte, "No IP change", "not changed", "skipping", "no update"))
        {
            return EtatSynchronisation.AucunChangement;
        }

        if (ContientUnDes(texte, "Updated", "Update complete"))
        {
            return EtatSynchronisation.MiseAJourEffectuee;
        }

        if (ContientUnDes(texte, "ERROR", "Unauthorized", "invalid", "denied"))
        {
            return EtatSynchronisation.AdresseRefusee;
        }

        if (codeHttp is >= 400 and < 500)
        {
            return EtatSynchronisation.AdresseRefusee;
        }

        return EtatSynchronisation.Inconnu;
    }

    private static bool ContientUnDes(string texte, params string[] morceaux) =>
        morceaux.Any(morceau => texte.Contains(morceau, StringComparison.OrdinalIgnoreCase));
}
