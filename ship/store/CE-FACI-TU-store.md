# Ce faci tu, ca Island să ajungă în Store

Scris pentru tine, simplu, ca să poți citi pe telefon. Aici sunt doar lucrurile pe care nu le poate face nimeni în locul tău: un cont, o verificare de identitate, câteva click-uri și două texte de lipit. Restul e gata, în folderul `ship/`.

Nimic nu a fost trimis nicăieri. Niciun cont nu a fost făcut. Aplicația împachetată **nu a rulat niciodată** (nici măcar fișierul pachet nu există încă: pasul 1 de mai jos îl face posibil).

## Înainte de orice

**1. „Doar în Store” a fost alegerea lui Claude, nu a ta.** El a citit paginile Microsoft și a ales Store pentru trei motive: Store semnează pachetul gratuit (nu cumperi niciun certificat cu numele tău pe el), Windows actualizează singur aplicația (deci aplicația nu are nevoie de internet) și înregistrarea e gratuită pentru o persoană. Un program de instalat din afara Store ar arăta tuturor „Windows protected your PC” și ar avea nevoie de internet ca să se actualizeze. Dacă vrei și un program de instalat obișnuit, **spune-i lui Claude înainte să te înregistrezi oriunde.**

**2. Numele public din Store se scrie o singură dată și nu se mai poate schimba.** Formularul îl completează singur din actul tău de identitate. **Șterge ce a scris el și pune `SnoweyLabs`.** Dacă nu te lasă, **oprește-te și spune-i lui Claude.** Dacă o persoană poate folosi un nume de brand acolo nu e confirmat în nicio pagină Microsoft găsită. (Pagina Microsoft spune că numele afișat al editorului, tipul contului și țara nu se pot schimba după înregistrare.)

**3. Ce fel de cont deschizi și dacă te declari „comerciant” sunt hotărâri ale tale.** Fiecare are urmări legale și publice; la fiecare pas unde contează scrie ce face public. Dacă nu ești sigur, întreabă pe cineva care știe legea. Claude nu știe.

**4. Nu cumpăra niciodată un certificat de semnare.** Ar purta numele tău adevărat, public.

## Ce e gata (în `ship/`)

- Programul, publicat într-un folder simplu, cu .NET-ul în el: `ship/app/` (se face din nou cu o comandă; nu e în git). Ocupă aproape 197 MB; fără .NET-ul inclus ar avea 26 MB (`ship/size.md`).
- Descrierea pachetului și pozele lui: `ship/package/` (iconița alesă de Claude e varianta A; tu alegi, vezi pasul 8).
- Textele pentru Store, în engleză și română, justificarea pentru „drepturi depline”, politica de confidențialitate, categoria: `ship/store/`.
- Extensia pentru Chrome, gata de încărcat, cu textele și pozele ei: `ship/addon/`.
- Lista paginilor din care s-a luat fiecare lucru: `ship/SOURCES.md`.

## Pașii tăi

### Pasul 1 — Unealta care face pachetul (o comandă)

Pe calculatorul ăsta nu e nicio unealtă de împachetat. Microsoft are una, în „public preview” (încă nu e versiunea finală). Instalarea, din pagina ei oficială (data paginii: 3 octombrie 2026): deschide **PowerShell** și scrie:

    winget install Microsoft.winappcli --source winget

Durează câteva minute. Apoi spune-i lui Claude Code: **„unealta de împachetat e instalată”** și el o folosește mai târziu, după pasul 5, când are cele trei valori de la Store (nu poate împacheta înainte). Dacă `winget` nu merge, pagina mai arată o descărcare manuală de pe pagina de lansări a uneltei.

⚠ Unealta, dacă o lași singură, pune în pachet numele contului tău de Windows. **Nu o rula tu.** Lasă-l pe Claude, care îi dă numele editorului în mod explicit.

### Pasul 2 — Un cont Microsoft pentru brand

