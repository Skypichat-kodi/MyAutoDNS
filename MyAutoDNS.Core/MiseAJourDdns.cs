using System.Diagnostics;
using System.Net.Http;

namespace MyAutoDNS.Core;

/// <summary>Résultat d'une tentative de mise à jour, prêt à être journalisé.</summary>
public sealed record ResultatRequete(bool Reussie, string Message);

/// <summary>
/// Envoi de la requête de mise à jour au fournisseur de nom de domaine.
///
/// Reprend exactement le comportement du script d'origine :
///     curl.exe -k -s http://sync.afraid.org/u/xxxxx/
/// c'est-à-dire une requête GET, sans vérification stricte du certificat, dont
/// la réponse est recopiée dans le journal.
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

            string message = reponse.IsSuccessStatusCode
                ? $"HTTP {(int)reponse.StatusCode} en {chronometre.ElapsedMilliseconds} ms : {corps}"
                : $"HTTP {(int)reponse.StatusCode} {reponse.ReasonPhrase} en {chronometre.ElapsedMilliseconds} ms : {corps}";

            return new ResultatRequete(reponse.IsSuccessStatusCode, message);
        }
        catch (TaskCanceledException) when (!jeton.IsCancellationRequested)
        {
            // HttpClient signale ainsi le dépassement de son propre délai.
            return new ResultatRequete(
                false,
                $"ÉCHEC : aucune réponse après {configuration.DelaiMaximalSecondes} s");
        }
        catch (OperationCanceledException)
        {
            return new ResultatRequete(false, "ÉCHEC : requête interrompue (arrêt du service)");
        }
        catch (HttpRequestException ex)
        {
            return new ResultatRequete(false, $"ÉCHEC : {ex.Message}");
        }
        catch (Exception ex)
        {
            return new ResultatRequete(false, $"ÉCHEC : {ex.GetType().Name} : {ex.Message}");
        }
    }
}
