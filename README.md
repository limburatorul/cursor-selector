# Cursor Selector

**Descărcare și detalii:** [protagonistlabs.app/cursorselector](https://protagonistlabs.app/cursorselector/)

Aplicație portabilă pentru schimbat cursorul Windows: o bibliotecă de scheme în folderul aplicației
și un client grafic care le previzualizează pe toate cele 17 roluri înainte să le aplici.

## Pornire

Dublu-click pe **`CursorSelector.exe`**.

Executabil nativ .NET Framework — fără instalare, fără drepturi de administrator, fără dependențe
externe și fără PowerShell. Se poate fixa în bara de activități sau în meniul Start.

## Cum funcționează biblioteca

Fiecare **subfolder** din `Library\` este o schemă. Pui înăuntru fișierele `.cur` / `.ani` și gata:

```
Library\
  Windows_11_dark\
    pointer.cur
    busy.ani
    ...
  Pachetul meu\
    Normal Select.cur
    ...
```

Aplicația potrivește automat fișierele cu rolurile Windows, în ordinea:

1. **`scheme.json`** din folder, dacă există — control manual, are prioritate
2. **`Install.inf`** din folder, dacă pachetul vine cu unul — maparea oficială a autorului
3. **numele fișierelor** — recunoaște convențiile uzuale (`Normal Select`, `pointer`, `dgn1`,
   `Diagonal Resize 1`, `VertRes`, `busy`, `link`, `handwriting` etc.)

Fișierele din rădăcina folderului au prioritate; se coboară în subfoldere doar dacă rădăcina e goală
(altfel un subfolder de „bonus cursors" ar fura roluri de la setul principal).

### `scheme.json` — când vrei să corectezi maparea

```json
{
  "name": "Numele afișat în listă",
  "cursors": {
    "Arrow": "pointer.cur",
    "Wait": "busy.ani",
    "Hand": "link.cur"
  }
}
```

Rolurile pe care nu le treci rămân nemapate. Numele rolurilor sunt cele din registrul Windows:
`Arrow, Help, AppStarting, Wait, Crosshair, IBeam, NWPen, No, SizeNS, SizeWE, SizeNWSE, SizeNESW,
SizeAll, UpArrow, Hand, Pin, Person`.

## Clientul

- **Stânga** — schemele găsite. Eticheta `Library` = folder din `Library\`; `System` = schemă deja
  înregistrată în Windows (temele proprii Windows și pachetele instalate prin `.inf`).
- **Dreapta** — cele 17 roluri, cu cursorul real desenat la 48px. Cursoarele `.ani` se animă.
  Rolurile fără fișier apar estompate, cu `—`, și revin la implicitul Windows dacă aplici schema.
  Plăcuțele au fundal gri mediu intenționat: e singura nuanță pe care se văd și cursoarele negre
  (Capitaine, VS Cursors), și cele albe (Windows Inverted) — pe alb sau pe negru unele ar dispărea.
- **Apply selected scheme** — scrie valorile în `HKCU\Control Panel\Cursors`, notifică sistemul
  (`SPI_SETCURSORS`) și înregistrează schema, ca să apară și în *Proprietăți mouse* din Windows.
  Efectul e imediat, fără restart sau logout.
- **Restore saved cursors** — revine la configurația de dinaintea primei aplicări.
- **Import and apply…** — alegi un `.zip` sau orice fișier din folderul unui pachet dezarhivat
  (`.cur`, `.ani`, `Install.inf`); același lucru prin drag & drop al unui folder sau `.zip` oriunde
  pe fereastră. Pachetul e copiat în `Library\` (dezarhivat, fără folderul-înveliș din arhivă),
  selectat și aplicat pe loc — descărcarea se poate șterge apoi. Un nume deja existent în bibliotecă
  e refolosit, nu suprascris.
- Schema aplicată acum are un punct de accent în fața numelui, în listă.

Interfața aplicației este în engleză. Totul e accesibil de la tastatură: săgeți pentru navigarea
prin listă, `Tab` între butoane, `Space` sau `Enter` pentru apăsare, cu inel de focus vizibil.

## Identitate vizuală

Interfața urmează identitatea vizuală Protagonist Labs, cu aceiași
tokeni ca site-urile:

- **Paletă** — `ground #0a0d13`, accent `#4c8dff`, trei trepte de text (`#e8eef6` / `#8a97aa` /
  `#5d6879`). În notă panourile și muchiile sunt alb cu alfa, ca să stea la fel peste orice fundal;
  aici sunt compuse în culori opace, fiindcă WinForms desenează controale opace și n-are un strat
  de compunere sub ele.
- **Tipografie pe roluri** — display pentru titlu, body pentru proză și butoane, mono pentru cifre,
  căi și etichete. Stack-urile sunt `Bricolage Grotesque → Segoe UI`, `IBM Plex Sans → Segoe UI`,
  `IBM Plex Mono → Cascadia Mono`: prima familie instalată câștigă, deci dacă instalezi fonturile
  proprii, aplicația le preia fără nicio modificare.
- **Rail de secțiune** — punct de accent cu halou, etichetă eyebrow și linie care se stinge spre
  dreapta. GDI+ n-are `letter-spacing`, așa că etichetele sunt desenate caracter cu caracter.
- **Butoane pilulă** — principal cu fundal accent, text `#06101f` și halou colorat; fantomă cu
  bordură `edge-bright` peste `panel`.
- **Rândul selectat** — tentă de accent plus o bară de 3px pe muchia din față, nu bordură de 1px:
  pe un rând lat, bordura ar citi ca input focusat.
- **DWM** — bară de titlu întunecată (atribut `20`), colțuri rotunjite (`33`) și culoare de bordură
  (`34`, în `0x00BBGGRR`, nu ordinea din hexul de paletă).

Două lucruri din notă **nu** se pot aplica aici:

- **Sticla acrylic.** Nota o spune explicit: WinForms e în aceeași situație ca Tk — GDI pictează
  zona client opac peste efectul DWM. Ar cere WPF.
- **Bara de derulare** rămâne cea din tema întunecată Windows, nu thumb-ul alb translucid de 6px.
  WinForms n-are echivalent de re-template; ar însemna un scrollbar desenat integral manual.

## Fișiere

| Fișier | Rol |
|---|---|
| `CursorSelector.exe` | aplicația |
| `Library\` | biblioteca de scheme, un subfolder per schemă |
| `backup.json` | configurația cursoarelor de dinaintea primei aplicări; se scrie o singură dată |
| `src\CursorSelector.cs` | codul sursă |
| `build.cmd` | recompilează executabilul |
| `app.ico` | iconița, înglobată în executabil la compilare |

Ștergerea unui folder din `Library\` scoate schema din listă. Dacă schema respectivă era aplicată,
Windows revine la cursoarele implicite pentru fișierele lipsă — folosește *Restore saved cursors* sau aplică
altă schemă.

## Recompilare

După modificări în `src\CursorSelector.cs`:

```
build.cmd
```

Folosește compilatorul C# din `%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\`, care vine cu
Windows — nu trebuie instalat niciun SDK.

Sursa e ASCII curat: caracterele speciale din interfață (`—`, `·`, `…`) sunt scrise ca escape-uri
`\uXXXX`. Așa nu depinde de un BOM și nu se strică dacă o deschizi într-un editor care îl elimină.
