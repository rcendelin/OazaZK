# Rozhodnutí, rizika a otevřené otázky (T00)

Stav k 27. 9. 2026.

## Rozhodnutí z 27. 9. 2026

| Id | Rozhodnutí | Dopad |
|---|---|---|
| **X1** | Platí pravidlo 7 zadání: `saldo > 0` = přeplatek (spolek dluží domu), `saldo < 0` = nedoplatek. | Opak dnešního kódu (`Settlement.Balance`, `TotalSaldo`, PDF, nápověda, negace na dashboardu člena). Nový ledger, mezizávěrky a exporty rovnou v nové konvenci; starý výpočet se nepřevrací, zanikne. |
| **X2** | Jen nový výpočetní model (`CostComponent` / `ComponentAllocationRule` / `Participation` / `CostEntry` / `OpeningBalance` / `InterimClosing` / ledger). Produkční data neexistují, nic se nemigruje. | Pravidlo 2 (zpětná kompatibilita) se pro výpočetní model neuplatní; testovací instance se po nasazení přeseeduje. Starý kód se odstraní až po zprovoznění náhrady. |
| **X7** | Import bankovního výpisu (Fio CSV) dokončí samostatná session mimo zadání. | Hotovo v PR #4 (tabulky `BankTransactions`, `BankAccountMappings`). T09 `bankTransactionRef` se páruje na `BankTransactions`, T06 `paidFrom = BANK` zatím jen evidenčně. |
| **T09** | Pokladnu čtou členové, zapisují **Admin a Accountant**; nová role se nezavádí. | `[RequireRole(Admin, Accountant)]` na zápisových endpointech. |
| **X5** | Varianta B z `docs/superpowers/specs/2026-09-27-x5-kalendarni-data-design.md`: nový model (T02–T10) používá `DateOnly` + `DateRange` (uzavřený interval po dnech), „dnes“ vždy z `IClock` v `Europe/Prague` — i pro uživatele mimo ČR; starý model se jen opraví (D1–D3). | Implementace kroků 1–3 návrhu před T02. |

## Výklad zadání (bez dopadu na to, co uživatel vidí)

| Kde | Výklad |
|---|---|
| T02 `RATIO` | `ComponentAllocationRule.RatioSource` vyplněné = váhy se berou dynamicky (např. spotřeba podle odečtů), dopočítá je rozpočet v T05/T06. Nevyplněné = statická `Participation.Weight`. T02 hodnoty jen ukládá a vrací. |
| T02 `METERED` | Metoda i základ `METERED` se v T02 jen evidují; rozpočet podle odečtů je součástí T05/T06. |
| T02 zákaz změn za mezizávěrkou | Kontrola `ComponentValidation.CheckNotClosed` je hotová; poslední uzavřený den dodá T08 (`InterimClosing`). Do té doby se předává „nic není uzavřeno“. |
| T02 `PERCENT` = 100 % v každém dni | Účastníci se uvnitř úseku nemění, kontrola proto běží po úsecích a hlásí první den úseku a skutečný součet. Úsek bez účastníků pod pravidlem `PERCENT` je chyba (součet 0 %). |

## Rizika

| Riziko | Proč | Zmírnění |
|---|---|---|
| **Neatomické zápisy do Table Storage** | Table Storage nemá transakce přes tabulky ani přes partition; víceřádkové operace (potvrzení importu, uzavření období) mohou skončit napůl. | Validovat vše před prvním zápisem, idempotentní klíče (deterministické RowKey), pořadí zápisů tak, aby opakování dokončilo zbytek (vzor: import bankovního výpisu). Nové entity navrhovat tak, aby jedna operace = jedna partition, kde to jde (batch transakce). |
| ~~**In-memory session importu odečtů**~~ (vyřešeno X7) | `InMemoryImportSessionCache` na Consumption plánu nepřežije restart ani jinou instanci — potvrzení může selhat „relace vypršela“. | X7: bezstavové potvrzení (klient posílá výsledek náhledu, server znovu validuje), jako u importu z banky. |
| **PROD nikdy neběžel** | Deploy z `master` zatím selhává (chybí hand-off: RBAC service principalu, secrety); nikdo neověřil konfiguraci ani výkon. | T01 hand-off, T14 release checklist a smoke test, app setting `Environment=prod`. |
| **Build lokálně jen přes jiné SDK** | `global.json` pinuje .NET 8 SDK; vývojová stanice s jiným SDK buildí mimo pin. | CI na .NET 8 je směrodatné; X6 build bez warningů. |

## Otevřené otázky

Znění podle zadání (`docs/ZADANI.md` §2.2). Všechny se implementují jako konfigurovatelné s auditní stopou (pravidlo 8).

| Id | Otázka | Požadavek na implementaci | Default | Souvisí | Stav |
|---|---|---|---|---|---|
| O1 | Ztráty vody lineárně, nebo poměrově? Rozhodne hlasování. | Přepínač per složka, efektivně datovaný, s auditem. | `EQUAL` (návrh Jindry) | T02, T05 | otevřená |
| O2 | Jak rozdělit přeplatek za elektřinu vodárny (cca 20 tis. Kč)? | Kredit složky se zadá jako počáteční stav. Metodu i okruh domů lze nastavit. | 4 domy, `EQUAL` | T03 | otevřená |
| O3 | Elektřina vodárny podle plateb, nebo podle elektroměru? | Složka podporuje obojí. Pro vodárnu se nastaví „podle nákladových záznamů“ (zálohy + vyúčtování). | podle záznamů | T02, T06 | otevřená |
| O4 | Oddělený fond: právní a daňové řešení | Feature flag, default vypnuto. | `OFF` | T10 | otevřená |
| O5 | Členské příspěvky vs. odpracování | **Neimplementovat** (zadání §5). | – | – | mimo rozsah |
