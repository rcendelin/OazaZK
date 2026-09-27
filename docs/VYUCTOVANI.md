# Zálohy, platby, saldo a odečty — jak to počítá kód

> **Zdroj pravdy je kód.** Tento dokument popisuje chování tak, jak je implementováno (stav větve `develop`, září 2026). Odkazy vedou do `api/src/`. Když se kód změní, aktualizuj i tento soubor.
>
> Uživatelsky orientované vysvětlení stejných pojmů je v aplikaci na stránce **Jak to funguje** (`/jak-to-funguje`, obsah v `web/src/content/help.ts`). Zadání nového výpočetního modelu (T02–T11) je v [ZADANI.md](ZADANI.md), rozhodnutí v [open-questions.md](open-questions.md).

Zkratky: **UC** = `Oaza.Application/UseCases/`, **EP** = `Oaza.Functions/Endpoints/`.

> **Starý model vyúčtování je odstraněn (X2, 27. 9. 2026).** Zúčtovací období (`BillingPeriod`), uložená vyúčtování (`Settlement`) a jejich PDF, faktury za vodu (`SupplierInvoice`), přehled faktur, staré saldo `GET /advances/saldo` a ceník záloh (cena vody, elektřina, koeficienty, společný základ, metoda ztrát) už v kódu nejsou. Náhradou jsou nákladové složky a náklady (`/naklady`), voda a ztráty (`/voda`), saldo domu (`/saldo-domu`) a mezizávěrky (`/mezizaverky`). **Platby domů zůstaly beze změny.** Staré tabulky ve storage (`BillingPeriods`, `SupplierInvoices`, `Settlements`) kód nečte; produkční data neexistují, nic se nemigruje.

---

## 1. Pojmy

| Pojem | Význam |
|-------|--------|
| **Hlavní vodoměr** | `WaterMeter.Type = Main`, bez `HouseId`. Měří celkový odběr sdružení. |
| **Domovní vodoměr** | `WaterMeter.Type = Individual` s `HouseId`. |
| **Ztráta** | Hlavní vodoměr − Σ domovní vodoměry v intervalu mezi odečty hlavního vodoměru (T05). |
| **Záloha** | `AdvancePayment` typu `Advance` — jedna na dům a měsíc (RowKey `YYYY-MM`). |
| **Doplatek** | `AdvancePayment` typu `Doplatek` — libovolná jednorázová platba, může být víc za měsíc. |
| **Výplata přeplatku** | `AdvancePayment` typu `Payout` — vrácení peněz domu. |
| **Složky platby** | Každá záloha/doplatek se dělí na `WaterAmount` / `ElectricityAmount` / `CommonAmount` (`Amount` = součet). |
| **Podíl na fondu** | `OpeningBalance` typu `FundShare` (T03) — počáteční stav domu v saldu. Starý `PaymentType.OpeningBalance` saldo ignoruje. |

Znaménková konvence salda (X1): **kladné = přeplatek, záporné = nedoplatek (dům dluží)**.

---

## 2. Platby

