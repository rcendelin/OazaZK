# Import bankovního výpisu (Fio CSV) — návrh

**Datum:** 2026-09-27
**Autor:** brainstorming session (Rosťa Čendelín + Claude)
**Stav:** implementováno (větev `feature/bank-import`)

## 1. Kontext

Spolek má účet u Fio banky (`2601634649/2010`). Domácnosti posílají měsíční zálohy (dnes 1 500 Kč) a občas doplatky. Admin dnes každou platbu ručně přepisuje do portálu (`POST /advances`, `POST /advances/doplatek`) včetně rozpadu na složky voda / elektřina / společné.

Fio internetbanking umí exportovat výpis jako **CSV** (UTF-8) a **GPC** (ABO, cp1250). GPC ořezává jméno protistrany na 20 znaků a **neobsahuje „Zprávu pro příjemce"**, kam lidé píší označení domu — proto zdrojem je **CSV**.

Identifikace plátce je ve vzorovém výpisu (srpen 2026) nejednotná (příklady anonymizované):

| Zpráva pro příjemce | VS | Poznámka |
|---|---|---|
| `Testovi - RD2` | – | dům ve zprávě |
| `RD4`, `RD1`, `RD3`, `RD8` | – / `0` | dům ve zprávě |
| `RD5`, `26-07 doplatek RD5` | `05` | dům ve zprávě i ve VS |
| `SPOLEK …` | `7` | dům jen ve VS |
| jméno plátce | dlouhé číslo | nic použitelného (VS vypadá jako rodné číslo) |

Zpráva ani VS proto nejsou spolehlivé. **Dům se určuje podle čísla protiúčtu**: každá domácnost platí ze svého účtu (případně z několika). Účty domů spravuje admin a neznámý účet se uloží k domu při prvním ručním přiřazení.

## 2. Cíl

Admin nahraje měsíční CSV výpis z Fio, portál navrhne pro každou **příchozí** platbu dům, typ (záloha / doplatek), měsíc a rozpad na složky; admin návrh zkontroluje/upraví a potvrdí. Opakované nahrání téhož (nebo překrývajícího se) výpisu nic nezdvojí.

## 3. Rozsah

**V rozsahu (v1):**
- Parser Fio CSV.
- Automatické přiřazení k domu podle čísla protiúčtu.
- Tabulka účtů domů `BankAccountMappings`, spravovaná ve Správě domácností a doplňovaná při importu.
- Tabulka zpracovaných pohybů `BankTransactions` (idempotence, i pro ignorované).
- Vytěžení výpočtu předepsané zálohy z `AdvanceSettingsFunctions` do sdíleného use case (refaktoring).
- Endpointy `POST /bank-import/preview`, `POST /bank-import/confirm` a správa účtů domu (Admin).
- Stránka „Import z banky" (`/advances/import`).

**Mimo rozsah v1:**
- GPC formát, Fio API (automatické stahování), jiné banky.
- Odchozí platby (výdaje, vratky přeplatků) — v náhledu se zobrazí šedě jako „odchozí — neimportuje se".
- Příchozí platby mimo domy (úroky apod.) jako `FinancialRecord` Income — v v1 je admin označí „Ignorovat".
- Ukládání původního souboru do blobu, kontrola návaznosti stavů mezi výpisy.

## 4. Formát Fio CSV

- UTF-8 s BOM, řádky `\r\n` i samostatné `\r` (vzor obsahuje `\r\r\n`), oddělovač `;`, všechna pole v uvozovkách, `""` = escapovaná uvozovka.
- **Hlavička výpisu** (před tabulkou), každý řádek jedno pole:
  - `Výpis č. 8/2026 z účtu "2601634649/2010"` → číslo účtu
  - `Období: 01.08.2026 - 31.08.2026`
  - `Počáteční stav…`, `Koncový stav…`, `Suma příjmů: +12500 CZK`, `Suma výdajů: 0 CZK`
- **Tabulka** začíná řádkem, jehož první pole je `ID operace`. Sloupce (19) se mapují **podle indexu**, protože název `Poznámka` je dvakrát:

