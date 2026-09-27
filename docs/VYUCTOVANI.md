# Vyúčtování vody, zálohy a saldo — jak to počítá kód

> **Zdroj pravdy je kód.** Tento dokument popisuje chování tak, jak je implementováno (stav větve `develop`, září 2026). Odkazy vedou do `api/src/`. Když se kód změní, aktualizuj i tento soubor.
>
> Uživatelsky orientované vysvětlení stejných pojmů je v aplikaci na stránce **Jak to funguje** (`/jak-to-funguje`, obsah v `web/src/content/help.ts`).

Zkratky: **UC** = `Oaza.Application/UseCases/`, **EP** = `Oaza.Functions/Endpoints/`.

---

## 1. Pojmy

| Pojem | Význam |
|-------|--------|
| **Hlavní vodoměr** | `WaterMeter.Type = Main`, bez `HouseId`. Měří celkový odběr sdružení. Použije se první nalezený. |
| **Domovní vodoměr** | `WaterMeter.Type = Individual` s `HouseId`. |
| **Ztráta** | Hlavní vodoměr − Σ domovní vodoměry (únik v síti, nepřesnost měření). |
| **Zúčtovací období** | `BillingPeriod` s `DateFrom`–`DateTo`, stav `Open` / `Closed`. |
| **Záloha** | `AdvancePayment` typu `Advance` — jedna na dům a měsíc (RowKey `YYYY-MM`). |
| **Doplatek** | `AdvancePayment` typu `Doplatek` — libovolná jednorázová platba, může být víc za měsíc. |
| **Výplata přeplatku** | `AdvancePayment` typu `Payout` — vrácení peněz domu. |
| **Počáteční zůstatek** | `AdvancePayment` typu `OpeningBalance` — jednorázový vstupní stav (+ nedoplatek, − přeplatek). |
| **Složky platby** | Každá záloha/doplatek se dělí na `WaterAmount` / `ElectricityAmount` / `CommonAmount` (`Amount` = součet). |
| **Společný fond** | Naspořené společné příspěvky mínus společné výdaje (viz §6). |

Znaménková konvence všude: **kladné = nedoplatek (dům dluží), záporné = přeplatek**.

---

## 2. Výpočet vyúčtování

`GET /billing-periods/{id}/calculate?method=Equal|ProportionalToConsumption` — pouze náhled, nic neukládá.
Implementace: `UC/CalculateSettlementUseCase.cs`.

### 2.1 Spotřeba vodoměru za období

Pro hlavní i každý domovní vodoměr (`GetMeterConsumptionAsync`):

- **počáteční odečet** = poslední odečet s `ReadingDate ≤ DateFrom`; pokud žádný není, použije se **nejstarší odečet vůbec**,
- **koncový odečet** = poslední odečet s `ReadingDate ≤ DateTo`; pokud není → chyba,
- `spotřeba = konec − začátek`; záporná spotřeba → chyba,
- odečty po `DateTo` se nepoužijí, nic se neinterpoluje.

> Prakticky: aby vyúčtování sedělo, musí existovat odečet **přesně k datu začátku a konce** období (nebo těsně před ním).

### 2.2 Domy a ztráta

1. Do výpočtu vstupují jen **aktivní domy** (`House.IsActive`).
2. Dům bez domovního vodoměru nebo s chybou v odečtech se **tiše vynechá** (jen záznam v logu) — nedostane řádek vyúčtování a jeho voda se projeví ve ztrátě.
3. `ztráta = spotřeba_hlavní − Σ spotřeba_domů`; záporná ztráta se ořízne na 0.
4. Rozpočet ztráty (`AllocateLoss`):
   - **Equal** — `ztráta / n`, kde *n* = počet domů **se spotřebou**,
   - **ProportionalToConsumption** — `ztráta × c_i / Σc` (při Σc = 0 se použije Equal).

> **Pozor na výchozí metodu:** endpointy `calculate` i `close` mají výchozí metodu **`Equal`**, pokud ji klient nepošle. Nastavení `AdvanceSettings.LossAllocationMethod` (výchozí `ProportionalToConsumption`) se používá jen pro saldo a výpočet záloh (§4, §5). UI metodu posílá explicitně.

### 2.3 Částka faktur za období

`SumInvoiceCostForPeriod` prochází **všechny** faktury dodavatele (`SupplierInvoice`):

- faktura **s řádky** (`InvoiceLineItem`): sečtou se řádky, jejichž `DateFrom` leží v `[DateFrom, DateTo]` období, každý jako `AmountExclVat × (1 + VatRatePercent/100)`. `DateTo` řádku se ignoruje, nic se nekrátí poměrem;
- **starší faktura bez řádků**: použije se `Amount`, pokud první den měsíce `Year/Month` leží v období.

