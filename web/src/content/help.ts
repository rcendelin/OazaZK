/**
 * Jediný zdroj textů nápovědy. Konzumují ho jak kontextové komponenty
 * (HelpNote / HelpDisclosure / HelpTerm), tak stránka /jak-to-funguje —
 * proto se nemohou rozejít.
 *
 * Texty popisující výpočty pocházejí z těchto use casů; při jejich změně
 * uprav i text zde:
 *   saldo, ztrata                 → HouseLedgerUseCase, WaterSettlementUseCase
 *   doporucenaZaloha              → CalculatePrescribedAdvancesUseCase
 *   interimClosing                → InterimClosingsUseCase
 *   openingBalances               → OpeningBalancesUseCase
 */

export type TermId =
  | 'ztrata' | 'saldo' | 'doporucenaZaloha'
  | 'rozpoustiPreplatek' | 'role' | 'chybiOdecet' | 'anomalie';

export type SectionId =
  | 'paymentTypes' | 'importTwoStep' | 'documentVersions'
  | 'interimClosing' | 'openingBalances';

export interface Term {
  label: string;
  short: string;
  long?: string;
}

export interface Section {
  /** Krátká poznámka (HelpNote); sekce může mít jen rozklikávačku. */
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
    long: 'Ztráta = spotřeba hlavního vodoměru minus součet spotřeb všech domů za úsek mezi odečty hlavního vodoměru. Zaplatit ji musí spolek, takže se její cena rozpočítá mezi domy podle metody platné v daném úseku. Rozpis najdete v části Voda a ztráty. Záporná ztráta znamená, že domovní vodoměry naměřily víc než hlavní; to signalizuje chybu v odečtech nebo výměnu vodoměru, ne úsporu.',
  },
  saldo: {
    label: 'Saldo domu',
    short: 'Počáteční podíl ve fondu + platby domu − jeho podíl na nákladech. Kladné = přeplatek, záporné = nedoplatek.',
    long: 'Saldo je jeden zůstatek za celý dům napříč všemi náklady — přeplatek u vody pokryje nedoplatek u elektřiny. Kladné saldo znamená, že spolek dluží domu (přeplatek), záporné, že dům dluží spolku (nedoplatek). U každého nákladu je v části Saldo domu vidět, jak vznikl.',
  },
  doporucenaZaloha: {
    label: 'Doporučená záloha',
    short: 'Náklady, které saldo přiřadilo domu za posledních 12 měsíců, děleno 12 a zaokrouhleno na celé koruny.',
    long: 'Voda zahrnuje i podíl na ztrátách, elektřina jsou složky elektřiny a do společné části spadá vše ostatní. Admin může u každého domu doporučenou zálohu přepsat vlastní hodnotou; ta pak platí jako skutečná záloha, dokud úpravu nezruší.',
  },
  rozpoustiPreplatek: {
    label: 'Rozpouští přeplatek',
    short: 'Poznámka pro správce, ne pro výpočet. Na žádné číslo v aplikaci nemá vliv.',
    long: 'Označuje domácnost, která se domluvila, že místo posílání měsíčních záloh nechá umořit svůj přeplatek — náklady mu ho postupně ubírají, dokud se nevyčerpá. U takové domácnosti je pak normální, že nechodí platby, a nejde o dluh.\n\nSaldo ani doporučené zálohy se u ní počítají úplně stejně jako u ostatních. Pokud čekáte, že zapnutí příznaku samo zastaví předepisování záloh, nezastaví.',
  },
  role: {
    label: 'Role',
    short: 'Určuje, co uživatel v portálu vidí.',
    long: '**Admin** — všechno, včetně správy domácností, uživatelů a vodoměrů, importu odečtů a plateb a mezizávěrek.\n\n**Účetní** — finanční data všech domácností (náklady, voda a ztráty, saldo všech domů, mezizávěrky), ale bez správy a bez importu odečtů.\n\n**Člen** — jen data své vlastní domácnosti.',
  },
  chybiOdecet: {
    label: 'Varování „chybí odečet"',
    short: 'Některý nakonfigurovaný vodoměr nemá v importovaném souboru hodnotu.',
    long: 'Varování import neblokuje, potvrdit ho můžete. Chybějící odečet se ale projeví ve výpočtu vody: spotřeba se počítá z nejbližších dostupných odečtů, a nemá-li vodoměr v úseku žádný odečet, vyjde jeho spotřeba nulová. Náklad za tuto domácnost pak nesou ostatní.',
  },
  anomalie: {
    label: 'Varování „anomálie"',
    short: 'Spotřeba je víc než dvojnásobek klouzavého průměru tohoto vodoměru.',
    long: 'Varování neblokuje uložení. Bývá to buď skutečný nárůst (bazén, zálivka, netěsnost), nebo překlep v odečtu. Než potvrdíte, porovnejte hodnotu s předchozím stavem.',
  },
};

