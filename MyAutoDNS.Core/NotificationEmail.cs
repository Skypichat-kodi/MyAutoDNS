using System.Net.Sockets;
using System.Text;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace MyAutoDNS.Core;

/// <summary>Résultat d'une tentative d'envoi, prêt à être journalisé.</summary>
public sealed record ResultatNotification(bool Envoye, string Message);

/// <summary>
/// Envoi du courriel d'alerte, une fois la nouvelle adresse enregistrée chez le
/// fournisseur DDNS.
///
/// S'appuie sur MailKit, comme l'onglet Email de MCEMonitor. System.Net.Mail a été
/// abandonné pour deux raisons constatées sur ce poste :
///  - il n'envoie l'authentification que si le serveur l'annonce dans sa bannière
///    EHLO, d'où le refus « Authentification requise » (OFR_402) d'Orange ;
///  - il ne sait pas négocier le SSL implicite du port 465.
///
/// MailKit expose en outre la connexion, l'authentification et l'envoi séparément,
/// ce qui permet à la fenêtre de test d'afficher le déroulement étape par étape.
/// </summary>
public static class NotificationEmail
{
    /// <summary>Délai accordé au serveur SMTP, en millisecondes.</summary>
    private const int DelaiEnvoi = 30_000;

    /// <summary>
    /// Envoie le courriel. Ne lève jamais : toute erreur est renvoyée sous forme
    /// de résultat, pour être journalisée sans interrompre la synchronisation.
    /// </summary>
    /// <param name="progression">
    /// Rapport d'avancement facultatif, utilisé par la fenêtre de test pour
    /// afficher chaque étape. Le service n'en passe pas.
    /// </param>
    public static async Task<ResultatNotification> EnvoyerAsync(
        ConfigurationEmail configuration,
        string sujet,
        string corps,
        CancellationToken jeton = default,
        IProgress<string>? progression = null)
    {
        string? invalide = configuration.Valider();

        if (invalide is not null)
        {
            return new ResultatNotification(false, $"Notification non envoyée : {invalide}");
        }

        try
        {
            MimeMessage message = new();

            message.From.Add(new MailboxAddress(
                $"MyAutoDNS — {Environment.MachineName}",
                configuration.Expediteur));

            message.To.Add(MailboxAddress.Parse(configuration.Destinataire));
            message.Subject = sujet;
            message.Body = new TextPart("plain") { Text = corps };

            using SmtpClient client = new()
            {
                Timeout = DelaiEnvoi,
            };

            progression?.Report(
                $"Connexion à {configuration.Serveur}:{configuration.Port} — {configuration.Securite.Libelle()}…");

            await client.ConnectAsync(
                configuration.Serveur,
                configuration.Port,
                Convertir(configuration.Securite),
                jeton);

            progression?.Report($"Connexion établie ({client.SslProtocol}).");

            progression?.Report($"Authentification de {configuration.Expediteur}…");
            await client.AuthenticateAsync(configuration.Expediteur, configuration.MotDePasse, jeton);
            progression?.Report("Authentification acceptée.");

            progression?.Report("Envoi du message…");
            await client.SendAsync(message, jeton);
            progression?.Report("Message remis au serveur.");

            await client.DisconnectAsync(true, jeton);
            progression?.Report("Déconnexion.");

            return new ResultatNotification(true, $"Notification envoyée à {configuration.Destinataire}.");
        }
        catch (AuthenticationException ex)
        {
            return new ResultatNotification(
                false,
                "Authentification refusée par le serveur. Vérifie l'adresse d'expédition "
                + $"et le mot de passe : {DecrireException(ex)}");
        }
        catch (SslHandshakeException ex)
        {
            return new ResultatNotification(
                false,
                "La négociation chiffrée a échoué. Essaie un autre mode de sécurité "
                + $"(SSL pour 465, STARTTLS pour 587) : {DecrireException(ex)}");
        }
        catch (SmtpCommandException ex)
        {
            return new ResultatNotification(
                false,
                $"Le serveur a refusé la commande ({ex.StatusCode}) : {DecrireException(ex)}");
        }
        catch (Exception ex)
        {
            return new ResultatNotification(false, $"Envoi de la notification impossible : {DecrireException(ex)}");
        }
    }

    private static SecureSocketOptions Convertir(ModeSecurite mode) => mode switch
    {
        ModeSecurite.Ssl => SecureSocketOptions.SslOnConnect,
        ModeSecurite.Aucun => SecureSocketOptions.None,
        _ => SecureSocketOptions.StartTls,
    };

    /// <summary>
    /// Vérifie que le port du serveur SMTP répond, sans rien envoyer.
    ///
    /// C'est l'étape la plus utile au diagnostic : elle distingue « serveur
    /// injoignable ou port filtré » de « serveur joignable mais identifiants
    /// refusés », deux causes qui n'ont pas le même remède.
    /// </summary>
    /// <exception cref="TimeoutException">Aucune réponse dans le délai imparti.</exception>
    /// <exception cref="SocketException">Connexion refusée, ou hôte inconnu.</exception>
    public static async Task TesterPortAsync(
        string hote,
        int port,
        TimeSpan delai,
        CancellationToken jeton = default)
    {
        using TcpClient client = new();
        using CancellationTokenSource limite = CancellationTokenSource.CreateLinkedTokenSource(jeton);

        limite.CancelAfter(delai);

        try
        {
            await client.ConnectAsync(hote, port, limite.Token);
        }
        catch (OperationCanceledException) when (!jeton.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"aucune réponse en {delai.TotalSeconds:F0} s. Le port {port} est filtré, "
                + "ou le nom du serveur est incorrect.");
        }
    }

    /// <summary>Sujet du courriel, avec le nom de la machine d'origine.</summary>
    public static string ConstruireSujet() =>
        $"MyAutoDNS — adresse mise à jour sur {Environment.MachineName}";

    /// <summary>Corps du courriel : la réponse du fournisseur, brute, et le contexte.</summary>
    public static string ConstruireCorps(string reponse, string cheminJournal, DateTime quand)
    {
        StringBuilder corps = new();

        corps.AppendLine($"Le nom de domaine a été mis à jour le {quand:dd/MM/yyyy} à {quand:HH:mm:ss}.");
        corps.AppendLine();
        corps.AppendLine($"Machine  : {Environment.MachineName}");
        corps.AppendLine($"Réponse  : {reponse}");

        if (!string.IsNullOrWhiteSpace(cheminJournal))
        {
            corps.AppendLine($"Journal  : {cheminJournal}");
        }

        corps.AppendLine();
        corps.AppendLine("Message envoyé automatiquement par MyAutoDNS.");

        return corps.ToString();
    }

    /// <summary>
    /// Décrit une exception avec ses causes imbriquées : un échec SMTP cache
    /// presque toujours la vraie raison dans l'exception interne (nom d'hôte
    /// introuvable, certificat refusé, connexion rejetée).
    /// </summary>
    public static string DecrireException(Exception exception)
    {
        StringBuilder texte = new(exception.Message);

        for (Exception? interne = exception.InnerException; interne is not null; interne = interne.InnerException)
        {
            texte.Append(" → ").Append(interne.Message);
        }

        return texte.ToString();
    }
}