| # | Sloupec | Použití |
|---|---|---|
| 0 | ID operace | klíč idempotence |
| 1 | Datum | `dd.MM.yyyy` |
| 2 | Objem | `cs-CZ` decimal, **znaménko určuje směr** |
| 3 | Měna | jen `CZK`, jinak chyba řádku |
| 4 | Protiúčet | **klíč přiřazení domu**, vč. předčíslí (`107-2222222222`), neořezávat |
| 5 | Název protiúčtu | zobrazení |
| 6 | Kód banky | **klíč přiřazení domu** |
| 9 | VS | jen zobrazení |
| 11 | Poznámka | jen zobrazení |
| 12 | Zpráva pro příjemce | zobrazení, `Note` platby, hledá se v ní „doplat" |
| 13 | Typ | jen zobrazení |
| 16 | Poznámka (2.) | jen zobrazení |

- Parser: malý stavový parser uvozovkového CSV (CsvHelper v projektu není, nezavádíme). Detekce kódování: platné UTF-8 → UTF-8, jinak cp1250 (fallback pro starší exporty).
- **Kontrola výpisu:** Σ kladných `Objem` = „Suma příjmů" z hlavičky, Σ záporných = „Suma výdajů". Nesouhlas → varování (ne chyba).
- Limit velikosti souboru 1 MB. Soubor bez řádku `ID operace` → chyba „Soubor nevypadá jako CSV výpis z Fio banky".

## 5. Datový model

Číslo domu pro párování není potřeba — dům se určuje jen podle protiúčtu.

### 5.1 `BankAccountMapping` (nová tabulka `BankAccountMappings`)

| Pole | Hodnota |
|---|---|
| PartitionKey | `MAP` |
| RowKey | normalizovaný protiúčet `{předčíslí-}{číslo}_{kód banky}` (bez `/`, který Table Storage v klíči nepovoluje) |
| HouseId | GUID domu |
| AccountName | poslední „Název protiúčtu" (pro zobrazení) |
| UpdatedAt | |

Více účtů → jeden dům je běžné (manželé); jeden účet patří vždy jen jednomu domu. Admin účty spravuje u domu ve Správě domácností (`GET /bank-accounts`, `POST /bank-accounts` `{houseId, accountNumber}`, `DELETE /bank-accounts/{accountKey}`). Import je doplňuje: při potvrzení se ručně přiřazený účet uloží jako upsert, takže přeřazení účtu na jiný dům přepíše starou vazbu.

### 5.2 `BankTransaction` (nová tabulka `BankTransactions`)

| Pole | Hodnota |
|---|---|
| PartitionKey | číslo vlastního účtu (`2601634649_2010`) |
| RowKey | Fio `ID operace` |
| Date, Amount, CounterAccount, CounterName, Message, VariableSymbol | kopie z výpisu (audit) |
| Status | `Imported` / `Ignored` |
| HouseId, PaymentRowKey | odkaz na vytvořený `AdvancePayment` (jen `Imported`) |
| ImportedAt, ImportedBy | |

### 5.3 `AdvancePayment.BankOwnAccountKey` + `BankTransactionId` (nová pole, `string?`)

Zpětný odkaz → v historii plateb badge „Z banky". **Smazání platby** (`DELETE /advances/{houseId}/{rowKey}`) smaže i odpovídající `BankTransaction`, aby šel pohyb znovu naimportovat (např. po chybném přiřazení).

## 6. Algoritmus náhledu

Pro každý řádek výpisu:

1. `Objem < 0` → stav **Odchozí** (jen zobrazení, nelze importovat).
2. `ID operace` už v `BankTransactions` → stav **Již importováno** / **Ignorováno** (jen zobrazení, s odkazem na dům).
3. **Přiřazení domu podle protiúčtu.** Klíč `{předčíslí-}{číslo}_{kód banky}` se hledá v `BankAccountMappings`.
   - Nalezen → dům vyplněn (`MatchSource = Account`).
   - Nenalezen → řádek je **nepřiřazený** (`None`); admin vybere dům a po potvrzení se účet uloží k domu.
   - Zpráva a VS se jen zobrazí jako nápověda pro ruční výběr.
