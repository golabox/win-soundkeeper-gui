# Sound Keeper GUI

Sound Keeper GUI apporte une interface Windows 11 moderne au moteur audio open source [Sound Keeper](https://github.com/vrubleg/soundkeeper) d’Evgeny Vrublevsky. Le moteur C++/WASAPI reste intact et responsable de l’audio ; le GUI C# ne fait que construire sa ligne de commande, le démarrer et l’arrêter proprement.

Cette version est un fork indépendant. Elle n’est ni publiée ni supportée officiellement par l’auteur original.

## Fonctionnalités

- état actif/arrêté avec PID, durée d’exécution, fréquence et commande Activer/Désactiver ;
- navigation latérale Windows 11 repliable avec pages Général, Avancé, Journaux et À propos ;
- périphérique principal, toutes les sorties, numériques, analogiques ou marquées par `!` ;
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

1. Ouvrir `SoundKeeper.GUI.slnx` dans Visual Studio.
2. Choisir `Release` et `x64` dans la barre d’outils.
3. Lancer **Générer > Générer la solution**. Le projet C++ crée `Bin\SoundKeeper64.exe`; le projet GUI le copie ensuite dans son dossier `Engine`.
4. Définir `SoundKeeper.GUI` comme projet de démarrage.
5. Appuyer sur `F5` (débogage) ou `Ctrl+F5` (sans débogage).

En ligne de commande, depuis un terminal développeur Visual Studio :

```powershell
msbuild SoundKeeper.GUI.slnx /restore /m /p:Configuration=Release /p:Platform=x64
dotnet run --project SoundKeeper.GUI.Tests/SoundKeeper.GUI.Tests.csproj -c Release -p:Platform=x64
```

Pour une publication locale autonome .NET :

```powershell
dotnet publish SoundKeeper.GUI/SoundKeeper.GUI.csproj -c Release -r win-x64 --self-contained true -p:Platform=x64
```

## Données utilisateur

La configuration et le journal sont stockés dans :

```text
%LOCALAPPDATA%\SoundKeeper.GUI\settings.json
%LOCALAPPDATA%\SoundKeeper.GUI\SoundKeeper.GUI.log
%LOCALAPPDATA%\SoundKeeper.GUI\SoundKeeper.GUI.log.1 (archives, au maximum .1 à .3)
```

La variable d’environnement `SOUNDKEEPER_ENGINE_PATH` peut temporairement pointer vers un exécutable moteur précis pour le diagnostic.

## Organisation

- `Sound Keeper Core` : sources C++ upstream à la racine, inchangées ;
- `SoundKeeper.GUI/` : application WinUI 3 non packagée ;
- `SoundKeeper.GUI.Tests/` : tests ciblés exécutables sans framework externe ;
- `docs/ARCHITECTURE.md` : composants et décisions ;
- `docs/UPSTREAM_SYNC.md` : procédure de synchronisation upstream.

## Licence

Le moteur Sound Keeper original est distribué sous licence MIT ; voir `License.md`. Les nouveaux fichiers du GUI sont distribués sous les mêmes termes. Les mentions de l’auteur original sont conservées dans l’application et la documentation.