export const sections: Record<SectionId, Section> = {
  interimClosing: {
    note: 'Mezizávěrka zafixuje saldo všech domů (nebo jednoho domu) k datu. Co je do toho dne, se už nemění; pozdní opravy se zaúčtují až po řezu.',
    disclosureTitle: 'Co mezizávěrka udělá a kdy ji dělat',
    disclosure: '**Co udělá.** Uloží saldo každého domu k vybranému dni (snímek) a od té chvíle zamkne vše, co do toho dne patří: pravidla a účast ve složkách, náklady, počáteční stavy, odečty i platby.\n\n**Roční závěrka** je mezizávěrka všech domů k 31. 12. Z jejího detailu stáhnete export pro účetní.\n\n**Mezizávěrka jednoho domu** se dělá při prodeji: den před předáním se uzavře saldo původního vlastníka.\n\n**Co když přijde vyúčtování až potom.** Zadejte ho normálně s celým obdobím. Portál ho rozdělí podle toho, kdo se kdy na složce podílel, ale do salda ho zaúčtuje k prvnímu dni po mezizávěrce jako opravný záznam — uzavřené saldo zůstane. Stejně tak pozdě zapsaná platba s datem před řezem se zaúčtuje po řezu.\n\n**Omyl.** Zrušit lze jen poslední mezizávěrku a jen s uvedeným důvodem. V detailu je vidět, jestli se saldo k datu mezizávěrky od snímku nezměnilo (rozdíl by měl být vždy nula).',
  },
  openingBalances: {
    note: 'Portál nepřepočítává celou historii. Začíná od známých hodnot k datu startu účtování — u každé hodnoty uveďte, odkud je, a dopočtenou označte jako odhad.',
    disclosureTitle: 'Jak zadat počáteční stavy',
    disclosure: '**1. Start účtování.** Zvolte datum (voda a elektřina vodárny se účtují od 1. 11. 2023) a klikněte na „Start účtování k datu“. Každý dům dostane období vlastnictví se současným vlastníkem.\n\n**2. Stavy vodoměrů.** Nejlépe skutečný odečet k tomu dni. Když chybí, tlačítko „Navrhnout z odečtů“ dopočítá stav mezi dvěma nejbližšími odečty (podle dní) a označí ho jako odhad. Hodnotu můžete přijmout nebo přepsat.\n\n**3. Podíl ve fondu.** Stav z poslední roční závěrky. Kladné číslo znamená, že dům má u spolku přeplatek.\n\n**4. Kredit u dodavatele.** Přeplatek složky u dodavatele (např. elektřina vodárny) zadejte záporně. „Náhled rozdělení“ ukáže, kolik připadne na každý dům.\n\n**Po mezizávěrce** jde hodnotu už jen opravit s uvedeným důvodem.',
  },
  importTwoStep: {
    note: 'Import má dva kroky. Nejdřív se soubor načte a zkontroluje — v této fázi se neuloží nic. Uloží se až po vašem potvrzení náhledu. Když náhled zavřete bez potvrzení, nestane se nic.',
  },
  documentVersions: {
    note: 'U každého dokumentu se drží historie posledních 10 verzí. Nahráním nové verze se původní soubor nepřepíše — zůstane dostupný ke stažení. Po překročení desáté verze se nejstarší odstraní.',
  },
  paymentTypes: {
    note: 'Záznamy jsou tří typů: měsíční záloha, doplatek a výplata přeplatku. Všechny se promítnou do salda domu.',
    disclosureTitle: 'Tři typy záznamů',
    disclosure: '• **Měsíční záloha** — pravidelná platba, jedna na domácnost a měsíc. Zvýší saldo domu.\n\n• **Doplatek** — mimořádná platba, může jich být víc. Zvýší saldo domu.\n\n• **Výplata přeplatku** — vrácení přeplatku domácnosti. Sníží saldo domu.\n\nPočáteční stavy (podíl ve fondu k datu startu účtování) se nezadávají jako platba, ale v části Správa → Počáteční stavy.',
  },
};

