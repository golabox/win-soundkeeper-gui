# Sound Keeper GUI

Sound Keeper GUI apporte une interface Windows 11 moderne au moteur audio open source [Sound Keeper](https://github.com/vrubleg/soundkeeper) d’Evgeny Vrublevsky. Le moteur C++/WASAPI reste responsable de l’audio, avec une seule extension : cibler des sorties précises (`selected`, `device=<id>`). Le GUI C# construit sa ligne de commande, le démarre et l’arrête proprement.

Cette version est un fork indépendant. Elle n’est ni publiée ni supportée officiellement par l’auteur original.

## Fonctionnalités

- état actif/arrêté avec PID, durée d’exécution, fréquence et commande Activer/Désactiver ;
- navigation latérale Windows 11 repliable avec pages Général, Avancé, Journaux et À propos ;
- sortie Windows par défaut, toutes les sorties ou sélection personnalisée des sorties Windows par leur nom (une sortie débranchée reste mémorisée) ; les modes numériques, analogiques et marqués restent proposés aux configurations qui les utilisent ;
- modes Fluctuate, Zero, OpenOnly, Sine, White, Brown et Pink ;
- réglages contextuels de fréquence, amplitude, durée, pause et fondu ;
- comportements SleepL, SleepD, Sleepy et NoSleep présentés en langage courant ;
- configuration JSON automatique et récupération sûre après un fichier invalide ;
- démarrage avec Windows, démarrage minimisé, réduction/fermeture vers le tray ;
- paramètres GUI séparés, surveillance du moteur et redémarrage automatique protégé contre les boucles ;
- application single-instance, thèmes système/clair/sombre et ressources françaises/anglaises/espagnoles ;
- arguments moteur supplémentaires et aperçu de la commande effective ;
- viewer de journaux intégré, journal local désactivable, rotation limitée à trois archives, diagnostic copiable et aucune télémétrie.

## Prérequis de développement

- Windows 11 x64 (ARM64 est préparé mais doit être validé sur matériel ARM64) ;
- Visual Studio 2026 avec les charges de travail C++ Desktop, développement .NET Desktop et Windows App SDK/WinUI ;
- SDK .NET 10 LTS ;
- accès NuGet pour restaurer `Microsoft.WindowsAppSDK` 2.4.0.

## Compiler et lancer

**Build Release officiel**, depuis la racine du dépôt (quitter d’abord l’application : zone de notification > Quitter) :

```powershell
.\build-release.ps1
```

Le script s’arrête à la première erreur et enchaîne :

1. le moteur C++ avec MSBuild (Visual Studio ou Build Tools 2026, charge C++ Desktop) → `Bin\SoundKeeper64.exe`, sans le pré-build upstream `BuildInfo.cmd` qui réécrirait la version du moteur ;
2. le GUI, les tests et la publication `win-x64` avec le SDK .NET ;
3. la vérification de la publication (`Engine`, `Strings`, PRI, XBF) ;
4. la création ou la mise à jour du raccourci local `Lancer Sound Keeper GUI.lnk`.

La sortie utilisateur est `SoundKeeper.GUI\publish\win-x64`. `bin`, `obj`, `publish` et le raccourci sont des artefacts locaux non versionnés.

Le moteur doit précéder le GUI, qui copie `Bin\SoundKeeper64.exe` dans `Engine` : la solution ne déclare pas cet ordre et `dotnet build SoundKeeper.GUI.slnx` ne compile pas le projet C++. Le script fait foi. Pour itérer sur le GUI une fois le moteur compilé :

```powershell
dotnet run --project SoundKeeper.GUI.Tests/SoundKeeper.GUI.Tests.csproj -c Release -p:Platform=x64
```

Le démarrage avec Windows vise la publication, repérée par le fichier `SoundKeeper.GUI.published` : une build lancée depuis Visual Studio ou `bin` ne remplace jamais une entrée valide.

## Données utilisateur

La configuration et le journal sont stockés dans :

```text
%LOCALAPPDATA%\SoundKeeper.GUI\settings.json
%LOCALAPPDATA%\SoundKeeper.GUI\SoundKeeper.GUI.log
%LOCALAPPDATA%\SoundKeeper.GUI\SoundKeeper.GUI.log.1 (archives, au maximum .1 à .3)
```

La variable d’environnement `SOUNDKEEPER_ENGINE_PATH` peut temporairement pointer vers un exécutable moteur précis pour le diagnostic.

## Organisation

- `Sound Keeper Core` : sources C++ upstream à la racine, étendues uniquement pour la sélection de sorties par identifiant ;
- `SoundKeeper.GUI/` : application WinUI 3 non packagée ;
- `SoundKeeper.GUI.Tests/` : tests ciblés exécutables sans framework externe ;
- `build-release.ps1` : build Release officiel ;
- `docs/ARCHITECTURE.md` : composants et décisions ;
- `docs/UPSTREAM_SYNC.md` : procédure de synchronisation upstream.

## Licence

Le moteur Sound Keeper original est distribué sous licence MIT ; voir `License.md`. Les nouveaux fichiers du GUI sont distribués sous les mêmes termes. Les mentions de l’auteur original sont conservées dans l’application et la documentation.
