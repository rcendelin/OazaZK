# Changelog

Všechny podstatné změny portálu. Formát vychází z [Keep a Changelog](https://keepachangelog.com/cs/1.1.0/).
Záznam se doplňuje v každém PR, které dokončuje task z `TASK.md` (sekce **Nevydáno**). Při nasazení na PROD
se sekce přejmenuje na verzi s datem.

## [Nevydáno]

### Přidáno
- **Alokační knihovna (X3).** `Oaza.Domain.Services.Allocator.Allocate(total, weights)` rozdělí částku podle vah
  na celé haléře metodou největšího zbytku; součet dílů se vždy rovná celku, shodné zbytky se řeší deterministicky
  podle pořadí, záporná částka (kredit) se rozpočítá zrcadlově. Základ pro všechny nové výpočty (T02–T08).
- **Changelog (X6).** Tento soubor.
- **Pruh s prostředím (T01).** Mimo produkci se nahoře zobrazuje „TESTOVACÍ / VÝVOJOVÉ PROSTŘEDÍ“; API hlásí prostředí
  přes `GET /api/environment` (app setting `Environment`), frontend dostává `VITE_ENVIRONMENT` z workflow a při
  nesouladu varuje.

### Opraveno
- **Nápověda k importu odečtů na stránce Vodoměry (X7 §8.9)** popisovala Excel obráceně; nově odpovídá parseru
  (vodoměry ve sloupci A, data v řádku 1).

## 2026-08-02 – Nápověda v UI

### Přidáno
- Stránka „Jak to funguje“ s návody a generovaným slovníkem pojmů.
- Kontextová nápověda (`HelpNote`, `HelpDisclosure`, `HelpTerm`) u vyúčtování, salda, fondu, importu, domácností,
  uživatelů, dokumentů a faktur; texty v jediném zdroji `web/src/content/help.ts`.
