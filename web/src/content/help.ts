/**
 * Jediný zdroj textů nápovědy. Konzumují ho jak kontextové komponenty
 * (HelpNote / HelpDisclosure / HelpTerm), tak stránka /jak-to-funguje —
 * proto se nemohou rozejít.
 *
 * Texty popisující výpočty pocházejí z těchto use casů; při jejich změně
 * uprav i text zde:
 *   billingAdmin, billingMember, castka, podil, ztrata  → CalculateSettlementUseCase
 *   fundDraw, waterPriceCarry                           → CloseBillingPeriodUseCase
 *   cistyZustatek, upravy                               → CalculateHouseSaldoUseCase
 */

export type TermId =
  | 'ztrata' | 'podil' | 'castka' | 'zalohy' | 'vysledek'
  | 'zFondu' | 'finalniSaldo' | 'upravy' | 'cistyZustatek'
  | 'rozpoustiPreplatek' | 'role' | 'chybiOdecet' | 'anomalie';

export type SectionId =
  | 'billingAdmin' | 'billingMember' | 'fundDraw' | 'waterPriceCarry'
  | 'closedSnapshot' | 'saldoLive' | 'lossMethod' | 'paymentTypes'
  | 'importTwoStep' | 'invoiceLineItems' | 'documentVersions'
  | 'receivedInvoicesFund';

export interface Term {
  label: string;
  short: string;
  long?: string;
}

export interface Section {
  /** Chybí u sekcí, které mají jen rozklikávačku — dnes jedině `lossMethod`. */
  note?: string;
  disclosureTitle?: string;
  disclosure?: string;
}

export interface Guide {
  id: string;
  title: string;
  body: string[];
}

