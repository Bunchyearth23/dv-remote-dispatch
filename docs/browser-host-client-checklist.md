# Campagne browser host/client — RemoteDispatchLive 1.8.1

Cette campagne nécessite l’utilisateur et une partie chargée. Elle ne doit pas démarrer Derail Valley automatiquement.

## Préparation

- Installer le même build `RemoteDispatchLive.dll` sur l’hôte uniquement ; conserver `Settings.xml` et désactiver l’ancien mod `RemoteDispatch` pour éviter un conflit de port.
- Définir un password non vide, activer les connexions remote, sauvegarder puis redémarrer le mod.
- Autoriser le port choisi uniquement sur le profil réseau privé du firewall. Ne pas publier ce port sur Internet.
- Préparer deux identités Basic distinctes : une lecture seule et une avec permissions Junctions, Locomotive Control et BDVM.
- Noter l’adresse LAN de l’hôte, les versions de Derail Valley, BDVM, BDVM.Dispatch, BDVM.Web, DoubleTrack, DVSignals, AITraffic et Multiplayer.

## Scénarios

1. Ouvrir `http://localhost:7245` sur l’hôte et `http://<adresse-hôte>:7245` sur le client. Vérifier les prompts Basic, la carte initiale, trains, voies, aiguillages, signaux et joueurs.
2. Avec l’identité lecture seule, vérifier HTTP 403 pour toggle, contrôle locomotive, apply/assign/control-ai et POST `/bdvm`. Confirmer qu’aucun state du jeu ou de BDVM ne change.
3. Avec l’identité autorisée, basculer un aiguillage libre puis vérifier le même state dans le jeu, sur les deux browsers et chez le client Multiplayer. Répéter sur un aiguillage verrouillé AI ou non synchronisé : attendre HTTP 409 et aucun changement.
4. Contrôler une locomotive libre depuis l’hôte Multiplayer. Vérifier le refus si un joueur occupe le consist, si l’autorité réseau manque ou si Remote Dispatch tourne côté client.
5. Prévisualiser puis appliquer une route joueur ; modifier un aiguillage ou déplacer le train entre preview et apply et vérifier le refus du token obsolète. Répéter l’affectation AI avec les réservations visibles.
6. Dans BDVM, lire le state puis soumettre `fleet.set-state` et `assignment.cancel`. Vérifier que le bridge BDVM décide le résultat, que les versions évoluent côté autorité et qu’aucun calcul de wallet/ownership n’apparaît dans le browser ou Remote Dispatch.
7. Couper le réseau client pendant plus de 30 secondes, le rétablir et vérifier la reprise sans reload, sans doublon de joueur/train et sans rafale continue de requests. Masquer puis réafficher l’onglet.
8. Recharger la partie, désactiver/réactiver le mod, puis fermer la partie avec un poll en attente. Vérifier l’arrêt immédiat du listener, l’absence d’exception répétée et une nouvelle session propre au retour.
9. Tester un Origin cross-site et la réutilisation du même `sessionId` avec une autre identité : attendre respectivement HTTP 403. Envoyer un payload BDVM supérieur à 4096 octets : attendre HTTP 413.

## Mesures et preuve

Conserver les logs RemoteDispatch/BDVM, les status HTTP, une capture du rôle Multiplayer et les valeurs `captureMainThreadMs`, `maxSliceMs`, `captureWallMs`. Pendant deux minutes avec les deux browsers ouverts, relever les freezes Unity, la cadence des snapshots et le nombre de requests simultanées. Consigner les versions exactes et chaque écart dans `docs/INDEX.md` avant toute publication stable.
