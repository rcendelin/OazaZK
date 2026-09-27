# Portál Oáza – zadání tasků pro Claude Code

> **Zdroj:** setkání 27. 9. 2026 (Rosťa, Jindra, Radka)
> **Cíl:** doplnit portál o funkce potřebné pro start ostrého provozu: počáteční stavy, nákladové složky s účastí domů, saldo domu, vyúčtování s mezizávěrkami, pokladna a oddělený fond.
> **Způsob práce:** tasky jsou seřazené podle závislostí. Každý task má akceptační kritéria a testy, které musí projít, než se pokračuje dalším.
>
> Rozhodnutí, která zadání upřesňují nebo mění (X1 znaménko, X2 jen nový model, X5 data, T09 oprávnění), jsou v `docs/open-questions.md`. Stav tasků je v `docs/gap-analysis.md`.

---

## 0. Pravidla pro Claude Code (čti první)

1. **Nejdřív průzkum, pak kód.** Začni taskem T00. Stack podle implementačního plánu z 3/2026 je Azure Functions (.NET 8, isolated worker) + React 19/TS/Vite/Tailwind + Azure Table Storage + Blob Storage, autentizace Entra ID + magic link, doména `oaza.cendelinovi.cz`. **Ověř skutečný stav repozitáře.** Pokud se liší, řiď se repozitářem a rozdíl zapiš do `docs/architecture-notes.md`.
2. **Nerozbíjej existující funkce.** Testovací instance obsahuje zkušební data a sekce výdaje, přehledy, dokumenty, hospodaření, zálohy, saldo a platby a nápovědu „Jak to funguje“. Všechny změny musí být zpětně kompatibilní nebo mít migraci.
3. **Žádná ostrá data, žádné secrets v repu.** Pracuj proti lokálnímu emulátoru (Azurite) nebo testovací instanci. Na produkční storage nikdy nesahej.
4. **Výpočetní logika je v doménové/aplikační vrstvě, ne v UI ani v HTTP handlerech.** Pokrytí unit testy u výpočtů má být ≥ 90 %.
5. **Peníze jako `decimal`, zaokrouhlení na 0,01 Kč, metoda největšího zbytku.** Součet rozpočtených částek se musí vždy rovnat rozpočítávané částce (viz S7).
6. **Časová pásma:** všechna data jsou kalendářní data v `Europe/Prague`. Období jsou uzavřené intervaly `[od, do]` po dnech.
7. **Znaménková konvence salda:** `saldo > 0` znamená přeplatek domu (spolek dluží domu), `saldo < 0` nedoplatek.
8. **Otevřená rozhodnutí se nehardcodují.** Vše z kapitoly 2.2 musí být konfigurovatelné s auditní stopou.
9. **Audit log:** každá změna konfigurace, počátečních stavů, metod rozpočtu, pokladny a fondu se loguje (kdo, kdy, stará hodnota, nová hodnota, důvod).
10. **Po každém tasku:** projdou testy, build frontendu i backendu je bez warningů, aktualizuj `CHANGELOG.md` a případně nápovědu (T12). Commit na feature branch `feature/Txx-nazev`.
11. **Při nejasnosti se zeptej.** Pokud task nejde dokončit bez rozhodnutí, které tu není, zastav se, sepiš otázku do `docs/open-questions.md` a pokračuj jiným nezávislým taskem.

---

## 1. Doménový slovník

