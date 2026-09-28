# Živé E2E testy nasazeného prostředí

Sada `e2e-live/` testuje **skutečně nasazenou** aplikaci (výchozí DEV): frontend, API i úložiště. Na rozdíl od
`e2e/` (mockované API, běží v CI) se spouští **ručně**, protože zapisuje data a potřebuje tajné údaje prostředí.
Poprvé proběhla 27.–28. 9. 2026. Našla 21 nálezů, opravy jsou v PR #49–#52. Po opravách prošlo 97/97 testů.

## Co pokrývá

| Soubor | Oblast |
|---|---|
| `01-public` | Veřejné části a zabezpečení: 401 bez tokenu, s cizím podpisem i s prošlým tokenem; seed; magic link bez prozrazení účtů; CORS; přímé otevření hlubokých adres; hlavičky; rychlost API. |
| `02-crawl` | Všechny stránky × 4 role (správce, účetní, člen, člen bez domu): bez chyb v konzoli, bez 5xx, zakázané stránky „Přístup odepřen“, menu podle role, mobil. |
| `03-authz` | Matice oprávnění vygenerovaná z `docs/API.md` (každý endpoint × role), člen vidí jen svůj dům. |
| `10`–`19` | Business scénáře: nákladové složky, počáteční stavy, odečty, náklady, voda a platby, zálohy, saldo, pokladna, dokumenty a import z banky, mezizávěrky a převod domu, audit, nápověda, robustnost, import počátečních dat. |

`live.ts` obsahuje pomocné funkce: přihlášení přes skutečnou stránku odkazu (podvrhne se jen výměna tokenu), API klienta a
fixture, která test shodí při chybě v konzoli, nezachycené výjimce, odpovědi 5xx nebo 401/403 z API.

## Spuštění (DEV)

1. **Záloha** úložiště DEV (viz `docs/release-checklist.md` §2), např. do `~/oaza-zalohy/dev-pred-e2e`.
2. **Testovací uživatelé** v tabulce `Users` na DEV: `e2e-admin` (Admin), `e2e-member` (Member, s domem),
   `e2e-accountant` (Accountant), `e2e-member-nohouse` (Member bez domu). E-maily `…@example.invalid`,
   `AuthMethod=MagicLink`. Vložte je přímo do tabulky (`az storage entity insert`), ne přes `POST /users`, aby
   nechodily pozvánky. **Typy uveďte explicitně** — `az` jinak uloží text a API pak na čtení uživatelů vrací 500:
   `NotificationsEnabled@odata.type=Edm.Boolean`, `MagicLinkRequestCount@odata.type=Edm.Int32`,
   `MagicLinkFailedAttempts@odata.type=Edm.Int32`.
3. **Proměnné prostředí** (hodnoty z app settings `func-oaza-dev-flex`; nikdy je neukládejte do repa):
   - `OAZA_LIVE_JWT_SECRET` = `JwtSecret`, `OAZA_LIVE_JWT_ISSUER` = `JwtIssuer` — pro podpis krátkodobých tokenů,
   - `OAZA_LIVE_MEMBER_HOUSE` = id domu člena,
   - volitelně `OAZA_LIVE_APP_URL`, `OAZA_LIVE_API_URL` a `OAZA_LIVE_ENV` (`dev` / `test` / `prod`; výchozí DEV).
     Na TEST stačí sady `01`–`03` (nezapisují data) jako kontrola z release checklistu §5–6.
4. `cd web && npx playwright test -c playwright.live.config.ts` (asi 7 minut, sériově).
5. **Obnova:** smažte tabulky, které vznikly až během testů (nebyly v záloze), a obnovte zálohu:
   `dotnet run --project api/tools/Oaza.StorageBackup -- restore <složka> --yes stoazadev`. Pak ověřte, že nová
   záloha je shodná s původní.

Na PROD spouštějte **jen sady `01`–`03`** (smoke test po nasazení; nezapisují data), nikdy `10`–`19` — zapisují data (mezizávěrky, převody domů, pokladnu).

Známé chování: API toleruje odchylku hodin 1 minutu (`ClockSkew`), proto test vypršelého přihlášení čeká 70 s.