export const guides: Guide[] = [
  {
    id: 'co-aplikace-dela',
    title: 'Co aplikace dělá',
    body: [
      'Portál spravuje sdílený vodovod sdružení — 8 domácností na jednom přívodu. Eviduje odečty, náklady (faktury dodavatelů), platby domácností a saldo každého domu; vedle toho slouží jako úložiště společných dokumentů a přehled hospodaření. Klíčová věc, kterou řeší: voda se nakupuje společně na jeden hlavní vodoměr, ale spotřebovává se individuálně.',
    ],
  },
  {
    id: 'rocni-cyklus',
    title: 'Roční cyklus',
    body: [
      '1. Každý měsíc odečty vodoměrů (import z Excelu, ze souboru odečítačky, nebo ručně).',
      '2. Průběžně náklady — zálohy a vyúčtování dodavatelů a jednorázové výdaje; portál je rozpočítá na domy podle pravidel nákladových složek.',
      '3. Průběžně platby domácností (zálohy, doplatky, výplaty přeplatků), ideálně importem z banky.',
      '4. Saldo domu je vidět kdykoli — počítá se průběžně ze všech nákladů a plateb.',
      '5. K 31. 12. roční závěrka (mezizávěrka všech domů), která saldo k tomu dni zafixuje.',
    ],
  },
  {
    id: 'zalohy-a-saldo',
    title: 'Platby, zálohy a saldo',
    body: [
      sections.paymentTypes.disclosure!,
      terms.saldo.short,
      terms.saldo.long!,
      `Doporučená záloha: ${terms.doporucenaZaloha.short}`,
      terms.doporucenaZaloha.long!,
    ],
  },
  {
    id: 'mezizaverky',
    title: 'Počáteční stavy a mezizávěrky',
    body: [sections.openingBalances.disclosure!, sections.interimClosing.disclosure!],
  },
  {
    id: 'odecty',
    title: 'Odečty a import',
    body: [
      sections.importTwoStep.note!,
      terms.chybiOdecet.short,
      terms.chybiOdecet.long!,
      terms.anomalie.short,
      terms.anomalie.long!,
    ],
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

/**
 * Rozdělí odstavec na úseky podle `**tučného**` vyznačení.
 *
 * Zpracovává jen PÁROVÉ `**` — osamocené nebo liché `**` projdou jako
 * doslovný text (žádné escapování). Komponenta `HelpNote` vykresluje `note`
 * bez rozkladu na tučné a `HelpTerm` dává `short` do atributu `title` (taky
 * bez rozkladu) — pokud ale stejný `note`/`short` text někdo zkopíruje do
 * `guides[].body`, tamní cestou už přes `splitBold` projde. Ať se `**`
 * v daném místě rozloží, nebo ne, do `note`/`short` nepatří — tato pole
 * nesmí obsahovat `**` ani jinou markdown syntaxi.
 */
export function splitBold(paragraph: string): { text: string; bold: boolean }[] {
  return paragraph
    .split(/\*\*(.+?)\*\*/g)
    .map((text, i) => ({ text, bold: i % 2 === 1 }))
    .filter((segment) => segment.text !== '');
}
