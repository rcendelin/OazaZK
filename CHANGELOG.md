# Changelog

Všechny podstatné změny portálu. Formát vychází z [Keep a Changelog](https://keepachangelog.com/cs/1.1.0/).
Záznam se doplňuje v každém PR, které dokončuje task z `TASK.md` (sekce **Nevydáno**). Při nasazení na PROD
se sekce přejmenuje na verzi s datem.

## [Nevydáno]

### Přidáno
- **Import bankovního výpisu (Fio CSV).** Stránka Import z banky: nahraný výpis se převede na zálohy a doplatky
  domácností, dům se určí podle čísla účtu (účty domů ve Správě domácností, nové se učí při importu), opakovaný
  import nic nezdvojí; platby mají štítek „Z banky“ (PR #4).
- **Alokační knihovna (X3).** `Oaza.Domain.Services.Allocator.Allocate(total, weights)` rozdělí částku podle vah
  na celé haléře metodou největšího zbytku; součet dílů se vždy rovná celku, shodné zbytky se řeší deterministicky
  podle pořadí, záporná částka (kredit) se rozpočítá zrcadlově. Základ pro všechny nové výpočty (T02–T08).
- **Changelog (X6).** Tento soubor.
- **Pruh s prostředím (T01).** Mimo produkci se nahoře zobrazuje „TESTOVACÍ / VÝVOJOVÉ PROSTŘEDÍ“; API hlásí prostředí
  přes `GET /api/environment` (app setting `Environment`), frontend dostává `VITE_ENVIRONMENT` z workflow a při
  nesouladu varuje.
- **Projektová dokumentace (T00).** README, `docs/ARCHITEKTURA.md`, `API.md`, `VYUCTOVANI.md`, `LOKALNI-VYVOJ.md` a nové
  `docs/architecture-notes.md` (rozcestník podle zadání), `docs/gap-analysis.md` (T01–T14: co je, co chybí)
  a `docs/open-questions.md` (rozhodnutí X1/X2/X7/T09, rizika, otázky O1–O4).
- **CI pro pull requesty (X6).** Nový workflow kontroluje každé PR: build API bez warningů (`TreatWarningsAsErrors`),
  testy, pokrytí výpočetní logiky ≥ 90 % (`api/coverage-gate.txt`, souhrn v PR) a lint + build webu.
- **Audit změn (X4).** Tabulka `AuditLog` (kdo, kdy, entita, stará a nová hodnota, důvod; jen přidávání), služba
  `IAuditLogger` pro use casy nového modelu, endpoint `GET /api/audit-log` a stránka Administrace → Audit změn.

### Opraveno
- **Nápověda k importu odečtů na stránce Vodoměry (X7 §8.9)** popisovala Excel obráceně; nově odpovídá parseru
  (vodoměry ve sloupci A, data v řádku 1).

## 2026-08-02 – Nápověda v UI

### Přidáno
- Stránka „Jak to funguje“ s návody a generovaným slovníkem pojmů.
- Kontextová nápověda (`HelpNote`, `HelpDisclosure`, `HelpTerm`) u vyúčtování, salda, fondu, importu, domácností,
  uživatelů, dokumentů a faktur; texty v jediném zdroji `web/src/content/help.ts`.