Platby zadává admin na stránce **Platby** (`/saldo`) nebo je načte z bankovního výpisu (`/advances/import`). Endpointy viz [API.md](API.md#zálohy-a-platby--advancefunctionscs). Mezizávěrka (T08) platby zamyká: úprava nebo smazání platby v uzavřeném období → 409, nová platba s datem v uzavřeném období se zaúčtuje na první otevřený den (záloha za uzavřený měsíc se stane doplatkem) a původní datum je v poznámce (`Domain/Helpers/ClosedPeriodPayments`).

---

## 3. Doporučené zálohy

`GET /advance-settings/calculate` (člen vidí jen svůj dům) — `UC/CalculatePrescribedAdvancesUseCase.cs`, UI `/advances`.

1. **Období:** posledních 12 měsíců před dneškem (Europe/Prague) — `[dnes − 12 měsíců, dnes − 1 den]`.
2. **Náklady domu** v tomto období vezme `LedgerCostCollector` (stejný zdroj jako saldo domu, T07) a rozdělí je na tři složky:
   - **voda** = voda PVK podle spotřeby + podíl na ztrátách,
   - **elektřina** = náklady složek, jejichž kód začíná `ELEKTRINA` (např. `ELEKTRINA_VODARNA`),
   - **společné** = náklady všech ostatních složek.
   Kredity u dodavatele (T03) se nezapočítávají — jsou jednorázovým počátečním stavem, ne průběžným nákladem.
3. **Doporučení** každé složky = `max(0, round(náklady ÷ 12, 0))` (celé Kč, zaokrouhlení od nuly).
4. Pokud má dům v `AdvanceSettings.HouseOverrides` **ruční přepis**, použijí se všechny tři složky z přepisu (`actual`); jinak `actual = recommended`.

`actual` používá i import bankovního výpisu k rozpoznání a rozdělení pravidelné zálohy. Nastavení `PUT /advance-settings` (Admin) ukládá jen přepisy; záporná částka → 400.

---

## 4. Saldo domu a společný fond

Saldo domu počítá nový model: `GET /ledger/houses/{houseId}` a přehled `GET /ledger/overview` (`Oaza.Application/Ledger/HouseLedgerUseCase.cs`, UI `/saldo-domu`):

```
saldo = podíl na fondu (FundShare) + platby − výplaty − náklady domu (T06) − voda a ztráty (T05) + kredity (T03)
```

Náklady se rozpočítávají po úsecích a měsících podle pravidel složek (T02, T06); kontrolní řádek přehledu porovná rozpočtenou částku složky se součtem přes domy. Mezizávěrka (T08) uloží snímek salda a zamkne období.

`GET /finance/fund` (Admin, Accountant) — `UC/GetFundBalanceUseCase.cs`:

```
FundBalance = Σ CommonAmount všech záloh a doplatků (všech domů)
            − Σ výdajů FinancialRecord s kategorií mimo {voda, elektro}
```

Příjmové záznamy se nezapočítávají.

---

## 5. Import odečtů

Dvoukrokový proces: **náhled → potvrzení**. Nic se neukládá, dokud admin nepotvrdí. Implementace `UC/ImportReadingsUseCase.cs`, `EP/ReadingFunctions.cs`.

### 5.1 Excel (`POST /readings/import`, max 5 MB)

Čte se první list, **data jsou po sloupcích**:

|   | A | B | C | … |
|---|---|---|---|---|
| **1** | *(popisek)* | 1. 1. 2026 | 1. 2. 2026 | … |
| **2** | číslo vodoměru | 1542,7 | 1550,2 | … |
| **3** | číslo vodoměru | … | … | … |

- Datum: excelové datum nebo text `d.M.yyyy`, `dd.MM.yyyy`, `d. M. yyyy`, `yyyy-MM-dd`.
- Vodoměr: shoda s `MeterNumber` (bez ohledu na velikost písmen), jinak první vodoměr, jehož číslo je **podřetězcem** buňky.
- Hodnota: stav vodoměru v m³ (kumulativní); české formáty `1 542,7` jsou podporované.

### 5.2 Schránka (`POST /readings/import/clipboard`)

Tělo `{ "text": "...", "readingDate": "2026-02-01" }`. Text oddělený tabulátory (export z odečítacího software), první řádek hlavička se sloupci **`Address`** a **`Value 1`** (případně `Value`). Vodoměr se páruje podle `WaterMeter.RadioAddress`, jinak `MeterNumber`. Všechny řádky dostanou jedno datum `readingDate`.

### 5.3 Validace

| Chyby (blokují potvrzení) | Varování (neblokují) |
|---------------------------|----------------------|
| neznámý vodoměr / adresa | prázdná buňka |
| nečitelné datum nebo hodnota | vodoměr v importu chybí |
| odečet pro stejný vodoměr a **stejný kalendářní měsíc** už existuje (v DB nebo v dávce) | anomálie: spotřeba > 2× průměr posledních 7 odečtů |
| hodnota nižší než předchozí odečet v DB | |

### 5.4 Potvrzení

`POST /readings/import/confirm { readings: [{ meterId, readingDate, value }] }`. **Bezstavové:** klient pošle odečty
z náhledu a server je znovu ověří stejnými pravidly jako náhled (§5.3) proti aktuálním datům. Náhled se na serveru
neukládá, takže nezáleží na tom, která instance Functions potvrzení obslouží.

- Celá dávka se ověří **před prvním zápisem** — když cokoli neprojde, neuloží se nic.
- Odečet, který už v DB je se stejným vodoměrem, datem i hodnotou, se přeskočí. Opakované potvrzení po výpadku
  uprostřed zápisu tak doběhne místo hlášení vlastních dřívějších zápisů jako konfliktu.

| Situace | HTTP |
|---------|------|
| žádné odečty, neznámý vodoměr, duplicita v dávce, záporná spotřeba | 400 |
| mezitím přibyl jiný odečet ve stejném měsíci | 409 |

### 5.5 Odhad odečtu (T04)

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

## 6. Známá omezení

1. **Doporučení záloh neřeší nový dům.** Dům bez nákladů v posledních 12 měsících (např. nově připojený) má doporučení 0 Kč — admin mu nastaví ruční přepis.
2. **Rozdělení na složky podle kódu.** Elektřina se pozná jen podle prefixu kódu složky `ELEKTRINA`; složka s jiným kódem spadne do „společné“.
