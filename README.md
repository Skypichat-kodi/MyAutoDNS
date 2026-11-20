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

## Journal

`C:\ProgramData\MyAutoDNS\logs\myautodns_AAAA-MM-JJ.log`, un fichier par jour,
une ligne par evenement, au format du script d'origine :

```
2026-11-20 14:32:11  HTTP 200 en 342 ms : Updated 1 host xxx
2026-11-20 15:32:12  ECHEC : aucune reponse apres 30 s
```

## Compilation

```bat
build.bat            REM publication des trois programmes + installeur Inno
build.bat --auto     REM sans pause ni ouverture de dossier
```

Resultat : `Installers\MyAutoDNS-<version>.exe`.

Prerequis : SDK .NET 8 (`global.json` fige la version 8.0.428) et Inno Setup 6.
Les executables sont publies en `--self-contained false` : le .NET 8 Desktop
Runtime doit etre present sur le poste cible.

## Avancement

- [x] `MyAutoDNS.Core` : chemins, configuration, etat, journal, requete DDNS
- [x] `MyAutoDNS.Service` : boucle de l'agent, relecture de configuration
- [ ] `MyAutoDNS.Tray` : icone de notification
- [ ] `MyAutoDNS.UI` : fenetre de configuration WPF
- [ ] Creation des taches planifiees (XML)
- [ ] `inno\MyAutoDNS.iss` et `build.bat`
