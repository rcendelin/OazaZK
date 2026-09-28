# Import počátečních dat (T13)

Šablony v `templates/` se vyplní ostrými daty a nahrají v aplikaci: **Správa → Import počátečních dat** (jen Admin).
Import vždy nejdřív proběhne „nanečisto“ nad kopií dat. Report ukáže, co vznikne, co už existuje beze změny,
konflikty, chyby a salda domů k dnešku. Report jde stáhnout jako Markdown nebo XLSX pro kontrolu před ostrým
spuštěním. Tlačítko **Zapsat** zapíše data jen tehdy, když zkouška nemá žádnou chybu ani konflikt.

- **Opakovaný import nic nezdvojí.** Řádky se párují podle přirozených klíčů (tabulka níže). Řádek, který už existuje
  se stejnými hodnotami, se přeskočí.
- **Existující záznam se nikdy nepřepíše.** Liší-li se hodnoty, je to **konflikt** a import se nezapíše. Opravte buď
  CSV, nebo záznam v aplikaci.
- **Stejná pravidla jako v aplikaci.** Import jde přes stejné kontroly jako ruční zadání: překryvy, PERCENT = 100 %,
  začátek účtování složky, mezizávěrky. Každý zápis je v auditu.
- **Vzorová data v `samples/`** jsou fiktivní scénáře S2 a S3 ze zadání. Testy (`SeedImportUseCaseTests`) na nich
  ověřují salda.
- **Demo data v `demo/`** jsou realistická, ale fiktivní data pro školení na TEST: 8 domů, převod domu 6 k 15. 3. 2025,
  kredit vodárny se zálohami, vyúčtování osvětlení, pojištění a údržba, změna metody ztrát od 2026. Odečty vody
  k nim nejsou, ty se na školení zadají přes Import odečtů. Test hlídá, že demo data jdou naimportovat bez chyb.

## Formát

- **Oddělovač:** `;` (nebo `,`), hodnoty lze dát do uvozovek.
- **Kódování:** UTF-8 (i s BOM) nebo Windows-1250 z českého Excelu.
- **Řádky:** první řádek je hlavička se sloupci podle šablony. Prázdné řádky a řádky začínající `#` se přeskočí.
- **Datum:** `RRRR-MM-DD` nebo `D.M.RRRR`.
- **Čísla:** desetinná čárka i tečka, mezery v tisících se ignorují.
- **Ano/ne:** `ano`/`ne` (i `true`/`false`, `1`/`0`).
- **Soubory jsou nepovinné.** Import zpracuje jen ty nahrané, v pořadí tabulky níže. Soubor se smí odkazovat jen na
  soubory nad sebou nebo na data, která už v aplikaci jsou.

## Soubory

| Soubor | Klíč (párování) | Sloupce |
|---|---|---|
| `houses.csv` | `name` | `name`, `address`, `contact_person`, `email`, `active` (výchozí ano) |
| `ownership_periods.csv` | `house` + `valid_from` | `house` (název domu), `owner_name`, `contact`, `valid_from`, `valid_to` (prázdné = trvá) — období domu se nesmí překrývat |
| `meters.csv` | `meter_number` | `meter_number`, `name`, `type` (`hlavni` / `domovni`), `house` (u hlavního prázdné), `installation_date`, `radio_address` |
| `components.csv` | `code` | `code` (A–Z, 0–9, `_`), `name`, `start_date`, `allocation_basis` (`naklady` / `odecty`), `water_role` (`spotreba` / `ztraty` / prázdné), `method` (`rovne` / `pomer` / `procenta` / `odecty`, prázdné = výchozí), `ratio_source` (kód složky, podle jejíž spotřeby se dělí, jen u složky podle odečtů), `note` |
| `allocation_rules.csv` | `component` + `valid_from` | změna metody od data: `component` (kód), `valid_from`, `method`, `ratio_source`, `reason` (povinný). První pravidlo vzniká ze `components.csv`. |
| `participations.csv` | `component` + `house` + `valid_from` | `component`, `house`, `valid_from`, `valid_to`, `weight` (u `procenta` procenta, u `pomer` bez `ratio_source` váha), `reason`. Nové účasti jedné složky se kontrolují dohromady (součet 100 %). |
| `opening_meter_readings.csv` | `meter_number` + `date` | `meter_number`, `date`, `value` (m³, max. 3 desetinná místa), `is_estimate`, `source` (povinný — odkud hodnota je), `note`. Zapíše se i jako odečet. |
| `opening_fund_shares.csv` | `house` + `date` | `house`, `date`, `value` (Kč; **kladné = přeplatek**, X1), `source`, `note`. Patří do období vlastnictví platného k `date`. |
| `component_credits.csv` | `component` | `component`, `date`, `value` (Kč; kredit u dodavatele je **záporný**), `source`, `note` |
| `cost_entries.csv` | `ref` | `ref` (vlastní jedinečné označení, např. `PRE-2024-01`), `component`, `type` (`zaloha` / `vyuctovani` / `jednorazovy`), `period_from`, `period_to`, `amount` (Kč), `quantity_m3` (jen u složky podle odečtů — faktura PVK), `supplier`, `paid_from` (`banka` / `kredit` / `pokladna` / `jine`, výchozí banka), `note` |

Místo českých hodnot lze psát i anglické názvy z kódu (`Main`, `Equal`, `Advance`, `SupplierCredit` …).

## Postup před ostrým spuštěním

1. Vyplnit šablony a nahrát je na **testovací** instanci. Opravit chyby a konflikty.
2. Stáhnout report (XLSX/MD) a nechat ho zkontrolovat (Jindra, Radka): salda domů, kontrolu složek.
3. Na produkci nahrát stejné soubory, zkontrolovat report a zapsat.
4. Zapsat ještě jednou: report musí ukázat „Není co zapsat — vše už existuje“.
