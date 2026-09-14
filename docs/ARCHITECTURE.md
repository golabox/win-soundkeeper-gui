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
- `Services/SoundKeeperEngineService.cs` crée des `ProcessStartInfo` sans console, expose l’état/PID/heure de démarrage, démarre le moteur et l’arrête via `kill`.
- `Services/RestartAttemptGuard.cs` limite le redémarrage automatique à trois tentatives par minute.
- `Services/SettingsService.cs` charge et sauvegarde le JSON local par remplacement atomique. Un JSON invalide revient aux valeurs par défaut et produit un avertissement dans le journal.
- `Services/StartupService.cs` gère uniquement la clé utilisateur `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`. Cette classe est le point à remplacer par une StartupTask lors d’un futur packaging MSIX.
- `Services/TrayIconService.cs` utilise `NotifyIcon` et `ContextMenuStrip` fournis par le framework Windows Forms, sans package externe.
- `MainWindow.xaml` applique le Golabox Utility System v1.0 (Shell B) : barre de titre native, `NavigationView` natif (`PaneDisplayMode=Auto`) avec quatre destinations Général, Avancé, Journaux, À propos. Les pages restent dans une fenêtre unique afin d’éviter une infrastructure de navigation superflue.
- `Themes/Tokens.xaml` et `Themes/Styles.xaml` portent les tokens et styles Golabox, identiques à QuickOutput (plus quelques ajouts Shell B délimités). Aucune couleur en dur : uniquement des ThemeResources Windows.
- `Services/LogViewFormatter.cs` transforme l’affichage du journal (heure `HH:mm:ss`, date en séparateur, filtre de niveau) sans modifier le fichier ; « Copier » conserve les lignes d’origine.
- `ViewModels/MainViewModel.cs` porte l’état d’écran et les actions. Il redémarre le moteur après une modification pertinente avec un court debounce et fournit au viewer la fin du journal courant.
- `App.xaml.cs` utilise `AppInstance` du Windows App SDK pour assurer le single-instance et réactiver la fenêtre existante.

## Contrat moteur observé

- Périphériques : `Primary` (défaut), `All`, `Digital`, `Analog`, `Marked`.
- Signaux : `Fluctuate` (défaut, F=50 Hz), `Zero`, `OpenOnly`, `Sine` (F=1 Hz, A=1 %, T=0,1 s), `White`, `Brown`, `Pink`.
- Paramètres : `F` fréquence (max moteur 96 000 Hz), `A` amplitude en pourcentage (max 100 %), `L` durée, `W` attente si `L` est défini, `T` fondu.
- Veille : `SleepL` verrouillage, `SleepD` écran éteint, `Sleepy` les deux, `NoSleep` désactive la détection de veille, `Remote` autorise la sortie audio distante.
- Une nouvelle instance moteur arrête l’ancienne via les objets nommés `SoundKeeperStopEvent` et `SoundKeeperMutex`. `soundkeeper kill` constitue l’arrêt officiel.

## Choix proportionnés

- Un seul projet applicatif et un petit projet de tests, sans conteneur d’injection ni framework MVVM/logging.
- Application non packagée pour la première version personnelle ; la séparation du démarrage Windows et l’usage du Windows App SDK gardent une migration MSIX possible.
- Ressources WinUI `.resw` françaises, anglaises et espagnoles, contrôles natifs et couleurs de thème ; une section = un titre + des lignes, seule la ligne est une carte. Le choix manuel est persistant et appliqué au lancement suivant.
- Le viewer Journaux lit au maximum les 250 000 derniers caractères et se rafraîchit toutes les deux secondes uniquement lorsque sa page est visible.
- Le moteur n’expose pas au GUI de signal fiable pour chaque impulsion : l’interface n’affiche donc que l’état du processus et la configuration demandée.
- Le champ d’arguments supplémentaires est ajouté tel quel après les arguments sûrs générés par le GUI afin de préserver son quoting pour de futures options upstream.