Součet faktur se v `BillingPeriod` **neukládá** — vždy se počítá.

### 2.4 Rozpočet na domy

Pro každý dům *i*:

```
podíl_i %          = (c_i + ztráta_i) / (Σc + ztráta) × 100          (při nule: 100 / počet domů)
CalculatedAmount_i = round(podíl_i × faktury_celkem, 2)
zálohy na vodu_i   = Σ WaterAmount záloh a doplatků s EffectiveDate v období
Balance_i          = round(CalculatedAmount_i − zálohy na vodu_i, 2)  (jen voda!)

měsíců             = (rok2 − rok1) × 12 + (měsíc2 − měsíc1) + 1       (kalendářní měsíce, min. 1)
ElectricityCharge_i = round(MonthlyElectricityCost × koeficient_i / 100 × měsíců, 2)
CommonCharge_i      = round(MonthlyCommonBaseFee × měsíců, 2)
```

- `EffectiveDate` zálohy = 1. den jejího měsíce; doplatku = `PaymentDate`. Výplaty a počáteční zůstatky do vyúčtování **nevstupují**.
- Elektřina a společné poplatky jsou **rozpočtové** (měsíční sazba × počet měsíců podle **aktuálního** nastavení), ne skutečné náklady. Pro ně se `Balance` nepočítá — vypořádání řeší saldo (§5).
- Zaokrouhlení: m³ na 3 desetinná místa, podíl na 2 (jen pro zobrazení), peníze na 2. Součet `CalculatedAmount` se může od součtu faktur lišit o haléře — zbytek se nerozpočítává.

---

## 3. Uzavření období

`POST /billing-periods/{id}/close` (Admin), tělo:

```json
{
  "lossAllocationMethod": "Equal | ProportionalToConsumption",
  "fundDrawAmount": 0,
  "applyNewWaterPrice": false,
  "newWaterPriceValidFrom": "2026-01-01"
}
```

Implementace: `UC/CloseBillingPeriodUseCase.cs`. Postup:

1. Spočítá vyúčtování (§2); období musí být `Open`.
2. **Čerpání z fondu** (`fundDrawAmount > 0`, `ApplyFundDrawAsync`):
   - částka nesmí přesáhnout zůstatek fondu (§6),
   - každému **aktivnímu** domu zapíše doplatek `round(částka / n, 2)` do složky voda s `IsFundTransfer = true`, datem `DateTo` a deterministickým RowKey `FUND-{periodId}` (opakování přepíše, neduplikuje),
   - zapíše jeden výdaj `FinancialRecord` (Id `fund-{periodId}`, kategorie `fond-voda`) na celou částku,
   - vyúčtování se přepočítá, takže `Balance` už doplatky z fondu zahrnuje.
3. **Nová cena vody** (`applyNewWaterPrice = true`, vyžaduje `newWaterPriceValidFrom`):
   `cena = round(faktury_celkem / (Σc + ztráta), 2)` → zapíše se do `AdvanceSettings.WaterPricePerM3` a `WaterPriceValidFrom`.
4. Uloží `Settlement` pro každý dům z výpočtu (snapshot).
5. Nastaví `Status = Closed`.

Náhled čerpání fondu i nové ceny počítá frontend z existujících endpointů (`/calculate`, `/finance/fund`, `/advance-settings`).

### Co uzavření zamyká

Uzavření je **nevratné** (neexistuje endpoint pro znovuotevření). Zámky se kontrolují podle data:

| Operace | Zamčeno? |
|---------|----------|
| Úprava / smazání faktury, nahrání přílohy | ano (podle měsíce hlavičky faktury) |
| Oprava odečtu (`PUT /readings/...`) | ano (podle původního data) |
| Úprava / smazání měsíční zálohy | ano |
| Úprava období (`PUT /billing-periods/{id}`) | ano (409) |
| Doplatky, výplaty, počáteční zůstatky | **ne** — záměrně, platby po uzavření se promítnou do salda |
| Import / ruční zadání odečtu | **ne** (viz §8) |

### PDF vyúčtování

`GET /billing-periods/{id}/settlements/{houseId}/pdf` a ZIP všech `GET /billing-periods/{id}/pdf` — jen pro uzavřená období. PDF se ukládá do blobu `settlements/{periodId}/{houseId}.pdf` a při dalším požadavku se vrací z cache. Obsahuje **jen vodu** (spotřeba, ztráta, podíl, částka, zálohy, přeplatek/doplatek). Implementace `UC/GenerateSettlementPdfUseCase.cs`.

