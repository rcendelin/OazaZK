# Release checklist (T14)

Postup vydání nové verze na **TEST** a **PROD**. Prostředí, secrety a workflow jsou popsané v
[DEPLOYMENT-TEST-PROD.md](DEPLOYMENT-TEST-PROD.md). Checklist projděte celý. Když krok neprojde, nevydávejte.

Značení: ☐ = odškrtněte. **(PROD)** = jen u produkce.

---

## 1. Před vydáním (větev `develop`)

- ☐ CI na posledním commitu `develop` je zelené. To zahrnuje build API bez warningů, všechny testy včetně golden
  scénářů S1–S8 a importu počátečních dat, pokrytí výpočtů ≥ 90 %, lint a build webu a e2e (Playwright).
- ☐ DEV (`oaza-dev.cendelinovi.cz`) běží na tomto commitu a hlavní stránky se otevřou bez chyby. Jde o Přehled,
  Saldo domu, Náklady, Voda a ztráty, Pokladnu a Mezizávěrky.
- ☐ `CHANGELOG.md`: sekci **[Nevydáno]** přejmenujte na verzi s datem (např. `## [1.0.0] – 2026-10-15`) a nad ni
  založte prázdnou sekci **[Nevydáno]**.
- ☐ Dokumentace odpovídá kódu. Týká se `docs/API.md` (nové a změněné endpointy), `docs/VYUCTOVANI.md` (výpočty)
  a nápovědy v aplikaci (`web/src/content/help.ts`). Screenshoty návodu pro správce přegenerujte, pokud se měnily
  obrazovky: `cd web && npm run build && GUIDE_SCREENSHOTS=1 npx playwright test e2e/adminGuide.spec.ts`.
- ☐ **Změny dat.** Table Storage nemá schéma a nové tabulky se zakládají samy při prvním zápisu
  (`TableStorageRepository`). Zkontrolujte v CHANGELOG a v PR, jestli verze nemění význam uložených dat, např.
  přejmenování sloupce, jiný formát klíče nebo jiné znaménko. Pokud ano, musí k verzi existovat převodní krok,
  vyzkoušený na zálohách z TEST. Nový nepovinný sloupec převod nepotřebuje.
- ☐ Nové app settings nebo feature flagy (např. `OFF_BOOK_FUND_ENABLED`) jsou zapsané v
  `local.settings.json.example` a `docs/LOKALNI-VYVOJ.md`. Máte připravené hodnoty pro TEST i PROD.

## 2. Záloha storage před nasazením

Záloha se dělá **vždy před nasazením na PROD** a před každým zápisem ostrých dat (import počátečních dat,
mezizávěrka po velké změně). Table Storage nemá obnovu k časovému bodu, záloha je jediná cesta zpět.

```bash
# connection string se čte z proměnné prostředí, nikdy ho nepište do příkazu ani do repa
export OAZA_STORAGE_CONNECTION="$(az storage account show-connection-string -n stoaza -g rg-oaza-prod -o tsv)"
dotnet run --project <repo>/api/tools/Oaza.StorageBackup -- backup ~/oaza-zalohy/prod-$(date +%Y%m%d-%H%M)
unset OAZA_STORAGE_CONNECTION
```

- ☐ Storage aplikace je zároveň úložištěm Functions App (klíče, balíčky nasazení). Tato systémová data nástroj
  **vynechává** při záloze i obnově — obnova tak nevrátí starší verzi kódu ani neznehodnotí klíče.
- ☐ Záloha doběhla a vypsala počty řádků všech tabulek (Users, Houses, … CostEntries, InterimClosings, CashBook,
  AuditLog) a počty souborů v kontejnerech (`documents`, `finance`).
- ☐ Záloha je uložená **mimo počítač**, na šifrovaném disku nebo v soukromém úložišti. Obsahuje osobní údaje
  členů. Nikdy ji nedávejte do repozitáře.
- ☐ U TEST stačí záloha před importem dat a před školením.

## 3. Nasazení na TEST

- ☐ Vytvořte release větev z `develop`: `git checkout -b release/1.0 develop && git push -u origin release/1.0`.
  Spustí se `deploy-test.yml`, případně ho spusťte ručně: Actions → *Build and Deploy (TEST)* → Run workflow.
- ☐ Workflow doběhl zeleně (`gh run watch`).
- ☐ Na TEST proveďte kontroly z bodů 5 a 6.
- ☐ Když jde o školení, nahrajte demo data (`seed/demo/`) přes Správa → Import počátečních dat. Nejdřív proveďte
  zkoušku nanečisto, pak zápis. Demo data jsou fiktivní.