export const terms: Record<TermId, Term> = {
  ztrata: {
    label: 'Ztráta m³',
    short: 'Voda, kterou naměřil hlavní vodoměr, ale žádný domovní. Vzniká úniky na společném řadu a měřicí nepřesností.',
    long: 'Ztráta = spotřeba hlavního vodoměru minus součet spotřeb všech domácností za období. Zaplatit ji musí společenství, takže se rozděluje mezi domácnosti — buď rovným dílem, nebo podle spotřeby. Metodu vybíráte u zúčtovacího období. Záporná ztráta znamená, že domovní vodoměry naměřily víc než hlavní; to signalizuje chybu v odečtech nebo výměnu vodoměru, ne úsporu.',
  },
  podil: {
    label: 'Podíl %',
    short: 'Jaký díl celkového nákladu za vodu připadá na tuto domácnost.',
    long: 'Podíl = (spotřeba domácnosti + přidělená ztráta) ÷ (celková spotřeba všech domácností + celá ztráta), v procentech. Počítá se tedy ze spotřeby včetně ztráty, ne ze samotné naměřené spotřeby. Součet podílů všech domácností v období dá 100 %.',
  },
  castka: {
    label: 'Částka Kč',
    short: 'Podíl domácnosti vynásobený nákladem za vodu za období.',
    long: 'Náklad za vodu není součet faktur, které v období přišly. Faktura se skládá z řádků a každý řádek má vlastní datum odečtu — do období vstupují jednotlivé řádky podle svého data, ne faktura jako celek. Jedna faktura přesahující přelom období tak přispěje do obou. Částky se berou včetně DPH.',
  },
  zalohy: {
    label: 'Zálohy Kč',
    short: 'Součet vodních složek záloh a doplatků, které domácnost v období zaplatila.',
    long: 'Do tohoto sloupce jde jen vodní složka. Elektřina a společný základ se vyúčtovávají zvlášť a najdete je v Saldu. Započítávají se zálohy i doplatky; výplaty přeplatků a počáteční stavy sem nevstupují — ty mění jen čistý zůstatek domácnosti.',
  },
  vysledek: {
    label: 'Výsledek Kč',
    short: 'Částka minus zálohy. Kladné číslo znamená, že domácnost doplácí, záporné, že má přeplatek.',
  },
  zFondu: {
    label: 'Z fondu Kč',
    short: 'Kolik z čerpání společného fondu připadlo na tuto domácnost.',
    long: 'Čerpaná částka se dělí rovným dílem mezi všechny aktivní domácnosti — ne podle spotřeby a ne podle toho, kolik která do fondu přispěla. Sloupec je nenulový jen tehdy, když u uzávěrky nastavíte čerpání.',
  },
  finalniSaldo: {
    label: 'Finální saldo Kč',
    short: 'Výsledek po odečtení příspěvku z fondu. To je částka, která jde do vyúčtování.',
  },
  upravy: {
    label: 'Úpravy',
    short: 'Výplaty přeplatků a počáteční stavy. Nevstupují do vyúčtování vody, mění jen čistý zůstatek domácnosti.',
  },
  cistyZustatek: {
    label: 'Čistý zůstatek',
    short: 'Jeden zůstatek za celou domácnost napříč složkami. Kladné = domácnost dluží, záporné = má u společenství peníze.',
    long: 'Přeplatek v jedné složce pokryje nedoplatek v jiné — peníze jsou zaměnitelné. Rozpad na vodu, elektřinu a společný základ je proto informativní; závazný je čistý zůstatek.',
  },
  rozpoustiPreplatek: {
    label: 'Rozpouští přeplatek',
    short: 'Poznámka pro správce, ne pro výpočet. Na žádné číslo v aplikaci nemá vliv.',
    long: 'Označuje domácnost, která se domluvila, že místo posílání měsíčních záloh nechá umořit svůj přeplatek — každé další vyúčtování mu ubere, dokud se nevyčerpá. U takové domácnosti je pak normální, že nechodí platby, a nejde o dluh.\n\nSaldo, vyúčtování ani doporučené zálohy se u ní počítají úplně stejně jako u ostatních. Pokud čekáte, že zapnutí příznaku samo zastaví předepisování záloh, nezastaví.',
  },
  role: {
    label: 'Role',
    short: 'Určuje, co uživatel v portálu vidí.',
    long: '**Admin** — všechno, včetně správy domácností, uživatelů a vodoměrů, importu odečtů a uzavírání období.\n\n**Účetní** — finanční data všech domácností a Přehled faktur, ale bez správy a bez importu odečtů.\n\n**Člen** — jen data své vlastní domácnosti.',
  },
  chybiOdecet: {
    label: 'Varování „chybí odečet"',
    short: 'Některý nakonfigurovaný vodoměr nemá v importovaném souboru hodnotu.',
    long: 'Varování import neblokuje, potvrdit ho můžete. Chybějící odečet se ale projeví ve vyúčtování: spotřeba se počítá z nejbližších dostupných odečtů, a nemá-li vodoměr žádný odečet uvnitř období, vyjde jeho spotřeba nulová. Náklad za tuto domácnost pak nesou ostatní.',
  },
  anomalie: {
    label: 'Varování „anomálie"',
    short: 'Spotřeba je víc než dvojnásobek klouzavého průměru tohoto vodoměru.',
    long: 'Varování neblokuje uložení. Bývá to buď skutečný nárůst (bazén, zálivka, netěsnost), nebo překlep v odečtu. Než potvrdíte, porovnejte hodnotu s předchozím stavem.',
  },
};

