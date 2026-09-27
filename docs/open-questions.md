# Rozhodnutí, rizika a otevřené otázky (T00)

Stav k 27. 9. 2026.

## Rozhodnutí z 27. 9. 2026

| Id | Rozhodnutí | Dopad |
|---|---|---|
| **X1** | Platí pravidlo 7 zadání: `saldo > 0` = přeplatek (spolek dluží domu), `saldo < 0` = nedoplatek. | Opak dnešního kódu (`Settlement.Balance`, `TotalSaldo`, PDF, nápověda, negace na dashboardu člena). Nový ledger, mezizávěrky a exporty rovnou v nové konvenci; starý výpočet se nepřevrací, zanikne. |
| **X2** | Jen nový výpočetní model (`CostComponent` / `ComponentAllocationRule` / `Participation` / `CostEntry` / `OpeningBalance` / `InterimClosing` / ledger). Produkční data neexistují, nic se nemigruje. | Pravidlo 2 (zpětná kompatibilita) se pro výpočetní model neuplatní; testovací instance se po nasazení přeseeduje. Starý kód se odstraní až po zprovoznění náhrady. |
| **X7** | Import bankovního výpisu (Fio CSV) dokončí samostatná session mimo zadání. | Hotovo v PR #4 (tabulky `BankTransactions`, `BankAccountMappings`). T09 `bankTransactionRef` se páruje na `BankTransactions`, T06 `paidFrom = BANK` zatím jen evidenčně. |
| **T09** | Pokladnu čtou členové, zapisují **Admin a Accountant**; nová role se nezavádí. | `[RequireRole(Admin, Accountant)]` na zápisových endpointech. |

## Rizika

| Riziko | Proč | Zmírnění |
|---|---|---|
| **Neatomické zápisy do Table Storage** | Table Storage nemá transakce přes tabulky ani přes partition; víceřádkové operace (potvrzení importu, uzavření období) mohou skončit napůl. | Validovat vše před prvním zápisem, idempotentní klíče (deterministické RowKey), pořadí zápisů tak, aby opakování dokončilo zbytek (vzor: import bankovního výpisu). Nové entity navrhovat tak, aby jedna operace = jedna partition, kde to jde (batch transakce). |
| **In-memory session importu odečtů** | `InMemoryImportSessionCache` na Consumption plánu nepřežije restart ani jinou instanci — potvrzení může selhat „relace vypršela“. | X7: bezstavové potvrzení (klient posílá výsledek náhledu, server znovu validuje), jako u importu z banky. |
| **PROD nikdy neběžel** | Deploy z `master` zatím selhává (chybí hand-off: RBAC service principalu, secrety); nikdo neověřil konfiguraci ani výkon. | T01 hand-off, T14 release checklist a smoke test, app setting `Environment=prod`. |
| **Build lokálně jen přes jiné SDK** | `global.json` pinuje .NET 8 SDK; vývojová stanice s jiným SDK buildí mimo pin. | CI na .NET 8 je směrodatné; X6 build bez warningů. |

## Otevřené otázky

Znění otázek O1–O4 je v zadání („Portál Oáza – zadání tasků pro Claude Code“, setkání 27. 9. 2026), které
v repozitáři není. Doplnit doslovně ze zadání spolu s odpovědí, až padne.

| Id | Otázka | Souvisí | Stav |
|---|---|---|---|
| O1 | *(doplnit ze zadání)* | | otevřená |
| O2 | *(doplnit ze zadání)* | | otevřená |
| O3 | *(doplnit ze zadání)* — souvisí se skutečnými náklady elektřiny a společných (`CostEntry`), které dnes nahrazují jen rozpočtové sazby | T06 | otevřená |
| O4 | *(doplnit ze zadání)* | | otevřená |