| Pojem | Význam |
|---|---|
| **Dům** (`House`) | Trvalá identita nemovitosti (např. RD1). Nemění se při prodeji. |
| **Období vlastnictví** (`OwnershipPeriod`) | Úsek, kdy dům patří konkrétnímu vlastníkovi. Při prodeji se uzavře a vznikne nové („model nového majitele“). |
| **Nákladová složka** (`CostComponent`) | Druh společného nákladu: Voda PVK, Ztráty vody, Elektřina – vodárna, Elektřina – osvětlení, Odvoz jímky… Každá má počáteční datum a metodu rozpočtu. |
| **Účast** (`Participation`) | Efektivně datovaný záznam, který dům se podílí na které složce a jakou vahou. Příklad: vodárnu platí od 1. 11. 2023 jen 4 domy. |
| **Metoda rozpočtu** | `METERED` (podle odečtů), `EQUAL` (lineárně / pevným dílem), `RATIO` (poměrově podle vah, typicky spotřeby), `PERCENT` (pevná procenta, součet 100 %). |
| **Počáteční stav** (`OpeningBalance`) | Startovní hodnota k datu: stav vodoměru, podíl domu ve fondu, kredit (přeplatek) složky u dodavatele. |
| **Nákladový záznam** (`CostEntry`) | Náklad složky za období: měsíční záloha (`ADVANCE`), vyúčtování (`SETTLEMENT`, kladné = doplatek, záporné = přeplatek) nebo jednorázový náklad (`ONE_OFF`). |
| **Mezizávěrka** (`InterimClosing`) | Řez k datu, který zafixuje salda. Vyúčtování přes řez se rozpočítá zvlášť pro každý úsek. |
| **Saldo domu** | Počáteční podíl + platby domu − rozpočtené náklady. |
| **Pokladna** (`CashBook`) | Evidence hotovosti spolku: vklady (výběr z účtu) a výdaje (i bez dokladu). |
| **Oddělený fond** (`OffBookFund`) | Neformální fond mimo účetnictví spolku (pracovně „Fond na ohňostroje“). Peníze jsou na soukromém účtu správce, portál vede jen evidenci. |

---

## 2. Rozhodnutí

### 2.1 Závazná (implementovat takto)

| # | Rozhodnutí |
|---|---|
| R1 | Start účtování **vody PVK: 1. 11. 2023**. |
| R2 | Start účtování **elektřiny za vodárnu: 1. 11. 2023**. Účastní se jen 4 domy napojené na vodárnu. |
| R3 | **Nerekonstruuje se kompletní historie.** Systém startuje z počátečních stavů k datu startu. |
| R4 | Počáteční stav vodoměru = **nejbližší dohledatelný odečet** k datu startu. Pokud chybí, použije se lineární interpolace mezi dvěma odečty a hodnota se označí jako **odhad**. |
| R5 | Počáteční podíl domu ve fondu = stav k poslední roční závěrce, zadaný ručně s poznámkou o zdroji. |
| R6 | **Náklad vzniká každý měsíc bez ohledu na zdroj úhrady.** Záloha hrazená z přeplatku u dodavatele je pro dům stále náklad. |
| R7 | Model **nového majitele:** při změně vlastníka se uzavře období a nový vlastník startuje s vlastními počátečními stavy. Nedědí historii. |
| R8 | Dva elektroměry: **vodárna** (4 domy) a **společné osvětlení** (všichni). |
| R9 | Odvozy jímky zůstávají jako běžný jednorázový náklad. Nic speciálního se pro ně nezavádí. |
| R10 | Pokladna eviduje i výdaje **bez dokladu** (co, kdy, komu, kolik). |
| R11 | Ostrá data půjdou do **nové instance**. Testovací zůstává pro zkoušení. |

### 2.2 Otevřená (implementovat jako konfigurovatelné)

| # | Otázka | Požadavek na implementaci | Default |
|---|---|---|---|
| O1 | Ztráty vody lineárně, nebo poměrově? Rozhodne hlasování. | Přepínač per složka, efektivně datovaný, s auditem. | `EQUAL` (návrh Jindry) |
| O2 | Jak rozdělit přeplatek za elektřinu vodárny (cca 20 tis. Kč)? | Kredit složky se zadá jako počáteční stav. Metodu i okruh domů lze nastavit. | 4 domy, `EQUAL` |
| O3 | Elektřina vodárny podle plateb, nebo podle elektroměru? | Složka podporuje obojí. Pro vodárnu se nastaví „podle nákladových záznamů“ (zálohy + vyúčtování). | podle záznamů |
| O4 | Oddělený fond: právní a daňové řešení | Feature flag, default vypnuto. | `OFF` |
| O5 | Členské příspěvky vs. odpracování | **Neimplementovat.** Viz kapitola 5. | – |

---

## 3. Tasky

Přehled závislostí:

```
T00 → T01 → T02 → T03 → T04 ─┐
                  │          ├→ T06 → T07 → T08 → T13 → T14
                  └→ T05 ────┘
T07 → T09
T07 → T10
T02..T10 → T11, T12
```

