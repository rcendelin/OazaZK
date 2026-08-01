# Dokumentace chování aplikace ve webovém rozhraní — návrh

> Datum: 2026-08-01 · Stav: návrh schválen, čeká na implementační plán

## 1. Cíl

Aplikace má sama vysvětlovat, jak funguje. Dnes to dělá nesystematicky — nejlépe zdokumentované jsou Vodoměry, Saldo a Seznam odečtů, zatímco nejsložitější a nevratné operace (uzávěrka období, čerpání fondu, přenos ceny vody) nemají vysvětlení žádné.

Cílem **není** obecná uživatelská příručka. Cílem je, aby člověk stojící před konkrétní obrazovkou pochopil, co vidí a co se stane, když na něco klikne.

## 2. Publikum

Tři skupiny, každá s jinou potřebou:

| Publikum | Frekvence | Potřeba |
|---|---|---|
| Členové domácností | 2× ročně | Proč jejich částka vyšla takhle. Proč saldo ukazuje jiné číslo než vyúčtování. |
| Správce | týdně | Co udělá nevratná akce, než ji spustí. |
| Správce za rok | — | Proč je něco spočítané zrovna takhle. Co znamená ten příznak. |

## 3. Rozhodnutí

### 3.1 Rozvrstvení podle váhy

Vysvětlení se dělí do tří vrstev podle toho, kolik místa si zaslouží:

- **Vždy viditelná věta** (nejvýše 3 věty) pod nadpisem sekce — co to je, řečeno co nejstručněji.
- **Rozklikávačka** pod ní — plný výklad včetně „proč".
- **`(?)` u pojmu** v záhlaví tabulky nebo u labelu — definice jednoho termínu.

Čím popisnější text, tím níž ve vrstvě žije. Vždy viditelná vrstva zůstává krátká, protože ji správce vidí každý týden.

### 3.2 Jeden zdroj pravdy

**Doložený opakovaný failure mode tohoto repozitáře je dokumentační drift.** `CLAUDE.md` popisuje `SupplierInvoice` ve starém tvaru, byznys pravidlo #1 přímo odporuje kódu, `DEPLOYMENT-DEV.md` mluví o větvi `main`, která neexistuje. Přidání druhého místa, kde aplikace popisuje sebe samu, zakládá třetí plochu, která se může rozejít.

Proto: **všechny texty žijí v jednom modulu** a stránka „Jak to funguje" renderuje tentýž zdroj jako kontextové nápovědy. Slovník pojmů na stránce je doslova tatáž data, která pohánějí `(?)` v tabulkách.

Zvažované a zamítnuté alternativy:

- **Texty přímo v JSX + centrální stránka psaná zvlášť** — rychlejší, ale vytváří dvojí zdroj pravdy, na kterém projekt už třikrát dojel.
- **Markdown soubory** — editovatelné bez zásahu do TSX, ale přidávají renderer do bundlu, který je dnes 1 112 kB a překračuje limit Vite; a stejně vyžadují deploy, takže hlavní výhodu nedodají.

## 4. Datový model obsahu

`web/src/content/help.ts`:

```ts
export type TermId = 'ztrata' | 'podil' | /* … */;
export type SectionId = 'billingAdmin' | 'fundDraw' | /* … */;

interface Term {
  label: string;     // popisek v UI, např. "Ztráta m³"
  short: string;     // do popoveru — 1–2 věty
  long?: string;     // plný výklad; je-li přítomen, popover nabídne "Více"
}

interface Section {
  note: string;          // vždy viditelná věta
  disclosureTitle?: string;
  disclosure?: string;   // plný výklad
}

interface Guide {
  id: string;        // kotva na stránce, např. "vyuctovani"
  title: string;
  body: string[];    // odstavce; sekce mohou skládat obsah ze sections/terms
}

export const terms: Record<TermId, Term>;
export const sections: Record<SectionId, Section>;
export const guides: Guide[];   // dlouhé výklady pro /jak-to-funguje
```

`TermId` a `SectionId` jsou union typy, takže překlep v `<HelpTerm id="ztarta" />` neprojde `tsc -b`.

**Pravidlo proti driftu:** u každého textu, který uvádí vzorec nebo pořadí kroků, je v modulu komentář se jménem use casu, ze kterého pochází — `CalculateSettlementUseCase`, `CloseBillingPeriodUseCase`, `CalculateHouseSaldoUseCase`. Budoucí úprava výpočtu ten text najde grepem. Nespoléhá se na to, že si někdo vzpomene.

