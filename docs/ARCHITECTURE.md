# Architecture

## Vue d’ensemble

```text
WinUI 3 / MainViewModel
          |
          v
SoundKeeperEngineService -- CLI --> SoundKeeper64.exe / SoundKeeperARM64.exe
          |
          +--> commande officielle `kill` pour l’arrêt
```

Le moteur upstream reste un exécutable autonome. Aucun code WASAPI n’est dupliqué en C# et aucune API interne C++ n’est exposée.

## Composants du GUI

- `Models/AppSettings.cs` contient la configuration utilisateur et les choix correspondant au contrat CLI.
- `Services/CliArgumentBuilder.cs` est l’unique traducteur des choix GUI vers la syntaxe Sound Keeper.
- `Services/EngineExecutableResolver.cs` choisit le binaire x64 ou ARM64 et cherche d’abord le dossier `Engine` livré avec le GUI.
- `Services/AudioOutputService.cs` liste les sorties Windows actives (API MMDevice : identifiant d’endpoint et nom convivial). `Services/OutputDeviceCatalog.cs` les fusionne avec la sélection sauvegardée (identifiant + dernier nom connu) : une sortie absente reste affichée comme indisponible et redevient ciblée à son retour. La liste est relue par le minuteur de la fenêtre quand la page Général affiche la sélection.
- `Services/SoundKeeperEngineService.cs` crée des `ProcessStartInfo` sans console, expose l’état/PID/heure de démarrage, démarre le moteur et l’arrête via `kill`.
- `Services/RestartAttemptGuard.cs` limite le redémarrage automatique à trois tentatives par minute.
- `Services/SettingsService.cs` charge et sauvegarde le JSON local par remplacement atomique. Un JSON invalide revient aux valeurs par défaut et produit un avertissement dans le journal ; une valeur d’enum hors plage revient à la valeur par défaut de ce réglage.
- `Services/StartupService.cs` gère uniquement la clé utilisateur `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`. Le réglage reste la source de vérité et l’entrée vise la publication stable, marquée par `SoundKeeper.GUI.published` (cible `MarkPublishedBuild`) : celle-ci reprend ou répare l’entrée à chaque lancement, une build de développement ne remplace jamais une entrée valide. Cette classe est le point à remplacer par une StartupTask lors d’un futur packaging MSIX.
- `Services/TrayIconService.cs` utilise `NotifyIcon` et `ContextMenuStrip` fournis par le framework Windows Forms, sans package externe ; ses textes viennent des mêmes `.resw` que la fenêtre. Son icône est `Assets/soundkeeper-tray.ico`, chargée à la taille d’icône réduite du système (16/20/24/32 selon le DPI) ; l’exécutable embarque `Assets/soundkeeper.ico` (`ApplicationIcon`), que la fenêtre reprend via `AppWindow.SetTaskbarIcon` et `SetTitleBarIcon` (frame réduite chargée à la taille du DPI de la fenêtre), et À propos affiche `Assets/soundkeeper-app.svg`. `Main.ico` à la racine reste l’icône du moteur upstream.
- `MainWindow.xaml` applique le Golabox Utility System v1.0 (Shell B) : barre de titre native, `NavigationView` natif (`PaneDisplayMode=Auto`) avec quatre destinations Général, Avancé, Journaux, À propos. Les pages restent dans une fenêtre unique afin d’éviter une infrastructure de navigation superflue.
- `Themes/Tokens.xaml` et `Themes/Styles.xaml` portent les tokens et styles Golabox, identiques à QuickOutput (plus quelques ajouts Shell B délimités). Aucune couleur en dur : uniquement des ThemeResources Windows.
- `Services/LogViewFormatter.cs` transforme l’affichage du journal (heure `HH:mm:ss`, date en séparateur, filtre de niveau) sans modifier le fichier ; « Copier » conserve les lignes d’origine.
- `Services/AppLogger.cs` écrit le journal au mieux : une erreur d’écriture (fichier verrouillé par un autre processus) est ignorée pour ne jamais bloquer le démarrage, le moteur ou la fermeture.
- `ViewModels/MainViewModel.cs` porte l’état d’écran et les actions. Il redémarre le moteur après une modification pertinente avec un court debounce et fournit au viewer la fin du journal courant.
- `App.xaml.cs` utilise `AppInstance` du Windows App SDK pour assurer le single-instance et réactiver la fenêtre existante. Il décide une seule fois d’un démarrage masqué (`--background` au démarrage de Windows ou « Démarrer minimisé ») : la fenêtre n’est alors pas activée avant d’aller dans la zone de notification. Un redémarrage depuis la fenêtre passe `--show` pour rester visible une fois.
- `SoundKeeper.GUI.csproj` complète la publication (`CopyWinUiResourcesToPublish`) : un `.xbf` par fichier XAML à son chemin relatif, le PRI, `Engine` et `Strings`. `build-release.ps1` est le build Release officiel : moteur MSBuild, puis GUI, tests et publication `dotnet`.

## Contrat moteur observé

- Périphériques : `Primary` (défaut), `All`, `Digital`, `Analog`, `Marked`, et `Selected` ajouté par ce fork : `selected` suivi de `device=<identifiant d’endpoint>` répétable ; sans identifiant, aucune sortie n’est maintenue. Le GUI propose `Primary`, `All` et la sélection ; les trois autres modes restent affichés seulement pour une configuration qui les utilise.
- Signaux : `Fluctuate` (défaut, F=50 Hz), `Zero`, `OpenOnly`, `Sine` (F=1 Hz, A=1 %, T=0,1 s), `White`, `Brown`, `Pink`.
- Paramètres : `F` fréquence (max moteur 96 000 Hz), `A` amplitude en pourcentage (max 100 %), `L` durée, `W` attente si `L` est défini, `T` fondu.
- Veille : sans mot-clé, pause du signal pendant la mise en veille système (et, sous Windows 7-10, juste avant la veille automatique ; indisponible sous Windows 11) ; `SleepL` ajoute le verrouillage, `SleepD` écran éteint, `Sleepy` les deux, `NoSleep` désactive la détection de veille, `Remote` autorise la sortie audio distante.
- Une nouvelle instance moteur arrête l’ancienne via les objets nommés `SoundKeeperStopEvent` et `SoundKeeperMutex`. `soundkeeper kill` constitue l’arrêt officiel.

## Choix proportionnés

- Un seul projet applicatif et un petit projet de tests, sans conteneur d’injection ni framework MVVM/logging.
- Application non packagée pour la première version personnelle ; la séparation du démarrage Windows et l’usage du Windows App SDK gardent une migration MSIX possible.
- Ressources WinUI `.resw` françaises, anglaises et espagnoles, contrôles natifs et couleurs de thème ; une section = un titre + des lignes, seule la ligne est une carte. Le choix manuel est persistant et appliqué au lancement suivant.
- Le viewer Journaux lit au maximum les 250 000 derniers caractères et se rafraîchit toutes les deux secondes uniquement lorsque sa page est visible.
- Le moteur n’expose pas au GUI de signal fiable pour chaque impulsion : l’interface n’affiche donc que l’état du processus et la configuration demandée.
- Le champ d’arguments supplémentaires est ajouté tel quel après les arguments sûrs générés par le GUI afin de préserver son quoting pour de futures options upstream.