### T00 – Průzkum repozitáře a plán změn
**Cíl:** pochopit aktuální stav a porovnat ho se zadáním. Tento task je bez implementace.

**Výstupy:**
- `docs/architecture-notes.md` obsahující: skutečný stack a vrstvy, existující entity a tabulky, jak se dnes počítá voda, zálohy a saldo, role a oprávnění, jak se nasazuje, jak se spouštějí testy.
- `docs/gap-analysis.md`: pro každý task T01–T14 uveď, co už existuje, co chybí a jaká migrace dat je potřeba.
- Seznam rizik a nejasností do `docs/open-questions.md`.

**Akceptace:** dokumenty existují, gap analýza pokrývá všechny tasky. Pokud se stack liší od předpokladu, je navržená úprava zadání.

### T01 – Oddělení prostředí test / prod
**Cíl:** umožnit provoz ostré instance vedle testovací (R11).

**Rozsah:**
- Konfigurace prostředí (`test`, `prod`) přes app settings. Oddělený storage account, případně prefix tabulek a kontejnerů.
- Viditelné označení prostředí v UI: barevný pruh „TESTOVACÍ PROSTŘEDÍ“ v `test`.
- IaC nebo skript pro založení prod resources (Bicep, pokud už v repu je; jinak `az` skript v `infra/`).
- CI/CD: nasazení do `prod` jen z tagu nebo `main` s ručním schválením.

**Akceptace:**
- Obě prostředí běží paralelně a nesdílejí data (ověřeno testem: zápis v `test` není vidět v `prod`).
- Banner je vidět jen v `test`.
- V repu nejsou žádné connection stringy ani klíče.

**Testy:** integrační test izolace dat (Azurite se dvěma konfiguracemi).

### T02 – Nákladové složky a efektivně datovaná účast domů
**Cíl:** obecný model „co se rozpočítává, komu a jak“.

**Datový model (minimum):**
- `CostComponent`: `id`, `name`, `code`, `startDate`, `allocationBasis` (`METERED` | `COST_ENTRIES`), `active`, `note`.
- `ComponentAllocationRule` (efektivně datované): `componentId`, `validFrom`, `validTo?`, `method` (`METERED` | `EQUAL` | `RATIO` | `PERCENT`), `ratioSource?` (např. spotřeba z jiné složky).
- `Participation`: `componentId`, `houseId`, `validFrom`, `validTo?`, `weight?` (pro `PERCENT`/`RATIO`).

**Business pravidla:**
- Intervaly účasti jednoho domu na stejné složce se nesmí překrývat.
- U `PERCENT` musí být součet vah aktivních účastníků v každém dni 100 %. Jinak validace selže s přesnou chybou (datum a součet).
- Změna pravidla nebo účasti v již uzavřeném úseku (za mezizávěrkou, T08) je zakázaná.
- Sjednocené hranice všech intervalů účasti a pravidel tvoří **úseky**, ve kterých je rozpočet konstantní. Služba `GetAllocationSegments(componentId, from, to)` je vrátí.

**Seed konfigurace (pro prod přes T13):**

| Složka | Start | Metoda | Účastníci |
|---|---|---|---|
| Voda PVK | 1. 11. 2023 | `METERED` | všechny domy s podružným vodoměrem |
| Ztráty vody | 1. 11. 2023 | `EQUAL` (O1) | všechny domy s vodoměrem |
| Elektřina – vodárna | 1. 11. 2023 | `COST_ENTRIES` + `EQUAL` | 4 domy (O2) |
| Elektřina – osvětlení | 1. 11. 2023 | `COST_ENTRIES` + `EQUAL` | všechny domy |
| Odvoz jímky | dle dat | `COST_ENTRIES` + `EQUAL` | všechny domy |

**UI (admin):** seznam složek, detail s časovou osou účasti a pravidel, přidání a ukončení účasti k datu.

**Akceptace:**
- Lze založit složku s 4 účastníky od 1. 11. 2023 a zobrazit úseky.
- Překryv účasti i `PERCENT` ≠ 100 % jsou odmítnuty s čitelnou chybou.
- Každá změna je v audit logu.