export const sections: Record<SectionId, Section> = {
  billingAdmin: {
    note: 'Zúčtovací období je časový úsek, za který se rozpočítá voda mezi domácnosti. Dokud je otevřené, můžete do něj přidávat faktury a odečty a průběžně si nechat spočítat náhled.',
    disclosureTitle: 'Jak se počítá vyúčtování',
    disclosure: '**Proč se vůbec něco přepočítává.** Do Oázy vede jeden přívod a na něm je hlavní vodoměr, který měří všechnu vodu, za kterou přijde faktura od dodavatele. Uvnitř má každá domácnost vlastní vodoměr. Součet domovních vodoměrů ale nikdy nedá přesně stav hlavního — část vody odteče úniky na společném řadu, část rozdílu vzniká měřicí nepřesností. Vyúčtování proto nemůže jen vynásobit spotřebu cenou. Musí rozdělit celou fakturu, včetně vody, kterou nikdo neodebral.\n\n**Krok za krokem:**\n\n1. **Spotřeba hlavního vodoměru** — konečný stav minus počáteční za období. To je množství, které společenství skutečně zaplatilo.\n\n2. **Spotřeba každé domácnosti** — totéž na jejím vodoměru.\n\n3. **Ztráta** = spotřeba hlavního minus součet spotřeb domácností. Bývá kladná. Záporná ztráta signalizuje chybu v odečtech nebo výměnu vodoměru.\n\n4. **Rozdělení ztráty** mezi domácnosti metodou zvolenou u období. Ztrátu nelze „nemít" — vodu za ni někdo zaplatil.\n\n5. **Podíl** = (spotřeba domácnosti + přidělená ztráta) ÷ (celková spotřeba + celá ztráta). Součet podílů všech domácností dá 100 %.\n\n6. **Částka** = podíl × náklad za vodu za období.\n\n7. **Zálohy** = součet vodních složek záloh a doplatků zaplacených v období.\n\n8. **Výsledek** = částka minus zálohy. Kladné číslo znamená doplatek, záporné přeplatek.\n\n**Co je „náklad za vodu za období".** Není to prostě součet faktur, které v období přišly. Faktura od dodavatele se skládá z řádků a každý řádek má vlastní datum odečtu. Do období vstupují jednotlivé řádky podle svého data, ne faktura jako celek — jedna faktura přesahující přelom období tak přispěje do obou. Částky se berou včetně DPH.',
  },
  billingMember: {
    note: 'Vaše částka za vodu se počítá z vaší spotřeby a z podílu na ztrátě na společném řadu. Od ní se odečtou zálohy, které jste za období zaplatili.',
    disclosureTitle: 'Odkud se bere moje částka',
    disclosure: 'Do Oázy vede jeden přívod vody s hlavním vodoměrem — za tu vodu chodí faktura od dodavatele. Vaše domácnost má vlastní vodoměr.\n\nSoučet domovních vodoměrů nikdy nedá přesně stav hlavního: část vody odteče úniky na společném řadu a část rozdílu vzniká měřicí nepřesností. Tomu rozdílu se říká **ztráta** a zaplatit ji musí společenství.\n\nVaše částka proto vychází ze dvou věcí — kolik jste sami odebrali a jaký díl ztráty na vás připadl. Z toho se spočítá váš podíl na celkovém nákladu za období. Od částky se odečtou zálohy, které jste za období zaplatili. Kladný výsledek znamená doplatek, záporný přeplatek.\n\nZtráta se mezi domácnosti dělí buď rovným dílem, nebo podle spotřeby — podle toho, co má společenství domluveno. Metodu použitou pro dané období najdete u vyúčtování.',
  },
  fundDraw: {
    note: 'Čerpáním přispějete domácnostem na toto vyúčtování z peněz, které leží ve společném fondu. Zapíše se to spolu s uzavřením období a nelze to vzít zpět.',
    disclosureTitle: 'Co je fond a co čerpání udělá',
    disclosure: '**Co je společný fond.** Každá domácnost platí v záloze složku „společný základ". Z těch peněz se hradí mimořádné náklady společenství — údržba, pojištění a podobně, tedy všechno kromě vody a elektřiny, které se vyúčtovávají zvlášť. Zůstatek je součet vybraných příspěvků minus tyto výdaje **za celou dobu**, ne jen za toto období.\n\n**Co udělá čerpání.** Vybranou částku rozdělí rovným dílem mezi všechny aktivní domácnosti a každé ji připíše jako doplatek k tomuto vyúčtování — sníží jim tedy to, co doplácejí za vodu. Zároveň se ve fondu zaeviduje výdaj o stejné výši, takže zůstatek klesne.\n\n**Kdy to použít.** Když ve fondu leží peníze, které chcete vrátit domácnostem, aniž byste každé zvlášť posílali platbu.\n\n**Na co si dát pozor.** Zapíše se to současně s uzavřením období a stejně jako uzávěrku to nelze vzít zpět. Dělí se rovným dílem mezi aktivní domácnosti — ne podle spotřeby a ne podle toho, kolik která do fondu přispěla.',
  },
  waterPriceCarry: {
    note: 'Efektivní cena je skutečná cena vody v tomto období — náklad za vodu dělený spotřebou včetně ztráty. Přepis se týká jen budoucích záloh, toto vyúčtování se už nezmění.',
    disclosureTitle: 'Co přepis ceny udělá',
    disclosure: '**Co je efektivní cena.** Cena, za kterou společenství v tomto období vodu skutečně pořídilo. Je vyšší než cena dodavatele za m³, protože ztrátu na společném řadu taky někdo zaplatil.\n\n**Co udělá přepis.** Nastaví tuto cenu jako cenu vody v Nastavení záloh s platností od zvoleného data. Od té chvíle se z ní počítají doporučené zálohy pro domácnosti.\n\n**Co neudělá.** Nezmění toto ani žádné jiné vyúčtování. Uzavřená období jsou zmrazená a nepřepočítávají se.',
  },
  closedSnapshot: {
    note: 'Toto vyúčtování je snímek pořízený v okamžiku uzavření období. Platba, doplatek nebo výplata zaznamenaná později se do něj nepromítne — to je záměr, aby se jednou rozeslané vyúčtování nemohlo zpětně změnit. Aktuální stav domácnosti najdete v Saldu.',
  },
  saldoLive: {
    note: 'Saldo počítá vždy z aktuálních dat, tedy i z plateb zaznamenaných po uzavření období. Proto může u téhož období ukázat jiné číslo než vyúčtování. Není to chyba — vyúčtování je zmrazený doklad, saldo je živý stav účtu.',
  },
  lossMethod: {
    disclosureTitle: 'Jak se ztráta rozděluje',
    disclosure: '**Proč se to musí vybrat.** Ztráta je reálná voda, kterou někdo zaplatil. Rozdělit ji jde dvěma obhajitelnými způsoby a ani jeden není „správnější" — je to dohoda společenství.\n\n**Rovným dílem** — ztráta se vydělí počtem domácností. Stojí na úvaze, že řad je společné zařízení a jeho netěsnosti nesouvisejí s tím, kolik kdo odebral.\n\n**Podle spotřeby** — každá domácnost nese díl úměrný své spotřebě. Stojí na úvaze, že kdo víc odebírá, ten síť víc zatěžuje.\n\n**Příklad:** ztráta 12 m³, 8 domácností, celková spotřeba 300 m³.\n\n• Rovným dílem — každá domácnost 1,5 m³.\n\n• Podle spotřeby — domácnost se spotřebou 60 m³ nese 2,4 m³, domácnost s 20 m³ nese 0,8 m³.\n\nRovným dílem tedy zvýhodní velké odběratele, podle spotřeby malé.\n\n**Kdy se změna projeví.** Až v příštím výpočtu. Uzavřená období se nepřepočítávají, takže zpětně nic nepřepíše.',
  },
  paymentTypes: {
    note: 'Záznamy jsou čtyř typů. Zálohy a doplatky vstupují do vyúčtování vody, výplaty a počáteční stavy mění jen čistý zůstatek domácnosti.',
    disclosureTitle: 'Čtyři typy záznamů',
    disclosure: '• **Měsíční záloha** — pravidelná platba, jedna na domácnost a měsíc. Vstupuje do vyúčtování.\n\n• **Doplatek** — mimořádná platba, může jich být víc. Vstupuje do vyúčtování.\n\n• **Výplata přeplatku** — vrácení přeplatku domácnosti. Sníží její kredit, do vyúčtování nevstupuje.\n\n• **Počáteční stav** — jednorázové nastartování zůstatku při zavádění systému. Do vyúčtování nevstupuje.',
  },
  importTwoStep: {
    note: 'Import má dva kroky. Nejdřív se soubor načte a zkontroluje — v této fázi se neuloží nic. Uloží se až po vašem potvrzení náhledu. Když náhled zavřete bez potvrzení, nestane se nic.',
  },
  invoiceLineItems: {
    note: 'Jedna faktura = celková částka + více dílčích odečtů (řádků). Do vyúčtování vstupují řádky dle období.',
    disclosureTitle: 'Proč má faktura řádky',
    disclosure: 'Faktura od dodavatele je jedna celková částka, ale skládá se z několika dílčích odečtů — řádků. Každý řádek má vlastní datum.\n\nDo vyúčtování období nevstupuje faktura jako celek, ale jednotlivé řádky podle svého data. Faktura, jejíž řádky přesahují přelom období, tak přispěje do obou období — každému tou částí, která do něj patří.\n\nProto zadávejte řádky s daty, ne jen celkovou částku.',
  },
  documentVersions: {
    note: 'U každého dokumentu se drží historie posledních 10 verzí. Nahráním nové verze se původní soubor nepřepíše — zůstane dostupný ke stažení. Po překročení desáté verze se nejstarší odstraní.',
  },
  receivedInvoicesFund: {
    note: 'Všechny přijaté faktury — voda i ostatní výdaje. Položky „voda" vstupují do vyúčtování vody. Položka kategorie „fond-voda" není přijatá faktura, ale interní převod ze společného fondu — do součtu „Celkem" se přesto započítává.',
  },
};

