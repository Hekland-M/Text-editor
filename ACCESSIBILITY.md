# WritingApp accessibility notes

## Goal

WritingApp should feel like Notepad or Microsoft Word when used with JAWS:

- focus enters the editor directly;
- the user remains in ordinary editing mode;
- there is no browser or forms-mode transition sound;
- Up Arrow and Down Arrow move through text normally;
- when the caret enters a heading paragraph, JAWS announces the heading level
  once before reading the line;
- ordinary paragraphs are read normally.

Desired example:

> Heading level 2, This is a section heading

The editor must remain fully editable. A separate read-only document view is
not an adequate replacement.

## Current editor

WritingApp currently uses the Windows Forms `RichTextBox`, wrapped by
`WritingAppTextBox` in `src/MainForm.cs`.

This control provides the native editing behavior that has worked best with
JAWS so far. WritingApp uses `PARAFORMAT2` messages for paragraph styles,
outline levels and list formatting.

DOCX files retain heading information in the document model and on export even
though JAWS does not currently announce that information inside WritingApp.

## Approaches already tested

### 1. Native RichEdit paragraph style and outline level

The application sent `EM_SETPARAFORMAT` with:

- `PFM_STYLE`
- `PFM_OUTLINELEVEL`
- negative native heading style values
- outline levels zero through five

The visual formatting was applied and DOCX export could preserve the intended
heading level. JAWS still read the paragraph as ordinary text.

The relevant implementation is in `WritingAppTextBox` and
`ApplyParagraphStyle` in `src/MainForm.cs`.

### 2. Changing the editor's accessible name

On caret movement, a prototype changed the accessible name of the whole editor
to a string such as:

> Heading level 2, This is a heading

It then raised an accessibility name-change event.

This was not real heading semantics. JAWS read the line normally, announced
the custom text, and sometimes announced the entire edit control again. The
result was duplicated or triplicated speech including phrases such as
"edit" and "type in text."

This approach is not acceptable and is not present in the main application.

### 3. HTML headings

A prototype using real HTML heading elements allowed JAWS to recognize heading
levels. However, entering the editor produced the unwanted browser/forms-mode
transition sound and browser-style interaction behavior.

WritingApp is intentionally a native writing application, so embedding a web
editor is not considered a satisfactory solution.

### 4. WinUI 3 RichEditBox

A separate prototype used:

- .NET 10
- Windows App SDK 2.3.1
- WinUI 3 `RichEditBox`
- `Microsoft.UI.Text.ParagraphStyle.Heading1` through `Heading4`

JAWS still read every paragraph as ordinary text. The change also substantially
increased the self-contained application size and did not improve the editing
experience.

## Questions for contributors

1. Can a native Windows editable text provider expose a heading level on a
   paragraph or text range in a way JAWS recognizes?
2. Would a custom UI Automation Text or Text2 provider be sufficient?
3. Does JAWS recognize `UIA_OutlineStylesAttributeId` as a heading inside an
   editable text range?
4. Is a custom `IRawElementProviderFragment` or `IAccessibleEx`
   implementation required?
5. Is there an existing native editor library with verified JAWS support for
   semantic headings?
6. How does Word expose paragraph headings without switching to web-style
   browsing or forms mode?

Concrete, minimal prototypes are especially useful. Please state the Windows
version, JAWS version and exact keyboard test used.

## JAWS test procedure

1. Launch the application with JAWS running.
2. Confirm that focus starts in the editor without a mode-change sound.
3. Use Down Arrow to move from an ordinary paragraph into headings of several
   levels.
4. Record the exact speech order.
5. Continue into the paragraph after each heading.
6. Type and edit text to confirm the control remains in normal editing mode.

Success means the heading level and text are spoken once, in the correct order,
without re-announcing the editor control.
