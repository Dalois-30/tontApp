import { describe, expect, it } from "vitest";
import { MESSAGE_HORS_BUREAU } from "./format";
import { avecTontine, echecDuStatut, ecranEchec, requeteTontine, tontineDemandee } from "./tontine";

const ID = "3f2a1b4c-5d6e-4f70-8a9b-0c1d2e3f4a5b";

describe("tontineDemandee", () => {
  it("aucune tontine demandée : null (tontine par défaut)", () => {
    expect(tontineDemandee(undefined)).toBeNull();
    expect(tontineDemandee(null)).toBeNull();
    expect(tontineDemandee("")).toBeNull();
  });
  it("un id valide est gardé, le premier d’un paramètre répété", () => {
    expect(tontineDemandee(ID)).toBe(ID);
    expect(tontineDemandee(ID.toUpperCase())).toBe(ID.toUpperCase());
    expect(tontineDemandee([ID, "autre"])).toBe(ID);
  });
  it("un id malformé est « invalide », jamais remplacé par la tontine par défaut", () => {
    expect(tontineDemandee("abc")).toBe("invalide");
    expect(tontineDemandee(`${ID}x`)).toBe("invalide");
    expect(tontineDemandee("1; DROP TABLE")).toBe("invalide");
  });
});

describe("avecTontine / requeteTontine", () => {
  it("garde la tontine choisie dans les liens", () => {
    expect(avecTontine("/paiements", ID)).toBe(`/paiements?tontine=${ID}`);
    expect(avecTontine("/paiements?page=2", ID)).toBe(`/paiements?page=2&tontine=${ID}`);
  });
  it("lien inchangé sans tontine choisie", () => {
    expect(avecTontine("/paiements", null)).toBe("/paiements");
    expect(avecTontine("/paiements", undefined)).toBe("/paiements");
  });
  it("paramètre des proxies", () => {
    expect(requeteTontine(ID)).toBe(`&tontineId=${ID}`);
    expect(requeteTontine(null)).toBe("");
  });
});

describe("echecDuStatut", () => {
  it("403 → hors bureau, 404 → aucune, le reste → erreur", () => {
    expect(echecDuStatut(403)).toBe("hors_bureau");
    expect(echecDuStatut(404)).toBe("aucune");
    expect(echecDuStatut(500)).toBe("erreur");
    expect(echecDuStatut(0)).toBe("erreur");
    expect(echecDuStatut(401)).toBe("erreur");
  });
});

describe("ecranEchec (écrans hors bureau)", () => {
  it("hors bureau sans tontine choisie : message de l’espace réservé au bureau", () => {
    expect(ecranEchec("hors_bureau", false, "x")).toEqual(["shield", "Vous n'êtes au bureau d'aucune tontine", MESSAGE_HORS_BUREAU]);
  });
  it("hors bureau de la tontine choisie : invite à en choisir une autre", () => {
    const [icone, titre] = ecranEchec("hors_bureau", true, "x");
    expect(icone).toBe("shield");
    expect(titre).toBe("Accès réservé au bureau de cette tontine");
  });
  it("aucune tontine : invite à la créer depuis le mobile, avec le texte de l’écran", () => {
    const [icone, titre, texte] = ecranEchec("aucune", false, "son journal apparaîtra ici");
    expect([icone, titre]).toEqual(["users", "Aucune tontine pour le moment"]);
    expect(texte).toContain("son journal apparaîtra ici");
  });
  it("tontine choisie introuvable", () => {
    expect(ecranEchec("aucune", true, "x").slice(0, 2)).toEqual(["alert", "Tontine introuvable"]);
  });
  it("erreur serveur", () => {
    expect(ecranEchec("erreur", false, "x")[1]).toBe("Impossible de charger la tontine");
    expect(ecranEchec("erreur", true, "x")[1]).toBe("Impossible de charger la tontine");
  });
});
