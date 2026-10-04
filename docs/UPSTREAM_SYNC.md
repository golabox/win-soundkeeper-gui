# Synchroniser avec Sound Keeper upstream

Les sources C++ originales restent à la racine et le nouveau code vit dans `SoundKeeper.GUI/`, `SoundKeeper.GUI.Tests/` et `docs/`. Cette séparation minimise les conflits.

Seul écart du moteur : `CSoundKeeper.hpp/.cpp` ajoutent le type de périphériques `Selected` (`selected`, `device=<id>`) et un tampon de ligne de commande dimensionné sur l’entrée. Le conserver lors des merges.

## Synchronisation

Depuis la racine du dépôt :

```powershell
git remote add upstream https://github.com/vrubleg/soundkeeper.git # seulement si absent
git fetch upstream
git merge upstream/master
```

Résoudre d’abord les éventuels conflits dans les fichiers upstream sans déplacer leur logique vers le GUI.

## Vérifications après merge

1. Lire les changements de `ReadMe.txt`, `CSoundKeeper.cpp`, `CSoundKeeper.hpp`, `SoundKeeper.vcxproj` et `BuildInfo.hpp`.
2. Vérifier les mots-clés, valeurs par défaut, bornes numériques, règles Sleep, noms de binaires et architectures.
3. Mettre à jour `CliArgumentBuilder` et les libellés seulement si le contrat a réellement changé.
4. Lancer `build-release.ps1` (moteur, GUI, tests, publication) ; le test `Upstream CLI contract` signale la disparition d’un mot-clé attendu.
5. Faire le smoke test Activer → Désactiver → changement de mode → réactivation → relance du GUI.

Ne pas modifier ou refactoriser les sources upstream uniquement pour adapter le GUI. Si une nouvelle option apparaît, elle reste immédiatement utilisable via le champ **Arguments Sound Keeper supplémentaires**.
