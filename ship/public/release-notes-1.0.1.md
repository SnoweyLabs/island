Island 1.0.1: four changes asked for after the first version.

- **Your own page order.** In Settings → Your pages, drag a page by its handle (or press Up and Down on it). The island, Tab and the number keys follow: put Terminals first, and 1 opens it.
- **Search, three ways.** After the things that match what you typed, there are always three more: search on YouTube, on Google, or on this computer (File Explorer's search, for files and folders).
- **The Chrome add-on in the setup.** The first start has an optional step that opens the add-on's folder and shows the three clicks in Chrome. (Already set up? Settings → General → Run the setup again.)
- **Fixed:** the + at the end of the row no longer turns thinner half a second after the island appears.

**Download:** `Island-Setup.exe` below (about 54 MB). Run it over 1.0.0: your settings and picks stay. If Island is running, the installer asks you to quit it first.

**Windows will warn you.** The installer is not signed with a paid certificate, so Windows shows "Windows protected your PC". Click **More info**, then **Run anyway**.

**What was tried:** made and tried on one Windows 11 laptop (x64), the installer as a quiet test copy there. Not tried on other computers, on Windows 10 or on ARM. No automatic updates: Island never contacts the internet.

**Checksum (SHA-256) of Island-Setup.exe:**

```
d4afb0d2e3aed2258b8045d28c553be77b563fcf3899114d67befa0918d11347  Island-Setup.exe
```

The same line is in `SHA256SUMS.txt`. In PowerShell: `Get-FileHash .\Island-Setup.exe -Algorithm SHA256`.
