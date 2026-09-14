# UniversalRemote

Premier lot des sprints 0 et 1 : SDK .NET 10 indépendant de MAUI et moteur de commandes local, avec un provider explicitement simulé. Aucune télévision réelle n'est pilotée dans ce lot.

## Installation du delta

1. Conserver deux dossiers voisins `DomainRelay/` et `UniversalRemote/`.
2. Extraire le ZIP DomainRelay d'origine dans `DomainRelay/` : `DomainRelay/src/DomainRelay/DomainRelay.csproj` doit exister.
3. Appliquer les fichiers du dossier `DomainRelay/` de ce delta sur cette extraction (README ajouté, diagnostics, projets Mapping et documentation XML corrigés). Ne pas remplacer le dossier complet.
4. Ajouter le dossier `UniversalRemote/` fourni. Aucun code UniversalRemote existant n'a été fourni : ce lot est une initialisation, pas un patch d'un dépôt UniversalRemote déjà analysé.
5. Installer le SDK .NET 10 stable et PowerShell 7. Ouvrir `UniversalRemote.slnx` ou un terminal dans `UniversalRemote/`.
6. Exécuter `pwsh -File ./build/verify.ps1`. Pour une autre disposition : `pwsh -File ./build/verify.ps1 -DomainRelayRoot 'E:/Sources/DomainRelay'`.

Le script arrête à la première erreur. Il restaure les dépendances NuGet, compile en Release, exécute les tests xUnit et le sample automatique, produit Abstractions/Core dans `artifacts/packages`, puis consomme le package Core depuis un projet sans ProjectReference. Il ne publie rien sur NuGet.

Après validation : `dotnet run --project samples/UniversalRemote.Console.Sample -c Release --no-build`. Saisir p, +, -, o ou h puis Entrée ; q termine la session. Le sample affiche sans ambiguïté qu'il s'agit d'une simulation.

## Architecture

- Abstractions : appareils immuables, routes, identifiants d'actions extensibles, capacités, résultats et contrats.
- Core : sélection d'une route supportée puis un seul appel provider. Aucun provider concret ni MAUI.
- Application : Command et Query DomainRelay, validation FluentValidation via DomainRelay, diagnostics et mapping DomainRelay.
- Provider.Simulator : démonstrateur isolé ; aucune simulation dans le moteur de production.
- Le futur client MAUI utilisera les mêmes cas d'usage. Presentation/Theming arrivent au sprint 3.

Les packages Application et Simulator ne sont pas publiables dans ce lot. Application référence les sources DomainRelay jointes pour éviter de supposer des versions NuGet publiques non vérifiées. Abstractions/Core sont packables sans DomainRelay. La bascule Application vers des PackageReference DomainRelay versionnées sera un changement explicite après qualification mobile.

## Contrat d'exécution

Les capacités sont portées par route, puis regroupées au niveau de l'appareil. Le moteur choisit la première route enregistrée supportant l'action. Il peut ignorer une route dont le provider manque avant tout envoi. Après le premier appel provider, il n'exécute ni retry ni fallback automatique, même sur échec : VolumeUp et PowerToggle peuvent avoir déjà eu un effet.

`Accepted` signifie que le provider a accepté ou émis la commande, pas que l'état physique est confirmé. `Unknown` impose de vérifier l'appareil avant de recommencer. L'annulation utilisateur reste une OperationCanceledException et ne signifie pas qu'une commande déjà émise a été annulée.

Le délai est coopératif (5 s par défaut, configurable entre plus de 0 et 60 s) : chaque provider doit respecter le token. Ce mécanisme ne promet pas d'arrêter un provider qui ignore l'annulation. Le moteur attend sa terminaison pour éviter de libérer prématurément une opération qui pourrait encore émettre. Il n'y a pas encore de file par appareil ni de déduplication entre deux appels distincts.

Seules les actions sans paramètre sont implémentées. Les actions de saisie texte, température et volume absolu devront recevoir des contrats typés et leur validation au sprint concerné. Endpoints et secrets restent hors des contrats d'affichage.

## CI

Le workflow nécessite les variables `DOMAINRELAY_REPOSITORY` (owner/repository) et `DOMAINRELAY_REF` (SHA complet d'un commit audité contenant les corrections de ce delta). Pour un dépôt privé séparé, ajouter un secret `DOMAINRELAY_READ_TOKEN` en lecture seule. Sans ces paramètres, le job échoue avec une instruction claire. Aucun dépôt ni secret n'a été inventé ou créé ; le workflow n'a pas été exécuté sur GitHub.

Ne pas publier publiquement ce SDK avant choix de licence, qualification API et validation Android. Les noms et versions des packages livrés sont des préversions de développement.

## Suivi

Voir `docs/SPRINTS.md`, `docs/DOMAINRELAY_AUDIT.md` et `docs/PRODUCT_DECISIONS.md`. La preuve des vérifications de ce lot est dans `docs/VALIDATION.md`.
