using System.Net;
using System.Net.Mail;
using System.Net.Sockets;
using System.Text;

namespace MyAutoDNS.Core;

/// <summary>Résultat d'une tentative d'envoi, prêt à être journalisé.</summary>
public sealed record ResultatNotification(bool Envoye, string Message);

/// <summary>
/// Envoi du courriel d'alerte, une fois la nouvelle adresse enregistrée chez le
/// fournisseur DDNS.
///
/// Le principe reprend celui de MCEMonitor, avec System.Net.Mail plutôt que
/// MailKit. Conséquence à connaître : cette bibliothèque ne sait faire que
/// STARTTLS. Un serveur qui n'accepte que le SSL implicite (port 465) ne
/// fonctionnera pas ici ; il faut utiliser 587 ou 25.
/// </summary>
public static class NotificationEmail
{
    /// <summary>Délai accordé au serveur SMTP, en millisecondes.</summary>
    private const int DelaiEnvoi = 30_000;

    /// <summary>
    /// Envoie le courriel. Ne lève jamais : toute erreur est renvoyée sous forme
    /// de résultat, pour être journalisée sans interrompre la synchronisation.
    /// </summary>
    public static async Task<ResultatNotification> EnvoyerAsync(
        ConfigurationEmail configuration,
        string sujet,
        string corps,
        CancellationToken jeton = default)
    {
        string? invalide = configuration.Valider();

        if (invalide is not null)
        {
            return new ResultatNotification(false, $"Notification non envoyée : {invalide}");
        }

        try
        {
            using MailMessage message = new()
            {
                From = new MailAddress(
                    configuration.Expediteur,
                    $"MyAutoDNS — {Environment.MachineName}"),
                Subject = sujet,
                SubjectEncoding = Encoding.UTF8,
                Body = corps,
                BodyEncoding = Encoding.UTF8,
                IsBodyHtml = false,
            };

            message.To.Add(configuration.Destinataire);

            using SmtpClient client = new(configuration.Serveur, configuration.Port)
            {
                EnableSsl = configuration.ConnexionChiffree,
                DeliveryMethod = SmtpDeliveryMethod.Network,
                UseDefaultCredentials = false,
                Credentials = new NetworkCredential(configuration.Expediteur, configuration.MotDePasse),
                Timeout = DelaiEnvoi,
            };

            await client.SendMailAsync(message, jeton);

            return new ResultatNotification(true, $"Notification envoyée à {configuration.Destinataire}.");
        }
        catch (Exception ex)
        {
            return new ResultatNotification(false, $"Envoi de la notification impossible : {DecrireException(ex)}");
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
