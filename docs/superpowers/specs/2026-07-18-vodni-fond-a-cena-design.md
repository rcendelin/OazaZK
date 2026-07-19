# Vodní fond a cena vody při uzávěrce vodního období — návrh

> Vzniklo brainstormingem 2026-07-18. Navazuje na existující subsystém vyúčtování vody (`BillingPeriod` + `SupplierInvoice` + `CalculateSettlementUseCase`) a na existující společný fond (`GET /finance/fund`). Cílem je umožnit, aby po příchodu reálné faktury za vodu (ať už jde o úplně první vyúčtování, nebo o další v pořadí) šlo případný nedoplatek/přeplatek doladit čerpáním ze společného fondu — a zároveň z reálné ceny na faktuře odvodit doporučenou cenu vody pro příští zálohy.

## 1. Kontext

Sdružení zatím platilo jen zálohy na vodu (stejná částka za dům každý měsíc), aniž by proti nim stála skutečná faktura. Teď dorazila první/další reálná faktura za vodu. Existující systém už umí spočítat, kolik má který dům doplatit/dostat zpět na základě spotřeby a alokace ztráty (`CalculateSettlementUseCase`) — chybí ale způsob, jak do tohoto vyrovnání vpustit i peníze, které sdružení má už naspořené ve společném fondu, rozpočítané rovnoměrně na domy (bez ohledu na to, kdo do fondu kolik přispěl). Zároveň chybí propojení mezi tím, co se na faktuře reálně zaplatilo za m³, a nastavením ceny pro doporučené budoucí zálohy.

## 2. Rozsah

