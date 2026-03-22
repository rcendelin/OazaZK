# Návod na zprovoznění DEV prostředí — Oáza Zadní Kopanina

> **Cíl:** Funkční DEV prostředí na Azure + GitHub Actions CI/CD, na kterém půjde celý portál ověřit před nasazením do TEST a PROD.

---

## Přehled prostředí

| Prostředí | Branch | Doména | Účel |
|-----------|--------|--------|------|
| **DEV** | `develop` | `oaza-dev.cendelinovi.cz` | Vývoj, testování |
| TEST | `release/*` | `oaza-test.cendelinovi.cz` | UAT (později) |
| PROD | `main` | `oaza.cendelinovi.cz` | Produkce (později) |

---

## Prerekvizity

- [ ] Azure subscription (Free Trial nebo Pay-As-You-Go)
- [ ] Azure CLI nainstalované (`az --version` ≥ 2.50)
- [ ] GitHub CLI nainstalované (`gh --version` ≥ 2.0)
- [ ] Přihlášen do Azure: `az login`
- [ ] Přihlášen do GitHub: `gh auth login`
- [ ] Přístup k DNS záznamům domény `cendelinovi.cz`
- [ ] Git repo: https://github.com/rcendelin/OazaZK.git (naklonováno)

---

## Krok 1 — Azure Resource Group

```bash
az group create \
  --name rg-oaza-dev \
  --location westeurope \
  --tags environment=dev project=oaza
```

Ověření:
```bash
az group show --name rg-oaza-dev --query "{name:name, location:location}" -o table
```

---

## Krok 2 — Storage Account (Table + Blob)

```bash
az storage account create \
  --name stoazadev \
  --resource-group rg-oaza-dev \
  --location westeurope \
  --sku Standard_LRS \
  --kind StorageV2 \
  --min-tls-version TLS1_2 \
  --allow-blob-public-access false
```

> **Pojmenování:** `stoazadev` (storage account name je globálně unikátní, max 24 znaků, lowercase). Pokud je obsazené, zvol např. `stoazadev01`.

Získej connection string (budeš ho potřebovat v kroku 5):
```bash
STORAGE_CONN=$(az storage account show-connection-string \
  --name stoazadev \
  --resource-group rg-oaza-dev \
  --query connectionString -o tsv)

echo "Connection string:"
echo "$STORAGE_CONN"
```

> **Zapiš si ho** — použiješ ho jako `TableStorageConnection`, `BlobStorageConnection` i `AzureWebJobsStorage`.

---

## Krok 3 — Azure Functions App

### 3.1 Vytvoření

```bash
az functionapp create \
  --name func-oaza-dev \
  --resource-group rg-oaza-dev \
  --storage-account stoazadev \
  --consumption-plan-location westeurope \
  --runtime dotnet-isolated \
  --runtime-version 8 \
  --functions-version 4 \
  --os-type Linux \
  --tags environment=dev project=oaza
```

> Pokud `func-oaza-dev` je obsazené, zvol `func-oaza-dev-01` a poznamenej si skutečný název.

### 3.2 Konfigurace (App Settings)

Vygeneruj JWT secret:
```bash
JWT_SECRET=$(openssl rand -base64 32)
echo "JWT Secret: $JWT_SECRET"
```

Nastav všechny proměnné:
```bash
az functionapp config appsettings set \
  --name func-oaza-dev \
  --resource-group rg-oaza-dev \
  --settings \
    "TableStorageConnection=$STORAGE_CONN" \
    "BlobStorageConnection=$STORAGE_CONN" \
    "JwtSecret=$JWT_SECRET" \
    "JwtIssuer=oaza-dev.cendelinovi.cz" \
    "AppUrl=https://oaza-dev.cendelinovi.cz" \
    "ENABLE_SEED=true"
```

> **Entra ID a SendGrid** nastavíme až po krocích 4 a 6. Prozatím je necháme prázdné — portál bude fungovat s magic link auth (po nastavení SendGrid) a bez Entra ID.

### 3.3 Stáhni Publish Profile

```bash
az functionapp deployment list-publishing-profiles \
  --name func-oaza-dev \
  --resource-group rg-oaza-dev \
  --xml > /tmp/func-oaza-dev-publish-profile.xml

cat /tmp/func-oaza-dev-publish-profile.xml
```

> **Celý obsah XML** budeš potřebovat jako GitHub Secret v kroku 7.

### 3.4 Nastav CORS

