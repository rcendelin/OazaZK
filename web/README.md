# Oaza Web — React 19 SPA

Frontend portálu Oáza Zadní Kopanina: React 19, TypeScript (strict), Vite 8, Tailwind CSS 4, React Router 7, MSAL a Recharts. Nasazuje se na Azure Static Web Apps.

## Spuštění

```bash
cp .env.example .env.local       # VITE_API_BASE_URL=http://localhost:7071/api pro lokální API
npm ci
npm run dev                      # http://localhost:5173
```

| Skript | Co dělá |
|--------|---------|
| `npm run dev` | dev server s HMR |
| `npm run build` | `tsc -b && vite build` → `dist/` |
| `npm run lint` | ESLint s pravidly React Compileru; v CI je blokující |
| `npm run preview` | náhled produkčního buildu |

Proměnné `VITE_*` jsou veřejné, protože se vkládají do bundlu. Popis proměnných je v [../docs/LOKALNI-VYVOJ.md](../docs/LOKALNI-VYVOJ.md).

## Struktura `src/`

| Složka | Obsah |
|--------|-------|
| `api/` | `client.ts` (base URL, bearer token, `ApiError`) + jeden modul na zdroj s typovanými funkcemi |
| `auth/` | `AuthContext` (Entra přes MSAL + magic-link JWT v paměti), `msalConfig` |
| `components/` | sdílené komponenty, `ProtectedRoute`, `Layout` (navigace), `help/` (komponenty nápovědy) |
| `content/help.ts` | jediný zdroj textů nápovědy a slovníku pojmů |
| `hooks/useApi.ts` | načítání dat: `{ data, loading, error, refetch }` |
| `pages/` | stránky podle rout; `pages/admin/` je administrace |
| `types/index.ts` | typy zrcadlící DTO z API |
| `utils/number.ts` | `parseCzechNumber` pro vstupy s desetinnou čárkou |

## Konvence

- Nové volání API přidej do `api/<zdroj>.ts` jako funkci s explicitním `Promise<T>` a typy dej do `types/index.ts`.
- Čísla a data formátuj přes `Intl.NumberFormat('cs-CZ')` a `Intl.DateTimeFormat('cs-CZ')`.
- Texty nápovědy nepiš přímo do stránek. Přidej je do `content/help.ts` a použij `<HelpNote>`, `<HelpDisclosure>` nebo `<HelpTerm>`, viz [../docs/ARCHITEKTURA.md](../docs/ARCHITEKTURA.md#nápověda-v-ui-websrccontenthelpts).
- Oprávnění rout řeší `ProtectedRoute requiredRole`, kterým projde vždy i Admin. Skutečnou ochranu dat ale zajišťuje API.