**Testy:**
- Unit: výpočet úseků (sjednocení hranic, otevřené intervaly, jednodenní úsek) a validace.
- API: CRUD včetně oprávnění (jen admin zapisuje).

### T03 – Počáteční stavy a model nového majitele
**Cíl:** start ze známých hodnot bez historie (R3, R5, R7).

**Datový model:**
- `OwnershipPeriod`: `houseId`, `ownerName`, `contact`, `validFrom`, `validTo?`.
- `OpeningBalance`: `type` (`METER_READING` | `FUND_SHARE` | `COMPONENT_CREDIT`), `houseId?`, `componentId?`, `meterId?`, `ownershipPeriodId?`, `date`, `value`, `isEstimate`, `source` (povinný text, např. „odečet 22. 5. 2023, foto Jindra“), `note`.

**Business pravidla:**
- Pro každou kombinaci (typ, dům/vodoměr/složka, období vlastnictví) existuje nejvýš jeden počáteční stav.
- `COMPONENT_CREDIT` je kredit složky u dodavatele k datu startu, typicky záporná hodnota, např. −20 000 Kč u vodárny. Rozpočte se podle pravidla složky platného k datu kreditu (O2) a vstoupí do salda domů jako záporný náklad.
- **Změna vlastníka** (akce „Převod domu“ s parametrem datum):
  1. automaticky vytvoří mezizávěrku pro daný dům k `datum − 1` (T08),
  2. uzavře `OwnershipPeriod`,
  3. vygeneruje závěrečné saldo starého vlastníka,
  4. založí nové období s povinnými počátečními stavy (stav vodoměru při předání, podíl ve fondu – default 0, volitelný počáteční vklad).
- Počáteční stavy jsou po první mezizávěrce, která je zahrnuje, needitovatelné. Oprava jde jen přes opravný záznam s auditem.

**UI (admin):**
- Průvodce „Počáteční stavy k datu“: tabulka domů × typ stavu, u každého pole zdroj a příznak odhadu.
- Akce „Převod domu“ s náhledem dopadů před potvrzením.

**Akceptace:**
- Lze zadat stavy všech vodoměrů a fondů k 1. 11. 2023 a kredit vodárny.
- Převod domu vytvoří závěrečné saldo starého vlastníka a nový vlastník začíná s nulovým fondem a zadaným stavem vodoměru (scénář S4).

**Testy:** unit pro pravidla jedinečnosti a převod domu, integrační pro scénář S4.

### T04 – Odečty: startovní odečet, interpolace, příznak odhadu
**Cíl:** podpořit R4.

**Rozsah:**
- Služba `EstimateReading(meterId, targetDate)`: lineární interpolace podle dní mezi nejbližším odečtem před a po cílovém datu. Pokud jeden chybí, vrátí nejbližší odečet a vzdálenost ve dnech. Výsledek vždy nese `isEstimate = true` a textový popis metody.
- UI v průvodci T03: u stavu vodoměru tlačítko „Navrhnout z odečtů“. Zobrazí se oba zdrojové odečty, vypočtená hodnota a vzdálenost ve dnech. Uživatel hodnotu přijme nebo přepíše.
- Odečty označené jako odhad jsou v přehledech vizuálně odlišené (ikona a tooltip se zdrojem).
- Stávající import odečtů (Excel) musí dál fungovat. Pokud importovaný odečet už existuje, upozorní na duplicitu.

**Akceptace:**
- Scénář S8 dává očekávanou hodnotu.
- Odhad je viditelný v přehledu i v exportu.

**Testy:** unit interpolace (přesné datum, mezi dvěma, jen před, jen po, žádný), regresní test existujícího importu.

### T05 – Ztráty vody: konfigurovatelný rozpočet
**Cíl:** O1.

**Rozsah:**
- Ztráta za období = odečet hlavního vodoměru − Σ podružných vodoměrů. Pokud je záporná, systém ji nerozpočítává, zobrazí varování a vyžádá kontrolu odečtů.
- Náklad ztráty = ztráta (m³) × jednotková cena PVK za období, případně podle konfigurace.
- Metoda rozpočtu podle `ComponentAllocationRule` složky „Ztráty vody“:
  - `EQUAL`: rovným dílem mezi účastníky aktivní v úseku, pro úseky kratší než období se použije pro-rata podle dní,
  - `RATIO`: poměrem spotřeby domu v období.
