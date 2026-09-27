# Oáza Zadní Kopanina — komunitní portál

Webový portál sdružení **Oáza Zadní Kopanina** (Praha, 8 domácností, ~15 uživatelů). Hlavní účel je správa společného vodovodu: měsíční odečty vodoměrů, zálohy, saldo a pololetní vyúčtování vody včetně rozpočtení ztrát v síti. Vedle toho portál slouží jako úložiště dokumentů spolku a přehled jeho hospodaření.

- **Produkce:** <https://oaza.cendelinovi.cz>
- **UI:** česky · **kód, komentáře, commity:** anglicky
- **Provoz:** jedna osoba (Rosťa Čendelín)

---

## Funkce

| Oblast | Co umí |
|--------|--------|
| **Odečty** | import z Excelu nebo ze schránky (export odečítacího zařízení), ruční zadání, opravy, graf spotřeby, detekce anomálií |
| **Zálohy** | výpočet doporučených měsíčních záloh (voda, elektřina vodárny, společný základ), ruční přepisy per dům |
| **Saldo a platby** | evidence záloh, doplatků, výplat přeplatků a počátečních stavů; jedno čisté saldo na dům |
| **Vyúčtování** | zúčtovací období, faktury dodavatele vody, výpočet podílů se ztrátou, čerpání ze společného fondu, uzavření s PDF vyúčtováním |
| **Hospodaření** | příjmy a výdaje spolku, přílohy, roční export PDF/XLSX, zůstatek fondu, přehled přijatých faktur |
| **Dokumenty** | stanovy, zápisy, smlouvy — verzované (posledních 10 verzí) |
| **Nápověda** | kontextová nápověda a stránka *Jak to funguje* se slovníkem pojmů |
| **Přihlášení** | Microsoft Entra ID nebo odkaz e-mailem (magic link); role Admin / Účetní / Člen |

---

## Architektura v kostce

```
React 19 SPA ──────────── Azure Static Web Apps (Free)
     │ HTTPS /api + Bearer token
.NET 8 Azure Functions ── Consumption plan, isolated worker
     │ Azure.Data.Tables / Azure.Storage.Blobs
Azure Table Storage + Blob Storage (LRS)      Entra ID · Azure Communication Services (e-mail)
```

Žádná relační databáze — pouze Table Storage přes repository pattern. Podrobnosti v [docs/ARCHITEKTURA.md](docs/ARCHITEKTURA.md).

---

## Struktura repozitáře

```
OazaZK/
├── README.md                 # tento soubor
├── CLAUDE.md                 # pravidla a kontext pro AI asistenta (konvence, omezení)
├── .github/workflows/        # CI/CD: deploy-dev.yml, deploy-test.yml, deploy.yml (PROD)
├── api/                      # .NET 8 backend
│   ├── Oaza.sln              # používej .sln (Oaza.slnx je zastaralý)
│   ├── global.json           # zámek SDK na .NET 8
│   ├── src/
│   │   ├── Oaza.Domain/          # entity, enumy, konstanty, rozhraní repozitářů
│   │   ├── Oaza.Application/     # use cases, DTO, validátory, mapování
│   │   ├── Oaza.Infrastructure/  # Table/Blob Storage, JWT, Entra, e-mail (ACS)
│   │   └── Oaza.Functions/       # HTTP endpointy, timer, middleware, DI
│   └── tests/                # xUnit — Domain, Application, Functions, Infrastructure (Azurite)
├── web/                      # React 19 + Vite + Tailwind frontend
│   └── src/                  # api/, auth/, components/, content/help.ts, hooks/, pages/, types/
└── docs/                     # dokumentace (viz níže)
```

---

## Rychlý start

```bash
# 1. Azurite (emulátor Table + Blob Storage)
docker run -d -p 10000:10000 -p 10001:10001 -p 10002:10002 mcr.microsoft.com/azure-storage/azurite

# 2. API
cd api/src/Oaza.Functions
cp local.settings.json.example local.settings.json
func start
curl -X POST http://localhost:7071/api/seed        # výchozí data (ENABLE_SEED=true)

# 3. Frontend
cd web
cp .env.example .env.local                          # nastav VITE_API_BASE_URL=http://localhost:7071/api
npm ci && npm run dev                               # http://localhost:5173
```

