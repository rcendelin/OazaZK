# Analýza adresáře — Oáza Zadní Kopanina

> Vygenerováno 2026-06-13. Metoda: 7 paralelních analytických agentů, každý měřil **skutečný kód proti `CLAUDE.md`** (autoritativní spec), následně adversariální ověření nálezů na úroveň `soubor:řádek`. Doplněno o reálný build / test / lint na tomto stroji.

> **Stav remediace (2026-06-13, větev `develop`, vše nasazeno do DEV se zelenou CI):**
> - ✅ **Fáze 0 — build:** floaty NuGet pinovány, SDK uzamčen na .NET 8, Blob SAS API zmigrováno, FE lint opraven + lint gate v CI. Build i 99 testů zelené.
> - ✅ **Fáze 1 — bezpečnost/korektnost:** #4 self-delete, #5 RBAC advance-settings, #6 metoda alokace ztráty, #7 import duplicit per-měsíc, #8 JWT `sub` (`MapInboundClaims=false`) — s regresními testy.
> - ✅ **Fáze 2 — testy:** hraniční/fallback testy vyúčtování, **Azurite integrační testy** repozitářů (běží i v CI), `CloseBillingPeriodUseCase` vytažen a otestován, SampleTest placeholdery odstraněny. Celkem **106 testů**.
> - ✅ **Fáze 3 — hygiena:** `CLAUDE.md` + `DEPLOYMENT-DEV.md` sjednoceny s realitou (ACS, hash tokenů, `DocumentVersion`/`AdvanceSettings`, extra endpointy), `GET /invoices` přes `[RequireRole]`.
> - ⏳ **Vědomě odloženo (nízká priorita):** zaokrouhlovací reziduum vyúčtování, validátory do DI, exhaustivní validátorové testy, FE kosmetika (sidebar 200px, code-splitting, catch-all route, duplicitní `staticwebapp.config.json`), SAS-vs-proxy download, Node 20 → 24 v CI.

---

## 1. Co to je

Komunitní portál pro sdružení „Oáza Zadní Kopanina" (8 domácností, ~15 uživatelů). Hlavní funkce: správa sdíleného vodovodu — měsíční odečty, vyúčtování, zálohy. Vedlejší: sdílené dokumenty a finanční přehled.

**Architektura:** třívrstvá Clean Architecture na Azure.
- **Frontend:** React 19 + TypeScript (Vite, Tailwind) → Azure Static Web Apps
- **Backend:** .NET 8 Azure Functions (Isolated Worker) → Azure Table + Blob Storage
- **Bez relační DB**, bez EF Core — pouze `Azure.Data.Tables` přes repository pattern.

**Rozsah:** 220 sledovaných souborů · ~11 700 řádků C# (142 souborů) · ~7 600 řádků TS/TSX (38 souborů) · 13 testovacích souborů · 59 commitů (2026‑03‑21 → 2026‑04‑01).

---

## 2. Stav implementace

**Verdikt: zralý, prakticky funkčně kompletní projekt — výrazně dál, než naznačuje kostra ve specifikaci. Implementace „přerostla" spec; `CLAUDE.md` je dnes na několika místech zastaralá vůči kódu, ne naopak.**

| Oblast | Spec | Skutečnost |
|---|---|---|
| **Fáze 1** (odečty, vyúčtování, domy/uživatelé, auth) | MVP | ✅ Hotovo a nad rámec |
| **Fáze 2** (Dokumenty, Finance, Notifikace) | „později" | ✅ **Plně postaveno** — včetně verzování dokumentů a kalkulačky záloh |
| Všech ~40 endpointů ze specifikace | — | ✅ Všechny přítomny, správná metoda + cesta + úroveň auth |
| Vrstvy Clean Architecture | — | ✅ Čisté závislosti (Domain nic, Application→Domain, Infrastructure→App+Domain, Functions skládá) |
| Výpočet vyúčtování (8 kroků) | — | ✅ Věrně dle spec, konfigurovatelná alokace ztráty |

---

