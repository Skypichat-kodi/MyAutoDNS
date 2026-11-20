# MyAutoDNS

Mise a jour automatique d'un nom de domaine dynamique (DDNS) a intervalle regulier.

Remplace le script d'origine :

```bat
:loop
curl.exe -k -s http://sync.afraid.org/u/xxxxx/ >> "%TEMP%\freedns_sync.log" 2>&1
timeout /t 3600 /nobreak
goto loop
```

par une application installee, configurable par interface graphique et dotee
d'une icone dans la zone de notification.

> Etat du projet : voir la section **Avancement** en fin de document.

## Principe

Trois programmes, deux emplacements, comme les autres logiciels de la machine.

```
C:\Program Files\MyAutoDNS\        interface de configuration + icone de notification
C:\ProgramData\MyAutoDNS\          agent de fond, settings.json, journal
```

| Programme | Role | Emplacement |
|---|---|---|
| `MyAutoDNS.Service.exe` | envoie la requete DDNS a intervalle regulier | ProgramData |
| `MyAutoDNS.UI.exe` | fenetre de configuration (WPF, elevee) | Program Files |
| `MyAutoDNS.Tray.exe` | icone de notification (WinForms) | Program Files |

Deux taches planifiees, creees par l'installeur :

| Tache | Declencheur | Compte | Remarque |
|---|---|---|---|
| `MyAutoDNS Service` | au demarrage de la machine | SYSTEM | duree **illimitee**, relance sur echec |
| `MyAutoDNS Tray` | a l'ouverture de session | utilisateur | pilotee par la case « Demarrer avec Windows » |

La duree illimitee est indispensable : `schtasks /Create` sans XML impose par
defaut une limite de 72 heures, apres laquelle Windows arrete la tache. Un agent
permanent mourrait donc tous les trois jours. Les taches sont pour cette raison
creees par import de XML (`ExecutionTimeLimit` = `PT0S`).

## Configuration

`C:\ProgramData\MyAutoDNS\settings.json`

| Cle | Sens | Defaut |
|---|---|---|
| `Url` | adresse de mise a jour fournie par le fournisseur DDNS | vide |
| `IntervalleMinutes` | frequence des requetes, en minutes | `60` |
| `DelaiMaximalSecondes` | abandon d'une requete au-dela de ce delai | `30` |
| `IgnorerErreurCertificat` | equivalent de `curl -k`, ignore les erreurs TLS | `true` |
| `JoursDeRetention` | purge des journaux au-dela de N jours, `0` = illimite | `30` |

Le fichier est reecrit par l'interface : il ne doit contenir aucun commentaire.

### Notifications par courriel

Un courriel peut partir lorsqu'une nouvelle adresse est **reellement enregistree**
chez le fournisseur. Le cas « adresse deja a jour », de loin le plus frequent,
n'envoie rien : sinon la boite recevrait un message a chaque cycle, pour ne rien dire.

Reglages dans `C:\ProgramData\MyAutoDNS\email.json`, saisis depuis le bouton
« Notifications... » de l'interface :

| Cle | Sens | Defaut |
|---|---|---|
| `Actif` | envoi des notifications | `false` |
| `Expediteur` | adresse d'expedition, sert aussi d'identifiant SMTP | vide |
| `Destinataire` | adresse qui recoit l'alerte | vide |
| `Serveur` | nom d'hote du serveur SMTP | vide |
| `Port` | port du serveur | `587` |
| `Securite` | `Ssl` (465), `StartTls` (587) ou `Aucun` (25) | `StartTls` |
| `MotDePasseChiffre` | mot de passe chiffre DPAPI, jamais en clair | vide |

Le mot de passe est chiffre avec DPAPI en portee **machine** : le service (SYSTEM)
et l'interface (administrateur) doivent tous deux pouvoir le relire, or ce sont
deux comptes distincts. Un fichier recopie sur un autre poste devient inutilisable.

L'envoi s'appuie sur **MailKit** (la meme bibliotheque que MCEMonitor).
`System.Net.Mail` a ete abandonne apres deux echecs constates : il n'envoie
l'authentification que si le serveur l'annonce dans sa banniere EHLO (refus
`OFR_402` d'Orange), et il ne sait pas negocier le SSL implicite du port 465.

## Journal

`C:\ProgramData\MyAutoDNS\logs\myautodns_AAAA-MM-JJ.log`, un fichier par jour,
**vide et recree a chaque demarrage de l'agent** : chaque session se lit seule, sans
se melanger aux precedentes. Encodage UTF-8 avec BOM, pour que `type` sous cmd.exe
et PowerShell 5.1 affichent les accents correctement.

```
2026-11-20 22:54:41  Service demarre.
2026-11-20 22:54:47  Adresse deja a jour - HTTP 200 en 939 ms : No IP change detected for skypichat.mooo.com with IP 90.62.247.246, skipping update
2026-11-20 23:54:52  Mise a jour effectuee - HTTP 200 en 412 ms : Updated 1 host skypichat.mooo.com to 90.62.247.247
```

### Etats reconnus

La reponse d'afraid.org est du texte libre. Elle est qualifiee avant d'etre
journalisee et affichee, dans l'interface comme dans l'info-bulle de l'icone :

| Reponse du fournisseur | Etat |
|---|---|
| contient `Updated` | Mise a jour effectuee |
| contient `No IP change`, `skipping`, `not changed` | Adresse deja a jour |
| HTTP 401/403, `Unauthorized`, `invalid`, `denied` | Adresse de mise a jour refusee |
| delai depasse, reseau, HTTP 5xx | Echec de la requete |
| tout autre texte | Reponse non reconnue |

L'etat et la reponse brute sont conserves dans `etat.json`, ce qui permet a
l'interface et a l'icone de les afficher sans relire tout le journal.

## Compilation

```bat
build.bat            REM publication des trois programmes + installeur Inno
build.bat --auto     REM sans pause ni ouverture de dossier
```

Resultat : `Installers\MyAutoDNS-<version>.exe`.

Au premier « Enregistrer et appliquer » dans l'interface, les deux taches
planifiees sont creees. L'installeur ne les cree pas lui-meme : seule
l'interface connait la configuration a inscrire dans le XML.

Prerequis : SDK .NET 8 (`global.json` fige la version 8.0.428) et Inno Setup 6.
Les executables sont publies en `--self-contained false` : le .NET 8 Desktop
Runtime doit etre present sur le poste cible.

## Avancement

- [x] `MyAutoDNS.Core` : chemins, configuration, etat, journal, requete DDNS
- [x] `MyAutoDNS.Service` : boucle de l'agent, relecture de configuration
- [x] Creation des taches planifiees (XML, duree illimitee, relance sur echec)
- [x] `MyAutoDNS.Tray` : icone de notification, clic gauche / clic droit
- [x] `MyAutoDNS.UI` : fenetre de configuration WPF (elevee)
- [x] `inno\MyAutoDNS.iss` et `build.bat`
- [x] Installation complete reelle et premiere synchronisation reussie
- [x] Journal remis a zero au demarrage, UTF-8 avec BOM
- [x] Qualification de la reponse du fournisseur DDNS
- [x] Arret propre valide : code de retour 0, aucune relance automatique
- [x] Verrou d'instance de l'interface valide visuellement
- [ ] Verification au prochain demarrage de Windows (reprise automatique)
- [x] Notifications par courriel sur mise a jour reelle (SMTP, mot de passe chiffre)
