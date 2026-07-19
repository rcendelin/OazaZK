# Nasazení TEST a PROD — Oáza Zadní Kopanina

> **Navazuje na** `docs/DEPLOYMENT-DEV.md`. TEST i PROD běží ve **stejném tenantu a subscription jako DEV**, oddělené pouze **resource groupou**. **Doména** (`cendelinovi.cz`) a **e-mailová služba** (Azure Communication Services) jsou **sdílené** napříč prostředími.

---

## Přehled prostředí

| Prostředí | Branch | Resource Group | Storage | Functions | SWA | Doména | GitHub env | Workflow |
|-----------|--------|----------------|---------|-----------|-----|--------|-----------|----------|
| **DEV** | `develop` | `rg-oaza-dev` | `stoazadev` | `func-oaza-dev` | `swa-oaza-dev` | `oaza-dev.cendelinovi.cz` | `dev` | `deploy-dev.yml` |
| **TEST** | `release/**` *(nebo ruční spuštění)* | `rg-oaza-test` | `stoazatest` | `func-oaza-test` | `swa-oaza-test` | `oaza-test.cendelinovi.cz` | `test` | `deploy-test.yml` |
| **PROD** | `master` | `rg-oaza-prod` | `stoaza` | `func-oaza-prod` | `swa-oaza-prod` | `oaza.cendelinovi.cz` | `production` | `deploy.yml` |

