#!/usr/bin/env python3
"""
Importe un classeur Excel de cotisations (format « GESTION_COTISATIONS ») dans un projet collectif.

Lit :
  - PARAMETRES : nom de l'association, paliers de cotisation
  - MEMBRES : ID, Nom, Prénom, Date d'adhésion, Palier
  - COTISATIONS_AAAA : pour chaque mois, Montant | Moyen | Date

Crée le projet (paliers, échéancier mensuel couvrant les feuilles), l'ouvre, inscrit les membres
comme participants sans compte et importe chaque paiement à sa date, déjà validé.

Usage :
  python3 scripts/import_classeur.py CLASSEUR.xlsx [--unite 1000] [--api http://localhost:5243/api/v1]
         [--tel +237670000002] [--mdp Demo2026!] [--comptes] [--membres] [--dry-run]

--unite : facteur appliqué aux montants du classeur (1000 si « 15 » signifie 15 000 FCFA).
--comptes : lie chaque participant au compte qui porte son numéro (colonne Telephone du classeur,
            sinon numéro de démo +2376900000NN dérivé de l'ID M0NN) ; les comptes doivent exister.
--membres : affiche « telephone<TAB>Prénom NOM » pour chaque membre, puis s'arrête (sert à créer les comptes).
"""
import argparse
import datetime as dt
import json
import sys
import urllib.error
import urllib.request

import openpyxl

MOIS = {"janv": 1, "fevr": 2, "févr": 2, "mars": 3, "avri": 4, "avr": 4, "mai": 5, "juin": 6, "juil": 7,
        "aout": 8, "août": 8, "sept": 9, "octo": 10, "oct": 10, "nove": 11, "nov": 11, "dece": 12, "déc": 12, "dec": 12}


def num(v):
    if v is None or v == "":
        return None
    try:
        return float(v)
    except (TypeError, ValueError):
        return None


def date_cell(v):
    if isinstance(v, dt.datetime):
        return v
    if isinstance(v, (int, float)):  # Excel serial date
        return dt.datetime(1899, 12, 30) + dt.timedelta(days=float(v))
    if isinstance(v, str):
        for fmt in ("%d/%m/%Y", "%Y-%m-%d"):
            try:
                return dt.datetime.strptime(v.strip(), fmt)
            except ValueError:
                pass
    return None


def telephone(cellule, ref):
    """Number from the workbook, else a demo number derived from the member ID (M001 -> +237690000001)."""
    chiffres = "".join(c for c in str(cellule or "") if c.isdigit())
    if len(chiffres) == 9:
        return "+237" + chiffres
    if len(chiffres) == 12 and chiffres.startswith("237"):
        return "+" + chiffres
    n = "".join(c for c in ref if c.isdigit())
    return f"+237690000{int(n):03d}" if n else None


def lire(path, unite):
    wb = openpyxl.load_workbook(path, data_only=True, read_only=True)

    association, paliers = None, []
    for row in wb["PARAMETRES"].iter_rows(values_only=True):
        if row[0] == "Association":
            association = str(row[1]).strip()
        elif isinstance(row[0], (int, float)) and num(row[1]) is not None:
            paliers.append(num(row[1]) * unite)

    membres = {}
    for row in wb["MEMBRES"].iter_rows(min_row=2, values_only=True):
        ref, nom, prenom = row[0], row[1], row[2]
        if not ref or not nom:
            continue
        nom_complet = f"{str(nom).strip()} {str(prenom or '').strip()}".strip()
        membres[nom_complet.upper()] = {
            "reference": str(ref), "nom": nom_complet, "telephone": telephone(row[4], str(ref)),
            # Account display name, first name first: the app greets "Bonjour <first word>".
            "nomCompte": f"{str(prenom or '').strip()} {str(nom).strip()}".strip(),
            "palier": (num(row[7]) or paliers[0] / unite) * unite,
            "dateAdhesion": (date_cell(row[6]) or dt.datetime(2000, 1, 1)).strftime("%Y-%m-%dT00:00:00"),
            "statut": str(row[9]).strip() if row[9] else None,
            "sexe": str(row[3]).strip() if row[3] else None,
        }

    paiements, mois_couverts = [], []
    for ws in wb.worksheets:
        if not ws.title.startswith("COTISATIONS_"):
            continue
        annee = int(ws.title.split("_")[1])
        rows = list(ws.iter_rows(values_only=True))
        entete = rows[2]
        colonnes = []  # (index of Montant column, month)
        for i, v in enumerate(entete):
            if isinstance(v, str) and v.strip().lower()[:4] in MOIS:
                colonnes.append((i, MOIS[v.strip().lower()[:4]]))
        mois_couverts += [(annee, m) for _, m in colonnes]
        for row in rows[4:]:
            if not row or not row[1] or str(row[1]).strip().upper().startswith("TOTAL"):
                continue
            m = membres.get(str(row[1]).strip().upper())
            if m is None:
                print(f"  ! membre inconnu dans {ws.title} : {row[1]}", file=sys.stderr)
                continue
            for i, mois in colonnes:
                montant = num(row[i]) if i < len(row) else None
                if not montant or montant <= 0:
                    continue
                moyen = row[i + 1] if i + 1 < len(row) and row[i + 1] not in (None, "-") else "non précisé"
                quand = date_cell(row[i + 2]) if i + 2 < len(row) else None
                quand = quand or dt.datetime(annee, mois, 15)
                paiements.append({"reference": m["reference"], "montant": montant * unite,
                                  "date": quand.strftime("%Y-%m-%dT12:00:00"), "moyen": str(moyen)})

    debut = min(mois_couverts)
    fin = max(mois_couverts)
    nb = (fin[0] - debut[0]) * 12 + fin[1] - debut[1] + 1
    return {
        "association": association, "paliers": sorted(set(paliers)), "membres": list(membres.values()),
        "paiements": paiements, "debut": f"{debut[0]}-{debut[1]:02d}-01", "nombreEcheances": nb,
    }


