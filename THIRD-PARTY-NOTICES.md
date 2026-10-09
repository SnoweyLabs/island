# Third-party notices

Island's own code uses no package: there is no `PackageReference` in any project, and nothing was copied in from another project's source.

What is copied into the shipped folder is Microsoft's own .NET runtime, which makes the build self-contained (`ship/app/`). It is published under the MIT licence; the licence files that come with the runtime packs are kept in `ship/third-party/` as they are:

- `ship/third-party/dotnet-runtime-LICENSE.TXT` — the licence of `Microsoft.NETCore.App.Runtime.win-x64` 10.0.12.
- `ship/third-party/dotnet-windowsdesktop-runtime-LICENSE.txt` — the licence of `Microsoft.WindowsDesktop.App.Runtime.win-x64` 10.0.12 (WPF and Windows Forms).
- `ship/third-party/dotnet-runtime-THIRD-PARTY-NOTICES.TXT` — the runtime's own list of the third-party code it carries and their licences, as Microsoft ships it.

The shipped folder holds files of those two runtime packs only (the SDK also restores a third, `Microsoft.AspNetCore.App.Runtime.win-x64`, into its cache; no file of it is in the folder, which a listing of the folder confirms).

The add-on (`extension/`) carries no library either.

The text of the MIT licence of the .NET runtime, as the pack ships it:

```
The MIT License (MIT)

Copyright (c) .NET Foundation and Contributors

All rights reserved.

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```
