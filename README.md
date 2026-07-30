# WritingApp

WritingApp is an early native Windows writing application designed around
keyboard use and screen-reader accessibility. It is intended to provide a
simple, account-free and subscription-free alternative for writing ordinary
documents.

The primary test environment is JAWS for Windows. VoiceOver support is a
longer-term goal.

## Current capabilities

- Native Windows editing experience without a web view
- Open and save TXT and RTF documents
- Structured DOCX import and export for paragraphs, headings and lists
- Read PDF text and export documents as PDF
- Multiple document windows and `Ctrl+N`
- Search and replace
- Word and character counts
- Automatic recovery copies
- Boundary sounds at the beginning and end of a document

This is an early personal project, not a finished release. Advanced DOCX
content such as tables, images, comments and arbitrary hyperlink labels is not
fully preserved yet.

## Accessibility question: headings inside the editor

The most important unresolved problem is exposing paragraph headings to JAWS
without turning the editor into a browser-style interaction surface.

Please read [ACCESSIBILITY.md](ACCESSIBILITY.md) before suggesting a solution.
It documents the required behavior and the approaches already tested.

Useful advice would include:

- a native editable control that exposes per-paragraph heading semantics;
- a practical UI Automation or `IAccessibleEx` provider design;
- information about how Microsoft Word exposes heading levels while remaining
  in normal editing mode;
- small, testable examples that work with JAWS.

## Building

Requirements:

- Windows 10 or Windows 11
- .NET SDK capable of targeting .NET Framework 4.8

From the `src` directory:

```powershell
dotnet restore
dotnet build
```

The project references [PdfPig](https://github.com/UglyToad/PdfPig) through
NuGet for PDF text extraction.

## Contributing advice

Issues containing technical explanations, references or minimal prototypes are
welcome. Pull requests are proposals only and are not merged automatically.
See [CONTRIBUTING.md](CONTRIBUTING.md).

## License status

No open-source license has been selected yet. Copyright remains with the
project owner. The source is published for review and discussion; please ask
before reusing or redistributing it outside GitHub's normal viewing and
forking features.
