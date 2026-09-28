# Rozhodnutí, rizika a otevřené otázky (T00)

Stav k 27. 9. 2026 (O1–O4 aktualizováno po dokončení T01–T14).

## Rozhodnutí z 27. 9. 2026

| Id | Rozhodnutí | Dopad |
|---|---|---|
| **X1** | Platí pravidlo 7 zadání: `saldo > 0` = přeplatek (spolek dluží domu), `saldo < 0` = nedoplatek. | Opak dnešního kódu (`Settlement.Balance`, `TotalSaldo`, PDF, nápověda, negace na dashboardu člena). Nový ledger, mezizávěrky a exporty rovnou v nové konvenci; starý výpočet se nepřevrací, zanikne. |
| **X2** | Jen nový výpočetní model (`CostComponent` / `ComponentAllocationRule` / `Participation` / `CostEntry` / `OpeningBalance` / `InterimClosing` / ledger). Produkční data neexistují, nic se nemigruje. | Pravidlo 2 (zpětná kompatibilita) se pro výpočetní model neuplatní; testovací instance se po nasazení přeseeduje. Starý kód se odstraní až po zprovoznění náhrady. |
| **X2 odstranění** | Starý model se odstraní hned (zúčtovací období, vyúčtování + PDF, faktury za vodu, přehled faktur, staré saldo, ceník záloh); platby domů zůstávají. Doporučená záloha = náklady domu za posledních 12 měsíců ÷ 12 (voda + ztráty / složky s kódem `ELEKTRINA…` / ostatní), ruční přepis zůstává. | Rozhodnuto 27. 9. 2026; `CalculatePrescribedAdvancesUseCase` nad `LedgerCostCollector`, stránka „Saldo a platby“ → „Platby“, dashboard ukazuje nové saldo. |
| **X7** | Import bankovního výpisu (Fio CSV) dokončí samostatná session mimo zadání. | Hotovo v PR #4 (tabulky `BankTransactions`, `BankAccountMappings`). T09 `bankTransactionRef` se páruje na `BankTransactions`, T06 `paidFrom = BANK` zatím jen evidenčně. |
| **T09** | Pokladnu čtou členové, zapisují **Admin a Accountant**; nová role se nezavádí. | `[RequireRole(Admin, Accountant)]` na zápisových endpointech. |
| **Cena vody (T05/T06)** | Cena PVK za m³ se bere z faktur: Σ částek faktur PVK ÷ Σ fakturovaných m³ za období. Faktury PVK jsou nákladové záznamy složky Voda PVK s povinným množstvím v m³. | Náklad domů pak sedí na faktury; rozdíl proti odečtům jde do ztrát. |
| **Období ztrát (T05)** | Ztráty se počítají za každý interval mezi dvěma odečty hlavního vodoměru a rozpočítávají se v úsecích pro-rata podle dní. | Záporná ztráta v intervalu se nerozpočítá a zobrazí se varování. |
| **T08 vyúčtování přes řez** | Náklad, jehož období zasahuje do uzavřeného období, se rozdělí podle původních úseků (kdo se kdy účastnil), ale do salda se zaúčtuje k prvnímu dni po mezizávěrce jako opravný záznam s povinným důvodem (`CostEntry.PostingDate`). Uzavřené saldo se nemění. | Rozhodnuto 27. 9. 2026. |
| **T08 zámek plateb** | Mezizávěrka zamyká i platby domů s datem do řezu (úprava, smazání); pozdně zapsaná platba se zaúčtuje k datu po řezu. | Rozhodnuto 27. 9. 2026; `ClosedPeriodPayments` (endpointy plateb i bankovní import). |
| **X5** | Varianta B z `docs/superpowers/specs/2026-09-27-x5-kalendarni-data-design.md`: nový model (T02–T10) používá `DateOnly` + `DateRange` (uzavřený interval po dnech), „dnes“ vždy z `IClock` v `Europe/Prague` — i pro uživatele mimo ČR; starý model se jen opraví (D1–D3). | Implementace kroků 1–3 návrhu před T02. |

## Výklad zadání (bez dopadu na to, co uživatel vidí)

| Kde | Výklad |
|---|---|
| T02 `RATIO` | `ComponentAllocationRule.RatioSource` vyplněné = váhy se berou dynamicky (např. spotřeba podle odečtů), dopočítá je rozpočet v T05/T06. Nevyplněné = statická `Participation.Weight`. T02 hodnoty jen ukládá a vrací. |
| T02 `METERED` | Metoda i základ `METERED` se v T02 jen evidují; rozpočet podle odečtů je součástí T05/T06. |
| T02 zákaz změn za mezizávěrkou | Kontrola `ComponentValidation.CheckNotClosed` je hotová; poslední uzavřený den dodá T08 (`InterimClosing`). Do té doby se předává „nic není uzavřeno“. |
| T02 `PERCENT` = 100 % v každém dni | Účastníci se uvnitř úseku nemění, kontrola proto běží po úsecích a hlásí první den úseku a skutečný součet. Úsek bez účastníků pod pravidlem `PERCENT` je chyba (součet 0 %). |
| T02 změna vah u `PERCENT` | API mění účast po jednom záznamu a každý krok se validuje, takže přerozdělení procent k datu (např. přidání domu) jednotlivými kroky neprojde. Seed konfigurace `PERCENT` nepoužívá; pokud bude potřeba, doplní se hromadná operace „nové váhy od data“. |