```bash
az functionapp cors add \
  --name func-oaza-dev \
  --resource-group rg-oaza-dev \
  --allowed-origins "https://oaza-dev.cendelinovi.cz" "http://localhost:5173"
```

---

## Krok 4 — Entra ID App Registration

### 4.1 Vytvoření aplikace

1. Otevři **[Azure Portal → Entra ID → App registrations](https://portal.azure.com/#view/Microsoft_AAD_RegisteredApps/ApplicationsListBlade)**
2. Klikni **New registration**
3. Vyplň:
   - **Name:** `Oáza ZK — DEV`
   - **Supported account types:** `Accounts in this organizational directory only` (Single tenant)
   - **Redirect URI:**
     - Platform: `Single-page application (SPA)`
     - URI: `https://oaza-dev.cendelinovi.cz`
4. Klikni **Register**

### 4.2 Poznamenej si ID

Na stránce Overview nové aplikace najdeš:
- **Application (client) ID** → zapiš jako `ENTRA_CLIENT_ID`
- **Directory (tenant) ID** → zapiš jako `ENTRA_TENANT_ID`

### 4.3 Přidej localhost pro vývoj

1. Jdi na **Authentication → Platform configurations → SPA**
2. Klikni **Add URI**
3. Přidej: `http://localhost:5173`
4. Ulož

### 4.4 Ověř tokeny

V sekci **Authentication**:
- Zkontroluj, že jsou zaškrtnuté: **Access tokens** a **ID tokens** (pod Implicit grant)

### 4.5 API Permissions

Výchozí permissions by měly stačit:
- `Microsoft Graph → User.Read` (delegated)

Pokud chybí, přidej:
1. **API permissions → Add a permission → Microsoft Graph → Delegated**
2. Vyber: `openid`, `profile`, `email`
3. Klikni **Grant admin consent**

### 4.6 Nastav Entra ID do Functions

```bash
az functionapp config appsettings set \
  --name func-oaza-dev \
  --resource-group rg-oaza-dev \
  --settings \
    "EntraId__TenantId=<ENTRA_TENANT_ID>" \
    "EntraId__ClientId=<ENTRA_CLIENT_ID>"
```

> Nahraď `<ENTRA_TENANT_ID>` a `<ENTRA_CLIENT_ID>` skutečnými hodnotami z kroku 4.2.

---

## Krok 5 — Azure Static Web Apps

### 5.1 Vytvoření

```bash
az staticwebapp create \
  --name swa-oaza-dev \
  --resource-group rg-oaza-dev \
  --location westeurope \
  --sku Free \
  --tags environment=dev project=oaza
```

### 5.2 Deployment token

```bash
SWA_TOKEN=$(az staticwebapp secrets list \
  --name swa-oaza-dev \
  --resource-group rg-oaza-dev \
  --query properties.apiKey -o tsv)

echo "SWA Token: $SWA_TOKEN"
```

> Zapiš si token — budeš ho potřebovat v kroku 7.

### 5.3 CORS na Functions App (místo Linked Backend)

> **Proč ne Linked Backend?** SWA Linked Backend vyžaduje Standard SKU (~210 Kč/měsíc). S Free tier používáme přímé volání frontendu na Functions App přes CORS.

```bash
az functionapp cors add \
  --name func-oaza-dev \
  --resource-group rg-oaza-dev \
  --allowed-origins "https://oaza-dev.cendelinovi.cz" "http://localhost:5173"
```

Frontend bude volat Functions App přímo přes `VITE_API_BASE_URL` env proměnnou (viz krok 7).

---

## Krok 6 — SendGrid (volitelné pro DEV)

> SendGrid je potřeba pro magic link přihlášení a notifikace. Pokud chceš DEV testovat jen s Entra ID, tento krok můžeš přeskočit.

1. Jdi na [sendgrid.com](https://app.sendgrid.com/signup) a vytvoř Free účet
2. **Settings → API Keys → Create API Key** (Full Access)
3. Zapiš API key

Nastav do Functions:
```bash
az functionapp config appsettings set \
  --name func-oaza-dev \
  --resource-group rg-oaza-dev \
  --settings \
    "SendGrid__ApiKey=<SENDGRID_API_KEY>" \
    "SendGrid__FromEmail=portal-dev@cendelinovi.cz" \
    "SendGrid__FromName=Oáza ZK DEV"
```

> **Sender verification:** V SendGrid → Settings → Sender Authentication ověř odesílatele (`portal-dev@cendelinovi.cz`) nebo celou doménu.

---

## Krok 7 — GitHub: Workflow pro DEV

### 7.1 Vytvoř GitHub Environment

```bash
# V GitHub UI: Settings → Environments → New environment → "dev"
# Nebo přes API (gh CLI nemá přímou podporu pro environments)
```

Jdi na https://github.com/rcendelin/OazaZK/settings/environments a vytvoř environment **`dev`**.

### 7.2 Nastav GitHub Secrets

Jdi na https://github.com/rcendelin/OazaZK/settings/secrets/actions a přidej:

| Secret | Hodnota | Poznámka |
|--------|---------|----------|
| `DEV_FUNCTIONAPP_PUBLISH_PROFILE` | Obsah XML z kroku 3.3 | Celý XML, ne jen URL |
| `DEV_SWA_API_TOKEN` | Token z kroku 5.2 | |
| `DEV_ENTRA_CLIENT_ID` | Application (client) ID z kroku 4.2 | |
| `DEV_ENTRA_TENANT_ID` | Directory (tenant) ID z kroku 4.2 | |
| `DEV_API_BASE_URL` | `https://func-oaza-dev.azurewebsites.net/api` | Functions App URL + /api |

### 7.3 Vytvoř DEV workflow

Potřebuješ nový workflow soubor, který deployuje z `develop` větve. Aktuální `deploy.yml` je pro `main` (PROD).

Vytvoř soubor `.github/workflows/deploy-dev.yml` (viz krok 8).

---

## Krok 8 — Úprava kódu pro multi-environment

### 8.1 Nový workflow `.github/workflows/deploy-dev.yml`

```yaml
name: Build and Deploy (DEV)

on:
  push:
    branches: [develop]

env:
  DOTNET_VERSION: '8.0.x'
  NODE_VERSION: '20.x'
  AZURE_FUNCTIONAPP_NAME: func-oaza-dev

jobs:
  build-api:
    name: Build & Test API
    runs-on: ubuntu-latest
    defaults:
      run:
        working-directory: api
    steps:
      - uses: actions/checkout@v4

      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: ${{ env.DOTNET_VERSION }}

      - name: Restore
        run: dotnet restore

      - name: Build
        run: dotnet build --configuration Release --no-restore

      - name: Test
        run: dotnet test --configuration Release --no-restore --verbosity normal

      - name: Publish
        run: dotnet publish src/Oaza.Functions/Oaza.Functions.csproj --configuration Release --output ./publish

      - name: Upload artifact
        uses: actions/upload-artifact@v4
        with:
          name: api-artifact
          path: api/publish

  build-web:
    name: Build Frontend
    runs-on: ubuntu-latest
    defaults:
      run:
        working-directory: web
    steps:
      - uses: actions/checkout@v4

      - name: Setup Node.js
        uses: actions/setup-node@v4
        with:
          node-version: ${{ env.NODE_VERSION }}
          cache: npm
          cache-dependency-path: web/package-lock.json

      - name: Install
        run: npm ci

      - name: Build
        run: npm run build
        env:
          VITE_ENTRA_CLIENT_ID: ${{ secrets.DEV_ENTRA_CLIENT_ID }}
          VITE_ENTRA_TENANT_ID: ${{ secrets.DEV_ENTRA_TENANT_ID }}
          VITE_API_BASE_URL: ${{ secrets.DEV_API_BASE_URL }}

      - name: Upload artifact
        uses: actions/upload-artifact@v4
        with:
          name: web-artifact
          path: web/dist

  deploy-api:
    name: Deploy API (DEV)
    needs: [build-api, build-web]
    runs-on: ubuntu-latest
    environment: dev
    steps:
      - name: Download artifact
        uses: actions/download-artifact@v4
        with:
          name: api-artifact
          path: api-artifact

      - name: Deploy to Azure Functions
        uses: Azure/functions-action@v1
        with:
          app-name: func-oaza-dev
          package: api-artifact
          publish-profile: ${{ secrets.DEV_FUNCTIONAPP_PUBLISH_PROFILE }}

  deploy-web:
    name: Deploy Frontend (DEV)
    needs: [build-api, build-web]
    runs-on: ubuntu-latest
    environment: dev
    steps:
      - uses: actions/checkout@v4

      - name: Download artifact
        uses: actions/download-artifact@v4
        with:
          name: web-artifact
          path: web/dist

      - name: Deploy to SWA
        uses: Azure/static-web-apps-deploy@v1
        with:
          azure_static_web_apps_api_token: ${{ secrets.DEV_SWA_API_TOKEN }}
          repo_token: ${{ secrets.GITHUB_TOKEN }}
          action: upload
          app_location: web/dist
          skip_app_build: true
          skip_api_build: true
```

---

## Krok 9 — Custom doména

### 9.1 DNS záznam

V DNS správci domény `cendelinovi.cz` přidej:

```
CNAME  oaza-dev  →  <swa-hostname>.azurestaticapps.net
```

Zjisti hostname:
```bash
az staticwebapp show \
  --name swa-oaza-dev \
  --resource-group rg-oaza-dev \
  --query defaultHostname -o tsv
```

### 9.2 Přidej doménu do SWA

```bash
az staticwebapp hostname set \
  --name swa-oaza-dev \
  --resource-group rg-oaza-dev \
  --hostname oaza-dev.cendelinovi.cz
```

HTTPS certifikát se vygeneruje automaticky (může trvat 5–15 minut).

### 9.3 Ověření

```bash
curl -I https://oaza-dev.cendelinovi.cz
```

Měl bys vidět `200 OK` s security headers (X-Frame-Options, CSP atd.).

---

## Krok 10 — První deploy a seed

### 10.1 Commitni DEV workflow

```bash
cd c:\PRIVATE\OazaZK

# Přepni na develop (nebo pracuj ve worktree)
git checkout develop

# Vytvoř workflow soubor
# (zkopíruj obsah z kroku 8.1 do .github/workflows/deploy-dev.yml)

git add .github/workflows/deploy-dev.yml
git commit -m "chore: add DEV deployment workflow"
git push origin develop
```

Push na `develop` automaticky spustí GitHub Actions → build → test → deploy.

### 10.2 Sleduj deploy

```bash
gh run list --branch develop --limit 3
gh run watch  # interaktivní sledování
```

Nebo: https://github.com/rcendelin/OazaZK/actions

### 10.3 Seed data

Po úspěšném deployi zavolej seed endpoint:

```bash
# Seed je [AllowAnonymous] s ENABLE_SEED gate — na DEV je povolený
curl -X POST https://func-oaza-dev.azurewebsites.net/api/seed
```

> Voláme přímo Functions URL (ne přes SWA, protože nepoužíváme Linked Backend).

Očekávaná odpověď:
```json
{
  "message": "Seed data created successfully.",
  "houses": 8,
  "meters": 9,
  "adminUser": "rostislav@cendelinovi.cz"
}
```

### 10.4 Deaktivuj seed (po úspěšném seedu)

```bash
az functionapp config appsettings delete \
  --name func-oaza-dev \
  --resource-group rg-oaza-dev \
  --setting-names ENABLE_SEED
```

---

## Krok 10.5 — Ruční nastavení Administrátora

Seed vytvořil admin uživatele s emailem `rostislav@cendelinovi.cz`, ale bez propojení s Entra ID. Aby přihlášení přes Microsoft fungovalo, musíš ručně nastavit `EntraObjectId`.

### 10.5.1 Zjisti svůj Entra Object ID

```bash
az ad signed-in-user show --query id -o tsv
```

Zapiš si výstup (GUID, např. `a1b2c3d4-e5f6-7890-abcd-ef1234567890`).

### 10.5.2 Uprav uživatele v Table Storage

1. Jdi na **Azure Portal → stoazadev → Storage browser → Tables → Users**
2. Najdi řádek s:
   - **PartitionKey** = `USER`
   - **Email** = `rostislav@cendelinovi.cz`
3. Klikni na řádek → **Edit entity**
4. Přidej nebo uprav property:
   - **Name:** `EntraObjectId`
   - **Type:** `String`
   - **Value:** tvůj Object ID z kroku 10.5.1
5. Klikni **Update**

### 10.5.3 Ověř přihlášení

1. Otevři `https://oaza-dev.cendelinovi.cz`
2. Klikni "Přihlásit přes Microsoft"
3. Po přihlášení by se měl zobrazit dashboard s tvým jménem

> **Poznámka:** Pokud property `EntraObjectId` v řádku neexistuje, přidej ji tlačítkem **Add property** v editoru entity. Typ musí být `String`.

> **Další uživatele** (členy spolku) přidáš přes admin rozhraní portálu: **Správa uživatelů → Pozvat uživatele**. Tam zadáš jméno, email, roli a přiřazení k domácnosti.

---

## Krok 11 — Ověření funkčnosti

Otevři v prohlížeči: **https://oaza-dev.cendelinovi.cz**

### Checklist

- [ ] Login page se zobrazí
- [ ] Tlačítko "Přihlásit přes Microsoft" přesměruje na Entra ID
- [ ] Po přihlášení se zobrazí dashboard
- [ ] Admin dashboard: 4 metriky, tabulka odečtů, quick actions
- [ ] Navigace v sidebaru funguje (všechny odkazy)
- [ ] Mobilní hamburger menu funguje (zúž okno < 768px)
- [ ] `/readings/import` — drag & drop zone se zobrazí
- [ ] `/billing` — seznam období, tlačítko vytvořit
- [ ] `/documents` — záložky kategorií, upload tlačítko (admin)
- [ ] `/finance` — metriky, tabulka, přidat záznam (admin)
- [ ] `/admin/houses` — seznam 8 domů ze seedu
- [ ] `/admin/users` — admin uživatel ze seedu

### Pokud něco nefunguje

1. **Login nefunguje:** Zkontroluj Entra ID redirect URI (`https://oaza-dev.cendelinovi.cz`)
2. **API vrací 500:** `az functionapp log tail --name func-oaza-dev --resource-group rg-oaza-dev`
3. **API vrací 404:** Zkontroluj, že `DEV_API_BASE_URL` secret je správně nastavený (`https://func-oaza-dev.azurewebsites.net/api`)
4. **CORS error:** Ověř krok 5.3 (CORS na Functions App) — origin musí být `https://oaza-dev.cendelinovi.cz`
5. **Seed selže:** Ověř, že `ENABLE_SEED=true` je v app settings
6. **Network error v konzoli:** Otevři DevTools → Network tab, zkontroluj zda API volání jdou na správnou URL

---

## Struktura Azure resources (DEV)

```
rg-oaza-dev/
├── stoazadev                    # Storage Account (LRS)
│   ├── Table Storage            # Users, Houses, WaterMeters, ...
│   └── Blob Storage             # documents, invoices, settlements, finance
├── func-oaza-dev                # Azure Functions (Consumption, Linux)
│   └── App Settings             # Connection strings, JWT, Entra ID, SendGrid
└── swa-oaza-dev                 # Static Web Apps (Free)
    └── Custom domain            # oaza-dev.cendelinovi.cz
    # Frontend volá Functions přímo přes CORS (VITE_API_BASE_URL)
```

---

## Odhadované měsíční náklady (DEV)

| Služba | Tier | Cena |
|--------|------|------|
| Static Web Apps | Free | 0 Kč |
| Functions | Consumption | ~0 Kč |
| Storage Account | LRS | ~2–5 Kč |
| SendGrid | Free (100/den) | 0 Kč |
| Entra ID | Free tier | 0 Kč |
| **Celkem** | | **~2–5 Kč/měsíc** |

---

## Lokální vývoj (bez Azure)

Pro vývoj bez Azure resources:

```bash
# 1. Spusť Azurite (lokální emulátor Table + Blob Storage)
azurite --silent --location /tmp/azurite --debug /tmp/azurite-debug.log

# 2. Spusť Azure Functions
cd api/src/Oaza.Functions
func start

# 3. Spusť React dev server
cd web
cp .env.example .env.local
# Uprav VITE_ENTRA_CLIENT_ID a VITE_ENTRA_TENANT_ID
npm run dev

# 4. (Volitelně) SWA CLI pro proxy
swa start http://localhost:5173 --api-location http://localhost:7071
```

`local.settings.json` v `api/src/Oaza.Functions/` je již nakonfigurovaný pro Azurite (`UseDevelopmentStorage=true`).

---

## Příprava na TEST a PROD (později)

Až bude DEV ověřený, vytvoříme analogicky:

| Prostředí | Resource Group | Storage | Functions | SWA | Branch |
|-----------|---------------|---------|-----------|-----|--------|
| DEV | rg-oaza-dev | stoazadev | func-oaza-dev | swa-oaza-dev | develop |
| TEST | rg-oaza-test | stoazatest | func-oaza-test | swa-oaza-test | release/* |
| PROD | rg-oaza-prod | stoaza | func-oaza-prod | swa-oaza-prod | main |

Každé prostředí bude mít:
- Vlastní Resource Group
- Vlastní Storage Account (izolovaná data)
- Vlastní Functions App (s vlastním JWT secret)
- Vlastní SWA (s vlastní doménou)
- Vlastní Entra ID App Registration (nebo sdílená s redirect URIs per prostředí)
- Vlastní GitHub Environment se secrets
- Vlastní GitHub Actions workflow (nebo parametrizovaný reusable workflow)