Fă un cont Microsoft nou, **cu o adresă de email a brandului** (de exemplu una pe care o faci doar pentru SnoweyLabs), nu cu a ta personală.

⚠ Emailul de suport poate fi afișat public în Store. Folosește o adresă de brand.

La sfârșit ai: un cont Microsoft și o adresă de email care nu e a ta.

### Pasul 3 — Înregistrarea în Store (gratuită pentru o persoană)

1. Intră **exact** la `https://storedeveloper.microsoft.com` și apasă **Get started for free**. Pagina Microsoft spune că acesta e singurul punct de intrare pentru înregistrarea gratuită: dacă începi din altă parte, apare fluxul vechi, cu taxă (19 dolari, scrie pagina).
2. Alege **Individual developer** (gratuit).
3. Intră cu contul Microsoft de la pasul 2.
4. Verificarea identității: un act de identitate și un selfie, făcute cu **telefonul**, la lumină bună, cu actele originale. Pagina nu spune cât durează.
5. Completezi profilul. Datele din act se pun singure în formular; pagina zice „verifică și schimbă dacă e nevoie”.

⚠ **Aici e căsuța cu numele public. Înlocuiește ce a pus formularul cu `SnoweyLabs`.** Nu se mai poate schimba. Dacă te obligă să rămână numele din act: oprește-te și spune-i lui Claude, nu trimite.

⚠ Telefonul și adresa sunt „opționale pentru dezvoltatorii individuali” și, spune pagina Microsoft despre datele de suport, „în general nu sunt afișate clienților” (pot fi date autorităților când legea cere). Pentru un cont de companie Microsoft le cere (în Franța sunt obligatorii); dacă devin publice nu am găsit scris pe o pagină citită.

La sfârșit ai: acces la Partner Center (poate dura vreo 5 minute până apare tile-ul „Apps & Games”; dacă nu apare, reîmprospătează pagina).

### Pasul 4 — Rezervă numele „Island”

În Partner Center, la pagina **Apps and games**, creează un produs nou și rezervă-i numele **Island**. Pagina Microsoft spune că numele trebuie să fie unic și că rămâne rezervat trei luni. Dacă e luat, încearcă pe rând: **Island Strip**, **Island Deck**, **SnoweyLabs Island**. Spune-i lui Claude care a mers.

La sfârșit ai: un nume rezervat.

### Pasul 5 — Cele trei valori pentru pachet, înapoi la Claude

În Partner Center, deschide produsul, extinde **Product management** în meniul din stânga și alege **Product identity**. Copiază cele trei valori pe care pagina Microsoft le numește pentru manifest: **Package/Identity/Name**, **Package/Identity/Publisher** și **Package/Properties/PublisherDisplayName**, exact, cu literele mari și mici (pagina spune că se potrivesc literă cu literă). Dă-le lui Claude Code.

⚠ **Dacă vreuna dintre ele conține numele tău real, oprește-te și spune-i lui Claude înainte de orice.**

La sfârșit ai: pachetul poate fi făcut. Claude le pune în `ship/package/AppxManifest.xml` în locul lui `__FROM_PARTNER_CENTER__` și face `Island.msix`, nesemnat (Store îl semnează singur; pagina Microsoft spune că nu trebuie semnat cu certificat de la o autoritate).

### Pasul 6 — Un loc pe internet pentru politica de confidențialitate

Store cere un text de confidențialitate pentru programele Win32 (regula 10.5.1). Textul e în `ship/store/privacy.md`. Trebuie să stea la o adresă pe internet (o pagină simplă, de exemplu într-un cont de brand pe un serviciu de găzduit pagini). Alege un serviciu cu cont **de brand, nu personal**.

⚠ Pune în text, în loc de `__OWNER__`, o adresă de email **de brand**. Adresa apare public.

⚠ **Adresa paginii (URL-ul) poate conține un nume**: numele contului sau al serviciului de găzduit apare în ea. Alege un nume de cont care e al brandului, nu al tău, înainte să publici textul.