Všechny tři workflow používají **jednotný způsob nasazení** (stejný jako ověřený DEV): `Azure/login` se sdíleným service principalem (`AZURE_CREDENTIALS`) → `az functionapp deployment source config-zip` pro Functions a `Azure/static-web-apps-deploy` s per-prostředí SWA tokenem pro frontend. (PROD dřív používal `publish-profile` a selhával na „No credentials found" — teď je sjednocený.)

---

## Co je sdílené vs. per-prostředí

| Prostředek | Sdílené / per-env | Poznámka |
|------------|-------------------|----------|
| Azure **tenant** + **subscription** | **sdílené** | Jeden service principal (`AZURE_CREDENTIALS`) pro deploy do všech RG |
| Resource Group | per-env | `rg-oaza-dev` / `rg-oaza-test` / `rg-oaza-prod` |
| Storage / Functions / SWA | per-env | Izolovaná data a JWT secret |
| Root **doména** `cendelinovi.cz` | **sdílená** | Každé prostředí má vlastní subdoménu (dev/test/prod) přes CNAME |
| **ACS e-mailová služba** (odesílání) | **sdílená** | Stejný `AzureCommunicationServices__ConnectionString` a `FromEmail` ve všech Functions App |
| `AppUrl` / `JwtIssuer` | per-env | Aby magic-link e-maily mířily na správnou subdoménu |

> **Sdílený e-mail:** všechny tři Functions Apps mají v App Settings **stejné** `AzureCommunicationServices__*` (jedna ACS resource, jeden odesílatel). Liší se jen `AppUrl`/`JwtIssuer` — proto přihlašovací odkaz v e-mailu vede na to prostředí, ze kterého požadavek přišel.

---

## Prerekvizity v Azure (jednorázově, per prostředí)

Pro **TEST** i **PROD** vytvoř resources analogicky ke krokům 1–5 v `DEPLOYMENT-DEV.md`, jen s příslušnými názvy. Zkráceně:

```bash
# ---- TEST (opakuj obdobně pro PROD: rg-oaza-prod / stoaza / func-oaza-prod / swa-oaza-prod) ----
az group create --name rg-oaza-test --location westeurope --tags environment=test project=oaza

az storage account create --name stoazatest --resource-group rg-oaza-test \
  --location westeurope --sku Standard_LRS --kind StorageV2 \
  --min-tls-version TLS1_2 --allow-blob-public-access false

az functionapp create --name func-oaza-test --resource-group rg-oaza-test \
  --storage-account stoazatest --consumption-plan-location westeurope \
  --runtime dotnet-isolated --runtime-version 8 --functions-version 4 --os-type Linux \
  --tags environment=test project=oaza

az staticwebapp create --name swa-oaza-test --resource-group rg-oaza-test \
  --location westeurope --sku Free --tags environment=test project=oaza
```

### App Settings na Functions App (per prostředí)

```bash
# TEST — pro PROD nahraď názvy a JwtIssuer/AppUrl/FromName
STORAGE_CONN=$(az storage account show-connection-string --name stoazatest --resource-group rg-oaza-test --query connectionString -o tsv)
JWT_SECRET=$(openssl rand -base64 32)   # každé prostředí má VLASTNÍ JWT secret

az functionapp config appsettings set --name func-oaza-test --resource-group rg-oaza-test --settings \
  "TableStorageConnection=$STORAGE_CONN" \
  "BlobStorageConnection=$STORAGE_CONN" \
  "JwtSecret=$JWT_SECRET" \
  "JwtIssuer=oaza-test.cendelinovi.cz" \
  "AppUrl=https://oaza-test.cendelinovi.cz" \
  "EntraId__TenantId=<SHARED_TENANT_ID>" \
  "EntraId__ClientId=<TEST_ENTRA_CLIENT_ID>" \
  "AzureCommunicationServices__ConnectionString=<SHARED_ACS_CONNECTION_STRING>" \
  "AzureCommunicationServices__FromEmail=<SHARED_ACS_FROM_EMAIL>" \
  "AzureCommunicationServices__FromName=Oáza ZK TEST"
```

> `AzureCommunicationServices__*` = **stejné hodnoty jako DEV/PROD** (sdílená ACS). `JwtSecret` naopak **vždy unikátní** per prostředí. Na PROD **nenastavuj** `ENABLE_SEED`.

### CORS na Functions App (frontend volá API přímo)

```bash
az functionapp cors add --name func-oaza-test --resource-group rg-oaza-test \
  --allowed-origins "https://oaza-test.cendelinovi.cz"
# PROD: --allowed-origins "https://oaza.cendelinovi.cz"
```

### Service principal pro deploy (sdílený `AZURE_CREDENTIALS`)

Deploy workflow se přihlašuje service principalem uloženým v secretu **`AZURE_CREDENTIALS`** (stejný, jaký už funguje pro DEV). Musí mít roli **Contributor** na **všech** cílových resource groupách:

```bash
# Ověř / uděl přístup SP i na test a prod RG (SP_APP_ID = appId service principalu z AZURE_CREDENTIALS)
az role assignment create --assignee <SP_APP_ID> --role Contributor \
  --scope /subscriptions/<SUB_ID>/resourceGroups/rg-oaza-test
az role assignment create --assignee <SP_APP_ID> --role Contributor \
  --scope /subscriptions/<SUB_ID>/resourceGroups/rg-oaza-prod
```

> Alternativně přiřaď Contributor na úrovni celé subscription — jeden SP pak pokryje dev/test/prod.

### Custom doména (sdílený root, per-env subdoména)

```bash
# CNAME v DNS cendelinovi.cz:  oaza-test -> <hostname>.azurestaticapps.net (viz DEPLOYMENT-DEV krok 9)
az staticwebapp hostname set --name swa-oaza-test --resource-group rg-oaza-test --hostname oaza-test.cendelinovi.cz
# PROD:  CNAME oaza -> …  ;  hostname oaza.cendelinovi.cz na swa-oaza-prod
```

---

## GitHub — Environments a Secrets

### Environments

V `Settings → Environments` vytvoř (pokud ještě nejsou): **`dev`**, **`test`**, **`production`**. U `production` doporučeno zapnout **Required reviewers** (ruční schválení před nasazením na produkci).

### Secrets

| Secret | Rozsah | Hodnota |
|--------|--------|---------|
| `AZURE_CREDENTIALS` | **sdílený** (repo) | JSON service principalu (`az ad sp create-for-rbac --sdk-auth`) s Contributor na dev/test/prod RG |
| `TEST_ENTRA_CLIENT_ID` | TEST | Application (client) ID Entra app registrace pro TEST |
| `TEST_ENTRA_TENANT_ID` | TEST | Directory (tenant) ID — stejný tenant jako DEV/PROD |
| `TEST_API_BASE_URL` | TEST | `https://func-oaza-test.azurewebsites.net/api` |
| `TEST_SWA_API_TOKEN` | TEST | Deployment token `swa-oaza-test` (`az staticwebapp secrets list`) |
| `PROD_ENTRA_CLIENT_ID` | PROD | Application (client) ID Entra app registrace pro PROD |
| `PROD_ENTRA_TENANT_ID` | PROD | Directory (tenant) ID — stejný tenant |
| `PROD_API_BASE_URL` | PROD | `https://func-oaza-prod.azurewebsites.net/api` |
| `PROD_SWA_API_TOKEN` | PROD | Deployment token `swa-oaza-prod` |

> `GITHUB_TOKEN` (použitý jako `repo_token` u SWA deploye) **nenastavuješ** — GitHub Actions ho poskytuje automaticky.
>
> **Tenant je sdílený** → `*_ENTRA_TENANT_ID` má ve všech prostředích stejnou hodnotu (drženo jako samostatné secrety kvůli konzistenci s DEV workflow). Entra **App registration** může být per-prostředí (vlastní redirect URI na příslušnou subdoménu), nebo jedna sdílená s více redirect URIs — pak `*_ENTRA_CLIENT_ID` bude všude stejné.
>
> Secrety lze uložit buď na úrovni repa, nebo na příslušný **Environment** (`test` / `production`) — environment-scoped je bezpečnější, protože je zpřístupní jen job běžící v daném prostředí.

---

## Aktivace

1. **Azure prerekvizity hotové** (RG + resources + App Settings + CORS + doména) pro TEST a/nebo PROD.
2. **GitHub secrets + environments nastavené** (viz výše). Bez nich deploy job selže na „No credentials found" / prázdném tokenu.
3. **Workflow soubory na správné větvi:**
   - PROD `deploy.yml` (trigger `master`) — dostane se na produkci **mergem `develop` → `master`**.
   - TEST `deploy-test.yml` (trigger `release/**`) — vytvoř release větev, např.:
     ```bash
     git checkout -b release/1.0 develop
     git push -u origin release/1.0     # spustí TEST deploy
     ```
     Nebo spusť ručně přes `Actions → Build and Deploy (TEST) → Run workflow`.
4. **Sleduj:** `gh run list --limit 5` / `gh run watch`.

> **Pořadí u PROD:** nejdřív nastav secrety, pak teprve merge na `master` — jinak se produkční deploy spustí a selže na chybějících secretech.

---

## Poznámky

- Build+test (a lint frontendu) běží v **každém** workflow před deployem — nasadí se jen zelený build.
- PROD workflow staví i na **pull requestu do `master`** (build+test jako CI brána), ale **deployuje jen** na push do `master`.
- Frontend se sestavuje **per prostředí** s vlastními `VITE_*` (API URL + Entra), takže jeden build míří vždy na správné API.
- Odhad nákladů zůstává jako u DEV (~jednotky Kč/měsíc na prostředí; sdílená ACS a doména náklady dál nezvyšují).
