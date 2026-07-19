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

---

## Skutečně naprovisionováno (2026-07-19)

Azure resources pro TEST i PROD byly vytvořeny v subscription **`ac40c613-8832-4e91-b6b5-75ef920d181d` („Provozní", tenant `cendelinovi.cz`)**, stejné jako DEV, region westeurope. Mirror DEV: Storage `Standard_LRS`/`StorageV2`/`TLS1_2`, Functions Linux Consumption `dotnet-isolated 8`, SWA Free.

| Resource | TEST | PROD |
|----------|------|------|
| Resource Group | `rg-oaza-test` | `rg-oaza-prod` |
| Storage | `stoazatest` | `stoaza` |
| Function App | `func-oaza-test` (`func-oaza-test.azurewebsites.net`) | `func-oaza-prod` (`func-oaza-prod.azurewebsites.net`) |
| SWA default host | `ambitious-mushroom-088260103.7.azurestaticapps.net` | `purple-ocean-088639603.7.azurestaticapps.net` |
| App Insights | auto (`func-oaza-test`) | auto (`func-oaza-prod`) |

**App Settings nastavené** (hodnoty nezobrazeny): `TableStorageConnection`/`BlobStorageConnection` (vlastní storage), unikátní `JwtSecret`, `JwtIssuer`+`AppUrl` per prostředí, `EntraId__TenantId` (sdílený `1161192b-7829-4c40-8920-31eb4bd5573f`), **sdílené** `AzureCommunicationServices__ConnectionString`/`FromEmail` (zkopírováno z DEV). TEST má navíc `ENABLE_SEED=true`. **CORS** povoluje custom doménu + SWA default host (+ localhost na TEST). Function Apps běží (state Running).

### Zbývá dokončit (vyžaduje tajné klíče / bezpečnostní nastavení / DNS — mimo automatizaci)

1. **RBAC** — deploy service principal (objectId `7724f095-0799-4679-aab1-cc159136d465`, ten z `AZURE_CREDENTIALS`) potřebuje Contributor na nové RG:
   ```bash
   SUB=ac40c613-8832-4e91-b6b5-75ef920d181d
   for rg in rg-oaza-test rg-oaza-prod; do
     az role assignment create --assignee-object-id 7724f095-0799-4679-aab1-cc159136d465 \
       --assignee-principal-type ServicePrincipal --role Contributor \
       --scope /subscriptions/$SUB/resourceGroups/$rg
   done
   ```
2. **GitHub environments** `test` a `production` + **secrety** (SWA tokeny se nezobrazí, čtou se přímo z Azure):
   ```bash
   gh secret set TEST_SWA_API_TOKEN --env test --body "$(az staticwebapp secrets list -n swa-oaza-test -g rg-oaza-test --query properties.apiKey -o tsv)"
   gh secret set PROD_SWA_API_TOKEN --env production --body "$(az staticwebapp secrets list -n swa-oaza-prod -g rg-oaza-prod --query properties.apiKey -o tsv)"
   gh secret set TEST_API_BASE_URL --env test --body "https://func-oaza-test.azurewebsites.net/api"
   gh secret set PROD_API_BASE_URL --env production --body "https://func-oaza-prod.azurewebsites.net/api"
   gh secret set TEST_ENTRA_TENANT_ID --env test --body "1161192b-7829-4c40-8920-31eb4bd5573f"
   gh secret set PROD_ENTRA_TENANT_ID --env production --body "1161192b-7829-4c40-8920-31eb4bd5573f"
   # TEST_/PROD_ENTRA_CLIENT_ID až po Entra registraci (bod 3)
   ```
3. **Entra App Registration** (portál) pro TEST a PROD — redirect URI `https://oaza-test.cendelinovi.cz` resp. `https://oaza.cendelinovi.cz`, Access+ID tokens, admin consent. Pak nastav `EntraId__ClientId` na příslušný Function App (`az functionapp config appsettings set … EntraId__ClientId=<id>`) a jako GitHub secret `TEST_/PROD_ENTRA_CLIENT_ID`.
4. **DNS** v `cendelinovi.cz`: `CNAME oaza-test → ambitious-mushroom-088260103.7.azurestaticapps.net`, `CNAME oaza → purple-ocean-088639603.7.azurestaticapps.net`; pak `az staticwebapp hostname set …`.
5. **Seed TEST** (po deployi): `curl -X POST https://func-oaza-test.azurewebsites.net/api/seed`, poté odeber `ENABLE_SEED`.
6. **Aktivace deploye**: TEST → větev `release/…`; PROD → merge `develop`→`master` (až budou secrety z bodů 1–2).
