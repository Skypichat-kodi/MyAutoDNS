; ============================================================================
;  MyAutoDNS - script d'installation Inno Setup
;
;  Contient les trois programmes :
;    - MyAutoDNS.Service.exe  ->  C:\ProgramData\MyAutoDNS
;    - MyAutoDNS.Tray.exe     ->  C:\Program Files\MyAutoDNS
;    - MyAutoDNS.UI.exe       ->  C:\Program Files\MyAutoDNS
;
;  Compilation : ISCC.exe MyAutoDNS.iss
;  (ou build.bat a la racine du projet)
;
;  Le contenu provient de publish\Serveur, produit par build.bat.
; ============================================================================

#define NomApp  "MyAutoDNS"
#define Editeur "MyAutoDNS"

; ---------------------------------------------------------------------------
; Version lue directement dans l'executable produit par build.bat : une seule
; source de verite, celle des .csproj.
; ---------------------------------------------------------------------------
#define FichierReference "..\publish\Serveur\ProgramFiles\MyAutoDNS\MyAutoDNS.UI.exe"

#if FileExists(FichierReference)
  #define VersionFichier GetFileVersion(FichierReference)
  #define VersionProduit GetStringFileInfo(FichierReference, "ProductVersion")
  #define PositionPlus Pos("+", VersionProduit)
  #if PositionPlus > 0
    #define Version Copy(VersionProduit, 1, PositionPlus - 1)
  #else
    #define Version VersionProduit
  #endif
#else
  #error Executable introuvable. Lancez build.bat avant de compiler l'installeur.
#endif

[Setup]
; Identifiant stable : il permet aux futures versions de mettre a jour
; l'installation existante au lieu d'en creer une seconde.
AppId={{A7C4E1B2-5D63-4F8A-9B21-6E3D0C9F4A57}
AppName={#NomApp}
AppVersion={#Version}
AppVerName={#NomApp} {#Version}
VersionInfoVersion={#VersionFichier}
AppPublisher={#Editeur}
DefaultDirName={commonpf}\MyAutoDNS
DefaultGroupName=MyAutoDNS
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\Installers
OutputBaseFilename=MyAutoDNS-{#Version}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
UninstallDisplayName={#NomApp}
UninstallDisplayIcon={app}\MyAutoDNS.UI.exe
SetupIconFile=..\MyAutoDNS.UI\app.ico

[Languages]
Name: "french"; MessagesFile: "compiler:Languages\French.isl"

[Files]
; Program Files : l'interface et l'icone de notification.
Source: "..\publish\Serveur\ProgramFiles\*"; DestDir: "{commonpf}"; \
    Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs

; ProgramData : l'agent de fond et la bibliotheque commune.
Source: "..\publish\Serveur\ProgramData\*"; DestDir: "{commonappdata}"; \
    Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{commonprograms}\MyAutoDNS\MyAutoDNS"; Filename: "{app}\MyAutoDNS.UI.exe"; \
    WorkingDir: "{app}"; Comment: "Configurer la mise a jour automatique du nom de domaine"
Name: "{commonprograms}\MyAutoDNS\Desinstaller MyAutoDNS"; Filename: "{uninstallexe}"

[Run]
; runascurrentuser est indispensable : une entree marquee postinstall s'execute
; sinon avec le compte d'origine, donc sans les droits d'administration que
; reclame le manifeste de l'interface.
;
; C'est l'interface qui cree les taches planifiees, au premier « Enregistrer et
; appliquer » : elle seule connait les reglages que le XML doit contenir.
Filename: "{app}\MyAutoDNS.UI.exe"; Description: "Lancer MyAutoDNS"; \
    Flags: postinstall nowait skipifsilent runascurrentuser

[UninstallRun]
; Sans cela, les taches continueraient de viser des executables supprimes.
Filename: "{sys}\schtasks.exe"; Parameters: "/Delete /TN ""MyAutoDNS Service"" /F"; \
    Flags: runhidden; RunOnceId: "SupprimerTacheService"
Filename: "{sys}\schtasks.exe"; Parameters: "/Delete /TN ""MyAutoDNS Tray"" /F"; \
    Flags: runhidden; RunOnceId: "SupprimerTacheTray"

[UninstallDelete]
; Volontairement vide : settings.json et logs\ survivent a la desinstallation.
; Une reinstallation retrouve ainsi la configuration, et les journaux restent
; consultables. Le dossier se supprime a la main, en connaissance de cause.

[Code]
// Les trois programmes peuvent etre en cours d'execution. Sans cet arret
// prealable, leurs fichiers seraient verrouilles et la mise a jour echouerait.
procedure ArreterMyAutoDNS();
var
  Code: Integer;
begin
  Exec(ExpandConstant('{sys}\schtasks.exe'), '/End /TN "MyAutoDNS Service"', '', SW_HIDE, ewWaitUntilTerminated, Code);
  Exec(ExpandConstant('{sys}\schtasks.exe'), '/End /TN "MyAutoDNS Tray"', '', SW_HIDE, ewWaitUntilTerminated, Code);
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/IM MyAutoDNS.UI.exe /F', '', SW_HIDE, ewWaitUntilTerminated, Code);
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/IM MyAutoDNS.Tray.exe /F', '', SW_HIDE, ewWaitUntilTerminated, Code);
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/IM MyAutoDNS.Service.exe /F', '', SW_HIDE, ewWaitUntilTerminated, Code);
  Sleep(600);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssInstall then
    ArreterMyAutoDNS();
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
    ArreterMyAutoDNS();
end;