## 3. Zdraví buildu / testů / lintu (reálně spuštěno)

| Krok | Výsledek | Poznámka |
|---|---|---|
| `dotnet build` (backend) | ❌ **SELHÁVÁ** | exit 1, **NU1605** package downgrade jako chyba — **ověřeno pod .NET 8 (4 chyby) i .NET 10 (9 chyb)** |
| `dotnet test` (backend) | ⚠️ **neproběhlo** | build selhal pod oběma SDK → testy se nespustily (nelze doložit, že procházejí) |
| `npm run build` (frontend) | ✅ **prošlo** | `tsc -b && vite build` OK, bundle 1,05 MB JS (varování > 500 kB) |
| `npm run lint` (frontend) | ❌ **SELHÁVÁ** | 6 chyb, React Compiler memoizace v `ReadingsOverviewPage.tsx` |

**Proč backend nejde sestavit (a proč je to vážné):**
Příčinou jsou **nepinované floating verze balíčků**, ne verze SDK. `Azure.Storage.Blobs 12.*` / `Azure.Data.Tables 12.*` se resolvnou na novější verze táhnoucí `Azure.Core 1.55.0`, který tranzitivně vyžaduje `Microsoft.Extensions.* ≥ 10.0.3`, zatímco projekty mají přímé reference pinované na `8.*`. Vznikne **package downgrade 10.x → 8.x**, který NuGet hlásí jako chybu NU1605 (v csproj není `TreatWarningsAsErrors` — jde o default chování).

- **Ověřeno empiricky:** build selhal `dotnet build` jak pod vynuceným **.NET 8 SDK (8.0.422)**, tak pod **.NET 10**. Není to tedy artefakt `rollForward: latestMajor` — ten pouze mění počet a konkrétní chyby (9 vs 4), příčina je ve floating verzích.
- **Časovaná bomba z floating verzí (potvrzeno):** poslední běh CI (commit `349ffe7`, 2026‑04‑01) byl **zelený** — tehdy se `Azure.* 12.*` resolvlo na starší verze kompatibilní s `Extensions 8.x`. Od té doby novější verze (`Azure.Storage.Blobs 12.29.0` → `Azure.Core 1.55.0`) vyžadují `Extensions ≥ 10.0.3` → konflikt. **Build je proto rozbitý teď** a **další běh CI (`setup-dotnet 8.0.x`) pravděpodobně spadne taky**, protože restore nově sáhne po `Azure.Core 1.55.0`.
- `rollForward: latestMajor` v `global.json` situaci jen zhoršuje (vývojář s .NET 10 narazí ještě dřív), ale **není kořenovou příčinou**.
- **Oprava:** pinovat verze balíčků (ideálně `Directory.Packages.props` / Central Package Management) tak, aby `Azure.*` netáhly `Extensions 10.x` proti pinům `8.*` — buď srovnat `Extensions.*` na `10.x`, nebo zafixovat `Azure.Core`/`Azure.Storage.Blobs` na verzi kompatibilní s `Extensions 8.x`.

---

## 4. Potvrzené nálezy (ověřeno na soubor:řádek)

Seřazeno dle priority. Vše níže prošlo adversariálním ověřením.

### 🔴 Vysoká priorita

