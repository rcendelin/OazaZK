# Lokální vývoj — Oáza Zadní Kopanina

Návod, jak rozběhnout API i frontend na vlastním stroji proti lokálnímu emulátoru úložiště (Azurite).

---

## Prerekvizity

| Nástroj | Verze | Poznámka |
|---------|-------|----------|
| .NET SDK | 8.0.x | `api/global.json` zamyká SDK na .NET 8 (`rollForward: latestFeature`) |
| Azure Functions Core Tools | v4 | příkaz `func` |
| Node.js | 20.x | stejná verze jako v CI (`NODE_VERSION` ve workflow) |
| Docker | libovolná | pro Azurite |
| SWA CLI *(volitelně)* | `npm i -g @azure/static-web-apps-cli` | proxy frontend + API na jednom originu |

---

## 1. Azurite (Table + Blob Storage)

```bash
docker run -d --name azurite \
  -p 10000:10000 -p 10001:10001 -p 10002:10002 \
  mcr.microsoft.com/azure-storage/azurite
```

Tabulky a blob kontejnery si aplikace vytváří sama při prvním použití.

---

## 2. API (Azure Functions)

```bash
cd api/src/Oaza.Functions
cp local.settings.json.example local.settings.json   # soubor je v .gitignore
func start                                            # http://localhost:7071/api
```

### Konfigurace (`local.settings.json` → `Values`)

| Klíč | Povinný | Význam |
|------|---------|--------|
| `AzureWebJobsStorage` | ano | Úložiště Functions runtime; zároveň záloha pro dva klíče níže |
| `TableStorageConnection` | ne* | Table Storage (*jinak se použije `AzureWebJobsStorage`) |
| `BlobStorageConnection` | ne* | Blob Storage (*jinak se použije `AzureWebJobsStorage`) |
| `JwtSecret` | ano | Klíč pro podpis vlastních JWT (HS256), **min. 32 znaků** — jinak `JwtService` vyhodí výjimku a požadavky selžou |
| `JwtIssuer` | ano | Issuer i audience vlastních JWT |
| `AppUrl` | ne | Základ URL v e-mailech (odkaz pro přihlášení, pozvánka). Výchozí `https://oaza.cendelinovi.cz` |
| `EntraId__TenantId`, `EntraId__ClientId` | ne | Přihlášení přes Microsoft Entra ID. Prázdné = Entra vypnuto |
| `AzureCommunicationServices__ConnectionString`, `__FromEmail`, `__FromName` | ne | Odesílání e-mailů (ACS). Prázdné = e-maily se neodešlou (chyba se jen zaloguje) |
| `ENABLE_SEED` | ne | `true` zpřístupní `POST /api/seed` (bez autentizace!) — **nikdy nezapínej v PROD** |
| `OFF_BOOK_FUND_ENABLED` | ne | `true` zapne modul Oddělený fond (T10). Výchozí vypnuto, dokud není vyřešeno právní a daňové řešení (O4). |

`Host.CORS` v příkladu povoluje volání z Vite dev serveru (`http://localhost:5173`).

### Seed dat

```bash
curl -X POST http://localhost:7071/api/seed
```

Vytvoří 8 domácností, hlavní vodoměr `HV-001`, domovní vodoměry `DV-001`–`DV-008` a jednoho administrátora (viz `api/src/Oaza.Functions/Endpoints/SeedFunctions.cs`). Pokud už nějaký dům existuje, nic nedělá.

---

## 3. Frontend (React + Vite)

```bash
cd web
cp .env.example .env.local
npm ci
npm run dev                 # http://localhost:5173
```

| Proměnná | Význam |
|----------|--------|
| `VITE_API_BASE_URL` | Adresa API. Vite **nemá dev proxy**, takže pro samostatný běh nastav `http://localhost:7071/api`. Prázdné = relativní `/api` (funguje přes SWA CLI). |
| `VITE_ENTRA_CLIENT_ID`, `VITE_ENTRA_TENANT_ID` | Konfigurace MSAL pro přihlášení Microsoft účtem. |

> Všechny `VITE_*` proměnné se vkládají do veřejného JS bundlu — **nikdy do nich nedávej tajemství**.

Alternativa bez CORS — SWA CLI spojí oba servery na jednom originu:

```bash
swa start http://localhost:5173 --api-location http://localhost:7071
```

---

## 4. Přihlášení při lokálním vývoji

