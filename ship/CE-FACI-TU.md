# Island pe GitHub — ce faci tu

Pagina asta e pentru tine, în română. Restul proiectului e în engleză.

## Unde e

- Codul: https://github.com/SnoweyLabs/island
- Versiunea 1.0.0, cu fișierul de instalare: https://github.com/SnoweyLabs/island/releases/tag/v1.0.0
- Linkul care dă mereu cea mai nouă versiune (pe ăsta îl pui pe site): https://github.com/SnoweyLabs/island/releases/latest/download/Island-Setup.exe

## 1. Instaleaz-o tu, prima dată

1. Închide insula veche: iconița din tray → **Quit**. De acum nu mai folosești `run.cmd`.
2. Descarcă `Island-Setup.exe` de la linkul de mai sus și dă-i dublu-click.
3. Windows o să spună „Windows protected your PC”. Apeși **More info**, apoi **Run anyway**.
4. Next, Next, gata. Nu cere drepturi de administrator. La final, căsuța „Start Island” e bifată: insula pornește.
5. Setările și elementele tale (din `%APPDATA%\Island`) sunt aceleași, deci insula vine cu ce aveai.
6. Dacă aveai pornit **Start with Windows**: în insula nouă o să apară oprit (ține minte programul vechi din `dist\Island`). Settings → General → pornește-l o dată, ca Windows să pornească insula instalată, nu pe cea veche.
7. Claude Code și Codex, dacă le-ai conectat, merg mai departe fără nimic de făcut.

## 2. Ce răspunzi când te întreabă cineva de avertisment

„Instalatorul nu e semnat cu un certificat plătit, de-asta Windows avertizează. E gratuit și codul e public pe GitHub. Apeși More info, apoi Run anyway. Dacă vrei să verifici fișierul, pe pagina versiunii e codul lui SHA-256.”

## 3. Versiunea următoare (1.0.1)

Îi spui lui Claude: „fă versiunea 1.0.1 a insulei și public-o pe GitHub”. Linkul de pe site rămâne același: dă mereu cea mai nouă versiune.

## 4. Pagina de pe snoweylabs.com

Textul și pozele sunt gata în `ship/web/island/`. Le dai sesiunii Claude care lucrează la site (sau îi spui lui Claude din Cowork unde e folderul site-ului).

## Pagina veche pentru Microsoft Store

E păstrată ca `ship/store/CE-FACI-TU-store.md`, dar nu se mai folosește: ai ales GitHub în loc de Store.
