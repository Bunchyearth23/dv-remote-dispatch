# Remote Dispatch Live — infrastructure en direct

Adaptation à l’installation locale avec DoubleTrack 2.1.1, DVSignals 1.1.3 et AITraffic 0.2.1. L’intégration DVSignals est optionnelle : aucun DLL de ce mod n’est redistribué.

## Fork BDVM

Ce fork public est maintenu sur [Bunchyearth23/dv-remote-dispatch](https://github.com/Bunchyearth23/dv-remote-dispatch), branche `bdvm-integration`. Il est fondé sur [mspielberg/dv-remote-dispatch](https://github.com/mspielberg/dv-remote-dispatch) au commit `0e032da48550e09e405b4b164f31136f2e925ebf` et conserve sa licence MIT et ses crédits.

Le fork fournit actuellement le host HTTP et le frontend nécessaires à `BDVM.Dispatch`. Il ne contient pas les assemblies BDVM : `BDVM.Dispatch`, `BDVM.Web` et leurs dépendances doivent être installés séparément pour les fonctions BDVM. Remote Dispatch reste utilisable sans BDVM pour ses fonctions upstream et locales qui n’appellent pas le bridge.

Version 1.7.0 : correction du chargement des trains et support des missions Yard Master (`SelfShunt` 1.0.0). Les noms de voie ne sont pas des identifiants uniques : les doublons reçoivent un suffixe de session partagé entre geometry, catalog, route et télémétrie AI. Toutes les voies natives sont conservées ; aucune voie homonyme n’est choisie arbitrairement. Recharger la page après installation ou changement de partie.

Le catalog des locomotives ne construit plus le graph complet et reste consultable lorsque l’adapter AI est indisponible ; les commandes gardent leurs contrôles de réservation. Les missions DirectHaul sont lues comme une paire de tâches de chargement/déchargement. Le tableau indique « À composer » avant l’attribution des wagons, puis suit les associations réellement inscrites sur leurs plaques. Une mission illisible est signalée individuellement. Cette intégration ne recrute pas de conducteur et ne choisit pas les wagons à la place de Yard Master.

La navigation ne relance plus un recadrage pendant chaque drag ou zoom. Les 1 842 signaux utilisent un canvas commun avec leur direction, couleur et tooltip. Les marqueurs hors viewport sont retirés du rendu ; les aiguillages et wagons deviennent visibles au zoom 16, les étiquettes de voie au zoom 17. Les locomotives restent visibles aux zooms plus éloignés. Les voies acceptent les clics seulement pendant la sélection d’une destination ou d’un passage. Les positions inchangées ne relancent pas l’animation.

La carte demande les jonctions et les aspects DVSignals toutes les 500 ms après chaque réponse. La version 1.2.2 répartit les lectures Unity entre plusieurs frames avec un budget souple de 1 ms par tranche (une lecture individuelle peut dépasser ce budget). Le JSON est construit sur un worker, et les clients partagent le même snapshot pendant 500 ms. Les éléments d’un snapshot sont donc lus à des instants légèrement différents. Le délai réel inclut la capture, le rafraîchissement interne de DVSignals et la latence réseau.

Les positions des trains et du joueur sont interpolées à chaque frame du navigateur avec un tampon de 150 ms. L’affichage se fige sur la dernière mesure en cas de coupure et se replace directement après une téléportation. Les marqueurs inchangés et les lignes de tableau inchangées ne sont plus reconstruits. Les requêtes périodiques sont suspendues lorsque l’onglet est masqué.

Pour diagnostiquer les à-coups, survoler le panneau de statut : il indique le temps total de lecture Unity et la plus longue tranche. L’endpoint `/infrastructure` expose aussi `captureMainThreadMs`, `maxSliceMs` et `captureWallMs`. La queue de commandes vise 2 ms par frame entre actions, sans interrompre une action en cours. Le scan des trains passe d’une fois par frame à 10 Hz ; leur position finale à l’arrêt est aussi publiée.

- Les branches sélectionnées sont surlignées près de la jonction. Un survol indique la voie choisie.
- Les triangles représentent les signaux, y compris les signaux distants et de manœuvre. Le survol donne le nom, l’aspect exact et le mode de fonctionnement.
- Rouge : DVSignals interdit le passage. Bleu : DVSignals ne l’interdit pas, mais l’aspect peut imposer des restrictions. Gris : signal éteint ou aspect inconnu. Ces couleurs sont une synthèse logique, pas une reproduction des lampes.
- Le panneau indique l’heure du snapshot et la disponibilité de DVSignals. Après trois secondes sans données fraîches, les symboles sont atténués et les commandes d’aiguillage sont désactivées. Les lectures reprennent automatiquement après une erreur réseau.

Ouvrir [la carte locale](http://localhost:7245) après chargement d’une partie. Les permissions existantes continuent de s’appliquer ; l’utilisateur configuré localement est `bunchy`. Après changement de carte ou de configuration DoubleTrack, recharger aussi la page pour récupérer toute la géométrie des voies.

## Transport Web — 1.8.1

Le serveur écoute désormais `localhost` par défaut. Pour un browser sur une autre machine, définir d’abord un password non vide, activer **Allow remote browser connections**, sauvegarder puis redémarrer le mod. Le mode remote utilise HTTP Basic sur HTTP : il convient uniquement à un LAN de confiance. Ne pas exposer le port à Internet ; pour traverser un réseau non fiable, placer le service derrière un tunnel chiffré ou un reverse proxy TLS.

Le frontend utilise du long polling HTTP sur `/updates/{sessionId}`, et non WebSocket. Chaque poll expire après 25 secondes côté serveur et 30 secondes côté browser. Les reconnexions utilisent un backoff borné avec jitter, les requests sont annulées lorsque l’onglet est masqué, le serveur limite la concurrence et annule sessions et callbacks main thread lors d’un unload. Un `sessionId` est borné et appartient à une seule identité Basic.

Toutes les commandes mutantes (aiguillages, locomotives, routes, AITraffic et intentions BDVM) vérifient l’identité, la permission dédiée et le same-origin. Des headers CSP, `nosniff` et `no-referrer` sont ajoutés. Le password est comparé sans arrêt anticipé. Les réglages des versions précédentes sont migrés au lieu d’être ignorés ; l’exposition remote reste désactivée par défaut.

Remote Dispatch ne devient pas une autorité économique : `/bdvm` transmet une identité et un intent JSON borné au bridge public de BDVM, sur le main thread. BDVM reste seul responsable de valider l’actor, l’ownership, les versions, les transitions et les écritures économiques. Voir la [campagne browser host/client](docs/browser-host-client-checklist.md).

## AITraffic (1.3.0)
 
Les joueurs locaux et multiplayer sont signalés par une flèche de taille fixe, un halo et une étiquette permanente. La flèche indique leur orientation, pas nécessairement le sens de déplacement du train. Le bouton de recentrage inclut les joueurs multiplayer reçus. Les aiguillages affichent une flèche alignée sur la branche sélectionnée et un trait turquoise épais ; les autres branches sont grises en pointillé. Cela indique la connexion physique, pas une autorisation de franchir un signal.

L’onglet **Trafic AI** liste les trains de trafic et les conducteurs engagés : état du conducteur, origine/destination, vitesse actuelle/cible, voie de destination, distance restante et prochain signal. Une recherche filtre les trains et les gares. Sélectionner un train centre la carte et affiche son trajet prévu. **Toutes les réservations** revient à la vue globale.

Les voies prévues sont bleues en pointillé ; les réservations effectives sont orange en continu. Les aiguillages verrouillés sont encadrés en orange, avec leur propriétaire au survol. Les calques trains AI et réservations/itinéraire sont activables séparément. Les étiquettes AI utilisent un tampon d’interpolation de 550 ms adapté à la cadence du snapshot ; les marqueurs du flux de positions normal gardent 150 ms.

L’adapter est en lecture seule. Il inspecte les champs privés existants d’AITraffic 0.2.1 (`TrafficManager.s_instance`, `RailGraph._trackReservations`, `JunctionController._activeLocks`) et copie les registres sous leur lock. Il ne crée pas de singleton, ne calcule pas d’itinéraire et ne prend ni ne libère de réservation. Les listes de réservations tentées des conducteurs ne servent pas de preuve de réservation obtenue. Si l’API devient incompatible, le panneau affiche l’indisponibilité et retire les anciennes données AI.

Une commande individuelle d’aiguillage relit le verrou AI côté serveur sur le main thread, juste avant l’action. Un verrou actif ou impossible à vérifier renvoie HTTP 409. Les contrôles de parcours et les commandes aux conducteurs sont décrits ci-dessous ; l’occupation des cantons, les réservations DVSignals distinctes pour le joueur et les horaires ne sont pas intégrés.

## Préparation d’itinéraires — 1.4.0

Ouvrir le panneau « Préparer un itinéraire », charger les trains puis sélectionner une locomotive. Le départ est relu au moment du calcul. Choisir une voie de destination par recherche ou avec « Sur carte », éventuellement une voie de passage, puis prévisualiser. Les destinations récentes sont conservées dans ce navigateur. Le calcul choisit le plus court parcours continu dans l’un des deux sens ; le panneau indique la prochaine voie pour vérifier le sens de départ. Les rebroussements et parcours repassant par une voie nécessitent plusieurs préparations.

Le réseau provient des connexions natives RailTrack, avec respect des extrémités et des branches des jonctions. La capture initiale se répartit entre frames avec un budget souple de 1 ms ; la recherche s’exécute sur worker. Aucun calcul périodique n’est ajouté. La distance indiquée additionne les voies complètes, sans corriger la position exacte du train sur les voies de départ et d’arrivée.

Pour un train joueur immobilisé, « Positionner les aiguillages » relit les connexions, l’occupation des voies par les bogies, les réservations AITraffic et les locks. Une voie adjacente occupée ou un véhicule à proximité bloque conservativement un changement d’aiguillage. Les vérifications et commandes s’exécutent dans le même callback Unity, sans yield. Un token associé à l’utilisateur expire après 60 secondes ; il est utilisable une seule fois. La permission de commande des aiguillages est obligatoire. Une interruption indique le nombre de changements déjà effectués ; aucune promesse de rollback ou de réservation globale n’est faite.

Le parcours joueur n’est pas réservé : d’autres acteurs peuvent modifier les aiguillages après la commande. Les réservations propres à DVSignals et les autorisations des signaux ne sont pas intégrées au contrôle de parcours joueur ; respecter la signalisation affichée séparément sur la carte. Le basculement manuel individuel existant reste distinct et ne bénéficie pas de tous les contrôles de cette commande de parcours.

## Affectation AI — 1.5.0

Sélectionner un train disposant d’un conducteur AITraffic actif. Si nécessaire, utiliser « Arrêter le conducteur AI », attendre son immobilisation puis prévisualiser la destination et le passage intermédiaire. « Affecter et démarrer le conducteur AI » transmet le parcours exact et demande la reprise. « Reprendre son parcours actuel » reprend la mission existante sans changer sa destination. Pour confier un train joueur à un nouveau conducteur, l’engager d’abord depuis AITraffic ; ces commandes pilotent les conducteurs déjà actifs.

L’affectation exige les permissions de locomotive et d’aiguillage, un token valide, le même conducteur et les contrôles live de parcours. Elle normalise la sélection vers la locomotive du conducteur. Les locks appartenant au train sont distingués de ceux des autres trains. Le train doit tenir sur la voie d’arrivée avec une marge de 25 m ; un conducteur engagé doit arriver sur une voie appartenant à une gare. Sa mission existante reçoit la destination et la distance du nouveau parcours, sans nouveau paiement ni remplacement du conducteur.

L’adapter de commandes est séparé de la télémétrie et limité à AITraffic 0.2.1. Il convertit les voies validées avec `BuildPathFromTracks`, vérifie que les voies et branches correspondent, remet les caches du conducteur à jour et libère ses réservations obsolètes. Au transfert, les réservations sous les bogies sont conservées ; les anciens locks près d’un véhicule restent protégés. Les réservations d’autres conducteurs ne sont jamais supprimées. AITraffic reprend ensuite la gestion normale des signaux, réservations et aiguillages. Une policy Harmony empêche uniquement les conducteurs affectés par ce panneau de substituer un détour au parcours validé ; les autres trains conservent leur comportement.

Les membres nécessaires sont résolus avant modification. En cas d’échec pendant l’affectation, le conducteur reste au frein, l’ancien trajet et les données de mission sont restaurés autant que possible ; les réservations déjà libérées ne sont pas reprises aveuglément. Recalculer avant de reprendre. Les arrêts de fin de parcours, livraisons des conducteurs engagés et despawn des trains de trafic restent gérés par AITraffic.

## Multiplayer — 1.6.0

Intégration optionnelle basée sur le fork [Bunchyearth23/dv-multiplayer](https://github.com/Bunchyearth23/dv-multiplayer), version 0.1.15.8 et API 1.1.0. Remote Dispatch Live lit l’API publique `MPAPI.MultiplayerAPI` par reflection ; aucun DLL multiplayer n’est livré avec ce mod. Le nouveau panneau affiche le rôle réseau et les autres joueurs chargés, leur équipage et leur véhicule. Le joueur local reste sur son marqueur habituel. Les marqueurs distants sont interpolés avec 550 ms de tampon, retirés à la déconnexion et capturés dans le snapshot fractionné existant.

Installer Remote Dispatch Live sur l’hôte et ouvrir sa carte web depuis les navigateurs des dispatchers. La déclaration `MultiplayerCompatibility: Host` évite d’imposer ce mod à tous les clients. Si Remote Dispatch Live est aussi lancé chez un client, sa carte permet la consultation et la préparation ; les commandes doivent être envoyées à la carte de l’hôte. Les identifiants web et permissions restent ceux de Remote Dispatch, sans correspondance automatique avec les comptes multiplayer.

Les commandes d’aiguillage vérifient que le composant `NetworkedJunction` est initialisé ; le `Junction.Switch` normal déclenche alors son événement de synchronisation multiplayer. Les commandes de locomotive et de conducteur AI sont réservées à l’hôte et refusées lorsqu’un joueur occupe le consist ou détient une commande de locomotive. L’adapter vérifie le composant réseau, son NetId et sa simulation. Un changement de session invalide le parcours préparé. Une API inconnue, une déconnexion ou une synchronisation incomplète refuse les commandes, sans fallback vers une écriture locale silencieuse. Le support de serveur dédié n’est pas ajouté.

L’ancien patch multiplayer destiné à l’Id `RemoteDispatch` n’est pas nécessaire et n’est pas installé sur `RemoteDispatchLive` : cela évite notamment sa lecture des transforms depuis le worker HTTP. Le code source multiplayer n’a pas été modifié et le mod multiplayer n’a pas été installé. Les tests utilisent des fixtures : tester deux joueurs, déplacement, départ/reconnexion, aiguillage vu des deux côtés, refus depuis un client et contrôle d’un train libre depuis l’hôte. La compatibilité de circulation d’AITraffic avec Multiplayer doit également être vérifiée en partie réelle.

## Build et vérification

```powershell
dotnet build -c Release '-p:GameDir=D:\Steam\steamapps\common\Derail Valley'
node --check main.js
node --test tests/infrastructure.test.cjs
node --test tests/motion.test.cjs
node --test tests/ai-traffic.test.cjs
node --test tests/route-planner.test.cjs
node --test tests/multiplayer.test.cjs
dotnet run --project tests/backend/BackendChecks.csproj -c Release
dotnet run --project tests/routes/RouteChecks.csproj -c Release
dotnet run --project tests/jobs/JobChecks.csproj -c Release
dotnet run --project tests/transport/TransportChecks.csproj -c Release
dotnet run --project tests/sessions/SessionChecks.csproj -c Release
```

Le build utilise les assemblies de l’installation du jeu. Installer `RemoteDispatchLive.dll` et `info.json` dans `Mods/RemoteDispatchLive`. Conserver `Settings.xml`. L’ancienne version doit rester désactivée pour éviter un conflit de port. Sur cette installation, elle est conservée dans `Mods-backup/RemoteDispatch-Original`. Les avertissements NU1701 viennent des packages UnityModManager et Harmony existants.

Le programme `tests/routes` utilise le runtime .NET 6 installé pour exécuter réellement le patch Harmony 2.2.2 sur les fixtures. Cette ancienne dépendance provoque une erreur CLR sous .NET 10 ; le mod continue de cibler netstandard2.0 et le runtime Unity du jeu. Les quatorze tests frontend et les scénarios backend ne remplacent pas les essais de circulation en partie réelle.

La compilation et les tests automatisés sont vérifiés. La comparaison avec une partie active reste à faire : comparer le trajet AI et les réservations, vérifier le refus de basculement d’un aiguillage verrouillé, observer un changement d’aspect DVSignals pendant un passage AI, couper puis rétablir la connexion, recharger une partie. Voir [le suivi](docs/INDEX.md).

## Credits

Icons made by [Freepik](https://www.freepik.com) from [Flaticon](https://www.flaticon.com/).