Přihlášení lokálně vyžaduje ACS nebo Entra ID — viz [docs/LOKALNI-VYVOJ.md](docs/LOKALNI-VYVOJ.md).

### Testy a kontroly

```bash
cd api && dotnet test Oaza.sln     # integrační testy potřebují běžící Azurite, jinak se přeskočí
cd web && npm run lint && npm run build
```

Lint i testy jsou v CI blokující.

---

## Prostředí a nasazení

| Prostředí | Branch | URL |
|-----------|--------|-----|
| DEV | `develop` | <https://oaza-dev.cendelinovi.cz> |
| TEST | `release/**` | <https://oaza-test.cendelinovi.cz> |
| PROD | `master` | <https://oaza.cendelinovi.cz> |

Push do větve spustí příslušný GitHub Actions workflow: build a testy → lint a build webu → nasazení Functions a Static Web App. Založení prostředí popisují [docs/DEPLOYMENT-DEV.md](docs/DEPLOYMENT-DEV.md) a [docs/DEPLOYMENT-TEST-PROD.md](docs/DEPLOYMENT-TEST-PROD.md).

---

## Dokumentace

| Dokument | Pro koho | Obsah |
|----------|----------|-------|
| [docs/ARCHITEKTURA.md](docs/ARCHITEKTURA.md) | vývojář | vrstvy, datový model, autentizace, role, frontend, e-maily, CI/CD |
| [docs/VYUCTOVANI.md](docs/VYUCTOVANI.md) | vývojář, admin | jak přesně se počítá vyúčtování, zálohy, saldo a fond; import odečtů; známá omezení |
| [docs/API.md](docs/API.md) | vývojář | všechny endpointy, oprávnění, těla požadavků, chybové odpovědi |
| [docs/LOKALNI-VYVOJ.md](docs/LOKALNI-VYVOJ.md) | vývojář | lokální rozběhnutí, konfigurace, testy, časté problémy |
| [docs/DEPLOYMENT-DEV.md](docs/DEPLOYMENT-DEV.md) | provoz | založení DEV prostředí v Azure krok za krokem |
| [docs/DEPLOYMENT-TEST-PROD.md](docs/DEPLOYMENT-TEST-PROD.md) | provoz | TEST a PROD, sdílené prostředky |
| [docs/architecture-notes.md](docs/architecture-notes.md) | vývojář | rozcestník podle osnovy zadání (stack, entity, výpočty, role, nasazení, testy) |
| [docs/gap-analysis.md](docs/gap-analysis.md) | vývojář, zadavatel | co ze zadání T01–T14 existuje, co chybí, co nahradí starý kód |
| [docs/open-questions.md](docs/open-questions.md) | zadavatel | rozhodnutí z 27. 9. 2026, rizika, otevřené otázky O1–O4 |
| [docs/ANALYZA-ADRESARE.md](docs/ANALYZA-ADRESARE.md) | vývojář | audit kódu z června 2026 a stav nápravy |
| [docs/superpowers/](docs/superpowers/) | vývojář | návrhy (specs) a implementační plány jednotlivých funkcí |
| aplikace → **Jak to funguje** | uživatel | uživatelská nápověda a slovník pojmů (`web/src/content/help.ts`) |

---

## Konvence

- **Větve:** práce ve feature větvích → PR do `develop`; `release/*` pro TEST; `master` = produkce. Žádný force push do `master`.
- **Commity:** [Conventional Commits](https://www.conventionalcommits.org/) anglicky (`feat:`, `fix:`, `docs:`, `chore:`).
- **Backend:** async všude (`…Async`), peníze vždy `decimal`, žádné magic strings (konstanty v `Oaza.Domain/Constants`), DTO místo entit, validace FluentValidation, NuGet verze pinované (žádné `12.*`).
- **Frontend:** funkční komponenty, TypeScript strict bez `any`, Tailwind, české formátování přes `Intl` (`cs-CZ`), texty nápovědy jen v `web/src/content/help.ts`.
- Při změně výpočtu aktualizuj [docs/VYUCTOVANI.md](docs/VYUCTOVANI.md) i související texty v `help.ts`; při změně endpointu [docs/API.md](docs/API.md).