4. **Typ a měsíc** (měsíc = měsíc data platby, lze změnit):
   - Zpráva obsahuje `doplat` (case-insensitive) → **Doplatek**.
   - Jinak částka = předepsaná měsíční záloha domu (`actual.total`) **a** dům pro daný měsíc ještě zálohu nemá (ani v DB, ani na dřívějším řádku téhož importu) → **Záloha**.
   - Jinak → **Doplatek** + varování („Neobvyklá částka" / „Záloha za 08/2026 už existuje").
5. **Rozpad na složky:**
   - Záloha s předepsanou částkou → přesně předepsaný rozpad (`actual.water/electricity/common`).
   - Ostatní → poměrně podle předepsaného rozpadu, zaokrouhleno na koruny, zbytek do vody (součet vždy = částka). Admin může upravit.
   - Dům bez nastavené zálohy (celkem 0) → vše do vody.

Předepsaná záloha se počítá stejně jako `GET /advance-settings/calculate`. Výpočet se proto přesune z `AdvanceSettingsFunctions.CalculateAdvancesAsync` do `CalculatePrescribedAdvancesUseCase` (Application), kterou použijí endpoint i import. Chování endpointu se nemění.

## 7. API

Obě jen `Admin` (jako všechny zápisové `/advances` endpointy).

### `POST /bank-import/preview` (tělo požadavku = CSV soubor)

Nic neukládá. Vrací:

```jsonc
{
  "statement": { "account": "2601634649/2010", "number": "8/2026", "dateFrom": "2026-08-01", "dateTo": "2026-08-31",
                 "totalIncome": 12500, "totalExpense": 0, "sumCheckOk": true },
  "rows": [{
    "transactionId": "27812661716", "date": "2026-08-29", "amount": 500,
    "counterAccount": "8888888888/5500", "counterName": "Marie Ukázková",
    "message": "26-07 doplatek RD5", "variableSymbol": "05",
    "status": "New",            // New | AlreadyImported | Ignored | Outgoing | UnsupportedCurrency
    "houseId": "…", "matchSource": "Account",   // Account | None
    "type": "Doplatek", "year": 2026, "month": 8,
    "waterAmount": 350, "electricityAmount": 50, "commonAmount": 100,
    "warnings": []
  }],
  "warnings": []
}
```

### `POST /bank-import/confirm` (JSON)

**Bezstavové** — na rozdíl od importu odečtů nepoužívá `IImportSessionCache`. In-memory cache je na Consumption plánu mezi instancemi nespolehlivá a admin tu řádky navíc ručně upravuje, takže klient pošle konečná rozhodnutí:

```jsonc
{
  "account": "2601634649/2010",
  "rows": [{
    "transactionId": "…", "date": "…", "amount": 500,
    "counterAccount": "…", "counterName": "…", "message": "…", "variableSymbol": "…",
    "action": "Import",          // Import | Ignore
    "houseId": "…", "type": "Doplatek", "year": 2026, "month": 8,
    "waterAmount": 350, "electricityAmount": 50, "commonAmount": 100,
    "note": "26-07 doplatek RD5"
  }]
}
```

Server znovu validuje: `amount > 0`; `Import` vyžaduje existující aktivní dům a součet složek = `amount`; složky ≥ 0; `transactionId` ještě není v `BankTransactions` (jinak řádek přeskočí a vrátí v `skipped`); záloha nesmí kolidovat s existující zálohou domu v daném měsíci (→ chyba řádku, nic se neuloží). Validace proběhne celá **před** prvním zápisem.

Zápis po řádcích (Table Storage nemá transakce přes tabulky): nejdřív `AdvancePayment` (záloha RowKey `YYYY-MM`, doplatek `D-…` stejně jako stávající endpointy, `Note` = zpráva pro příjemce, `BankTransactionId`), pak `BankTransaction`, pak upsert `BankAccountMapping`. Když zápis spadne uprostřed, opakovaný import dokončí zbytek (pohyby bez `BankTransaction` jsou znovu `New`; záloha, která už existuje, se v náhledu nabídne jako doplatek s varováním).

Odpověď: `{ imported, ignored, skipped: [transactionId…] }`.

Zápis využije stávající logiku vytvoření zálohy/doplatku — vytáhnout ji z `AdvanceFunctions` do `RecordPaymentUseCase` (nebo ekvivalentu), aby RowKey a validace byly na jednom místě.