export const guides: Guide[] = [
  {
    id: 'co-aplikace-dela',
    title: 'Co aplikace dělá',
    body: [
      'Portál spravuje sdílený vodovod sdružení — 8 domácností na jednom přívodu. Eviduje odečty, faktury, zálohy a jejich vyúčtování; vedle toho slouží jako úložiště společných dokumentů a přehled hospodaření. Klíčová věc, kterou řeší: voda se nakupuje společně na jeden hlavní vodoměr, ale spotřebovává se individuálně.',
    ],
  },
  {
    id: 'rocni-cyklus',
    title: 'Roční cyklus',
    body: [
      '1. Každý měsíc odečty vodoměrů (import z Excelu, ze souboru odečítačky, nebo ručně).',
      '2. Průběžně faktury za vodu s dílčími odečty.',
      '3. Průběžně zálohy domácností rozdělené na vodu, elektřinu a společný základ.',
      '4. Na konci období uzávěrka — náhled, případné čerpání z fondu, uzavření, PDF pro všechny domácnosti.',
      '5. Po uzávěrce doplatky a výplaty; ty vidíte v Saldu, uzavřené vyúčtování se jimi už nemění.',
    ],
  },
  {
    id: 'vyuctovani',
    title: 'Vyúčtování krok za krokem',
    body: [sections.billingAdmin.disclosure!, sections.lossMethod.disclosure!],
  },
  {
    id: 'zalohy-a-saldo',
    title: 'Zálohy, doplatky a saldo',
    body: [sections.paymentTypes.disclosure!, terms.cistyZustatek.long!],
  },
  {
    id: 'fond',
    title: 'Společný fond',
    body: [sections.fundDraw.disclosure!, sections.waterPriceCarry.disclosure!],
  },
  {
    id: 'odecty',
    title: 'Odečty a import',
    body: [sections.importTwoStep.note!, terms.chybiOdecet.long!, terms.anomalie.long!],
  },
  {
    id: 'role-a-pristup',
    title: 'Role a přístup',
    body: [terms.role.long!],
  },
  {
    id: 'prihlaseni',
    title: 'Přihlášení',
    body: ['Do portálu se přihlásíte odkazem zaslaným e-mailem. Odkaz platí 15 minut a lze ho použít jen jednou. Pokud vám nedorazil, zkontrolujte složku s nevyžádanou poštou a zkuste odkaz vyžádat znovu.'],
  },
];

/** Rozdělí odstavec na úseky podle `**tučného**` vyznačení. */
export function splitBold(paragraph: string): { text: string; bold: boolean }[] {
  return paragraph
    .split(/\*\*(.+?)\*\*/g)
    .map((text, i) => ({ text, bold: i % 2 === 1 }))
    .filter((segment) => segment.text !== '');
}