| # | Nález | Místo |
|---|---|---|
| 1 | **Backend nejde sestavit** — NU1605 package downgrade z **nepinovaných floating verzí** (`Azure.* → Azure.Core 1.55.0` vyžaduje `Extensions ≥ 10.x` proti pinům `8.*`). Ověřeno pod .NET 8 i 10. CI bylo 2026‑04‑01 zelené, ale floaty driftly → další běh nejspíš spadne (viz §3). | `*.csproj` (viz #12) |
| 2 | **Klíčová peněžní operace bez testu** — `CloseBillingPeriodAsync` (zápis finálního vyúčtování + nevratné uzamčení období) žije ve Functions vrstvě, kde je jen `SampleTest.cs` (`Assert.True(true)`). Mapování preview→uložené entity, dvojí uzavření ani úplnost settlementů nikdo netestuje. | `BillingPeriodFunctions.cs:189` |
| 3 | **Chybí Azurite integrační testy** požadované spec — žádný projekt `Oaza.Infrastructure.Tests`, žádná Azurite reference. PK/RK dotazy 12 repozitářů (inverted‑timestamp, `YYYY-MM`, date‑range) jsou netestované. | `api/tests/` |

### 🟠 Střední priorita

| # | Nález | Místo |
|---|---|---|
| 4 | **Admin může smazat sám sebe** — pojistka „nelze smazat sebe" čte `Items["UserId"]`, ale middleware ukládá uživatele pod klíč `"AuthenticatedUser"`. Klíč `"UserId"` nikdo nezapisuje → `currentUserId` je vždy `null`, podmínka je mrtvý kód. | `UserFunctions.cs:252` |
| 5 | **Únik dat napříč domy (RBAC)** — `GET /advance-settings` a `GET /advance-settings/calculate` nemají `[RequireRole]`, takže **kterýkoli přihlášený Member** vidí spotřebu, podíly a doporučené zálohy **všech domů**. Spec: Member = „read own house data". (`PUT` je správně Admin‑only.) | `AdvanceSettingsFunctions.cs:45` |
| 6 | **Alokace ztráty v doporučení záloh je natvrdo „rovným dílem"** — `AdvanceSettings` ukládá a v UI zobrazuje `LossAllocationMethod` (default `ProportionalToConsumption`), ale výpočet vždy dělá `monthlyLoss / početDomů`. Uložená metoda nemá na výpočet **žádný** vliv → uživatel vidí label „proporcionálně", dostane rovný díl. (Týká se jen *doporučení záloh*, ne ostrého vyúčtování — to je správně konfigurovatelné.) | `AdvanceSettingsFunctions.cs:167` |
| 7 | **Kontrola duplicit při importu je „per den", potvrzení „per měsíc"** — preview hlásí duplicitu jen při shodě celého data, ale `ConfirmImportAsync` i ruční zadání kontrolují rok+měsíc. Druhý odečet ve stejném měsíci jiného dne projde preview, pak spadne na 409 při uložení s hláškou „data se mohla změnit" (ač se nezměnila). | `ImportReadingsUseCase.cs:194` |
| 8 | **JWT claim remapping ruší primární lookup přes `sub`** — výchozí `MapInboundClaims=true` přemapuje `sub`→`NameIdentifier`, takže `FindFirstValue("sub")` vrací `null` a přihlášení přežívá jen díky e‑mailovému fallbacku. Křehké (změna e‑mailu může mis‑resolvnout). Oprava: `handler.MapInboundClaims=false`. *(Ověřeno samostatným repro.)* | `JwtService.cs:92` |
| 9 | **Settlement „closest‑reading" fallback bez testu** — `GetMeterConsumptionAsync` při chybějícím odečtu před začátkem období sáhne po nejstarším dostupném (může podhodnotit spotřebu). Všechny testy dávají odečty přesně na hranicích → větev netestovaná. | `CalculateSettlementUseCase.cs:221` |
| 10 | **Jen 3 z 19 validátorů mají testy** — netestované mj. `CreateUser` (role/dům), `MagicLinkRequest/Verify` (auth vstup), `CreateInvoice/CreateAdvance` (peníze). | `Validators/` |
| 11 | **Žádné frontend testy** — `web/package.json` nemá `test` skript ani runner (vitest/jest). Auth, RBAC routing, formátování peněz — bez automatické pokrytí. | `web/package.json` |
| 12 | **Nepinované floating verze NuGet** (`8.*`, `2.*`, `12.*`, `0.*`…) — **kořenová příčina nálezu #1**, nereprodukovatelné buildy. Bez Central Package Management. | všechny `*.csproj` |

### 🟡 Nízká priorita / kvalita

- **Download dokumentu neodpovídá spec** — spec: „Get SAS URL"; kód streamuje bajty skrz Function. Metoda `BlobStorageService.GetDownloadUrlAsync` (sestavuje SAS) je **mrtvý kód** (žádný caller). Proxy přístup je bezpečnější, ale dražší na egress. — `DocumentFunctions.cs:190`
- **Zaokrouhlovací reziduum vyúčtování** — částky se zaokrouhlují per dům nezávisle; součet se nemusí na haléř rovnat faktuře (žádný residual‑allocation krok). Pro 8 domů max pár haléřů. — `CalculateSettlementUseCase.cs:149`
- **`UploadedAt` u Document/DocumentVersion bez `SpecifyKind(Utc)`** — na rozdíl od ostatních peněžních/datových mapperů (pozn.: User mapper píše `MagicLinkExpiry`/`LastLogin` syrově také). Dnes neškodí (call‑sites používají `UtcNow`), ale je to nekonzistence / latentní křehkost. — `TableEntityMapper.cs:269,300`
- **`GET /invoices` řeší roli inline** místo `[RequireRole(Admin, Accountant)]` jako jinde — funkční, ale snadno regresní. — `InvoiceFunctions.cs:50`
- **FluentValidation validátory nejsou v DI** — instancují se `new XxxValidator()` v endpointech; odchylka od konvence (spec: validátory přes DI). — `HouseFunctions.cs:88`
- **Anonymní `POST /seed` vytvoří Admina** když `ENABLE_SEED=true` — dvojitě hlídováno (404 bez env var + idempotence), ale doporučeno po bootstrapu vypnout. — `SeedFunctions.cs:43`
- **Timing side‑channel na `/auth/magic-link`** — známý e‑mail dělá upsert+odeslání před odpovědí, neznámý se vrací hned → měřitelný rozdíl umožní enumeraci e‑mailů. Nízké riziko u 15 uživatelů. — `RequestMagicLinkUseCase.cs:33`
- **Web CI nemá lint/test gate** — `build-web` spouští jen `npm ci` + `npm run build`, ne `npm run lint` (a lint je dnes červený, viz §3). API stranu CI gateuje na `dotnet test`. — `deploy.yml:52`
- **`docs/DEPLOYMENT-DEV.md` je zastaralý** — stále dokumentuje odstraněný **SendGrid** (`SendGrid__ApiKey` …); projekt přešel na Azure Communication Services. Podle návodu by se magic‑link e‑maily nenakonfigurovaly. — `DEPLOYMENT-DEV.md:240`
- **Zbytkové `SampleTest.cs`** ve všech 3 test projektech; u `Oaza.Functions.Tests` je to jediný test (prázdná zelená výplň).
- **Bundle 1,05 MB** JS (gzip 283 kB) — varování Vite > 500 kB; zvážit code‑splitting.
- **Drobnosti FE:** šířka sidebaru 260 px vs spec 200 px; chybí catch‑all route; `staticwebapp.config.json` duplikován v `web/` i `web/public/`; `documents.ts:29` čte `error.error` z netypovaného JSON (implicit‑any).

---

## 5. Co je nad rámec specifikace (spec je zastaralá, ne kód neúplný)

Tyto věci v kódu **jsou**, ale `CLAUDE.md` je nepopisuje — doporučuji doplnit spec:

- **Verzování dokumentů** — entita `DocumentVersion` + tabulka + repo, 3 endpointy (`POST/GET .../versions`, download verze), drží posledních 10 verzí.
- **Subsystém `AdvanceSettings`** — cenotvorba záloh (cena za m³ vody, koeficienty elektřiny per dům, společný poplatek, per‑house overrides) + endpointy `GET/PUT /advance-settings`, `GET /advance-settings/calculate`. Spec zná zálohy jen jako zaznamenané `AdvancePayment`.
- **Bezpečnostní vylepšení magic‑linku** — token uložen jako **SHA‑256 hash** (`MagicLinkTokenHash`), ne plaintext jak píše spec; navíc rate‑limit počítadla + lockout po 5 pokusech (pole `MagicLinkRequestCount`, `…WindowStart`, `…FailedAttempts`).
- **Extra endpointy:** `DELETE /users/{id}`, `GET /readings/all`, `GET /finance/balance`, `POST /seed`.
- **`WaterMeter.Name`** (zobrazovací popisek).

> Pozn.: zdánlivý rozpor „Entra přes MSAL.js vs SWA proxy" je **vnitřní rozpor specifikace** — `CLAUDE.md:305` říká SWA built‑in proxy, ale `CLAUDE.md:422‑427` popisuje klientský MSAL.js. Kód odpovídá řádkům 422‑427; konflikt je jen s řádkem 305.

---

## 6. Co je v pořádku (potvrzená shoda se specifikací)

- ✅ **Vrstvy Clean Architecture** čisté v csproj i v reálných `using` (Domain bez závislostí).
- ✅ **DI kompletní** — každý repozitář, use‑case, služba, middleware i timer trigger má registraci a konzumenta.
- ✅ **Výpočet vyúčtování** — všech 8 kroků; **konfigurovatelná alokace ztráty** (Equal i Proportional v `AllocateLoss`); celek období se **počítá, neukládá**; zálohy sčítány per měsíc v rozsahu období; znaménko balance (+ = doplatek). Uzavření období **nevratné**, zamyká odečty i faktury.
- ✅ **Český formát čísel** při importu (čárka, mezery v tisících, cs‑CZ kultura) + 5 validačních pravidel + dvoukrokový import.
- ✅ **Timer CRON** `0 0 8 1 * *` přesně dle spec.
- ✅ **JWT** HMAC‑SHA256, secret z konfigurace (≥256 bit), validace issuer/audience/lifetime/signature, claimy `sub/email/role/houseId/authMethod`, 24h expirace. **Žádné hardcoded secrety** v `api/src`.
- ✅ **RBAC middleware fail‑closed**, anonymní plocha = právě `{magic-link, magic-link/verify, seed}`. Per‑house scoping pro Membery v odečtech.
- ✅ **FE konvence** — JWT v paměti (ne localStorage), žádné `any`/`console.log`, TS strict, `Intl` cs‑CZ, dvoukrokový import, ztráta explicitně v UI.
- ✅ **Hygiena** — žádné commitnuté secrety, `.env.example` jen placeholdery, CI vše přes `${{ secrets.* }}`, API deploy gateuje na `dotnet test`.

---

## 7. Doporučení (priorita shora)

1. **Opravit build** — **pinovat verze NuGet** (CPM přes `Directory.Packages.props`) tak, aby `Azure.*` netáhly `Extensions 10.x` proti pinům `8.*`. Samotná změna `rollForward` nestačí — build padá i pod .NET 8. (#1, #12)
2. **Otestovat peněžní cestu** — testy pro `CloseBillingPeriodAsync` (mapování, dvojí uzavření, úplnost) a pro `GetMeterConsumptionAsync` hraniční/fallback větve (#2, #9). Ideálně doplnit i Azurite integrační testy repozitářů (#3).
3. **Zavřít RBAC díru** — přidat `[RequireRole]`/per‑house filtr na `advance-settings` read endpointy (#5).
4. **Opravit mrtvou pojistku** self‑delete (#4) a JWT `MapInboundClaims=false` (#8).
5. **Sjednotit duplicitní kontrolu importu** na per‑měsíc i v preview (#7) a **napojit `LossAllocationMethod`** do výpočtu doporučených záloh (#6).
6. **Aktualizovat `CLAUDE.md`** o reálné přírůstky (verzování dokumentů, `AdvanceSettings`, hash tokenů, extra endpointy) a opravit `DEPLOYMENT-DEV.md` (SendGrid → ACS).
7. **FE kvalita** — opravit červený lint, přidat lint/test gate do web CI, zvážit code‑splitting bundlu.

---

*Hloubková shoda kód ↔ `CLAUDE.md`. Nálezy s prioritou High/Med byly adversariálně ověřeny na úroveň `soubor:řádek`; build/test/lint spuštěny reálně na tomto stroji.*
