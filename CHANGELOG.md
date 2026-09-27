# Changelog

Všechny podstatné změny portálu. Formát vychází z [Keep a Changelog](https://keepachangelog.com/cs/1.1.0/).
Záznam se doplňuje v každém PR, které dokončuje task ze zadání `docs/ZADANI.md` (sekce **Nevydáno**). Při nasazení na PROD
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
- **E2E smoke testy (X6).** Playwright v CI ověřuje v prohlížeči pruh s prostředím a průchod importem z banky
  (s podvrženým API, bez backendu).
- **Property-based testy (X6).** FsCheck ověřuje invarianty alokační knihovny na náhodných částkách a vahách
  (součet = celek, díly na haléře, nulová váha nic nedostane, záporná částka zrcadlově).
- **Odhad odečtu (T04, část).** Odečet může být označený jako odhad s popisem, jak vznikl (v přehledech „≈“ s nápovědou);
  `GET /api/readings/estimate` dopočítá stav vodoměru k datu interpolací po dnech mezi odečty, případně vezme
  nejbližší odečet, když existuje jen z jedné strany.
- **Zadání v repozitáři.** `docs/ZADANI.md` (setkání 27. 9. 2026) včetně scénářů S1–S8; `docs/open-questions.md`
  doplněno o znění O1–O5 a rozhodnutí X5 (varianta B: `DateOnly` v novém modelu, „dnes“ v `Europe/Prague`).
- **Kalendářní dny v pražském čase (X5).** `IClock`/`PragueClock` („dnes“ = den v `Europe/Prague`) a `DateRange`
  (uzavřený interval po dnech: počet dnů, průnik, řez pro mezizávěrky) jako základ nového modelu T02–T10.
- **Nákladové složky – doménový model (T02, část A).** Entity `CostComponent`, `ComponentAllocationRule` a `Participation`
  s efektivním datováním, výpočet úseků, ve kterých je rozpočet konstantní (`AllocationSegments`), a validace:
  nepřekrývání účasti a pravidel, `PERCENT` = 100 % v každém dni (hláška s datem a součtem), zákaz změny za mezizávěrkou.
- **Nákladové složky – API (T02, část B).** Tabulky `CostComponents`, `ComponentAllocationRules`, `Participations`
  a endpointy `/cost-components…`: založení složky (s prvním pravidlem), změna metody od data s povinným důvodem,
  přidání, ukončení a smazání účasti, výpis úseků. Čtou Admin a Accountant, zapisuje jen Admin, každá změna jde do auditu.

### Opraveno
- **„Dnes“ ve formulářích a PDF** už v noci mezi půlnocí a 1:00 (v létě 2:00) nenabízí včerejšek: výchozí datum
  importu odečtů, plateb na stránce Saldo, filtru auditu a grafu spotřeby, kontrola „datum není v budoucnosti“
  a datum vystavení PDF se berou z pražského kalendáře.
- **Potvrzení importu odečtů (X7 §8.6/§8.7)** už nezávisí na paměti serveru (dřív mohlo skončit „relace vypršela“,
  když požadavek obsloužila jiná instance) a neukládá napůl: celá dávka se ověří předem a opakované potvrzení po
  výpadku dokončí zbytek.
- **Nápověda k importu odečtů na stránce Vodoměry (X7 §8.9)** popisovala Excel obráceně; nově odpovídá parseru
  (vodoměry ve sloupci A, data v řádku 1).

## 2026-08-02 – Nápověda v UI

### Přidáno
- Stránka „Jak to funguje“ s návody a generovaným slovníkem pojmů.
- Kontextová nápověda (`HelpNote`, `HelpDisclosure`, `HelpTerm`) u vyúčtování, salda, fondu, importu, domácností,
  uživatelů, dokumentů a faktur; texty v jediném zdroji `web/src/content/help.ts`.
