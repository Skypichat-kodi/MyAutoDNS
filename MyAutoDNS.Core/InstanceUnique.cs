namespace MyAutoDNS.Core;

/// <summary>
/// Empêche deux exemplaires d'un même programme de tourner en parallèle.
///
/// Le verrou est créé dans l'espace de noms « Global » et non « Local » : le
/// Service tourne en session 0 sous le compte SYSTEM, alors que le Tray tourne
/// dans la session de l'utilisateur. Un verrou local ne les verrait pas l'un
/// l'autre.
/// </summary>
public static class InstanceUnique
{
    /// <summary>
    /// Tente de prendre le verrou.
    /// </summary>
    /// <param name="nom">Nom du verrou, par exemple « MyAutoDNS.Service ».</param>
    /// <param name="dejaLance">Vrai si un autre exemplaire détient déjà le verrou.</param>
    /// <returns>
    /// Un objet à libérer en fin de programme, ou null si un autre exemplaire
    /// est déjà en cours d'exécution.
    /// </returns>
    public static IDisposable? Acquerir(string nom, out bool dejaLance)
    {
        Mutex mutex = new(initiallyOwned: false, name: $@"Global\{nom}");

        bool possede;

        try
        {
            possede = mutex.WaitOne(0);
        }
        catch (AbandonedMutexException)
        {
            // L'exemplaire précédent s'est arrêté sans libérer le verrou
            // (plantage, arrêt forcé). Windows nous en donne la propriété.
            possede = true;
        }

        if (!possede)
        {
            mutex.Dispose();
            dejaLance = true;
            return null;
        }

        dejaLance = false;
        return new Verrou(mutex);
    }

    private sealed class Verrou : IDisposable
    {
        private readonly Mutex _mutex;

        public Verrou(Mutex mutex) => _mutex = mutex;

        public void Dispose()
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // Le verrou n'était plus détenu : rien à faire.
            }

            _mutex.Dispose();
        }
    }
}