- ☐ Správce projde **Návod pro správce** (`/navod`) krok za krokem bez pomoci. To je akceptace T12.

## 4. Nasazení na PROD (PROD)

- ☐ Máte čerstvou zálohu PROD z bodu 2.
- ☐ GitHub environment `production` má zapnuté **Required reviewers**, takže nasazení čeká na ruční schválení.
- ☐ Merge release větve do `master` (PR `release/1.0` → `master`). Po push do `master` se spustí `deploy.yml`.
  Deploy job čeká na schválení v Actions → *Review deployments*.
- ☐ Schvalte až ve chvíli, kdy máte čas na kontroly z bodů 5 a 6 a případný rollback.
- ☐ Po nasazení vraťte `master` zpět do `develop`, aby se změna CHANGELOG neztratila:
  `git checkout develop && git merge master`.

## 5. Kontrola prostředí a oprávnění

- ☐ **Pruh prostředí.** Na TEST je nahoře „TESTOVACÍ PROSTŘEDÍ“, na PROD žádný pruh není.
  `curl https://func-oaza-prod-flex.azurewebsites.net/api/environment` vrací `prod`, u TEST `test`.
- ☐ **(PROD) `ENABLE_SEED` není nastavené.** `curl -X POST …/api/seed` vrací 404.
- ☐ **Oprávnění člena.** Přihlaste se jako člen (testovací účet s rolí Member):
  - v menu nejsou položky Náklady, Voda a ztráty, Mezizávěrky, Návod pro správce, Import z banky ani správa;
  - v Saldu domu je jen jeho vlastní dům; API na detail cizího domu (`GET /api/ledger/houses/{cizí id}` s jeho
    přihlášením) vrátí 403 „Detail cizího domu není dostupný.“;
  - Zálohy ukazují jen jeho dům;
  - Pokladnu vidí, ale nemůže zapisovat.
- ☐ **Oprávnění účetní.** Vidí náklady, vodu, mezizávěrky i export pro účetní. Nemůže zakládat mezizávěrky ani měnit
  správu.
- ☐ **Oddělený fond (O4).** Pokud není schválený, `OFF_BOOK_FUND_ENABLED` není nastavené a položka Oddělený fond
  v menu chybí.

## 6. Smoke test po nasazení

Do 15 minut po nasazení, na tom prostředí, kam se nasazovalo:

- ☐ Přihlášení odkazem z e-mailu funguje (odkaz vede na správnou doménu) a přihlášení přes Microsoft účet funguje.
- ☐ Přehled se načte. První požadavek po nečinnosti může trvat 5–10 s, to je studený start Functions.
- ☐ Saldo domu: přehled všech domů se načte a kontrolní řádek složek sedí (žádná červená „nesedí“).
- ☐ Voda a ztráty ukazuje intervaly a metodu ztrát. Náklady ukazují seznam a „Rozpad na domy“.
- ☐ Dokumenty: stažení existujícího dokumentu funguje.
- ☐ Export pro účetní z poslední mezizávěrky se stáhne (XLSX).
- ☐ V Application Insights (`func-oaza-prod`, sdílí ho i `func-oaza-prod-flex`) nejsou nové chyby 500.

## 7. Rollback

Když smoke test neprojde nebo se objeví chyba v číslech:

1. **Kód.** Revertujte merge na `master` (`git revert -m 1 <merge commit>` → push). Workflow nasadí předchozí verzi
   a znovu čeká na schválení.
   Na TEST nasaďte předchozí `release/…` větev ručním spuštěním workflow.
2. **Data**, jen pokud nová verze zapsala chybná data (např. špatný import nebo převod):
   - zastavte zápisy, tedy dejte vědět správci a účetní;
   - obnovte zálohu z bodu 2:
     ```bash
     export OAZA_STORAGE_CONNECTION="…"   # viz bod 2
     dotnet run --project <repo>/api/tools/Oaza.StorageBackup -- restore ~/oaza-zalohy/prod-YYYYMMDD-HHMM --yes stoaza
     ```
     Obnova přepíše všechny zálohované tabulky a kontejnery do stavu zálohy a smaže, co v záloze nebylo. **Ztratí
     se i správné zápisy po záloze**, ty je potřeba zadat znovu (audit log ze zálohy ukazuje, co bylo předtím).
   - bez `--yes <název účtu>` nástroj nic neudělá, to je ochrana proti obnově do špatného prostředí;
   - obnovu nejdřív vyzkoušejte na TEST: zálohu PROD obnovte do `stoazatest`.
3. Zapište do CHANGELOG, co se stalo a co se vrátilo.

---

**Vzniklo pro T14.** Jde o první verzi. Doplňujte ji, když se při vydání ukáže krok, který chyběl.