**V rozsahu:**
- Volitelné čerpání ze společného fondu (kategorie „Společné") při uzávěrce vodního období, rozpočítané rovnoměrně mezi aktivní domy.
- Volitelné provázání efektivní ceny vody z právě uzavíraného období s nastavením `WaterPricePerM3` pro doporučené budoucí zálohy.
- Voda zůstává jednou ze složek celkového salda domu — žádná změna v tom, jak saldo skládá vodu/elektřinu/společné.

**Mimo rozsah (vědomě odloženo):**
- Elektřina za studnu a společné náklady — nedotčeny, počítají se dál paušálem podle měsíců (`MonthlyElectricityCost`, `MonthlyCommonBaseFee`), ne podle faktury.
- Cokoliv ohledně samotného vzniku/editace faktur, období nebo odečtů — používá se beze změny existující flow (viz `docs/ANALYZA-ADRESARE.md`, kde jsou navíc popsané otevřené nálezy k opravě u editace období a modelu faktur s řádky — tato specifikace na nich nestaví a neřeší je).

## 3. Co už existuje beze změny

| Krok | Kde |
|---|---|
| Spotřeba domu = rozdíl odečtů na hranicích období | `CalculateSettlementUseCase.GetMeterConsumptionAsync` |
| Ztráta = hlavní odečet − součet domů, alokace dle zvolené metody | `CalculateSettlementUseCase.AllocateLoss` |
| Podíl domu = (spotřeba + alokovaná ztráta) / (celková spotřeba + celková ztráta) | `CalculateSettlementUseCase.CalculateAsync` |
| Částka domu = podíl × celková faktura (vč. DPH) za období | `CalculateSettlementUseCase.SumInvoiceCostForPeriod` |
| Zálohy domu na vodu = součet `WaterAmount` ze Zálohy+Doplatek v období | `IAdvancePaymentRepository.GetByHouseAndPeriodAsync` |
| Saldo domu (dnes) = Částka domu − Zálohy domu | `CalculateSettlementUseCase.CalculateAsync` (pole `Balance`) |
| Zůstatek společného fondu = Σ příspěvků do složky Společné − Σ nákladů mimo vodu/elektro | `FinanceFunctions.GetFundBalanceAsync` |
| Doporučené budoucí zálohy = průměrná spotřeba domu × `WaterPricePerM3` (+ alokace ztráty) | `AdvanceSettingsFunctions.CalculateAdvancesAsync` |

Uzavření období zůstává nevratné (business pravidlo #5) a stále dvoukrokové: nejdřív náhled (`CalculateAsync`), pak potvrzení (`CloseBillingPeriodUseCase.CloseAsync`), které teprve zapisuje `Settlement` a zamyká období.

## 4. Nové chování

### 4.1 Čerpání ze společného fondu

Na obrazovce náhledu před uzavřením vodního období přibude volitelná sekce:

1. Zobrazí se aktuální zůstatek fondu `Z` (živě, `GetFundBalanceAsync`, beze změny vzorce).
2. Admin posuvníkem 0…`Z` zvolí částku `K` k čerpání.
3. Příspěvek na dům `P = K / počet aktivních domů`, zaokrouhleno na 2 desetinná místa (stejná konvence jako všude jinde v systému).
4. Finální saldo domu = Částka domu − Zálohy domu − `P`.
5. Náhled průběžně ukazuje i zůstatek fondu po uzávěrce (`Z − K`) — vše se přepočítává živě, nic se nezapisuje.

Při potvrzení uzávěrky (atomicky, jako jedna dávka před finálním přepočtem a zápisem `Settlement`):

6. Pro každý aktivní dům se založí doplatek (`AdvancePayment`, `PaymentType.Doplatek`, `WaterAmount = P`), s příznakem `IsFundTransfer = true` a poznámkou „Použito ze společného fondu — {název období}". Doplatek se do výpočtu vodního vyúčtování promítne automaticky, beze změny `CalculateSettlementUseCase` (ta už dnes sčítá zálohy+doplatky v období).
7. Založí se jeden výdajový záznam ve Financích (`FinancialRecord`, `Type = Expense`, kategorie `fond-voda`, `Amount = K`, popis „Čerpání společného fondu pro vyúčtování vody — {název období}") — sníží zůstatek fondu přes existující, nezměněný vzorec (`GetFundBalanceAsync` odečítá náklady mimo vodu/elektro; nová kategorie `fond-voda` do tohoto odečtu spadá).
8. Pořadí zápisu je důležité: doplatky z kroku 6 musí existovat v úložišti **před** finálním přepočtem vyúčtování v `CloseBillingPeriodUseCase`, aby se `Balance` každého domu v uloženém `Settlement` už počítalo se sníženou hodnotou.

Pokud admin zvolí `K = 0` (posuvník na nule / sekci nepoužije), nevznikají žádné doplatky ani finanční záznam — chování je shodné s dneškem.

### 4.2 Cena vody pro příští zálohy

Na téže obrazovce, nezávisle na sekci 4.1:

1. Spočítá se efektivní cena právě uzavíraného období: `Celková faktura (vč. DPH) ÷ (celková spotřeba domů + celková ztráta)` za dané období — stejná čísla, která už `CalculateSettlementUseCase` počítá pro náhled.
2. Zobrazí se vedle aktuálně nastavené `WaterPricePerM3` a její platnosti.
3. Zaškrtávátko „Nastavit jako novou cenu vody" (výchozí zaškrtnuto), s editovatelným datem platnosti, předvyplněným na den následující po `DateTo` právě uzavíraného období — nová cena se totiž vždy zjišťuje až zpětně z faktury, nemá smysl řešit „dopředu známé" datum.
4. Při potvrzení uzávěrky se (jen pokud je zaškrtnuto) běžnou cestou (stejná logika jako `PUT /advance-settings`) uloží nová `AdvanceSettings.WaterPricePerM3` + `WaterPriceValidFrom`.

Pokud admin zaškrtávátko odškrtne, `AdvanceSettings` se nemění.

## 5. Datový model — změny

Minimální dopad, žádná nová entita:

- **`AdvancePayment.IsFundTransfer`** (bool, výchozí `false`) — nové pole. Odlišuje doplatek vzniklý čerpáním z fondu od běžně ručně zadaného doplatku, spolehlivěji než rozpoznávání podle textu poznámky. Frontend podle něj zobrazí odznak „Z fondu" v historii domu.
- **`FinancialRecord.Category`** — přidat novou hodnotu `"fond-voda"` vedle existujících (`voda`, `elektro`, `udrzba`, `pojisteni`, `jine`), aby šly tyto výdaje ve finančním přehledu/exportu odlišit.
- **`AdvanceSettings`** — beze změny struktury, jen se přepíšou existující pole `WaterPricePerM3`/`WaterPriceValidFrom` běžnou cestou.

## 6. UI / UX

Rozšíření obrazovky náhledu/uzávěrky vodního období (dnešní `BillingPage.tsx`, sekce vyúčtování) o dvě volitelné podsekce nad tlačítkem „Uzavřít období":

- **① Čerpání ze společného fondu** — zůstatek fondu, posuvník, tři živé hodnoty (použito / na dům / zbyde ve fondu), vysvětlující poznámka co uzávěrka založí.
- **② Cena vody pro příští zálohy** — efektivní cena vs. aktuální nastavená cena, zaškrtávátko + datum platnosti.
- Tabulka salda domů se po každé změně posuvníku přepočítá živě (sloupec „Saldo bez fondu" zůstává, přidá se „Z fondu" a „Finální saldo").

Rozvržení bylo odsouhlaseno interaktivním mockupem během brainstormingu (posuvník nahoře, cena pod ním, finální tabulka salda dole, obojí volitelné a nezávislé na sobě).

## 7. Chybové stavy a hraniční případy

- Posuvník nesmí jít nad aktuální zůstatek fondu (`K ≤ Z`) — validace na frontendu i backendu (backend je autoritativní).
- Fond je nulový nebo záporný → sekce se zobrazí needitovatelná s hláškou „Fond je aktuálně prázdný", žádná chyba.
- Nula aktivních domů → sekce fondu se nezobrazí (stejná podmínka, jaká už dnes hlídá vyúčtování samotné).
- Přepočet náhledu (změna posuvníku, otevření obrazovky) nikdy nic nezapisuje — zápis (doplatky, finanční záznam, případně nová cena) proběhne výhradně při potvrzení uzávěrky, stejně jako u vyúčtování samotného dnes.
- Selhání uprostřed zápisu (např. výpadek mezi zápisem doplatků a zápisem `Settlement`) nesmí nechat „osiřelé" fondové doplatky bez odpovídajícího vyúčtování — doplatky i finanční záznam se zapisují jako jedna dávka před finálním přepočtem, a `CloseBillingPeriodUseCase` musí zůstat idempotentní i vůči této dávce (dnes je idempotentní vůči `Settlement` samotnému).
- Zrušení uzávěrky (admin obrazovku opustí bez potvrzení) → nic se neuloží, ani doplatky, ani finanční záznam, ani nová cena.
- Zaokrouhlení `K / počet domů` na 2 desetinná místa — případný haléřový zbytek zůstává ve fondu; stejná zanedbatelná tolerance jako existující zaokrouhlovací reziduum u vyúčtování (viz `docs/ANALYZA-ADRESARE.md`).

## 8. Testování

- Unit testy nové use-case logiky v `Oaza.Application.Tests` (analogicky k `CloseBillingPeriodUseCaseTests`/`CalculateHouseSaldoUseCaseTests`): správný výpočet `P = K / počet domů`; `K = 0` nevytvoří žádné záznamy; `K > Z` je odmítnuto; zaškrtnutí/nezaškrtnutí ceny správně (ne)aktualizuje `AdvanceSettings`.
- End-to-end test uzávěrky s čerpáním z fondu — ověřit, že výsledný `Settlement.Balance` už zahrnuje odečtení fondového doplatku (pořadí zápisu z bodu 4.1/8 funguje).
- Test na atomicitu/idempotenci — opakované volání uzávěrky po částečném selhání nevytvoří fondové doplatky ani finanční záznam dvakrát.
- Tyto testy se píší **jako součást implementace**, ne odloženě — jde o peníze domácností a poslední audit (`docs/ANALYZA-ADRESARE.md`) opakovaně ukázal, že nové peněžní cesty bez testů rychle nastřádají skryté chyby.
- Frontend nemá test runner (potvrzeno auditem) → manuální ověření: posuvník na 0 a na maximu fondu, zaškrtávátko ceny zapnuto/vypnuto, validace data platnosti.