def appel(api, methode, chemin, corps=None, jeton=None):
    req = urllib.request.Request(api + chemin, method=methode, data=None if corps is None else json.dumps(corps).encode(),
                                 headers={"Content-Type": "application/json", **({"Authorization": f"Bearer {jeton}"} if jeton else {})})
    try:
        with urllib.request.urlopen(req, timeout=120) as r:
            return json.loads(r.read() or b"{}")
    except urllib.error.HTTPError as e:
        sys.exit(f"Erreur {e.code} sur {chemin} : {e.read().decode(errors='replace')[:300]}")


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("classeur")
    ap.add_argument("--unite", type=float, default=1000)
    ap.add_argument("--api", default="http://localhost:5243/api/v1")
    ap.add_argument("--tel", default="+237670000002")
    ap.add_argument("--mdp", default="Demo2026!")
    ap.add_argument("--comptes", action="store_true")
    ap.add_argument("--membres", action="store_true")
    ap.add_argument("--dry-run", action="store_true")
    a = ap.parse_args()

    d = lire(a.classeur, a.unite)
    if a.membres:
        for m in d["membres"]:
            print(f"{m['telephone']}\t{m['nomCompte']}")
        return
    attendu = sum(m["palier"] for m in d["membres"]) * d["nombreEcheances"]
    total = sum(p["montant"] for p in d["paiements"])
    fcfa = lambda v: f"{v:,.0f}".replace(",", " ") + " FCFA"
    print(f"{d['association']} : {len(d['membres'])} membres, paliers {' / '.join(fcfa(x) for x in d['paliers'])}, "
          f"{d['nombreEcheances']} échéances depuis {d['debut']}, {len(d['paiements'])} paiements ({fcfa(total)}), "
          f"attendu total {fcfa(attendu)}")
    if a.dry_run:
        return

    jeton = appel(a.api, "POST", "/auth/login", {"telephone": a.tel, "motDePasse": a.mdp})["accessToken"]
    projet = appel(a.api, "POST", "/projets", {
        "nom": d["association"], "description": "Importé du classeur Excel de cotisations",
        "objectif": attendu, "paliers": d["paliers"], "periodicite": "Monthly",
        "dateDebut": d["debut"], "nombreEcheances": d["nombreEcheances"],
    }, jeton)
    appel(a.api, "POST", f"/projets/{projet['id']}/ouvrir", {}, jeton)
    r = appel(a.api, "POST", f"/projets/{projet['id']}/import", {
        "participants": [{k: m[k] for k in ("reference", "nom", "palier", "dateAdhesion", "sexe", "statut")}
                         | ({"telephone": m["telephone"]} if a.comptes else {}) for m in d["membres"]],
        "paiements": d["paiements"],
    }, jeton)
    p = r["projet"]
    print(f"Projet {p['id']} : {r['participants']} participants, {r['paiements']} paiements importés, "
          f"collecté {fcfa(p['totalCollecte'])} ({p['progression']} %), journal {'intègre' if p['journalValide'] else 'ALTÉRÉ'}")


if __name__ == "__main__":
    main()