- Přepnutí metody je efektivně datované, vyžaduje důvod (např. „hlasování schůze 10/2026“) a loguje se.
- V přehledu vyúčtování je u ztrát vidět použitá metoda a výpočet (transparentnost pro členy).

**Akceptace:** scénář S1 v obou variantách, přepnutí metody se projeví jen od data platnosti.

**Testy:** unit (obě metody, záporná ztráta, změna účastníků uprostřed období), snapshot test výstupu vyúčtování.

### T06 – Nákladové záznamy a časové rozlišení
**Cíl:** R6, O3 a zadávání faktur a záloh dodavatelů.

**Datový model:**
- `CostEntry`: `componentId`, `type` (`ADVANCE` | `SETTLEMENT` | `ONE_OFF`), `periodFrom`, `periodTo`, `amount`, `supplier`, `documentId?` (odkaz na PDF v Dokumentech), `paidFrom` (`BANK` | `SUPPLIER_CREDIT` | `CASH` | `OTHER`), `note`.

**Business pravidla:**
- Náklad se rozpočítává podle období (`periodFrom`–`periodTo`) a pravidel složky. **`paidFrom` na rozpočet nemá vliv** (R6). Slouží jen pro evidenci a párování s bankou.
- `ADVANCE` se pro rozpočet rozprostře rovnoměrně po dnech období, např. čtvrtletní záloha na tři měsíce.
- `SETTLEMENT` je „13. položka“: rozdíl vyúčtování (doplatek > 0, přeplatek < 0) se rozpočítá do úseků podle T02/T08.
- Náklady složky s `allocationBasis = METERED` (voda PVK) se počítají z odečtů. Záznamy slouží jen k odsouhlasení fakturace a zobrazí se rozdíl.
- Import faktur zatím ručně: formulář a nahrání PDF do Dokumentů s propojením. Automatické vytěžování není předmětem.

**UI:** seznam nákladů podle složek s filtrem období a vazbou na PDF. Pro zálohy formulář „Opakovaná záloha“ (částka, perioda, od–do), který vygeneruje série záznamů.

**Akceptace:**
- Scénář S2: zálohy vodárny hrazené z kreditu se rozpočítávají stejně jako hrazené z banky.
- Opakovaná záloha vygeneruje správný počet záznamů.

**Testy:** unit rozprostření zálohy po dnech (přestupný rok, přelom měsíce), integrační S2.

### T07 – Saldo domu (ledger)
**Cíl:** hlavní přehled „kdo kolik zaplatil, jaký má náklad a saldo“.

**Rozsah:**
- Služba `GetHouseLedger(houseId, ownershipPeriodId?, from, to)` vrací položky:
  - počáteční podíl ve fondu,
  - platby domu (existující evidence plateb a záloh),
  - rozpočtené náklady po složkách a úsecích včetně kreditu složky (T03) a ztrát (T05),
  - průběžné saldo.
- Každá rozpočtená položka má **drill-down**: celková částka, metoda, úsek, podíl domu a výpočet. Cílem je, aby člen pochopil, proč má daný náklad.
- Přehled pro všechny domy: tabulka dům × složka a saldo. Kontrolní řádek „Σ domů = Σ nákladů složky“ se při nesouladu zvýrazní.
- Oprávnění: člen vidí detail svého domu a souhrn ostatních bez osobních údajů (dle existujícího RBAC, v T00 ověřit). Admin a účetní vidí vše.
- Export do XLSX a CSV.

**Akceptace:**
- Pro seed data S1–S4 sedí saldo každého domu na ruční výpočet.
- Kontrolní součty sedí na haléř.
- Člen nevidí detail cizího domu.

**Testy:** unit agregace, property-based test „Σ rozpočtu = celkem“ pro náhodné vstupy (FsCheck nebo ekvivalent), API test oprávnění.

### T08 – Mezizávěrky a rozpočet vyúčtování přes úseky
**Cíl:** správné vracení přeplatků a doplatků, když se v průběhu období mění účast nebo metoda.