## 8. UI

- Nová stránka `web/src/pages/BankImportPage.tsx`, route `/advances/import` (Admin), odkaz „Import z banky" v sidebaru pod Zálohami a tlačítko na `AdvancesPage`. Vzorem je `ReadingsImportPage.tsx`.
- Krok 1: nahrání souboru (drag & drop) + nápověda „Fio internetbanking → Výpisy → Export CSV".
- Krok 2: souhrn výpisu (účet, období, příjmy, kontrola součtu) a tabulka řádků:
  - datum, částka, protistrana, zpráva, VS,
  - **dům** (select; u automaticky přiřazených badge „podle účtu"),
  - **typ** (Záloha / Doplatek), **měsíc** (u zálohy),
  - složky voda / elektřina / společné (editovatelné, kontrola součtu),
  - akce Importovat / Ignorovat.
  - Barvy: zelená = přiřazeno automaticky bez varování, žlutá = varování, červená = nepřiřazeno, šedá = odchozí / již zpracováno.
- „Potvrdit import" je aktivní, až je každý `New` řádek buď přiřazený k domu, nebo označený Ignorovat.
- Po potvrzení souhrn („Naimportováno 9, ignorováno 0") a odkaz na Zálohy / Saldo.
- `HousesPage` — seznam bankovních účtů domu (přidat / odebrat).
- `AdvancesPage` historie — badge „Z banky" u plateb s `BankTransactionId`.

## 9. Testy

- **Fixture:** anonymizovaná kopie vzorového CSV (vymyšlená jména a čísla účtů), zachovávající všechny zvláštnosti: BOM, `\r\r\n`, dvojí `Poznámka`, předčíslí účtu, VS `05`/`0`/`0000`/dlouhý VS, doplatek ve zprávě, dvě platby téhož plátce v jeden den. **Skutečný výpis se do repozitáře necommituje** (osobní údaje, VS připomínající rodné číslo).
- Parser: hlavička, mapování sloupců podle indexu, desetinná čárka, záporné částky, kontrola součtu, cp1250 fallback, nevalidní soubor.
- Přiřazení: známý a neznámý protiúčet, předčíslí v klíči, stejné číslo u jiné banky.
- Typ/rozpad: záloha vs. doplatek, druhá platba v témže měsíci, poměrný rozpad a zaokrouhlení.
- Confirm: idempotence (druhé potvrzení = `skipped`), kolize zálohy, validace součtu, uložení účtu k domu, přeřazení účtu na jiný dům, Ignore.
- Refaktoring předepsané zálohy: stávající testy `/advance-settings/calculate` beze změny výsledků.

## 10. Otevřené body

- Účty domů jde zadat předem ve Správě domácností; jinak je admin při prvním importu přiřadí ručně a od dalšího měsíce se přiřazují samy.

## 11. Odchylky implementace od návrhu

- Správa účtů je na `/bank-accounts` (ne `/houses/{id}/bank-accounts`); UI načte všechny účty jedním voláním.
- Preview přijímá soubor přímo jako tělo požadavku (`apiClient.uploadFile`), ne multipart.
- Preview navíc vrací `houses` (předepsaný rozpad) a `existingAdvanceMonths`, aby UI po ruční volbě domu přepočítalo návrh typu a rozpadu bez dalšího volání.
- Platba v jiné měně než CZK má stav `UnsupportedCurrency` a neimportuje se.
- Zápis zálohy/doplatku sdílí RowKey přes `PaymentRowKeys` (Domain) místo samostatného `RecordPaymentUseCase`.
- Tlačítko „Import z banky" je na stránce Saldo a platby (kde je seznam plateb) a v menu Hospodaření, ne na stránce Zálohy.
- Potvrzení vyžaduje existující domácnost (nemusí být aktivní); náhled ale automaticky přiřazuje jen aktivním domácnostem.
- Známé omezení: když se po zápisu platby nepovede zapsat `BankTransaction`, opakovaný import nabídne pohyb znovu. U zálohy to zachytí kontrola měsíce, u doplatku by vznikl druhý doplatek — admin ho musí smazat.