La sfârșit ai: o adresă (URL) pentru politică.

### Pasul 7 — Trimiterea în Store

În Partner Center, pe rând, paginile trimiterii (în ordinea din pagina Microsoft): Pricing and availability (gratuit), Properties (categoria: **Productivity**, politica de confidențialitate: adresa de la pasul 6), Age ratings (un chestionar; răspunzi tu, vezi `ship/store/age-and-category.md`), Packages (încarci `Island.msix`), Store listings (lipești textele din `ship/store/listing-en.md` și `listing-ro.md`, pozele din `ship/store/screenshots/` și `ship/store/images/`), Submission options (la „Restricted capabilities” lipești textul din `ship/store/full-rights-justification.md`). Apoi **Submit**. Certificarea ia „până la trei zile lucrătoare”.

⚠ Magazinul poate refuza aplicația sau cere schimbări. Dacă drepturile depline („runFullTrust”) vor fi aprobate nu se știe.

La sfârșit ai: aplicația trimisă la certificare.

### Pasul 8 — Iconița

Alege iconița: `review/choices/app-icon.png` arată trei variante (A bila cu marginea aprinsă, B capsula cu lumina sus, C bila cu cele cinci culori). Acum e A. Spune-i lui Claude dacă vrei alta, **înainte** de pasul 5 (pachetul se face cu iconița aleasă).

### Pasul 9 — Extensia în Chrome Web Store

1. Fă un cont Google **de brand**, cu o adresă de email de brand: adresa de contact se afișează public. ⚠ Un cont Google nou îți cere un nume: pune numele brandului, nu al tău.
2. Înscrie-te ca dezvoltator Chrome (taxă unică; **suma nu e scrisă pe nicio pagină oficială găsită**). ⚠ **Numele care apare sub titlul extensiei** se alege acolo: pune `SnoweyLabs`. Dacă un cont personal poate folosi un nume de brand în Chrome Web Store nu e confirmat (UNVERIFIED); dacă nu te lasă, oprește-te și spune-i lui Claude. ⚠ Taxa se plătește printr-un profil de plată: **înainte să plătești verifică ce nume și ce adresă arată acel profil**, și oprește-te dacă sunt ale tale (ce nume arată profilul, nu am găsit pe nicio pagină: UNVERIFIED).
3. Încarcă `ship/addon/island-addon.zip`.
4. Completează cele patru file cu textele din `ship/addon/` (`listing-en.md`, `single-purpose.md`, `permissions.md`, `data-use.md`) și pozele din `ship/addon/images/`.
5. La întrebarea „ești comerciant?”: ⚠ **dacă spui că ești comerciant, numele legal, adresa și telefonul tău „vor fi afișate public la sfârșitul paginii extensiei”** (pagina Chrome despre comercianți). Hotărăște tu, cu cineva care știe legea.
6. ⚠ **Înainte să trimiți:** deschide previzualizarea paginii extensiei și citește numele afișat ca editor. Dacă e cel adevărat al tău, **oprește-te și spune-i lui Claude.**
7. **După încărcare, Chrome Web Store dă extensiei un ID. Spune-i lui Claude acest ID.** Insula acceptă acum doar extensia cu ID-ul pe care îl are cheia ei; dacă ID-ul din Store e altul, insula refuză extensia până îl adaugă Claude (o linie și un test).

La sfârșit ai: extensia trimisă la verificare (zile, uneori săptămâni).

## Ce face Claude după fiecare pas

- După **pasul 1**: nimic încă.
- După **pasul 3 și 4**: nimic încă.
- După **pasul 5**: pune cele trei valori, face `Island.msix` (nesemnat) cu iconița aleasă și verifică (garda de nume, garda manifestului) că nu e nicio urmă de-a ta în el; îți spune când e gata de încărcat.
- După **pasul 6**: pune adresa politicii în texte.
- După **pasul 7**: citește răspunsul certificării cu tine și repară ce cere.
- După **pasul 9**: adaugă ID-ul extensiei în insulă și pornește testul.
- Când pachetul rulează prima dată, faci verificările de mai jos împreună cu el.

