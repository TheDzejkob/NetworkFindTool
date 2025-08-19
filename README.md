# NetworkFindTool

Desktopová WPF aplikace pro rychlé zobrazení hostů připojených ke switchi a automatický objev topologie (CDP/LLDP) od jednoho výchozího zařízení až po celou strukturu.

Aplikace používá SSH ke spuštění show příkazů (MAC tabulka, ARP tabulka, stav portů) a umí průběžně objevovat sousední switche napříč vendory (Cisco, Aruba/ProCurve, Huawei, apod.).

## Hlavní funkce

- Zobrazení hostů připojených ke switchi:
  - show mac address-table (VLAN, MAC, typ, port)
  - show ip arp (IP, stáří záznamu)
  - show interfaces status (jméno portu, status)
  - Sloučení MAC ↔ ARP ↔ status portů do jedné tabulky s filtrováním
- Objevování switchů:
  - Start z jedné IP/hostu, BFS přes CDP/LLDP sousedy
  - Průběžné (procedurální) přidávání nalezených zařízení do seznamu
  - Vypnutí stránkování CLI (terminal length 0 / no page / screen-length disable)
  - Fallbacky pro různé vendory (show cdp entry <id>, různé LLDP příkazy)
  - Fallback na DNS, pokud soused nemá inzerovanou management IP
- Příjemné UI:
  - Okno pro zadání přihlašovacích údajů
  - Okno pro objev switchů s “Double‑click to select”
  - Jednoduché vyhledávání hostů (IP/MAC)

## Technologie

- .NET 6, WPF
- [SSH.NET](https://github.com/sshnet/SSH.NET) (2025.0.0)

## Požadavky

- Windows 10/11
- Přístup přes SSH na cílové switche
- Povolené CDP a/nebo LLDP na linkách mezi zařízeními (pro discovery)


## Použití

- Zobrazení hostů:
  1. Zadejte IP/hostname switche v hlavním okně.
  2. Klikněte na “Submit”.
  3. Zadejte přihlašovací údaje.
  4. Aplikace provede:
     - show mac address-table
     - show ip arp
     - show interfaces status
     - Výsledky sloučí a zobrazí v tabulce; lze filtrovat dle IP/MAC.

- Objevování switchů:
  1. Klikněte “Pick from list”.
  2. Zadejte startovní IP/hostname a přihlašovací údaje.
  3. Klikněte “Discover”.
  4. Nalezené switche se přidávají do seznamu průběžně (procedurálně). Dvojklik vybere zařízení a přenese IP zpět do hlavního okna.

Poznámka: Některé CLI výpisy vyžadují privilegovaný mód. Aplikace se o něj pokusí (“enable”) s běžným heslem.

## Jak discovery funguje (stručně)

- SSH ShellStream (ne blokující RunCommand) + detekce promptu s timeouty
- Vypne stránkování (terminal length 0 / no page / screen-length disable)
- Určí hostname z show hostname / show version / running-config
- Načte sousedy:
  - CDP: show cdp neighbors detail → pokud chybí IP, použije show cdp neighbors + show cdp entry <id>
  - LLDP: show lldp neighbors detail (příp. “show lldp info remote-device detail” na HP/Aruba, “display lldp neighbor-information verbose” na Huawei)
- Běží BFS: nalezené IP se frontují, UI se plní průběžně

## Tipy a omezení

- Pokud “objeví jen sebe”:
  - Ověřte, že CDP/LLDP je povoleno na linkách a sousedi inzerují management IP.
  - Zkuste stejné přihlašovací údaje na sousedních switchích.
  - Firewall/ACL musí povolit SSH mezi zařízeními.
- Výstupy “show …” se liší podle vendora a verze OS. Parsery jsou robustní, ale pokud máte atypický formát, přiložte vzorky a parser zpřesníme.
- DNS fallback se používá jen pokud soused vrátí jméno bez IP.

## Bezpečnost

- Přihlašovací údaje se používají pouze k připojení přes SSH. Ujistěte se, že jsou přenášeny v bezpečné síti.
- Upravte logování dle zásad vaší organizace (v projektu se neukládají citlivé výstupy).

## ScreenShots
 -main screen
<img width="286" height="588" alt="image" src="https://github.com/user-attachments/assets/63a9d9d7-4ee9-4bc1-b6ef-5a45db250989" />
-switch discover
<img width="496" height="544" alt="image" src="https://github.com/user-attachments/assets/99cca30b-672f-486e-8e6e-7d41ffecd10c" />