## 5. Komponenty

`web/src/components/help/`:

| Komponenta | Použití | Chování |
|---|---|---|
| `<HelpNote sectionId>` | pod nadpisem sekce | `text-sm text-text-secondary` — přesně dnešní vzorec podtitulku |
| `<HelpDisclosure sectionId>` | pod `HelpNote` | nativní `<details>`/`<summary>` |
| `<HelpTerm id>` | v `<th>` a u labelů | `(?)` → popover se `short`; má-li termín `long`, odkaz „Více" na `/jak-to-funguje#<id>` |

**Proč nativní `<details>` a ne vlastní stav:** funguje bez JS, přístupnost z klávesnice zdarma, a hlavně — projekt má přísný React Compiler lint (`preserve-manual-memoization`, `set-state-in-effect`), na kterém se build už jednou zasekl. Nativní element se do toho nemá jak zamotat.

## 6. Mapa umístění

### Tier 1 — peníze a nevratné akce

| Místo | Co přibude |
|---|---|
| `BillingPage` → admin, hlavička | `HelpNote` `billingAdmin` + `HelpDisclosure` „Jak se počítá vyúčtování" |
| `SettlementTable` → `<th>` | `HelpTerm` na Ztráta m³, Podíl %, Částka Kč, Zálohy Kč, Výsledek Kč, Z fondu Kč, Finální saldo Kč |
| Blok ① Čerpání ze společného fondu | `HelpNote` `fundDraw` + `HelpDisclosure` |
| Blok ② Cena vody pro příští zálohy | `HelpNote` `waterPriceCarry` + `HelpDisclosure` |
| `ConfirmDialog` uzávěrky | **Rekapitulace nastavených hodnot** nad stávajícím textem |
| `BillingPage` → member, hlavička | `HelpNote` `billingMember` + `HelpDisclosure` „Odkud se bere moje částka" |
| `BillingPage` → uzavřené období | `HelpNote` `closedSnapshot` |

### Tier 2 — pojmy, které si nikdo nezapamatuje

| Místo | Co přibude |
|---|---|
| `HousesPage` → „Rozpouští přeplatek" | `HelpTerm` `rozpoustiPreplatek` |
| `AdvancesPage` → „Metoda rozdělení ztrát" | `HelpDisclosure` `lossMethod` |
| `SaldoPage` → `<th>` | `HelpTerm` na Úpravy, Čistý zůstatek |
| `SaldoPage` → formulář plateb | `HelpNote` `paymentTypes` + `HelpDisclosure` |
| `SaldoPage` → hlavička | `HelpNote` `saldoLive` |
| `InvoicesOverviewPage` | rozšíření stávající věty o `fond-voda` |

### Tier 3 — postupy

| Místo | Co přibude |
|---|---|
| `ReadingsImportPage` | `HelpNote` `importTwoStep`; `HelpTerm` na varování „chybí odečet" a „anomálie" |
| `InvoicesSection` | `HelpDisclosure` `invoiceLineItems` |
| `DocumentsPage` | `HelpNote` `documentVersions` |
| `UsersPage` | `HelpTerm` `role` |

### Mimo rozsah

`DashboardPage` (jen agreguje — vysvětlení patří ke zdrojům), `ReadingsListPage` a `ReadingsOverviewPage` (mají dobré popisky), `MetersPage` (dnes nejlépe zdokumentovaná stránka), `LoginPage` (magic link dostane odstavec na stránce Jak to funguje, ne na přihlašovací obrazovce — tam uživatel chce jen dovnitř).

## 7. Pravidla psaní

