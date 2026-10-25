# 00 - Pieges du modele et regles d'or (lire en premier)

Ce skill rassemble les pieges verifies sur des maquettes reelles. Il est servi par `get_skill('00')` a
n'importe quel client MCP. Le lire avant la premiere tache sur un modele, et le relire avant une
isolation ou une recherche qui ne donne pas le resultat attendu.

---

## 1. Explorer avant d'agir

Ne **jamais** deviner un nom de categorie ou de propriete. Toujours :

```
search_by_name           # trouver un element representatif
get_item_properties      # lire ses vraies categories et proprietes
search_by_property       # construire la requete avec les noms exacts
isolate_by_search        # agir seulement ensuite
```

Une recherche qui renvoie zero ne prouve rien tant que la meme requete n'a pas renvoye un resultat non
nul sur un element connu pour etre bon. Verifier l'instrument avant de conclure sur la mesure.

Pour une API sans outil dedie : `lookup_navis_api` pour lire les signatures, puis `navis_query` pour
sonder. Detail dans `get_skill('14')`.

## 2. Performance : ce qui fait tomber Navisworks

- **JAMAIS** iterer ou cacher 400 000 elements un par un : gel ou plantage.
- **JAMAIS** deux isolations en parallele : plantage. Un seul appel d'isolation a la fois, attendre sa
  reponse avant le suivant.
- L'isolation travaille par **branche** (enfants de premier niveau) : echantillonner environ 200
  resultats pour identifier les branches, puis cacher les branches sans correspondance.
- Toujours `show_all` avant une nouvelle isolation.
- Dans du code C#, ne jamais pomper la boucle de messages (`Application.DoEvents`) : le plugin le
  refuse, c'est la cause probable du plantage du 2026-08-06.

## 3. Noms de proprietes FR vs EN

Les modeles de conception sont souvent en anglais, ceux d'entreprise en francais. Chercher dans les deux :

| EN | FR |
|---|---|
| `Identity Data > Workset` | `Données d'identification > Sous-projet` |
| `Construction > Function` | `Construction > Fonction` |
| `Electrical - Circuiting > Electrical Data` | `Electricité - Circuit > Données électriques` |
| `Element` | `Elément` (accent sur le 2e e seulement) |

Les noms de **types de tache TimeLiner** sont aussi localises (`Construire`, `Demolir`, `Temporaire`) :
un nom inconnu ne leve aucune erreur, la simulation devient muette.

## 4. Workset n'est pas discipline

Un coude de ventilation peut avoir `Workset = PL_Alimentation`, un luminaire `Sous-projet = A_INT`. Les
raccords sont souvent dans un workset different de leur discipline.
**Filtrer par propriete metier** (`System Classification`, `Fonction`, `Electrical Data`), pas par
workset seul.

## 5. Modeles ACC / SVF

- Les noeuds `LcOaPartition` ont un GUID nul (`00000000-0000-0000-0000-000000000000`) : isoler par nom
  (`isolate_by_multi_search` avec `branches`) ou par propriete (`isolate_by_search`, `isolate_by_multi_search`), jamais par GUID.
- `get_item_properties` echoue (`Not supported if '!IsDisplayString'`) sur les groupes `LcSvfGroup` :
  descendre aux feuilles `Geometry` pour lire les proprietes.
- `Element > Type` vaut toujours `Geometry` : inutile pour filtrer.
- Les proprietes Revit natives peuvent etre inaccessibles a `search_by_property` : `search_by_name`
  est le repli fiable.
- `list_models` avant d'isoler par nom (`branches`), pour connaitre les noms exacts des partitions.

## 6. Comptes et identifiants

- Un re-export Revit change les comptes : recompter, ne jamais reutiliser les chiffres d'une session
  precedente.
- Filtrer sur la famille ne suffit pas toujours : seul le **type** distingue certains elements (une
  plateforme a pour famille `Sol`). Prevoir exclusion par famille **et** par type.

## 7. Ecrire dans la maquette

- Lire d'abord (`navis_query`), ecrire ensuite (`navis_modify`), jamais dans le meme envoi.
- Demander a l'utilisateur d'enregistrer le `.nwf` avant la premiere ecriture : le plugin ne
  l'enregistre jamais a sa place.
- Chaque ecriture par code est soumise a l'approbation de l'utilisateur dans Navisworks. Un refus est
  une reponse : lui demander ce qu'il veut, ne pas reproposer le meme code.
- `list_runs` / `get_run` relisent ce qui a ete fait, y compris l'appel qui a fait tomber Navisworks.
