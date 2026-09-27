# Changelog

Všechny podstatné změny portálu. Formát vychází z [Keep a Changelog](https://keepachangelog.com/cs/1.1.0/).
Záznam se doplňuje v každém PR, které dokončuje task ze zadání `docs/ZADANI.md` (sekce **Nevydáno**). Při nasazení na PROD
se sekce přejmenuje na verzi s datem.

## [Nevydáno]

### Přidáno
- **Regresní E2E scénáře S1–S8 (T14).** Automatické testy procházejí v prohlížeči všech osm zkušebních scénářů
  ze zadání — ztráty vody, kredit vodárny, přistoupení domu, převod domu, pokladnu, oddělený fond, zaokrouhlení
  a dopočet odečtu — a kontrolují čísla tak, jak je uvidí uživatel. Data pro ně vyrábí skutečný výpočet backendu,
  takže test odhalí chybu ve výpočtu i v zobrazení.
- **Návod pro správce (T12).** Nová stránka Návod pro správce (v menu pod „Jak to funguje“, pro správce a účetní):
  pět postupů krok za krokem s obrázky — zadat měsíční odečty, vložit fakturu a zálohu, zapsat výdaj z pokladny,
  udělat mezizávěrku a převést dům na nového majitele. Automatické testy procházejí stejné kroky, takže návod
  odpovídá tomu, co aplikace opravdu dělá.
- **Import bankovního výpisu (Fio CSV).** Stránka Import z banky: nahraný výpis se převede na zálohy a doplatky
  domácností, dům se určí podle čísla účtu (účty domů ve Správě domácností, nové se učí při importu), opakovaný
  import nic nezdvojí; platby mají štítek „Z banky“ (PR #4).
- **Alokační knihovna (X3).** `Oaza.Domain.Services.Allocator.Allocate(total, weights)` rozdělí částku podle vah
  na celé haléře metodou největšího zbytku; součet dílů se vždy rovná celku, shodné zbytky se řeší deterministicky
  podle pořadí, záporná částka (kredit) se rozpočítá zrcadlově. Základ pro všechny nové výpočty (T02–T08).
- **Changelog (X6).** Tento soubor.
- **Pruh s prostředím (T01).** Mimo produkci se nahoře zobrazuje „TESTOVACÍ / VÝVOJOVÉ PROSTŘEDÍ“; API hlásí prostředí
  přes `GET /api/environment` (app setting `Environment`), frontend dostává `VITE_ENVIRONMENT` z workflow a při
  nesouladu varuje.
- **Projektová dokumentace (T00).** README, `docs/ARCHITEKTURA.md`, `API.md`, `VYUCTOVANI.md`, `LOKALNI-VYVOJ.md` a nové
  `docs/architecture-notes.md` (rozcestník podle zadání), `docs/gap-analysis.md` (T01–T14: co je, co chybí)
  a `docs/open-questions.md` (rozhodnutí X1/X2/X7/T09, rizika, otázky O1–O4).
- **CI pro pull requesty (X6).** Nový workflow kontroluje každé PR: build API bez warningů (`TreatWarningsAsErrors`),
  testy, pokrytí výpočetní logiky ≥ 90 % (`api/coverage-gate.txt`, souhrn v PR) a lint + build webu.
- **Audit změn (X4).** Tabulka `AuditLog` (kdo, kdy, entita, stará a nová hodnota, důvod; jen přidávání), služba
  `IAuditLogger` pro use casy nového modelu, endpoint `GET /api/audit-log` a stránka Administrace → Audit změn.
- **E2E smoke testy (X6).** Playwright v CI ověřuje v prohlížeči pruh s prostředím a průchod importem z banky
  (s podvrženým API, bez backendu).
- **Property-based testy (X6).** FsCheck ověřuje invarianty alokační knihovny na náhodných částkách a vahách
  (součet = celek, díly na haléře, nulová váha nic nedostane, záporná částka zrcadlově).
- **Odhad odečtu (T04, část).** Odečet může být označený jako odhad s popisem, jak vznikl (v přehledech „≈“ s nápovědou);
  `GET /api/readings/estimate` dopočítá stav vodoměru k datu interpolací po dnech mezi odečty, případně vezme
  nejbližší odečet, když existuje jen z jedné strany.
- **Zadání v repozitáři.** `docs/ZADANI.md` (setkání 27. 9. 2026) včetně scénářů S1–S8; `docs/open-questions.md`
  doplněno o znění O1–O5 a rozhodnutí X5 (varianta B: `DateOnly` v novém modelu, „dnes“ v `Europe/Prague`).
- **Kalendářní dny v pražském čase (X5).** `IClock`/`PragueClock` („dnes“ = den v `Europe/Prague`) a `DateRange`
  (uzavřený interval po dnech: počet dnů, průnik, řez pro mezizávěrky) jako základ nového modelu T02–T10.
- **Nákladové složky – doménový model (T02, část A).** Entity `CostComponent`, `ComponentAllocationRule` a `Participation`
  s efektivním datováním, výpočet úseků, ve kterých je rozpočet konstantní (`AllocationSegments`), a validace:
  nepřekrývání účasti a pravidel, `PERCENT` = 100 % v každém dni (hláška s datem a součtem), zákaz změny za mezizávěrkou.
- **Nákladové složky – API (T02, část B).** Tabulky `CostComponents`, `ComponentAllocationRules`, `Participations`
  a endpointy `/cost-components…`: založení složky (s prvním pravidlem), změna metody od data s povinným důvodem,
  přidání, ukončení a smazání účasti, výpis úseků. Čtou Admin a Accountant, zapisuje jen Admin, každá změna jde do auditu.
- **Nákladové složky – stránka (T02, část C).** Administrace → Nákladové složky: seznam složek s dnešní metodou
  a počtem domů, založení složky, časová osa metody a účasti domů, změna metody od data s důvodem, přidání, ukončení
  a smazání účasti a výpis úseků rozpočtu. Chyby z pravidel se zobrazí všechny najednou.
- **Počáteční stavy – API (T03, část A).** Období vlastnictví domů (start účtování k datu založí první období) a počáteční
  stavy: stav vodoměru (zapíše se i jako odečet, případně odhad), podíl domu ve fondu spolku a kredit složky u dodavatele
  s náhledem rozdělení mezi domy (např. −20 000 Kč vodárny → 4 × −5 000 Kč). Každá kombinace nejvýš jednou, za
  mezizávěrkou jen oprava s důvodem, vše v auditu.
- **Počáteční stavy – průvodce (T03, část B).** Administrace → Počáteční stavy: start účtování k datu, stav každého
  vodoměru s tlačítkem „Navrhnout z odečtů“ (interpolace, zdroj a příznak odhadu se vyplní samy), podíl ve fondu
  spolku a kredit složky s náhledem rozdělení mezi domy. Uzavřené hodnoty lze jen opravit s důvodem.
- **Nákladové záznamy – API (T06, část A).** Zálohy dodavatelům, vyúčtování a jednorázové náklady složek: rozpočet
  do úseků podle dní a mezi domy účastné v každém úseku (součet vždy přesně sedí), rozpad záznamu po domech, opakovaná
  záloha (měsíční až roční série). Úhrada z přeplatku u dodavatele je pro domy stále náklad (R6). Faktury za vodu PVK
  nesou fakturované m³ pro výpočet ceny.
- **Náklady – stránka (T06, část B).** Hospodaření → Náklady: záznamy po složkách s filtrem období, součtem a u vody
  s průměrnou cenou za m³, rozpad každého nákladu na domy s výpočtem po úsecích, odkaz na PDF doklad, přidání nákladu
  nebo faktury a formulář „Opakovaná záloha“.
- **Voda a ztráty – výpočet (T05, část A).** Za každý interval mezi odečty hlavního vodoměru: spotřeba domů, ztráta,
  cena za m³ z faktur PVK, náklad vody podle spotřeby a ztráty podle metody složky „Ztráty vody“ (rovným dílem, nebo
  poměrem spotřeby; změna metody platí od svého data). Záporná ztráta se nerozpočítá a ukáže varování, chybějící odečet
  se dopočítá a označí jako odhad. Složky mají roli ve vyúčtování vody (Voda PVK / Ztráty vody).
- **Voda a ztráty – stránka (T05, část B).** Hospodaření → Voda a ztráty: pro každý úsek spotřeba hlavního vodoměru
  a domů, ztráta, cena za m³, použitá metoda ztrát a výpočet po úsecích, náklad každého domu (≈ u odhadu), varování
  a rozdíl proti fakturám PVK; na konci součty za období.
- **Saldo domu – výpočet (T07, část A).** Nové saldo v konvenci kladné = přeplatek: počáteční podíl ve fondu, platby
  domu a jeho podíly na nákladech všech složek (včetně kreditu vodárny, vody a ztrát) s průběžným zůstatkem a rozpadem
  výpočtu u každé položky; saldo se vede za období vlastnictví (nový majitel začíná znovu). Přehled všech domů po
  složkách s kontrolním řádkem „Σ domů = rozpočteno“. Člen vidí detail jen svého domu.
- **Saldo domu – stránka a export (T07, část B).** Hospodaření → Saldo domu: přehled všech domů po složkách s kontrolním
  řádkem (nesoulad červeně) a varováními, detail domu s průběžným saldem, volbou období vlastnictví a u každého nákladu
  „Jak vznikl“; saldo slovy (přeplatek / nedoplatek). Export přehledu i salda domu do XLSX a CSV.
- **Mezizávěrky (T08, část A).** Řez k datu pro všechny domy (roční závěrka) nebo pro jeden dům (prodej) se snapshotem
  salda; data do řezu se už nemění (pravidla, účast, náklady, počáteční stavy). Vyúčtování, které přijde po mezizávěrce
  a zasahuje do uzavřeného období, se rozdělí podle toho, kdo se kdy účastnil, a zaúčtuje jako opravný záznam k prvnímu
  dni po řezu — uzavřené saldo zůstane. Detail mezizávěrky ukáže rozdíl snapshotu proti dnešnímu přepočtu. Zrušit lze jen
  poslední mezizávěrku s důvodem.
- **Mezizávěrky zamykají i odečty a platby (T08, část B).** Odečet ke dni v uzavřeném období nejde zadat, opravit, přesunout
  ani naimportovat. Platby domů s datem do řezu nejde upravit ani smazat; pozdě zapsaná platba (i z bankovního importu)
  se zaúčtuje k prvnímu dni po mezizávěrce — záloha za uzavřený měsíc jako doplatek — s původním datem v poznámce.
- **Mezizávěrky – stránka a roční závěrka (T08, část C).** Hospodaření → Mezizávěrky: nová mezizávěrka (všechny domy
  nebo jeden dům) s nápovědou, seznam, detail se snímkem salda a porovnáním s dneškem (rozdíl červeně), export pro účetní
  (XLSX/CSV; roční závěrka k 31. 12. za celý rok) a zrušení poslední mezizávěrky s důvodem. Nápověda „Jak zadat
  počáteční stavy“ v průvodci Počáteční stavy.
- **Převod domu (T03, část C).** Administrace → Počáteční stavy → Převod domu: náhled dopadů (závěrečné saldo původního
  vlastníka, návrh stavu vodoměru z odečtů, co brání převodu) a převod — mezizávěrka domu den před předáním, ukončení
  období vlastnictví, nový vlastník od data předání s vlastním stavem vodoměru a nulovým podílem ve fondu (nebo vkladem).
- **Pokladna – API (T09, část A).** Pokladní kniha spolku: vklady (výběr z účtu s odkazem na pohyb), výdaje i bez dokladu
  (pak je povinné komu), průběžný zůstatek, který nikdy nesmí být záporný (ani při zpětném zápisu), storno místo mazání,
  volitelné navázání výdaje na nákladovou složku (vznikne náklad hrazený hotově). Export pokladní knihy do XLSX a PDF.
- **Pokladna – stránka (T09, část B).** Hospodaření → Pokladna: vidí ji všichni členové; zůstatek, příjmy a výdaje za
  období, záznamy s průběžným zůstatkem a štítkem „bez dokladu“, stornované přeškrtnuté. Správce a účetní zapisují vklad
  nebo výdaj (i jako společný náklad složky), stornují s důvodem a exportují do XLSX a PDF.
- **Oddělený fond (T10).** Evidence neformálního fondu mimo účetnictví spolku (např. na ohňostroje), za přepínačem
  `OFF_BOOK_FUND_ENABLED` (výchozí vypnuto — modul pak není v menu a API vrací 404). Výzvy k příspěvkům s přehledem, kdo
  zaplatil a kdo ne, příspěvky, výdaje (i placené předem) a vyrovnání; zůstatek fondu. Trvalé upozornění „Fond mimo
  účetnictví spolku – peníze nejsou na účtu spolku“. Data fondu se nikdy nedostanou do salda, mezizávěrek, pokladny
  ani exportů pro účetní.
- **Podklady od správkyně (T11).** V Dokumentech nová kategorie „Faktury a vyúčtování“ (jen PDF a obrázky) a hromadné
  nahrání více souborů najednou s přiřazením nákladové složky; u faktury tlačítko „Vytvořit náklad“, které otevře Náklady
  s předvyplněnou složkou i dokladem. V Nákladech přehled „Dokumenty bez zaúčtování“ — dokument z něj zmizí, jakmile
  k němu vznikne náklad.
- **Export odečtů (T04).** Na stránce Seznam odečtů lze stáhnout odečty za zvolené období do XLSX nebo CSV; export
  ukazuje spotřebu od předchozího odečtu a u odhadnutých hodnot sloupec „Odhad“ s popisem, jak vznikly.
- **Import počátečních dat (T13), část A — výpočet a API.** Šablony CSV v `seed/templates/` (domy, období
  vlastnictví, vodoměry, složky a jejich pravidla, účasti, počáteční stavy vodoměrů, podíly na fondu, kredity, náklady)
  s návodem `seed/README.md`. Import vždy nejdřív proběhne nanečisto a ukáže report (co vznikne, konflikty, chyby,
  salda domů k dnešku) — ke stažení v Markdownu a XLSX; zapíše se jen bez chyb a konfliktů a opakovaný import nic
  nezdvojí ani nepřepíše.
- **Import počátečních dat (T13), část B — stránka.** Správa → Import počátečních dat: nahrání CSV (UTF-8 i Windows-1250
  z Excelu), zkouška nanečisto s reportem, stažení reportu a zápis po potvrzení.
- **Jak to funguje pro nový model (T12, část A).** Nové kapitoly: počáteční stavy a proč se nepřepočítává historie,
  nákladové složky a účast domů, voda a ztráty (správce a účetní vidí aktuální metodu ztrát), přeplatek vodárny,
  mezizávěrky a převod domu, pokladna; slovník rozšířen o tyto pojmy. V průvodci počátečními stavy, u převodu domu
  a u nové mezizávěrky je u každého kroku ikona „?“ s vysvětlením.
- **Release checklist a záloha dat (T14).** `docs/release-checklist.md`: postup vydání na TEST a PROD včetně zálohy
  storage, kontroly pruhu prostředí a oprávnění členů, smoke testu a rollbacku. Nástroj `api/tools/Oaza.StorageBackup`
  zazálohuje a obnoví všechny tabulky a soubory účtu; obnova vyžaduje potvrzení názvem účtu.
- **Demo data pro školení (T14).** `seed/demo/` — fiktivní, ale realistický rok a půl provozu (8 domů, převod domu,
  kredit vodárny, vyúčtování, změna metody ztrát) k nahrání přes Import počátečních dat.
- **Oddělení prostředí (T01, dokončení).** Skript `infra/provision.sh` založí prostředí dev/test/prod (s režimem
  nanečisto a potvrzením pro produkci). API odmítne nastartovat nad úložištěm jiného prostředí (např. produkce nad
  testovacím storage). Test v CI ověřuje, že zápis v jednom prostředí není vidět v druhém.
- **Záloha storage vynechává systémová data Functions App.** Úložiště aplikace slouží i samotné Functions App
  (klíče, balíčky nasazení); obnova ze zálohy je dřív mohla vrátit a rozbít běžící aplikaci.

### Odebráno
- **Starý model vyúčtování (X2).** Stránky Vyúčtování a Přehled faktur, zúčtovací období, uložená vyúčtování a jejich
  PDF, faktury za vodu, staré saldo a ceník záloh (cena vody, elektřina s koeficienty, společný základ, metoda ztrát)
  jsou pryč — nahradily je Náklady, Voda a ztráty, Saldo domu a Mezizávěrky. Platby domů zůstaly; stránka
  „Saldo a platby“ se jmenuje **Platby** a karta salda na přehledu ukazuje nové saldo (kladné = přeplatek).
  **Doporučené zálohy** se nově počítají z nákladů domu za posledních 12 měsíců ÷ 12 (voda, elektřina, společné);
  ruční přepis zálohy zůstal.

### Opraveno
- **Stažené dokumenty a exporty hospodaření byly prázdné (0 B).** Odpověď se v isolated workeru neodeslala;
  všechny soubory teď jdou přes jednotný zápis (`FileResponse`). Dokument s diakritikou v názvu („Zápis…“) už nekončí
  chybou 500 a exporty se ukládají pod správným jménem (dřív „saldo.xlsx“).
- **Neplatné nebo extrémní vstupy vracely chybu serveru** (poznámka 20 000 znaků, částka 1e30) — teď 400 s hláškou;
  náklad má limity délky poznámky a dodavatele.
- **Mezizávěrka jednoho domu (prodej) zamykala i ostatní domy.** Odečty domovního vodoměru zamyká jen mezizávěrka
  jeho domu nebo všech domů, pokladnu jen mezizávěrka všech domů.
- **Audit** nově zaznamenává platby (ruční i z banky), ruční odečty, jejich opravy a import a nahrání, nové verze
  a smazání dokumentů.
- Chybové hlášky odečtů ukazují čísla s desetinnou čárkou.
- **Počáteční stavy nešly uložit na nasazeném prostředí.** Dvě funkce API měly stejné jméno, takže po nasazení
  chyběl endpoint `POST /opening-balances` (průvodce počátečními stavy hlásil chybu). Nový test hlídá unikátní jména
  i cesty všech funkcí. Nalezeno živým E2E testem na DEV.
- **Členové a účetní neviděli odečty.** Přehled člena a stránka Odečty volaly seznam odečtů dostupný jen správci;
  člen teď dostane hlavní vodoměr a vodoměry svého domu, účetní všechny.
- **Písmo Inter se nenačítalo** (blokovala ho bezpečnostní politika stránky). Písmo je teď součástí aplikace — žádné
  načítání z Google Fonts.
- **Filtry období se znovu načtou.** Tlačítko „Zobrazit“ na stránkách Voda a ztráty, Saldo domu, Pokladna, Náklady,
  Nákladové složky a Audit a výběr období vlastnictví v saldu domu teď opravdu načtou data pro zvolené období
  (dřív zůstala zobrazená původní data). Odhalily to regresní scénáře T14.
- **„Dnes“ ve formulářích a PDF** už v noci mezi půlnocí a 1:00 (v létě 2:00) nenabízí včerejšek: výchozí datum
  importu odečtů, plateb na stránce Saldo, filtru auditu a grafu spotřeby, kontrola „datum není v budoucnosti“
  a datum vystavení PDF se berou z pražského kalendáře.
- **Potvrzení importu odečtů (X7 §8.6/§8.7)** už nezávisí na paměti serveru (dřív mohlo skončit „relace vypršela“,
  když požadavek obsloužila jiná instance) a neukládá napůl: celá dávka se ověří předem a opakované potvrzení po
  výpadku dokončí zbytek.
- **Nápověda k importu odečtů na stránce Vodoměry (X7 §8.9)** popisovala Excel obráceně; nově odpovídá parseru
  (vodoměry ve sloupci A, data v řádku 1).

## 2026-08-02 – Nápověda v UI

### Přidáno
- Stránka „Jak to funguje“ s návody a generovaným slovníkem pojmů.
- Kontextová nápověda (`HelpNote`, `HelpDisclosure`, `HelpTerm`) u vyúčtování, salda, fondu, importu, domácností,
  uživatelů, dokumentů a faktur; texty v jediném zdroji `web/src/content/help.ts`.