**Rozsah:**
- `InterimClosing`: `date`, `scope` (`ALL` | `HOUSE`), `houseId?`, `createdBy`, `reason`, snapshot salda všech dotčených domů.
- **Automatické úseky:** vyúčtování (`SETTLEMENT`) přes období, ve kterém se mění účast nebo pravidlo, se rozdělí do úseků pro-rata podle dní. V každém úseku se rozpočítá mezi tehdejší účastníky.
- **Ruční mezizávěrka:** admin může vytvořit řez k libovolnému datu. Data do řezu jsou pak neměnná. Opravy jdou jen opravným záznamem v otevřeném období s odkazem na původní.
- Roční závěrka = mezizávěrka `ALL` k 31. 12. s exportem pro účetní (**bez odděleného fondu**, viz T10).
- UI: seznam mezizávěrek, detail se snapshotem a rozdílem proti aktuálnímu stavu.

**Akceptace:**
- Scénář S3: vyúčtování za 1. 7.–31. 12. s domem, který se připojí 1. 10., se rozdělí 92 : 92 dní a přidaný dům nese podíl jen z druhého úseku.
- Pokus o změnu dat před mezizávěrkou je odmítnut.

**Testy:** unit dělení na úseky (hranice, jednodenní úsek, více změn), integrační S3, test neměnnosti.

### T09 – Pokladna
**Cíl:** R10. Transparentní evidence hotovosti, aby nevznikalo podezření z „černého fondu“.

**Datový model:**
- `CashBookEntry`: `date`, `type` (`DEPOSIT` | `EXPENSE` | `CORRECTION`), `amount` (> 0), `category` (např. údržba okolí, materiál, služby), `description`, `counterparty` (komu nebo od koho), `hasReceipt` (bool), `documentId?`, `bankTransactionRef?` (u vkladu z výběru z účtu), `createdBy`.

**Business pravidla:**
- Zůstatek pokladny = Σ vkladů − Σ výdajů ± korekce. **Výdaj, který by způsobil záporný zůstatek, je odmítnut.**
- Vklad typu „výběr z účtu“ lze spárovat s bankovní transakcí, pokud je v systému evidence banky. Jinak se zapíše reference ručně.
- Záznamy nelze mazat. Oprava jde storno záznamem (`CORRECTION` s odkazem na původní).
- Výdaj bez dokladu je povolený, ale `description` a `counterparty` jsou povinné. V přehledu je označen ikonou „bez dokladu“.
- Výdaje z pokladny, které jsou společným nákladem, lze volitelně navázat na nákladovou složku (vznikne `CostEntry` typu `ONE_OFF` s `paidFrom = CASH`).
- Viditelnost: členové čtou (transparentnost), zapisuje admin nebo pověřená osoba.
- Export pokladní knihy za období (XLSX a PDF) pro účetní.

**Akceptace:** scénář S5, export obsahuje všechny záznamy včetně příznaku bez dokladu, storno funguje a je auditované.

**Testy:** unit zůstatku a záporného stavu, API oprávnění, snapshot exportu.

### T10 – Oddělený fond (feature flag, default vypnuto)
**Cíl:** evidence neformálního fondu **striktně oddělená** od hospodaření spolku (O4).

> ⚠️ Právní a daňové řešení není uzavřené. Implementuj za feature flag `OFF_BOOK_FUND_ENABLED` (default `false`). V UI modulu zobrazuj trvalé upozornění „Fond mimo účetnictví spolku – peníze nejsou na účtu spolku“.

**Datový model:**
- `OffBookFund`: `name` (např. „Fond na ohňostroje“), `purpose`, `managerName`, `accountDescription` (text, např. „soukromý účet správce“; **neukládat celé číslo účtu**), `active`.
- `FundCall` (výzva): `fundId`, `amountPerHouse`, `dueDate`, `text`, `houses[]`.
- `FundContribution`: `callId?`, `houseId`, `date`, `amount`, `method` (převod, hotovost).
- `FundExpense`: `date`, `amount`, `description`, `paidBy?` (kdo zaplatil předem), `hasReceipt`.
- `FundSettlement` („vyrovnání“): přeposlání zálohy tomu, kdo platil předem.

