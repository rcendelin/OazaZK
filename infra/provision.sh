#!/usr/bin/env bash
# Založí (nebo doplní) Azure prostředky jednoho prostředí Oázy (T01): resource group, storage, Functions App,
# Static Web App, app settings a CORS. Názvy podle docs/DEPLOYMENT-TEST-PROD.md.
#
#   infra/provision.sh <dev|test|prod> [--dry-run] [--yes]
#
# Sdílené hodnoty se čtou z proměnných prostředí (nikdy je nepište do repa):
#   OAZA_ENTRA_TENANT_ID, OAZA_ENTRA_CLIENT_ID, OAZA_ACS_CONNECTION_STRING, OAZA_ACS_FROM_EMAIL
#
# Skript je opakovatelný: existující prostředky nechá být a JwtSecret vygeneruje jen tehdy, když ještě není nastavený
# (přegenerování by odhlásilo všechny uživatele). --dry-run jen vypíše, co by udělal. Produkce vyžaduje --yes.
set -euo pipefail

ENV="${1:-}"
shift || true
DRY_RUN=false
YES=false
for arg in "$@"; do
  case "$arg" in
    --dry-run) DRY_RUN=true ;;
    --yes) YES=true ;;
    *) echo "Neznámý parametr: $arg" >&2; exit 2 ;;
  esac
done

LOCATION=westeurope
case "$ENV" in
  dev)  RG=rg-oaza-dev;  STORAGE=stoazadev;  FUNC=func-oaza-dev-flex;  SWA=swa-oaza-dev;  DOMAIN=oaza-dev.cendelinovi.cz;  FROM_NAME="Oáza ZK DEV" ;;
  test) RG=rg-oaza-test; STORAGE=stoazatest; FUNC=func-oaza-test-flex; SWA=swa-oaza-test; DOMAIN=oaza-test.cendelinovi.cz; FROM_NAME="Oáza ZK TEST" ;;
  prod) RG=rg-oaza-prod; STORAGE=stoaza;     FUNC=func-oaza-prod-flex; SWA=swa-oaza-prod; DOMAIN=oaza.cendelinovi.cz;      FROM_NAME="Oáza Zadní Kopanina" ;;
  *) echo "Použití: infra/provision.sh <dev|test|prod> [--dry-run] [--yes]" >&2; exit 2 ;;
esac

if [[ "$ENV" == prod && "$YES" != true && "$DRY_RUN" != true ]]; then
  echo "Produkce: spusťte nejdřív s --dry-run, pak s --yes." >&2
  exit 3
fi

for var in OAZA_ENTRA_TENANT_ID OAZA_ENTRA_CLIENT_ID OAZA_ACS_CONNECTION_STRING OAZA_ACS_FROM_EMAIL; do
  if [[ -z "${!var:-}" && "$DRY_RUN" != true ]]; then
    echo "Chybí proměnná prostředí $var." >&2
    exit 2
  fi
done

run() {
  if [[ "$DRY_RUN" == true ]]; then
    # tajné hodnoty nevypisovat
    printf '[dry-run]'; printf ' %q' "$@" | sed -E 's/(Connection(String)?=|ConnectionString=|JwtSecret=)[^ ]*/\1***/g'; echo
  else
    "$@"
  fi
}

exists() { [[ "$DRY_RUN" != true ]] && "$@" >/dev/null 2>&1; }

TAGS=(environment="$ENV" project=oaza)

echo "== $ENV: $RG / $STORAGE / $FUNC / $SWA"
run az group create --name "$RG" --location "$LOCATION" --tags "${TAGS[@]}" --output none

if exists az storage account show --name "$STORAGE" --resource-group "$RG"; then
  echo "storage $STORAGE existuje"
else
  run az storage account create --name "$STORAGE" --resource-group "$RG" --location "$LOCATION" \
    --sku Standard_LRS --kind StorageV2 --min-tls-version TLS1_2 --allow-blob-public-access false \
    --tags "${TAGS[@]}" --output none
fi
# Smazané bloby (dokumenty, faktury) lze 14 dní obnovit.
run az storage account blob-service-properties update --account-name "$STORAGE" --resource-group "$RG" \
  --enable-delete-retention true --delete-retention-days 14 --output none

if exists az functionapp show --name "$FUNC" --resource-group "$RG"; then
  echo "functions $FUNC existuje"
else
  # Flex Consumption (.NET 10 does not run on Linux Consumption); 512 MB, no always-ready instances, max 10 instances.
  run az functionapp create --name "$FUNC" --resource-group "$RG" --storage-account "$STORAGE" \
    --flexconsumption-location "$LOCATION" --runtime dotnet-isolated --runtime-version 10 --instance-memory 512 \
    --tags "${TAGS[@]}" --output none
  run az functionapp scale config set --name "$FUNC" --resource-group "$RG" --maximum-instance-count 10 --output none
fi

if exists az staticwebapp show --name "$SWA" --resource-group "$RG"; then
  echo "static web app $SWA existuje"
else
  run az staticwebapp create --name "$SWA" --resource-group "$RG" --location "$LOCATION" --sku Free --tags "${TAGS[@]}" --output none
fi

STORAGE_CONN="<connection-string-$STORAGE>"
if [[ "$DRY_RUN" != true ]]; then
  STORAGE_CONN=$(az storage account show-connection-string --name "$STORAGE" --resource-group "$RG" --query connectionString -o tsv)
fi

SETTINGS=(
  "TableStorageConnection=$STORAGE_CONN"
  "BlobStorageConnection=$STORAGE_CONN"
  "JwtIssuer=$DOMAIN"
  "AppUrl=https://$DOMAIN"
  "EntraId__TenantId=${OAZA_ENTRA_TENANT_ID:-<tenant>}"
  "EntraId__ClientId=${OAZA_ENTRA_CLIENT_ID:-<client>}"
  "AzureCommunicationServices__ConnectionString=${OAZA_ACS_CONNECTION_STRING:-<acs>}"
  "AzureCommunicationServices__FromEmail=${OAZA_ACS_FROM_EMAIL:-<from>}"
  "AzureCommunicationServices__FromName=$FROM_NAME"
  "Environment=$ENV"
)
HAS_JWT=false
if [[ "$DRY_RUN" != true ]] && az functionapp config appsettings list --name "$FUNC" --resource-group "$RG" --query "[?name=='JwtSecret'].name" -o tsv | grep -q JwtSecret; then
  HAS_JWT=true
fi
if [[ "$HAS_JWT" != true ]]; then
  SETTINGS+=("JwtSecret=$(openssl rand -base64 32)")
fi
run az functionapp config appsettings set --name "$FUNC" --resource-group "$RG" --settings "${SETTINGS[@]}" --output none

# Produkce nikdy nesmí mít zapnutý anonymní /api/seed.
if [[ "$ENV" == prod ]]; then
  run az functionapp config appsettings delete --name "$FUNC" --resource-group "$RG" --setting-names ENABLE_SEED --output none
fi

run az functionapp cors add --name "$FUNC" --resource-group "$RG" --allowed-origins "https://$DOMAIN" --output none

cat <<NEXT
== Hotovo ($ENV). Zbývá ručně (viz docs/DEPLOYMENT-TEST-PROD.md):
  - DNS: CNAME ${DOMAIN%%.cendelinovi.cz} -> výchozí hostname $SWA, pak: az staticwebapp hostname set -n $SWA -g $RG --hostname $DOMAIN
  - GitHub secrety prostředí (SWA token, API URL, Entra) a u prod Required reviewers na environmentu 'production'
  - role Contributor pro deploy service principal na $RG
NEXT
