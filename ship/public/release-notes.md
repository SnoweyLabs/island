The first version of Island: a small glass island at the top of your Windows screen that keeps the programs, folders and websites you use most one key away.

**Download:** `Island-Setup.exe` below (about 54 MB). Double-click it: it installs for you only, without asking for administrator rights, puts Island in the Start menu, and can start it at the end. Then press **Ctrl+Q**.

**Windows will warn you.** The installer is not signed with a paid certificate, so Windows shows "Windows protected your PC". Click **More info**, then **Run anyway**. Some antivirus programs may also be wary of a new, unsigned installer.

**What was tried, and what was not:** Island 1.0.0 was made and tried on one Windows 11 laptop (x64). The installer itself was tried only as a quiet test copy on that laptop. Nobody has installed it on another computer yet, and it was never tried on Windows 10 or on an ARM computer. There are no automatic updates: Island never contacts the internet.

**The Chrome add-on** is optional and not in the Chrome Web Store: the installer puts it beside the program, and the README says how to load it by hand.

**Checksum (SHA-256) of Island-Setup.exe:**

```
78ff15bc7029262ba0151bce13e8ddc43d641e36265f79945c0d64ddf4b21e7b  Island-Setup.exe
```

The same line is in `SHA256SUMS.txt`. In PowerShell: `Get-FileHash .\Island-Setup.exe -Algorithm SHA256`.