1. **Popiš, co to dělá, ne co to je.**
2. **Příklad s čísly poráží vzorec.** Kde jde o volbu, ukaž dopad na konkrétních m³.
3. **Řekni i to, co se nestane.** „Toto vyúčtování se už nezmění" bývá důležitější než popis akce.
4. **Zachovej dnešní hlas aplikace** — oznamovací pro výklad, rozkazovací pro pokyn („Zadejte kumulativní stav…").
5. **Nikdy neopisuj `CLAUDE.md`** — je prokazatelně rozejitá s kódem. Zdrojem pravdy je use case.
6. **Vysvětli, proč to tak je.** Kdo ví proč, odvodí si pravidlo i za rok.
7. **Předejdi špatné domněnce.** Pokud název svádí k mylnému očekávání, řekni výslovně, co se *nestane*.
8. **Netvrď nic, co nejde doložit v kódu.** Zejména žádné odhady velikosti dopadu.

## 8. Plné znění textů

### 8.1 Pojmy (`terms`)

**`ztrata` — Ztráta m³**

> *short:* Voda, kterou naměřil hlavní vodoměr, ale žádný domovní. Vzniká úniky na společném řadu a měřicí nepřesností.
>
> *long:* Ztráta = spotřeba hlavního vodoměru minus součet spotřeb všech domácností za období. Zaplatit ji musí společenství, takže se rozděluje mezi domácnosti — buď rovným dílem, nebo podle spotřeby. Metodu vybíráte u zúčtovacího období. Záporná ztráta znamená, že domovní vodoměry naměřily víc než hlavní; to signalizuje chybu v odečtech nebo výměnu vodoměru, ne úsporu.

**`podil` — Podíl %**

> *short:* Jaký díl celkového nákladu za vodu připadá na tuto domácnost.
>
> *long:* Podíl = (spotřeba domácnosti + přidělená ztráta) ÷ (celková spotřeba všech domácností + celá ztráta), v procentech. Počítá se tedy ze spotřeby včetně ztráty, ne ze samotné naměřené spotřeby. Součet podílů všech domácností v období dá 100 %.

**`castka` — Částka Kč**

> *short:* Podíl domácnosti vynásobený nákladem za vodu za období.
>
> *long:* Náklad za vodu není součet faktur, které v období přišly. Faktura se skládá z řádků a každý řádek má vlastní datum odečtu — do období vstupují jednotlivé řádky podle svého data, ne faktura jako celek. Jedna faktura přesahující přelom období tak přispěje do obou. Částky se berou včetně DPH.

**`zalohy` — Zálohy Kč**

> *short:* Součet vodních složek záloh a doplatků, které domácnost v období zaplatila.
>
> *long:* Do tohoto sloupce jde jen vodní složka. Elektřina a společný základ se vyúčtovávají zvlášť a najdete je v Saldu. Započítávají se zálohy i doplatky; výplaty přeplatků a počáteční stavy sem nevstupují — ty mění jen čistý zůstatek domácnosti.

**`vysledek` — Výsledek Kč**

> *short:* Částka minus zálohy. Kladné číslo znamená, že domácnost doplácí, záporné, že má přeplatek.

**`zFondu` — Z fondu Kč**

> *short:* Kolik z čerpání společného fondu připadlo na tuto domácnost.
>
> *long:* Čerpaná částka se dělí rovným dílem mezi všechny aktivní domácnosti — ne podle spotřeby a ne podle toho, kolik která do fondu přispěla. Sloupec je nenulový jen tehdy, když u uzávěrky nastavíte čerpání.

**`finalniSaldo` — Finální saldo Kč**

> *short:* Výsledek po odečtení příspěvku z fondu. To je částka, která jde do vyúčtování.

**`upravy` — Úpravy**

> *short:* Výplaty přeplatků a počáteční stavy. Nevstupují do vyúčtování vody, mění jen čistý zůstatek domácnosti.

**`cistyZustatek` — Čistý zůstatek**

> *short:* Jeden zůstatek za celou domácnost napříč složkami. Kladné = domácnost dluží, záporné = má u společenství peníze.
>
> *long:* Přeplatek v jedné složce pokryje nedoplatek v jiné — peníze jsou zaměnitelné. Rozpad na vodu, elektřinu a společný základ je proto informativní; závazný je čistý zůstatek.

**`rozpoustiPreplatek` — Rozpouští přeplatek**

> *short:* Poznámka pro správce, ne pro výpočet. Na žádné číslo v aplikaci nemá vliv.
>
> *long:* Označuje domácnost, která se domluvila, že místo posílání měsíčních záloh nechá umořit svůj přeplatek — každé další vyúčtování mu ubere, dokud se nevyčerpá. U takové domácnosti je pak normální, že nechodí platby, a nejde o dluh.
>
> Saldo, vyúčtování ani doporučené zálohy se u ní počítají úplně stejně jako u ostatních. Pokud čekáte, že zapnutí příznaku samo zastaví předepisování záloh, nezastaví.

*(Doloženo: `House.cs:15` — „Operational flag only; does not change the saldo math.")*

**`role` — Role**

> *short:* Určuje, co uživatel v portálu vidí.
>
> *long:* **Admin** — všechno, včetně správy domácností, uživatelů a vodoměrů, importu odečtů a uzavírání období.
> **Účetní** — finanční data všech domácností a Přehled faktur, ale bez správy a bez importu odečtů.
> **Člen** — jen data své vlastní domácnosti.

**`chybiOdecet` — Varování „chybí odečet"**

> *short:* Některý nakonfigurovaný vodoměr nemá v importovaném souboru hodnotu.
>
> *long:* Varování import neblokuje, potvrdit ho můžete. Chybějící odečet se ale projeví ve vyúčtování: spotřeba se počítá z nejbližších dostupných odečtů, a nemá-li vodoměr žádný odečet uvnitř období, vyjde jeho spotřeba nulová. Náklad za tuto domácnost pak nesou ostatní.

**`anomalie` — Varování „anomálie"**

> *short:* Spotřeba je víc než dvojnásobek klouzavého průměru tohoto vodoměru.
>
> *long:* Varování neblokuje uložení. Bývá to buď skutečný nárůst (bazén, zálivka, netěsnost), nebo překlep v odečtu. Než potvrdíte, porovnejte hodnotu s předchozím stavem.

### 8.2 Sekce (`sections`)

**`billingAdmin`**

> *note:* Zúčtovací období je časový úsek, za který se rozpočítá voda mezi domácnosti. Dokud je otevřené, můžete do něj přidávat faktury a odečty a průběžně si nechat spočítat náhled.
>
> *disclosure — „Jak se počítá vyúčtování":*
>
> **Proč se vůbec něco přepočítává.** Do Oázy vede jeden přívod a na něm je hlavní vodoměr, který měří všechnu vodu, za kterou přijde faktura od dodavatele. Uvnitř má každá domácnost vlastní vodoměr. Součet domovních vodoměrů ale nikdy nedá přesně stav hlavního — část vody odteče úniky na společném řadu, část rozdílu vzniká měřicí nepřesností. Vyúčtování proto nemůže jen vynásobit spotřebu cenou. Musí rozdělit celou fakturu, včetně vody, kterou nikdo neodebral.
>
> **Krok za krokem:**
> 1. **Spotřeba hlavního vodoměru** — konečný stav minus počáteční za období. To je množství, které společenství skutečně zaplatilo.
> 2. **Spotřeba každé domácnosti** — totéž na jejím vodoměru.
> 3. **Ztráta** = spotřeba hlavního minus součet spotřeb domácností. Bývá kladná. Záporná ztráta signalizuje chybu v odečtech nebo výměnu vodoměru.
> 4. **Rozdělení ztráty** mezi domácnosti metodou zvolenou u období. Ztrátu nelze „nemít" — vodu za ni někdo zaplatil.
> 5. **Podíl** = (spotřeba domácnosti + přidělená ztráta) ÷ (celková spotřeba + celá ztráta). Součet podílů všech domácností dá 100 %.
> 6. **Částka** = podíl × náklad za vodu za období.
> 7. **Zálohy** = součet vodních složek záloh a doplatků zaplacených v období.
> 8. **Výsledek** = částka minus zálohy. Kladné číslo znamená doplatek, záporné přeplatek.
>
> **Co je „náklad za vodu za období".** Není to prostě součet faktur, které v období přišly. Faktura od dodavatele se skládá z řádků a každý řádek má vlastní datum odečtu. Do období vstupují jednotlivé řádky podle svého data, ne faktura jako celek — jedna faktura přesahující přelom období tak přispěje do obou. Částky se berou včetně DPH.

*(Zdroj: `CalculateSettlementUseCase.CalculateAsync`, `SumInvoiceCostForPeriod`, `AllocateLoss`)*

**`billingMember`**

> *note:* Vaše částka za vodu se počítá z vaší spotřeby a z podílu na ztrátě na společném řadu. Od ní se odečtou zálohy, které jste za období zaplatili.
>
> *disclosure — „Odkud se bere moje částka":*
>
> Do Oázy vede jeden přívod vody s hlavním vodoměrem — za tu vodu chodí faktura od dodavatele. Vaše domácnost má vlastní vodoměr.
>
> Součet domovních vodoměrů nikdy nedá přesně stav hlavního: část vody odteče úniky na společném řadu a část rozdílu vzniká měřicí nepřesností. Tomu rozdílu se říká **ztráta** a zaplatit ji musí společenství.
>
> Vaše částka proto vychází ze dvou věcí — kolik jste sami odebrali a jaký díl ztráty na vás připadl. Z toho se spočítá váš podíl na celkovém nákladu za období. Od částky se odečtou zálohy, které jste za období zaplatili. Kladný výsledek znamená doplatek, záporný přeplatek.
>
> Ztráta se mezi domácnosti dělí buď rovným dílem, nebo podle spotřeby — podle toho, co má společenství domluveno. Metodu použitou pro dané období najdete u vyúčtování.

**`fundDraw`**

> *note:* Čerpáním přispějete domácnostem na toto vyúčtování z peněz, které leží ve společném fondu. Zapíše se to spolu s uzavřením období a nelze to vzít zpět.
>
> *disclosure — „Co je fond a co čerpání udělá":*
>
> **Co je společný fond.** Každá domácnost platí v záloze složku „společný základ". Z těch peněz se hradí mimořádné náklady společenství — údržba, pojištění a podobně, tedy všechno kromě vody a elektřiny, které se vyúčtovávají zvlášť. Zůstatek je součet vybraných příspěvků minus tyto výdaje **za celou dobu**, ne jen za toto období.
>
> **Co udělá čerpání.** Vybranou částku rozdělí rovným dílem mezi všechny aktivní domácnosti a každé ji připíše jako doplatek k tomuto vyúčtování — sníží jim tedy to, co doplácejí za vodu. Zároveň se ve fondu zaeviduje výdaj o stejné výši, takže zůstatek klesne.
>
> **Kdy to použít.** Když ve fondu leží peníze, které chcete vrátit domácnostem, aniž byste každé zvlášť posílali platbu.
>
> **Na co si dát pozor.** Zapíše se to současně s uzavřením období a stejně jako uzávěrku to nelze vzít zpět. Dělí se rovným dílem mezi aktivní domácnosti — ne podle spotřeby a ne podle toho, kolik která do fondu přispěla.

*(Zdroj: `CloseBillingPeriodUseCase.ApplyFundDrawAsync`, `GetFundBalanceUseCase`)*

**`waterPriceCarry`**

> *note:* Efektivní cena je skutečná cena vody v tomto období — náklad za vodu dělený spotřebou včetně ztráty. Přepis se týká jen budoucích záloh, toto vyúčtování se už nezmění.
>
> *disclosure — „Co přepis ceny udělá":*
>
> **Co je efektivní cena.** Cena, za kterou společenství v tomto období vodu skutečně pořídilo. Je vyšší než cena dodavatele za m³, protože ztrátu na společném řadu taky někdo zaplatil.
>
> **Co udělá přepis.** Nastaví tuto cenu jako cenu vody v Nastavení záloh s platností od zvoleného data. Od té chvíle se z ní počítají doporučené zálohy pro domácnosti.
>
> **Co neudělá.** Nezmění toto ani žádné jiné vyúčtování. Uzavřená období jsou zmrazená a nepřepočítávají se.

*(Zdroj: `CloseBillingPeriodUseCase.ApplyNewWaterPriceAsync`)*

**`closedSnapshot`**

> *note:* Toto vyúčtování je snímek pořízený v okamžiku uzavření období. Platba, doplatek nebo výplata zaznamenaná později se do něj nepromítne — to je záměr, aby se jednou rozeslané vyúčtování nemohlo zpětně změnit. Aktuální stav domácnosti najdete v Saldu.

**`saldoLive`**

> *note:* Saldo počítá vždy z aktuálních dat, tedy i z plateb zaznamenaných po uzavření období. Proto může u téhož období ukázat jiné číslo než vyúčtování. Není to chyba — vyúčtování je zmrazený doklad, saldo je živý stav účtu.

**`lossMethod`**

> *disclosure — „Jak se ztráta rozděluje":*
>
> **Proč se to musí vybrat.** Ztráta je reálná voda, kterou někdo zaplatil. Rozdělit ji jde dvěma obhajitelnými způsoby a ani jeden není „správnější" — je to dohoda společenství.
>
> **Rovným dílem** — ztráta se vydělí počtem domácností. Stojí na úvaze, že řad je společné zařízení a jeho netěsnosti nesouvisejí s tím, kolik kdo odebral.
>
> **Podle spotřeby** — každá domácnost nese díl úměrný své spotřebě. Stojí na úvaze, že kdo víc odebírá, ten síť víc zatěžuje.
>
> **Příklad:** ztráta 12 m³, 8 domácností, celková spotřeba 300 m³.
> • Rovným dílem — každá domácnost 1,5 m³.
> • Podle spotřeby — domácnost se spotřebou 60 m³ nese 2,4 m³, domácnost s 20 m³ nese 0,8 m³.
>
> Rovným dílem tedy zvýhodní velké odběratele, podle spotřeby malé.
>
> **Kdy se změna projeví.** Až v příštím výpočtu. Uzavřená období se nepřepočítávají, takže zpětně nic nepřepíše.

*(Zdroj: `CalculateSettlementUseCase.AllocateLoss`. Pozn.: při nulové celkové spotřebě spadne metoda „podle spotřeby" zpět na rovný díl.)*

**`paymentTypes`**

> *note:* Záznamy jsou čtyř typů. Zálohy a doplatky vstupují do vyúčtování vody, výplaty a počáteční stavy mění jen čistý zůstatek domácnosti.
>
> *disclosure — „Čtyři typy záznamů":*
>
> • **Záloha** — pravidelná měsíční platba, jedna na domácnost a měsíc. Vstupuje do vyúčtování.
> • **Doplatek** — mimořádná platba, může jich být víc. Vstupuje do vyúčtování.
> • **Výplata** — vrácení přeplatku domácnosti. Sníží její kredit, do vyúčtování nevstupuje.
> • **Počáteční stav** — jednorázové nastartování zůstatku při zavádění systému. Do vyúčtování nevstupuje.

**`importTwoStep`**

> *note:* Import má dva kroky. Nejdřív se soubor načte a zkontroluje — v této fázi se neuloží nic. Uloží se až po vašem potvrzení náhledu. Když náhled zavřete bez potvrzení, nestane se nic.

**`invoiceLineItems`**

> *disclosure — „Proč má faktura řádky":*
>
> Faktura od dodavatele je jedna celková částka, ale skládá se z několika dílčích odečtů — řádků. Každý řádek má vlastní datum.
>
> Do vyúčtování období nevstupuje faktura jako celek, ale jednotlivé řádky podle svého data. Faktura, jejíž řádky přesahují přelom období, tak přispěje do obou období — každému tou částí, která do něj patří.
>
> Proto zadávejte řádky s daty, ne jen celkovou částku.

**`documentVersions`**

> *note:* U každého dokumentu se drží historie posledních 10 verzí. Nahráním nové verze se původní soubor nepřepíše — zůstane dostupný ke stažení. Po překročení desáté verze se nejstarší odstraní.

*(Doloženo: `DocumentFunctions.cs:28` `MaxVersions = 10`, ořez na `:331-336`.)*

**`receivedInvoicesFund`** — rozšíření stávající věty na `InvoicesOverviewPage`

> Stávající text zůstává: „Všechny přijaté faktury — voda i ostatní výdaje. Položky „voda" vstupují do vyúčtování vody."
>
> Přibude: Položka kategorie „fond-voda" není přijatá faktura — je to interní převod peněz ze společného fondu na vyúčtování vody, který vznikl při uzávěrce období. Do součtu „Celkem" se přesto započítává.

### 8.3 Rekapitulace v potvrzovacím dialogu uzávěrky

Nad stávající text dialogu se vloží shrnutí nastavených hodnot. Zobrazí se jen body, které skutečně platí.

> Uzavřením se zapíše vyúčtování pro {N} domácností a vygenerují se PDF. Období už nepůjde otevřít ani upravit.
> • Z fondu se čerpá **{X} Kč** — {Y} Kč na domácnost.  *(jen když fundDraw > 0)*
> • Cena vody se přepíše na **{Z} Kč/m³** s platností od {datum}.  *(jen když applyNewPrice)*

Stávající věta „Opravdu chcete uzavřít období? Tato akce je nevratná…" zůstává pod shrnutím.

## 9. Stránka „Jak to funguje"

Route `/jak-to-funguje`, dostupná všem rolím. V navigaci jako samostatná položka za skupinou „Odečty", před oddělovačem administrace.

| Sekce | Obsah |
|---|---|
| Co aplikace dělá | Portál spravuje sdílený vodovod sdružení — 8 domácností na jednom přívodu. Eviduje odečty, faktury, zálohy a jejich vyúčtování; vedle toho slouží jako úložiště společných dokumentů a přehled hospodaření. Klíčová věc, kterou řeší: voda se nakupuje společně na jeden hlavní vodoměr, ale spotřebovává se individuálně. |
| Roční cyklus | 1. Každý měsíc odečty vodoměrů (import z Excelu, ze souboru odečítačky, nebo ručně). 2. Průběžně faktury za vodu s dílčími odečty. 3. Průběžně zálohy domácností rozdělené na vodu, elektřinu a společný základ. 4. Na konci období uzávěrka — náhled, případné čerpání z fondu, uzavření, PDF pro všechny domácnosti. 5. Po uzávěrce doplatky a výplaty; ty vidíte v Saldu, uzavřené vyúčtování se jimi už nemění. |
| Vyúčtování krok za krokem | Plné znění `billingAdmin.disclosure` + `lossMethod.disclosure` |
| Zálohy, doplatky a saldo | Plné znění `paymentTypes` + `cistyZustatek.long`, plus znaménková konvence |
| Společný fond | Plné znění `fundDraw.disclosure` + `waterPriceCarry.disclosure` |
| Odečty a import | `importTwoStep` + oba dovozní kanály + `chybiOdecet.long`, `anomalie.long` |
| Role a přístup | `role.long` |
| Přihlášení | Odstavec o magic linku — odkaz zaslaný e-mailem, platný 15 minut, na jedno použití |
| Slovník pojmů | Generovaný ze `terms`; každý pojem s kotvou `#<TermId>`, na kterou míří `(?)` z tabulek |

## 10. Co návrh neřeší

- **Nález #41** (`fond-voda` nafukuje součet „Celkem" v Přehledu faktur) — text situaci vysvětlí, ale součet zůstane nafouknutý. Skutečná oprava je v kódu a patří jinam.
- **Formátovací pomocníci** (nález #31) — `content/` modul zakládá vzorec, který se pro ně později hodí, ale konsolidace není součástí tohoto návrhu.
- **Testy frontendu** (nález #10) — typované klíče odhalí překlep při `tsc -b`, ale sirotčí termín (definovaný a nikde nepoužitý) neodhalí. Zakládat kvůli tomu test runner by bylo nad rámec.
- **Onboarding / průvodce prvním přihlášením** — vědomě vynecháno, YAGNI při 15 uživatelích.

## 11. Rizika

| Riziko | Ošetření |
|---|---|
| Texty s formulemi se rozejdou s kódem | Jeden zdroj pravdy + komentář se jménem use casu u každého takového textu |
| Rekapitulace v dialogu se rozejde s tím, co backend skutečně provede | Rekapitulace čte tytéž stavové proměnné, které se posílají na API — ne vlastní kopii |
| Vždy viditelné texty zahltí obrazovku | `HelpNote` limitována na 3 věty; seznamy a delší obsah povinně do `HelpDisclosure` |
| Přírůstek do bundlu (dnes 1 112 kB, nad limitem Vite) | Texty jsou prostý řetězec bez závislostí; stránka Jak to funguje je kandidát na `lazy()` |

## 12. Fakta ověřená v kódu při psaní návrhu

Zaznamenáno, protože několik z nich odporuje tomu, co by čtenář čekal:

- `House.cs:15` — `DissolveOverpayment` je **operational flag only; does not change the saldo math**. Název „Rozpouští přeplatek" svádí k opaku.
- `CalculateSettlementUseCase.AllocateLoss:363-373` — metoda „podle spotřeby" při nulové celkové spotřebě **spadne zpět na rovný díl**.
- `CloseBillingPeriodUseCase:141,155` — čerpání fondu se dělí mezi **aktivní** domácnosti, zatímco vyúčtování pokrývá domácnosti **s vodoměrem a odečty**. Nemusí to být tatáž množina (nález #40).
- `DocumentFunctions.cs:28,331-336` — `MaxVersions = 10` platí; ořez je v endpointu, ne v repozitáři.
- `Layout.tsx:38-65` — Účetní vidí navíc jen „Přehled faktur"; širší přístup k datům všech domácností mu dávají endpointy, ne navigace.
- Potvrzovací dialog uzávěrky **už dnes říká, že akce je nevratná**. Chybí mu rekapitulace nastavených hodnot, ne varování.