API přijímá dva druhy tokenů (viz [ARCHITEKTURA.md](ARCHITEKTURA.md#4-autentizace)). Lokálně je potřeba mít funkční alespoň jeden:

- **Magic link** — vyžaduje vyplněné `AzureCommunicationServices__*` (lze použít sdílenou DEV ACS resource). Odkaz přijde e-mailem a vede na `AppUrl`, proto `AppUrl=http://localhost:5173`. Odkaz se **neloguje**, bez ACS se tímto způsobem přihlásit nelze.
- **Microsoft Entra ID** — vyplň `EntraId__*` v API i `VITE_ENTRA_*` ve webu a v registraci aplikace v Entra přidej redirect URI `http://localhost:5173` (typ SPA).

Uživatel musí existovat v tabulce `Users` (seed vytvoří administrátora; další přidáš v UI *Administrace → Uživatelé*).

> Po obnovení stránky se magic-link session ztrácí (JWT je jen v paměti React stavu) — je to záměr.

---

## 5. Build, testy, lint

```bash
# Backend — vždy přes Oaza.sln (Oaza.slnx je zastaralý)
cd api
dotnet build Oaza.sln
dotnet test Oaza.sln

# Pokrytí výpočetní logiky (stejná kontrola jako v CI, hranice 90 %)
dotnet test Oaza.sln --collect:"XPlat Code Coverage" --results-directory ./coverage
python3 tools/coverage_gate.py ./coverage

# Frontend
cd web
npm run lint                # CI gate — musí projít
npm run build               # tsc -b && vite build
```

- **Unit testy:** `Oaza.Domain.Tests`, `Oaza.Application.Tests` (use cases, validátory, mapování), `Oaza.Functions.Tests` (JWT).
- **Integrační testy:** `Oaza.Infrastructure.Tests` běží proti Azurite. Používají `[SkippableFact]` — bez běžícího Azurite se přeskočí (CI Azurite spouští jako service container).
- **Frontend** nemá unit testy; lint používá přísná pravidla React Compileru (`react-hooks/preserve-manual-memoization`, `set-state-in-effect`).
- **E2E smoke testy (Playwright)** v `web/e2e/` běží proti produkčnímu buildu (`vite preview`) s podvrženým API
  (`e2e/mockApi.ts`), backend se nespouští. V CI job `e2e`. Lokálně:
  ```bash
  cd web
  npx playwright install chromium   # jednou
  npm run build && npx playwright test
  ```
  Nový test: `mockApi(page, { '/cesta': () => odpověď })`, přihlášení `signInAsAdmin`, přechod `navigate` (reload by zahodil JWT v paměti).
- **Property-based testy:** FsCheck (`FsCheck.Xunit`, atribut `[Property]`) v `Oaza.Domain.Tests` — invarianty výpočtů na náhodných vstupech (vzor: `AllocatorPropertyTests`). Použít pro ledger T07 (Σ rozpočtu = celkem).
- **Warningy = chyby.** `api/Directory.Build.props` zapíná `TreatWarningsAsErrors` (výjimka: NuGet advisories NU1901–NU1904).
- **CI na pull requestech** (`.github/workflows/ci.yml`): build API bez warningů, testy, kontrola pokrytí souborů
  z `api/coverage-gate.txt` (výpočetní logika, kombinovaně ≥ 90 %; nové výpočty tam přidávat) a lint + build webu.
  Deploy workflowy běží až po pushi do `develop` / `release/**` / `master`.

---

## 6. Časté problémy

| Příznak | Příčina / řešení |
|---------|------------------|
| API vrací 500, v logu chyba `JwtService` | `JwtSecret` kratší než 32 znaků nebo chybí `JwtIssuer` |
| `Table Storage connection string is not configured` | chybí `AzureWebJobsStorage` i `TableStorageConnection` |
| CORS chyba v prohlížeči | `Host.CORS` v `local.settings.json` neodpovídá originu frontendu, nebo použij SWA CLI |
| 403 „Uživatel není v systému registrován" | token je platný, ale uživatel není v tabulce `Users` (Entra: páruje se `oid`, pak e-mail) |
| Potvrzení importu vrací 404 | náhled vypršel (30 min) nebo se restartoval `func` — náhled je jen v paměti |
| Build API selže na verzích balíčků | nepoužívej floating verze NuGet (`12.*`) — viz `CLAUDE.md` |