**Business pravidla:**
- Data fondu se **nikdy** neobjeví v saldu domů (T07), mezizávěrkách a roční závěrce (T08), exportech pro účetní, pokladně (T09) ani v přehledech hospodaření spolku. Tuto izolaci hlídají automatické testy.
- Výzva vygeneruje očekávané příspěvky. Přehled ukazuje, kdo zaplatil a kdo ne.
- Zůstatek fondu = příspěvky − výdaje ± vyrovnání.

**Akceptace:** scénář S6. S vypnutým flagem není modul v UI ani v API (404).

**Testy:** izolační test (export roční závěrky ani ledger neobsahují položky fondu), test feature flagu.

### T11 – Zadávání podkladů od Radky
**Cíl:** umožnit správci pohodlně vložit faktury a vyúčtování, která Radka posílá (PDF e-mailem nebo přes Slack).

**Rozsah:**
- Hromadné nahrání více PDF do Dokumentů najednou s přiřazením kategorie a složky.
- Z detailu dokumentu jde rovnou vytvořit `CostEntry` s předvyplněnou vazbou na dokument.
- Přehled „Dokumenty bez zaúčtování“ (PDF nahrané, ale bez navázaného nákladového záznamu).
- Automatické vytěžování dat z PDF **není** předmětem.

**Akceptace:** nahrání 5 PDF najednou, vytvoření nákladu z dokumentu, dokument zmizí z přehledu nezaúčtovaných.

**Testy:** API upload (limit velikosti min. 10 MB/soubor, jen PDF a obrázky), E2E průchod.

### T12 – Nápověda a uživatelská příručka
**Cíl:** „blbuvzdorné“ ovládání pro správce (Radka) a srozumitelné vysvětlení pro členy.

**Rozsah:**
- Aktualizace záložky **„Jak to funguje“**: počáteční stavy a proč se nerekonstruuje historie, nákladové složky a účast, ztráty (aktuální metoda), přeplatek vodárny, saldo, mezizávěrky, pokladna.
- Stránka **„Návod pro správce“** s postupy krok za krokem a screenshoty (lze generovat Playwrightem):
  1. zadat měsíční odečty,
  2. vložit fakturu a zálohu,
  3. zapsat výdaj z pokladny,
  4. udělat mezizávěrku,
  5. převést dům na nového majitele.
- Kontextová nápověda (ikona „?“) u průvodců T03 a T08.
- Vše česky, bez technického žargonu.

**Akceptace:** každý postup z návodu lze projít na testovací instanci bez znalosti kódu. Kontrola: E2E testy kopírují kroky návodu.

### T13 – Import ostrých počátečních dat (seed prod)
**Cíl:** jednorázově a opakovatelně naplnit prod instanci.

**Rozsah:**
- Šablony CSV v `seed/templates/`: `houses.csv`, `ownership_periods.csv`, `meters.csv`, `opening_meter_readings.csv` (včetně `is_estimate`, `source`), `opening_fund_shares.csv`, `components.csv`, `participations.csv`, `component_credits.csv`, `cost_entries.csv`.
- CLI nebo admin funkce `seed import --env prod --dry-run`:
  - validuje vstupy (referenční integrita, T02 validace, součty),
  - vypíše report dopadů (salda domů k dnešku),
  - teprve s `--apply` zapíše.
- **Idempotence:** opakovaný import stejných dat nevytvoří duplicity (přirozené klíče).
- Report ve formátu Markdown a XLSX pro kontrolu Jindrou a Radkou před ostrým spuštěním.

**Akceptace:**
- Dry-run na vzorových datech projde a report odpovídá ručnímu výpočtu.
- Druhý `--apply` nic nezmění.

**Testy:** integrační (import → ledger → porovnání s očekávanými salda), test idempotence.

### T14 – Regresní E2E a release checklist
**Cíl:** ověřit celek před představením sousedům.

**Rozsah:**
- Playwright E2E sada pokrývající scénáře S1–S8 přes UI.
- `docs/release-checklist.md`: migrace, zálohy storage před nasazením, ověření banneru prostředí, kontrola oprávnění členů, smoke test po nasazení, rollback postup.
- Seed testovací instance demo daty, která vypadají realisticky, ale jsou fiktivní, pro školení.

**Akceptace:** všechny E2E testy zelené v CI proti `test` prostředí, checklist prošel na testovací instanci.