| Zaokrouhlení po měsících (#16) | Haléře se zaokrouhlují jednou za celý nákladový záznam (přesný podíl domu za celé období → největší zbytek), pak se podíl domu rozpadne do úseků a měsíců. Rozhodnuto 28. 9. 2026 (`CostEntryAllocation`). |
| T13 podoba importu | Admin stránka v aplikaci (rozhodnuto 27. 9. 2026), ne CLI — přístup k produkčnímu storage nikdy neopouští Azure. `/seed-import` není za `ENABLE_SEED`. |
| T13 existující záznamy | Řádek se stejným přirozeným klíčem a stejnými hodnotami se přeskočí; s jinými hodnotami je to konflikt, který zablokuje zápis celého importu (nic se nepřepisuje, oprava v CSV nebo v aplikaci). |
| T13 soubory navíc | K šablonám ze zadání přibyl `allocation_rules.csv` (změny metody od data); první pravidlo složky je ve sloupcích `method`/`ratio_source` v `components.csv`. Náklady mají povinný sloupec `ref` (přirozený klíč, `CostEntry.ExternalRef`). |
| T13 náklady v uzavřeném období | Import nezadává důvod, proto náklad zasahující do mezizávěrky skončí chybou — ostrá data se importují před první mezizávěrkou. |
| T03 první období vlastnictví | Produkční data nejsou (X2), proto se období nemigrují: akce „Start účtování k datu“ založí každému aktivnímu domu bez období jedno období od data startu (vlastník = kontaktní osoba domu). Opakované spuštění nic nezmění. |
| T03 stav vodoměru | Počáteční stav vodoměru je zároveň skutečný odečet k datu (s příznakem odhadu a poznámkou se zdrojem), aby ho všechny výpočty spotřeby viděly bez zvláštního případu. Existující odečet se stejnou hodnotou se jen použije, jiný se nepřepíše. |
| T03 znaménko `FundShare` | Podle X1: kladné = dům má u spolku přeplatek. Starý počáteční zůstatek na stránce Saldo (`PaymentType.OpeningBalance`) má opačné znaménko a zůstává do náhrady ledgerem (T07). |
| T03 „opravný záznam“ | Po mezizávěrce se počáteční stav opravuje úpravou s povinným důvodem, v auditu jako `Correction`. Jedinečnost (jeden stav na kombinaci) tím zůstává zachovaná. |
| T06 náklady před startem | Náklad, jehož období začíná před začátkem účtování složky, se odmítne (R3). Vyúčtování přes datum startu se zadá jen za část od startu. |
| T06 náklad do uzavřeného období | Od T08 se zaúčtuje jako opravný záznam do otevřeného období (viz rozhodnutí T08). |
| T08 rozsah zámku | `IClosingBoundary.GetLastClosedDayAsync(houseId)`: bez domu (změny, které ovlivní všechny domy — pravidla, účast, náklady, odečty) platí jakákoli mezizávěrka; s domem (jeho platby, počáteční stavy) mezizávěrky všech domů a toho domu. Zrušit lze jen poslední mezizávěrku, s důvodem. |

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
| O1 | Ztráty vody lineárně, nebo poměrově? Rozhodne hlasování. | Přepínač per složka, efektivně datovaný, s auditem. | `EQUAL` (návrh Jindry) | T02, T05 | **rozhodnuto 28. 9. 2026: rovným dílem (`EQUAL`)** mezi domy napojené na vodovod. Nastaví se pravidlem složky „Ztráty vody“ (import: `components.csv` `method=rovne`); změnu lze později zadat v Nákladové složky → Ztráty vody → „Změnit metodu“ s důvodem. |
| O2 | Jak rozdělit přeplatek za elektřinu vodárny (cca 20 tis. Kč)? | Kredit složky se zadá jako počáteční stav. Metodu i okruh domů lze nastavit. | 4 domy, `EQUAL` | T03 | připraveno — čeká na rozhodnutí. Kredit se zadá v Počáteční stavy → Kredit složky u dodavatele (záporně, s náhledem rozdělení); okruh domů = účast ve složce, metoda = pravidlo složky. |
| O3 | Elektřina vodárny podle plateb, nebo podle elektroměru? | Složka podporuje obojí. Pro vodárnu se nastaví „podle nákladových záznamů“ (zálohy + vyúčtování). | podle záznamů | T02, T06 | připraveno — čeká na rozhodnutí. Složka „podle nákladových záznamů“ (zálohy a vyúčtování, výchozí), nebo „podle odečtů“ při elektroměru — volba při založení složky, změna metody od data. |
| O4 | Oddělený fond: právní a daňové řešení | Feature flag, default vypnuto. | `OFF` | T10 | připraveno — čeká na právní a daňové posouzení. Zapíná se app settingem `OFF_BOOK_FUND_ENABLED=true` (výchozí vypnuto); fond je oddělený od salda, závěrek i exportů. |
| O5 | Členské příspěvky vs. odpracování | **Neimplementovat** (zadání §5). | – | – | mimo rozsah |
