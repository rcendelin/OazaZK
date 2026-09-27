/**
 * „Návod pro správce“ (T12): step-by-step procedures for the administrator, shown on `/navod` (AdminGuidePage).
 *
 * Plain data only — the Playwright spec `e2e/adminGuide.spec.ts` imports this file, walks the same steps through
 * the UI and saves a screenshot for every step that names one (`GUIDE_SCREENSHOTS=1`, into `web/public/navod/`).
 * The spec checks that it produced exactly the screenshots listed here, so a step added or removed here must be
 * mirrored in the spec. Quote buttons and fields exactly as the UI labels them.
 */

export interface GuideStep {
  /** Czech, no technical jargon. „…“ quotes a label exactly as it appears on screen. */
  text: string;
  /** File name under `web/public/navod/` (served as `/navod/<file>`). */
  screenshot?: string;
}

export interface GuideProcedure {
  id: string;
  title: string;
  intro: string;
  steps: GuideStep[];
  /** The page where the procedure starts („Otevřít stránku“). */
  path: string;
}

export const adminProcedures: GuideProcedure[] = [
  {
    id: 'odecty',
    title: 'Zadat měsíční odečty',
    intro:
      'Stavy vodoměrů se zadávají jednou měsíčně. Nejrychlejší je nahrát tabulku z Excelu nebo soubor z odečítačky; '
      + 'když chybí jen pár čísel, zadejte je ručně. Při nahrání souboru se nic neuloží, dokud náhled nepotvrdíte.',
    path: '/readings/import',
    steps: [
      {
        text: 'V menu otevřete „Odečty“ → „Import odečtů“. Nahoře jsou tři záložky: „Excel (.xlsx)“, '
          + '„Odečítačka (.txt / schránka)“ a „Ruční zadání“.',
        screenshot: 'odecty-1.png',
      },
      {
        text: 'Na záložce „Excel (.xlsx)“ přetáhněte soubor s odečty do rámečku (nebo na rámeček klikněte a soubor vyberte). '
          + 'Pod rámečkem se objeví název souboru — klikněte na „Importovat“. '
          + 'Máte-li data z odečítačky, použijte záložku „Odečítačka (.txt / schránka)“: vyberte datum odečtu, '
          + 'soubor z odečítačky (nebo data vložte do pole) a klikněte na „Načíst náhled“.',
        screenshot: 'odecty-2.png',
      },
      {
        text: 'Zobrazí se „Náhled importu“ — tabulka s daty a stavy jednotlivých vodoměrů. Zatím se nic neuložilo. '
          + 'Červené „Chyby“ je potřeba opravit v souboru a nahrát ho znovu (dokud tam jsou, potvrdit nejde). '
          + 'Žlutá „Upozornění“ jsou jen k zamyšlení — třeba neobvykle velká spotřeba nebo chybějící vodoměr.',
        screenshot: 'odecty-3.png',
      },
      {
        text: 'Když čísla sedí, klikněte na „Potvrdit import“. Objeví se „Import byl úspěšný. Importováno … odečtů.“ '
          + 'Pokud se vám náhled nelíbí, klikněte na „Zrušit“ a nic se neuloží.',
        screenshot: 'odecty-4.png',
      },
      {
        text: 'Ruční zadání: na záložce „Ruční zadání“ nastavte „Datum odečtu“ a u každého vodoměru napište do sloupce '
          + '„Stav vodoměru (m³)“ číslo, které ukazuje vodoměr (ne spotřebu za měsíc). Desetinná čárka i tečka fungují, '
          + 'prázdné řádky se přeskočí. Nakonec klikněte na „Uložit odečty“.',
        screenshot: 'odecty-5.png',
      },
    ],
  },
  {
    id: 'faktura',
    title: 'Vložit fakturu a zálohu',
    intro:
      'Každá faktura, vyúčtování nebo záloha dodavateli se zapisuje jako náklad k nákladové složce (např. elektřina '
      + 'vodárny). Aplikace ho sama rozpočítá mezi domy. Doklad (PDF nebo fotku) je dobré nejdřív nahrát do Dokumentů, '
      + 'aby byl k nákladu připojený.',
    path: '/naklady',
    steps: [
      {
        text: 'Nahrání dokladu: v „Dokumenty“ klikněte na „Nahrát faktury a vyúčtování (více souborů najednou)“. '
          + 'Vyberte soubory (PDF, JPG nebo PNG), případně „Nákladová složka (nepovinné)“, a klikněte na „Nahrát …“ (číslo za slovem je počet vybraných souborů). '
          + 'Doklady se uloží do kategorie „Faktury a vyúčtování“.',
        screenshot: 'faktura-1.png',
      },
      {
        text: 'Otevřete „Hospodaření“ → „Náklady“. Nahraný doklad je nahoře v rámečku „Dokumenty bez zaúčtování“ — '
          + 'klikněte u něj na „Zaúčtovat“. (Stejně funguje odkaz „Vytvořit náklad“ u faktury v Dokumentech.) '
          + 'Otevře se správná složka a doklad je už vybraný ve formuláři.',
        screenshot: 'faktura-2.png',
      },
      {
        text: 'Ve formuláři „Přidat náklad“ vyberte „Druh“: „Jednorázový / faktura“ pro běžnou fakturu, „Vyúčtování“ '
          + 'pro roční vyúčtování (doplatek kladně, přeplatek se znaménkem minus), „Záloha“ pro jednu zálohu. '
          + 'Vyplňte „Období od“ a „do“ (za jaké období faktura je), „Částka (Kč)“, „Dodavatel“ a „Úhrada“ '
          + '(z účtu, hotově…). Klikněte na „Přidat“ — náklad se objeví v tabulce. U vody z vodárny se formulář '
          + 'jmenuje „Přidat fakturu“ a vyplňuje se i „Množství (m³)“ z faktury.',
        screenshot: 'faktura-3.png',
      },
      {
        text: 'Pravidelné zálohy nemusíte zadávat po jedné: ve formuláři „Opakovaná záloha“ vyplňte „Částka (Kč)“, '
          + '„Perioda“ (měsíčně, čtvrtletně…), „Od“, „Do“, „Dodavatel“ a „Úhrada“ a klikněte na „Vytvořit zálohy“. '
          + 'Aplikace vypíše, kolik záloh vytvořila.',
        screenshot: 'faktura-4.png',
      },
      {
        text: 'Kontrola: u každého nákladu v tabulce ukáže „Rozpad na domy“, kolik z něj připadlo na který dům. '
          + 'Chybný záznam smažete tlačítkem „Smazat“ (u uzavřeného období to nejde).',
        screenshot: 'faktura-5.png',
      },
    ],
  },
  {
    id: 'pokladna',
    title: 'Zapsat výdaj z pokladny',
    intro:
      'Pokladna eviduje hotovost spolku. Záznamy vidí všichni členové a nedají se smazat — omyl se opraví stornem. '
      + 'Výdaj jde zapsat i bez dokladu, jen je pak potřeba napsat, co se platilo a komu.',
    path: '/pokladna',
    steps: [
      {
        text: 'Otevřete „Hospodaření“ → „Pokladna“. Nahoře vidíte „Zůstatek v pokladně“, pod ním formulář pro nový '
          + 'záznam a pod ním pokladní knihu.',
        screenshot: 'pokladna-1.png',
      },
      {
        text: 'Ve formuláři nechte „Druh“ = „Výdaj“ a vyplňte „Datum“, „Částka (Kč)“, „Kategorie“ (vyberte z nabídky), '
          + '„Co“ (co se koupilo nebo zaplatilo) a „Komu“. Když doklad nemáte, odškrtněte „mám doklad“. '
          + 'Je-li to společný náklad, který se má rozpočítat mezi domy (třeba sekání trávy), vyberte '
          + '„Společný náklad složky“. Naskenovaný doklad vyberete v poli „Doklad (PDF)“.',
        screenshot: 'pokladna-2.png',
      },
      {
        text: 'Klikněte na „Zapsat“. Výdaj se objeví v pokladní knize a zůstatek se sníží. Pokud by pokladna šla do '
          + 'mínusu, aplikace výdaj nezapíše a napíše proč — nejdřív zapište vklad (výběr z účtu).',
        screenshot: 'pokladna-3.png',
      },
      {
        text: 'Omyl: u záznamu klikněte na „Storno“, napište „Důvod storna“ a potvrďte tlačítkem „Stornovat …“. '
          + 'Původní záznam zůstane přeškrtnutý a storno se zapíše jako nový řádek.',
        screenshot: 'pokladna-4.png',
      },
    ],
  },
  {
    id: 'mezizaverka',
    title: 'Udělat mezizávěrku',
    intro:
      'Mezizávěrka „zamkne“ saldo domů k určitému dni — typicky na konci roku. Co do toho dne patří, už nejde změnit; '
      + 'pozdě došlá faktura se zapíše jako oprava v otevřeném období. Před mezizávěrkou proto zadejte všechny odečty, '
      + 'faktury a platby za uzavírané období.',
    path: '/mezizaverky',
    steps: [
      {
        text: 'Zkontrolujte, že jsou zadané odečty, faktury, zálohy i platby až do dne, ke kterému chcete uzavírat '
          + '(přehled najdete v „Hospodaření“ → „Saldo domu“).',
      },
      {
        text: 'Otevřete „Hospodaření“ → „Mezizávěrky“. Ve formuláři „Nová mezizávěrka“ vyplňte „Uzavřít do (včetně)“ '
          + '(např. 31. 12.), „Rozsah“ nechte „Všechny domy“ (pro jeden dům vyberte „Jeden dům“ a „Dům“) '
          + 'a napište „Důvod“, třeba „roční závěrka 2026“.',
        screenshot: 'mezizaverka-1.png',
      },
      {
        text: 'Klikněte na „Uzavřít“. Otevře se detail mezizávěrky se snímkem salda všech domů. Zelená věta '
          + '„Saldo k datu mezizávěrky odpovídá snímku.“ znamená, že je vše v pořádku.',
        screenshot: 'mezizaverka-2.png',
      },
      {
        text: 'Pro účetní stáhněte tlačítkem „Export pro účetní (XLSX)“ tabulku a pošlete jí ji.',
      },
      {
        text: 'Když jste uzavřeli omylem: zrušit jde jen poslední mezizávěrku. V jejím detailu vyplňte „Důvod zrušení“ '
          + 'a klikněte na „Zrušit mezizávěrku“.',
      },
    ],
  },
  {
    id: 'prevod',
    title: 'Převést dům na nového majitele',
    intro:
      'Při prodeji domu se původnímu majiteli uzavře saldo ke dni před předáním a nový majitel začíná „od nuly“ '
      + '— se stavem vodoměru z předávacího protokolu. Před zápisem aplikace ukáže, jaký bude dopad.',
    path: '/admin/opening-balances',
    steps: [
      {
        text: 'Otevřete „Administrace“ → „Počáteční stavy“ a sjeďte dolů k části „Převod domu na nového majitele“. '
          + 'Vyberte „Dům“ a do „Nový vlastník od“ zadejte den předání domu. Klikněte na „Náhled dopadů“.',
        screenshot: 'prevod-1.png',
      },
      {
        text: 'Zkontrolujte náhled: „Původní vlastník“, den, ke kterému se „Uzavře se k“, a „Závěrečné saldo“ — '
          + 'přeplatek nebo nedoplatek, který je potřeba s původním vlastníkem vyrovnat. Pokud se místo formuláře '
          + 'objeví červený seznam problémů, nejdřív je vyřešte.',
        screenshot: 'prevod-2.png',
      },
      {
        text: 'Vyplňte „Nový vlastník“ a „Kontakt (e-mail)“. Do „Stav vodoměru … (m³)“ napište stav z předávacího '
          + 'protokolu (aplikace předvyplní odhad z odečtů; když skutečný stav nemáte, zaškrtněte „odhad“). '
          + 'Do „Zdroj stavu“ napište např. „předávací protokol“. „Podíl ve fondu (Kč)“ nechte 0, pokud nový '
          + 'majitel nic nevložil. Nechte zaškrtnuté „změnit kontakt domu“.',
        screenshot: 'prevod-3.png',
      },
      {
        text: 'Klikněte na „Převést dům“. Zobrazí se „Dům převeden. Závěrečné saldo původního vlastníka: …“. '
          + 'Novému majiteli pak v „Administrace“ → „Uživatelé“ založte přístup k domu.',
        screenshot: 'prevod-4.png',
      },
    ],
  },
];