---

## 4. Výpočet doporučených záloh

`GET /advance-settings/calculate` (Member vidí jen svůj dům). Implementace přímo v `EP/AdvanceSettingsFunctions.cs`.

1. **Průměrná měsíční spotřeba** každého vodoměru z posledních 4 odečtů: `(poslední − první) / max(1, dní / 30)`, min. 0. Při < 2 odečtech = 0.
2. `měsíční ztráta = max(0, hlavní − Σ domy)`, rozpočtená podle `AdvanceSettings.LossAllocationMethod` mezi **všechny aktivní domy**.
3. Doporučení (zaokrouhleno na celé Kč):
   ```
   voda      = round((c_i + ztráta_i) × WaterPricePerM3, 0)
   elektřina = round(MonthlyElectricityCost × koeficient_i / 100, 0)
   společné  = MonthlyCommonBaseFee
   ```
4. Pokud má dům v `HouseOverrides` **ruční přepis**, použijí se všechny tři složky z přepisu.

Nastavení (`PUT /advance-settings`, Admin): pokud jsou zadány koeficienty elektřiny, jejich součet musí být 100 (tolerance ±0,1).

---

## 5. Saldo domu

`GET /advances/saldo[?houseId=]` — implementace `UC/CalculateHouseSaldoUseCase.cs`, UI `/saldo`.

Saldo je **jedno čisté číslo na dům**; rozpad na vodu / elektřinu / společné je jen analytický (peníze jsou zaměnitelné).

- **Předpis (charges) za období:**
  - uzavřené období → uložený snapshot `Settlement` (`CalculatedAmount`, `ElectricityCharge`, `CommonCharge`),
  - otevřené období → živý přepočet (§2) s metodou z `AdvanceSettings`; pokud výpočet selže (např. chybí odečet), období se v saldu neprojeví.
- **Zaplaceno:** vždy živě z plateb. Zálohy a doplatky se přiřadí prvnímu období, do kterého padne jejich `EffectiveDate`; ostatní jsou **Nezařazené platby** (zaplaceno bez předpisu).
- **Čisté úpravy:** výplaty (`Payout`, kladná částka) a počáteční zůstatky (`OpeningBalance`, se znaménkem).

```
ComponentSaldo = round(Σ období (předpis − zaplaceno) za vodu + elektřinu + společné, 2)
TotalSaldo     = round(ComponentSaldo + Σ čisté úpravy, 2)
```

