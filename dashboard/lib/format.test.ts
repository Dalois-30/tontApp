import { describe, expect, it } from "vitest";
import {
  doitSurLeTour, formatMontant, libelleRolesBureau, libelleStatutMembre, peutGerer, peutValider, resteTourOuvert,
} from "./format";

/** Intl writes a narrow no-break space between thousands: compare with plain spaces. */
const texte = (s: string | undefined) => s?.replace(/\s/g, " ");

const COTISATION = 10000;

describe("formatMontant", () => {
  it("sépare les milliers et ajoute FCFA", () => {
    expect(texte(formatMontant(1500000))).toBe("1 500 000 FCFA");
  });
});

describe("libelleStatutMembre", () => {
  it("payé sans détail : libellé par défaut", () => {
    expect(libelleStatutMembre({ statut: "paye", resteTour: 0 })).toBeUndefined();
  });
  it("payé grâce à un report : « Payé · dont … reportés »", () => {
    expect(texte(libelleStatutMembre({ statut: "paye", resteTour: 0, reporteTour: 3000 })))
      .toBe("Payé · dont 3 000 FCFA reportés");
  });
  it("payé avec un excédent en attente", () => {
    expect(texte(libelleStatutMembre({ statut: "paye", enAttenteTour: 8000, enAttenteExcedentTour: 8000 })))
      .toBe("Payé · 8 000 FCFA à valider (dont 8 000 FCFA en excédent)");
  });
  it("payé partiellement : jamais un simple « Payé », avec le reste", () => {
    expect(texte(libelleStatutMembre({ statut: "paye_partiel", resteTour: 6000 }))).toBe("Payé partiellement · reste 6 000 FCFA");
  });
  it("payé partiellement avec une part à valider et un report", () => {
    expect(texte(libelleStatutMembre({ statut: "paye_partiel", resteTour: 2000, enAttenteTour: 4000, reporteTour: 1000 })))
      .toBe("Payé partiellement · reste 2 000 FCFA · dont 1 000 FCFA reportés · 4 000 FCFA à valider");
  });
  it("payé partiellement, reste couvert par l’attente", () => {
    expect(texte(libelleStatutMembre({ statut: "paye_partiel", resteTour: 0, enAttenteTour: 6000 })))
      .toBe("Payé partiellement · 6 000 FCFA à valider");
  });
  it("attente partielle : montant à valider et reste", () => {
    expect(texte(libelleStatutMembre({ statut: "en_attente", resteTour: 6000, enAttenteTour: 4000 })))
      .toBe("4 000 FCFA à valider · reste 6 000 FCFA");
  });
  it("attente au-delà de ce que le tour doit : excédent signalé", () => {
    expect(texte(libelleStatutMembre({ statut: "en_attente", resteTour: 0, enAttenteTour: 13000, enAttenteExcedentTour: 3000 })))
      .toBe("13 000 FCFA à valider (dont 3 000 FCFA en excédent)");
  });
  it("attente couvrant exactement le tour : libellé par défaut", () => {
    expect(libelleStatutMembre({ statut: "en_attente", resteTour: 0, enAttenteTour: COTISATION })).toBeUndefined();
  });
  it("non dû, à payer, en retard, bénéficiaire : libellé par défaut", () => {
    for (const statut of ["non_du", "a_payer", "en_retard", "beneficiaire"]) {
      expect(libelleStatutMembre({ statut, resteTour: COTISATION })).toBeUndefined();
    }
  });
});

describe("resteTourOuvert", () => {
  it("prend le reste du serveur quand il est fourni, même 0", () => {
    expect(resteTourOuvert({ statut: "paye_partiel", resteTour: 6000 }, COTISATION)).toBe(6000);
    expect(resteTourOuvert({ statut: "a_payer", resteTour: 0 }, COTISATION)).toBe(0);
  });
  it("repli ancienne API : à payer ou en retard doit la cotisation, les autres rien", () => {
    expect(resteTourOuvert({ statut: "a_payer" }, COTISATION)).toBe(COTISATION);
    expect(resteTourOuvert({ statut: "en_retard" }, COTISATION)).toBe(COTISATION);
    expect(resteTourOuvert({ statut: "paye" }, COTISATION)).toBe(0);
    expect(resteTourOuvert({ statut: "en_attente" }, COTISATION)).toBe(0);
  });
});

describe("doitSurLeTour", () => {
  it("doit tant qu’il reste quelque chose, dont un tour payé partiellement", () => {
    expect(doitSurLeTour({ statut: "paye_partiel", resteTour: 6000 }, COTISATION)).toBe(true);
    expect(doitSurLeTour({ statut: "en_attente", resteTour: 6000 }, COTISATION)).toBe(true);
    expect(doitSurLeTour({ statut: "en_retard" }, COTISATION)).toBe(true);
  });
  it("ne doit rien quand l’attente couvre le tour ou que tout est payé", () => {
    expect(doitSurLeTour({ statut: "en_attente", resteTour: 0 }, COTISATION)).toBe(false);
    expect(doitSurLeTour({ statut: "paye", resteTour: 0 }, COTISATION)).toBe(false);
  });
  it("bénéficiaire, non dû, suspendu, adhésion en attente : jamais relancés, quel que soit le reste", () => {
    for (const statut of ["beneficiaire", "non_du", "suspendu", "adhesion_en_attente"]) {
      expect(doitSurLeTour({ statut, resteTour: COTISATION }, COTISATION)).toBe(false);
    }
  });
});

describe("rôles du bureau", () => {
  const t = (mesRoles: string[] | null) => ({ mesRoles });

  it("peutGerer : responsable ou admin seulement", () => {
    expect(peutGerer(t(["responsable"]))).toBe(true);
    expect(peutGerer(t(["admin"]))).toBe(true);
    expect(peutGerer(t(["tresorier"]))).toBe(false);
    expect(peutGerer(t([]))).toBe(false);
    expect(peutGerer(t(null))).toBe(false);
    expect(peutGerer(undefined)).toBe(false);
    expect(peutGerer(null)).toBe(false);
  });

  it("peutValider : trésorier ou admin seulement", () => {
    expect(peutValider(t(["tresorier"]))).toBe(true);
    expect(peutValider(t(["admin"]))).toBe(true);
    expect(peutValider(t(["responsable"]))).toBe(false);
    expect(peutValider(t([]))).toBe(false);
    expect(peutValider(t(null))).toBe(false);
    expect(peutValider(undefined)).toBe(false);
  });

  it("cumul responsable et trésorier : les deux droits", () => {
    expect(peutGerer(t(["responsable", "tresorier"]))).toBe(true);
    expect(peutValider(t(["responsable", "tresorier"]))).toBe(true);
  });

  it("libelleRolesBureau", () => {
    expect(libelleRolesBureau(t(["responsable", "tresorier"]))).toBe("Responsable · Trésorier");
    expect(libelleRolesBureau(t(["tresorier", "responsable"]))).toBe("Responsable · Trésorier");
    expect(libelleRolesBureau(t(["tresorier"]))).toBe("Trésorier");
    expect(libelleRolesBureau(t(["admin"]))).toBe("Administrateur");
    expect(libelleRolesBureau(t(["admin", "responsable"]))).toBe("Responsable");
    expect(libelleRolesBureau(t([]))).toBe("");
    expect(libelleRolesBureau(undefined)).toBe("");
  });
});
