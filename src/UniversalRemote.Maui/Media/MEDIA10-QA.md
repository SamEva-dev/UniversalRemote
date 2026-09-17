# MEDIA 10 — Horizon Media / QA produit

Ce sprint finalise l’expérience Media sans introduire de logique M3U/Xtream dans les Pages. Les écrans restent alimentés par les contrats Media et les capabilities de cible/appareil.

## Critères MEDIA-043 à MEDIA-047

- **MEDIA-043 — Horizon shell** : tokens visuels Media isolés, mode sombre premium, cibles tactiles >= 44 px et bascule **Contrôle ↔ Médias** accessible en un geste.
- **MEDIA-044 — Accueil/catalogue** : accueil Horizon avec sources, appareils, reprise/favoris et catalogue responsive (Live 1–2 colonnes, Films/Séries 2–4 colonnes selon largeur).
- **MEDIA-045 — EPG/détail** : Guide TV et fiche média harmonisés, états vide/chargement/restriction conservés, accès Guide/Scénario sans connaissance provider.
- **MEDIA-046 — Player/target picker** : lecteur 16:9, timeline, commandes locales, remote overlay capability-driven et sélection de destination explicite.
- **MEDIA-047 — QA/accessibilité** : libellés d’automatisation sur les actions critiques, contrastes renforcés, états Selected visibles et largeur max sur tablette/desktop.

## Parcours de recette manuelle

1. Depuis **Télécommande**, toucher **Médias** et vérifier que l’accueil Media s’ouvre en un geste.
2. Depuis **Média**, toucher **Contrôle** et vérifier le retour à la télécommande sans perdre l’appareil sélectionné.
3. Vérifier l’accueil à ~360 px, ~720 px et >= 1080 px de largeur : aucune coupe horizontale inattendue.
4. Ouvrir **TV en direct** : 1 colonne téléphone, 2 colonnes grand écran ; sélectionner une chaîne puis sa fiche.
5. Ouvrir **Films** et **Séries** : 2 colonnes téléphone, 3 tablette, 4 grand écran ; vérifier les artworks et fallback par initiale.
6. Ouvrir une fiche média avec/sans artwork, avec/sans reprise, puis tester Favori, Guide (Live) et Scénario.
7. Dans **Où regarder ?**, vérifier Local + cibles détectées, état Selected visible et bouton de lecture désactivé pour les cibles Preview.
8. Démarrer une lecture locale : timeline, -10 s, +30 s, play/pause/stop ; vérifier l’absence de seek Live.
9. Avec une cible liée à un Device, vérifier que le remote overlay n’affiche/exécute que les RemoteAction supportées.
10. Vérifier Guide TV, Recherche, Bibliothèque, Profils et Scénarios avec leurs états vide/erreur.
11. Passer sur un profil Enfant/Invité et vérifier qu’un contenu interdit reste bloqué jusque dans le démarrage de lecture.
12. Inspecter logs/persistance : aucune URL de stream, username, password, token ou credential ne doit apparaître dans les modèles/UI/logs Media.

## Non-régression

- Les layouts Classic/Nova/Elite/Horizon/Fusion/Neo de la télécommande ne doivent pas hériter des styles Media : `MediaHorizon.xaml` n’expose que des styles **keyed**.
- Aucune condition Samsung/LG/AndroidTV/M3U/Xtream n’est ajoutée dans les Pages Media.
- La sélection de cible reste distincte de la source Media.