**„Vystačí ~N měsíců"** — jen při přeplatku (`TotalSaldo < 0`) a pokud má dům ruční přepis záloh:
`N = round(|TotalSaldo| / Σ složek přepisu, 1)`. Příznak `House.DissolveOverpayment` („rozpouštět přeplatek") je informativní.

---

## 6. Společný fond

`GET /finance/fund` (Admin, Accountant) — `UC/GetFundBalanceUseCase.cs`:

```
FundBalance = Σ CommonAmount všech záloh a doplatků (všech domů)
            − Σ výdajů FinancialRecord s kategorií mimo {voda, elektro}
```

Příjmové záznamy se nezapočítávají. Výdaj `fond-voda` z čerpání při uzavření fond snižuje.

---

## 7. Import odečtů

Dvoukrokový proces: **náhled → potvrzení**. Nic se neukládá, dokud admin nepotvrdí. Implementace `UC/ImportReadingsUseCase.cs`, `EP/ReadingFunctions.cs`.

### 7.1 Excel (`POST /readings/import`, max 5 MB)

Čte se první list, **data jsou po sloupcích**:

|   | A | B | C | … |
|---|---|---|---|---|
| **1** | *(popisek)* | 1. 1. 2026 | 1. 2. 2026 | … |
| **2** | číslo vodoměru | 1542,7 | 1550,2 | … |
| **3** | číslo vodoměru | … | … | … |

- Datum: excelové datum nebo text `d.M.yyyy`, `dd.MM.yyyy`, `d. M. yyyy`, `yyyy-MM-dd`.
- Vodoměr: shoda s `MeterNumber` (bez ohledu na velikost písmen), jinak první vodoměr, jehož číslo je **podřetězcem** buňky.
- Hodnota: stav vodoměru v m³ (kumulativní); české formáty `1 542,7` jsou podporované.

### 7.2 Schránka (`POST /readings/import/clipboard`)

Tělo `{ "text": "...", "readingDate": "2026-02-01" }`. Text oddělený tabulátory (export z odečítacího software), první řádek hlavička se sloupci **`Address`** a **`Value 1`** (případně `Value`). Vodoměr se páruje podle `WaterMeter.RadioAddress`, jinak `MeterNumber`. Všechny řádky dostanou jedno datum `readingDate`.

### 7.3 Validace

| Chyby (blokují potvrzení) | Varování (neblokují) |
|---------------------------|----------------------|
| neznámý vodoměr / adresa | prázdná buňka |
| nečitelné datum nebo hodnota | vodoměr v importu chybí |
| odečet pro stejný vodoměr a **stejný kalendářní měsíc** už existuje (v DB nebo v dávce) | anomálie: spotřeba > 2× průměr posledních 7 odečtů |
| hodnota nižší než předchozí odečet v DB | |

### 7.4 Potvrzení

`POST /readings/import/confirm { importSessionId }`. Náhled je uložen v paměti procesu (`InMemoryImportSessionCache`) na **30 minut** a potvrdit ho smí jen stejný uživatel.

| Situace | HTTP |
|---------|------|
| session neexistuje / vypršela | 404 |
| jiný uživatel | 403 |
| náhled obsahuje chyby / žádné odečty | 400 |
| mezitím přibyl odečet ve stejném měsíci | 409 |

### 7.5 Odhad odečtu (T04)

`GET /readings/estimate?meterId=&date=` spočítá stav vodoměru k libovolnému dni (např. počáteční stav pro nového majitele).
Počítá se po celých dnech, výsledek na 3 desetinná místa (0,001 m³ = 1 litr), zaokrouhlení od nuly.

| Situace | Výsledek | Odhad? |
|---|---|---|
| odečet přesně v ten den | hodnota odečtu | ne |
| odečty před i po | `před + (po − před) × dnů_od_před / dnů_mezi` | ano |
| jen dřívější odečty | nejbližší předchozí hodnota (bez extrapolace), v poznámce vzdálenost ve dnech | ano |
| jen pozdější odečty | nejbližší následující hodnota, v poznámce vzdálenost ve dnech | ano |
| žádný odečet | bez hodnoty | — |

Uložený odečet nese `IsEstimate` a `EstimateNote` (popis metody a zdrojových odečtů); v přehledech odečtů je označen „≈“.
Ruční zadání odhadu vyžaduje popis. Kód: `Oaza.Domain.Services.ReadingEstimator`.

---

## 8. Známá omezení a nekonzistence

Zjištěno při sepisování dokumentace. Nejde o dokumentační chyby, ale o chování kódu, které stojí za pozdější opravu:

1. **Dvě pravidla pro „faktury v období".** Vyúčtování sčítá řádky faktur podle `DateFrom` (§2.3), ale seznam období (součty), odpověď při vytvoření/úpravě období, PDF („Celková faktura dodavatele") a zámek faktur používají měsíc hlavičky faktury (`Year/Month` = nejstarší řádek) a `Amount`. U faktur přes více měsíců se čísla mohou lišit.
2. **Výchozí metoda ztráty.** `calculate`/`close` mají výchozí `Equal`, saldo pro otevřené období používá `AdvanceSettings.LossAllocationMethod` (výchozí proporcionální).
3. **Neúplný zámek uzavřeného období.** Import a ruční zadání odečtu ani přesun odečtu na nové datum (`PUT` s `newDate`) nekontrolují uzavřené období.
4. **Uzavření není atomické.** Zápisy plateb, výdaje, ceny, vyúčtování a stavu jsou oddělené. Při opakování po částečném selhání už zapsaný výdaj `fund-{periodId}` snižuje zůstatek fondu, takže kontrola zůstatku může nové čerpání odmítnout.
5. **Čerpání z fondu** jde všem aktivním domům (i těm, které vyúčtování vynechalo) a kvůli zaokrouhlení se Σ doplatků může o haléře lišit od výdaje. Smazání doplatku `FUND-…` výdaj neodstraní.
6. **Session importu je v paměti.** Na Consumption plánu s více instancemi může potvrzení skončit na jiné instanci → 404; stačí import zopakovat.
7. **Potvrzení importu není atomické** — při 409 zůstanou dříve uložené odečty uložené.
8. **Období se mohou překrývat** — validátor překryv nekontroluje.
9. **Nápověda k Excel importu na stránce Vodoměry je obráceně.** `web/src/pages/admin/MetersPage.tsx` uvádí „vodoměry jako záhlaví sloupců, sloupec A = datum", parser ale čte data v řádku 1 a čísla vodoměrů ve sloupci A (§7.1).
10. **Opačné znaménko na dashboardu.** Karta „Stav účtu" člena zobrazuje `−TotalSaldo` (kladné = přeplatek), stránka Saldo používá konvenci kladné = nedoplatek.