---

## 4. Testovací scénáře (golden data, fiktivní hodnoty)

Domy pro scénáře: **A, B, C, D** (napojené na vodárnu), **E, F** (nenapojené).

**S1 – Ztráty vody.** Období 1. 1.–30. 6. Hlavní vodoměr 100 m³, podružné A 40, B 30, C 15, D 5 (Σ 90). Ztráta 10 m³, náklad ztráty 1 000,00 Kč.
- `EQUAL`: 250,00 / 250,00 / 250,00 / 250,00.
- `RATIO`: 444,44 / 333,33 / 166,67 / 55,56, součet 1 000,00. Postup: zaokrouhlení dolů dává 999,98 a dva haléře se přidají domům s největším zbytkem (C 0,007, D 0,006).

**S2 – Kredit vodárny.** K 1. 11. 2023 kredit složky „Elektřina – vodárna“ −20 000,00 Kč. Účast A–D, `EQUAL` → každý −5 000,00 (dům má kredit). Měsíční zálohy 500,00 Kč (`paidFrom = SUPPLIER_CREDIT`) → každý dům +125,00 nákladu měsíčně. E a F mají 0 na obou položkách.

**S3 – Změna účasti uprostřed období.** Složka „Osvětlení“, účast A–D, od 1. 10. se přidá E. Vyúčtování za 1. 7.–31. 12. je doplatek 1 840,00 Kč. Úsek 1 (1. 7.–30. 9., 92 dní) = 920,00 Kč / 4 = 230,00 pro A–D. Úsek 2 (1. 10.–31. 12., 92 dní) = 920,00 Kč / 5 = 184,00 pro A–E. Celkem A–D 414,00, E 184,00.

**S4 – Převod domu.** Dům B prodán k 15. 3. Mezizávěrka B k 14. 3. a závěrečné saldo původního vlastníka. Nový vlastník od 15. 3.: fond 0, stav vodoměru 1 234,567 m³ (zadaný). Náklady od 15. 3. jdou na nové období vlastnictví.

**S5 – Pokladna.** Vklad 5 000 (výběr z účtu). Výdaj 2 000 „úprava okolí“ bez dokladu → zůstatek 3 000. Výdaj 4 000 → **odmítnut**. Storno výdaje 2 000 → zůstatek 5 000.

**S6 – Oddělený fond.** Výzva 1 230 Kč × 6 domů, zaplatí 5 → přehled ukazuje 1 dlužníka, zůstatek 6 150. Export roční závěrky ani ledger žádného domu neobsahuje položky fondu.

**S7 – Zaokrouhlení.** 1 000,00 Kč `EQUAL` mezi 3 domy → 333,34 / 333,33 / 333,33. Součet je vždy přesně 1 000,00.

**S8 – Interpolace odečtu.** Odečty 22. 5. 2023 = 100,000 m³ a 19. 1. 2025 = 700,000 m³. Odhad k 1. 11. 2023: 163 dní z 608 → 100 + 600 × 163/608 = **260,855 m³** (3 desetinná místa), `isEstimate = true`.

---

## 5. Mimo rozsah (nyní neimplementovat)

- **Členské příspěvky vs. odpracování** (O5). Čeká na návrh pravidel s Jardou včetně daňového řešení. Doménový model ho zatím neblokuje.
- **Pravidla sdílené sekačky.** Jde o organizační dohodu, v aplikaci nic.
- **Rekonstrukce historie** před daty startu (R3).
- **Automatické vytěžování PDF a párování bankovních výpisů.**

---

## 6. Definition of Done (pro každý task)

- [ ] Akceptační kritéria splněna a ověřena testem.
- [ ] Unit a integrační testy zelené. Pokrytí výpočetní logiky ≥ 90 %.
- [ ] Build backendu i frontendu bez warningů, lint čistý.
- [ ] Změny auditovatelné (audit log), oprávnění ověřena testem.
- [ ] Migrace dat (pokud je potřeba) je idempotentní a otestovaná na kopii testovacích dat.
- [ ] Nápověda „Jak to funguje“ aktualizována (T12).
- [ ] `CHANGELOG.md` doplněn, PR s popisem a odkazem na task.
