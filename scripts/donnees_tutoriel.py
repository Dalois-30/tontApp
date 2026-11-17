#!/usr/bin/env python3
"""Données du tutoriel filmé (docs/tutoriel), à lancer après `make seed CLASSEUR=…`, API lancée.

Crée « Tontine de la Jeunesse » entre cinq membres du classeur (tour 1 ouvert : un paiement complet,
un partiel, un trop-perçu reporté, une déclaration en attente), nomme BEMO Grace trésorière du projet
importé, ajoute le rapport de séance « Assemblée générale de novembre » et une déclaration en attente.
Ce ne sont pas des données du classeur : `make reset` puis `make seed` les retire.
"""
import json, os, urllib.request, urllib.error
A = os.environ.get("API", "http://localhost:5243/api/v1")
def call(m, p, b=None, t=None, ok=True):
    r = urllib.request.Request(A + p, method=m, data=None if b is None else json.dumps(b).encode(),
                               headers={"Content-Type": "application/json", **({"Authorization": "Bearer " + t} if t else {})})
    try:
        x = urllib.request.urlopen(r).read(); return json.loads(x) if x else {}
    except urllib.error.HTTPError as e:
        if ok: raise SystemExit(f"{m} {p} -> {e.code} {e.read()[:200]}")
        return {"HTTP": e.code}
tok = lambda tel: call("POST", "/auth/login", {"telephone": tel, "motDePasse": "Demo2026!"})["accessToken"]
G = tok("+237670000002")
membres = {n: tok(f"+2376900000{n:02d}") for n in (1, 3, 13, 16, 19)}  # Grace, Filomene, Agnes, Ruth, Dalois
# Tontine
t = call("POST", "/tontines", {"name": "Tontine de la Jeunesse", "description": "Épargne mensuelle des membres de l'association",
                                "contributionAmount": 10000, "periodicity": "Monthly", "maxMembers": 8}, G)
tid = t["id"]
code = call("GET", f"/tontines/{tid}/invitation/generer?nombreUsagesMax=10&expirationJours=30", t=G)["code"]
noms = {1: "Grace BEMO", 3: "Filomene BOUDJEKA", 13: "Agnes NGWOP", 16: "Ruth TENE MOMO", 19: "Dalois NGUENANG"}
for n, tk in membres.items():
    call("POST", "/tontines/rejoindre", {"code": code, "memberName": noms[n]}, tk)
call("POST", f"/tontines/{tid}/activate", {}, G)
d = call("GET", f"/tontines/{tid}/details", t=G)
tour = [x for x in d["tours"] if x["status"] == "Ouvert"][0]
benef = tour["beneficiaire"]["nom"]
mem = {m["nom"]: m for m in d["membres"]}
print("bénéficiaire du tour 1 :", benef)
payeurs = [n for n in (19, 13, 16, 3, 1) if noms[n] != benef]
# 1er : complet + validé ; 2e : partiel validé ; 3e : surplus validé (avance) ; 4e : déclaration en attente
montants = [10000, 4000, 18000, 10000]
for n, mt, valider in zip(payeurs, montants, [True, True, True, False]):
    r = call("POST", "/versements/manuel", {"tontineId": tid, "tourId": tour["id"], "montant": mt, "moyen": "Orange Money", "reference": f"OM-{n:02d}{mt}"}, membres[n])
    if valider: call("POST", f"/versements/{r['versementId']}/valider", {}, G)
print("tontine", tid, "code", code)
# Gouvernance du projet : Grace trésorière, séance
pid = call("GET", "/projets", t=G)[0]["id"]
gv = call("GET", f"/projets/{pid}/gouvernance", t=G)
grace = [m for m in gv["membres"] if m["nom"].startswith("BEMO")][0]["utilisateurId"]
call("PUT", f"/projets/{pid}/gouvernance/tresorier", {"utilisateurId": grace}, G)
call("POST", f"/projets/{pid}/seances", {"date": "2026-11-15", "titre": "Assemblée générale de novembre", "lieu": "Douala",
     "presents": ["BEMO Grace Merveille", "NGUENANG Dalois", "BOUDJEKA Filomene", "NGWOP Agnes Mariane"],
     "ordreDuJour": "Point financier ; retards ; achat de médicaments",
     "compteRendu": "La trésorière présente l'état des cotisations : 51 % de l'objectif collecté. Les retards sont passés en revue.",
     "decisions": ["Relancer les membres en retard de plus de 3 mois", "Achat de médicaments reporté à décembre"]}, membres[3])
# une déclaration de projet en attente pour la trésorière
call("POST", f"/projets/{pid}/paiements", {"montant": 30000, "moyen": "Orange Money", "preuve": "OM 7781204"}, membres[3])
print("projet", pid, "trésorière Grace nommée, séance rédigée, paiement en attente")
print(json.dumps({"tontine": tid, "projet": pid, "code": code, "beneficiaire": benef}))