## Ce încerci prima dată în aplicația din pachet

Fiecare e o acțiune și o întrebare cu da/nu.

1. Pornește Island din pachet: se vede insula când apeși tasta ei?
2. Cu extensia pusă în Chrome și un tab de YouTube deschis: se aprinde pătratul YouTube pe insulă? (Microsoft nu spune dacă un program din pachet poate fi găsit de extensie.)
3. Iconița insulei apare lângă ceas?
4. Settings → General: pornește „Start with Windows”, deloghează-te și intră din nou: pornește singură, în tăcere (doar iconița și tasta)?
5. În Setările Windows → Aplicații → Pornire, oprește Island: comutatorul din Island spune că Windows îl ține oprit și nu se pornește peste „nu”-ul tău?
6. Settings → Coding agents → Connect (asta schimbă fișierul de setări al lui Claude Code, care e în afara folderului Island; Island arată liniile și cere confirmare, și salvează o copie lângă el): liniile arată `Island.Notify.exe` (nu o cale lungă)? Când Claude Code termină o sarcină, apare notificarea?
7. Iconița „Open settings file” din meniul de lângă ceas deschide ecranul de setări?
8. După o actualizare din Store, tastele, paginile și lucrurile tale sunt tot acolo?
9. Pe un calculator care nu a avut niciodată Island: se deschide singură configurarea în cinci pași, iar „Done” lasă insula deschisă?

## Ce mai e marcat în `ship/` și nu e sigur

Fiecare lucru de mai jos e scris în fișierul numit. Nu e o greșeală: așa e scris ce nu se poate afla fără contul tău sau fără pachetul care încă nu există.

- `__FROM_PARTNER_CENTER__` (le dă Store, pasul 5): în `ship/package/AppxManifest.xml` (numele pachetului, editorul, numele afișat al editorului).
- `__OWNER__` (le scrii tu): în `ship/store/privacy.md` (adresa de contact), în `ship/addon/data-use.md` și în `ship/addon/listing-en.md` (adresa politicii și pagina de suport).
- Extensia: în `ship/addon/permissions.md` (permisiunea pentru adresa 127.0.0.1 poate fi de prisos, nu s-a putut confirma; felul în care găsește playerele celor cinci site-uri e o presupunere) și în `ship/addon/island-addon.zip`: scripturile `lib/media.js` și `content/media-main.js` au în comentarii `UNVERIFIED` (niciunul dintre cele cinci site-uri nu a fost deschis când au fost scrise; selectorii butoanelor sunt presupuneri). Extensia nu a rulat niciodată într-un browser adevărat.
- `UNVERIFIED` (nu s-a putut confirma pe o pagină oficială): în `ship/SOURCES.md` (lista completă, cu pagina fiecăruia: de exemplu dacă cheia `key` din manifestul extensiei trebuie scoasă la încărcare, dacă un pachet cu totul nesemnat e primit, dacă „drepturile depline” sunt aprobate), în `ship/store/age-and-category.md` (chestionarul de vârstă se vede doar după autentificare) și în `ship/addon/data-use.md` (dacă un program de pe același calculator e „terță parte” pentru Chrome Web Store; formularea căsuțelor de confidențialitate).
- În `ship/CHECKS-FOR-OWNER.md`: ce nu s-a încercat niciodată într-un pachet (extensia conectată, cheia globală, iconița de lângă ceas), scrise ca `UNVERIFIED`.
- `ship/store/full-rights-justification.md` și `ship/store/listing-en.md` nu au marcaje; textele au fost scrise de un model și **niciun jurist nu le-a citit**. Tu citești `ship/store/listing-ro.md`.
