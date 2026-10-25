# Skill 06 — Verification de Coherence Interdisciplinaire

## Objectif
Verifier que les differentes disciplines du modele federe sont coherentes entre elles : alignements, reservations, niveaux, zones.

## Quand l'utiliser
- Avant une reunion de synthese
- Apres reception de nouvelles versions de maquettes
- Validation de coordination avant depot de permis ou appel d'offres
- Controle qualite periodique

## Workflow

### 1. Verification de presence des disciplines
```
Outil : get_document_info
```
Confirmer que toutes les disciplines attendues sont presentes dans le modele federe.

**Checklist :**
- [ ] Architecture (ARC)
- [ ] Structure beton (STR)
- [ ] Structure acier (STA)
- [ ] CVC / Mecanique (CVC)
- [ ] Plomberie (PLB)
- [ ] Electricite (ELE)
- [ ] Piping process (PIP)
- [ ] Protection incendie (PI)

### 2. Verification des clashs par paire de disciplines
```
Outil : list_clash_tests
```
Verifier qu'il existe un test pour chaque paire critique :

| Paire | Test attendu | Priorite |
|---|---|---|
| ARC vs STR | Structurel vs Architectural | Haute |
| ARC vs CVC | Gaines dans faux-plafonds | Haute |
| ARC vs PLB | Traversees de murs/dalles | Haute |
| ARC vs ELE | Chemins de cables | Moyenne |
| STR vs CVC | Poutres vs Gaines | Critique |
| STR vs PLB | Poutres vs Tuyaux | Critique |
| STR vs ELE | Poutres vs Cable Trays | Haute |
| CVC vs PLB | Gaines vs Tuyaux | Haute |
| CVC vs ELE | Gaines vs Chemins cables | Moyenne |
| PLB vs ELE | Tuyaux vs Chemins cables | Moyenne |

### 3. Isolation croisee pour verification visuelle
Superposer 2 disciplines pour verification :
```
Outil : isolate_by_multi_search
Params : filters = "", branches = "PRJ_A,M_Ventilation"
```
Verifier visuellement :
- Les gaines passent dans les faux-plafonds (pas dans les murs porteurs)
- Les traversees ont des reservations
- Les hauteurs libres sont respectees

### 4. Verification par zone/niveau
Pour chaque niveau critique :
1. Isoler la zone
2. Verifier la superposition des disciplines
3. Documenter les observations

### 5. Documentation
```
Outil : save_current_viewpoint
Params : name = "VERIF_[DISCIPLINE1]-[DISCIPLINE2]_[ZONE]_[DATE]"
```

## Rapport de coherence

### Matrice de coordination

| | ARC | STR | CVC | PLB | ELE |
|---|---|---|---|---|---|
| **ARC** | — | OK/KO | OK/KO | OK/KO | OK/KO |
| **STR** | | — | OK/KO | OK/KO | OK/KO |
| **CVC** | | | — | OK/KO | OK/KO |
| **PLB** | | | | — | OK/KO |
| **ELE** | | | | | — |

### Points d'attention identifies

| # | Zone | Disciplines | Description | Gravite | Action |
|---|---|---|---|---|---|
| 1 | N+2 Local Tech | CVC vs STR | Gaine traverse poutre | Critique | MEP a modifier |
| 2 | N+1 Entree | PLB vs ARC | Pas de reservation | Haute | ARC a ajouter |
