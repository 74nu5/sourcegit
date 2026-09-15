# CLAUDE.md

Fork of [sourcegit-scm/sourcegit](https://github.com/sourcegit-scm/sourcegit): a Git
GUI in C# on Avalonia, cross-platform. This fork adds history-view features upstream
does not have.

Read `DEVELOPMENT.md` first — branch layout, release process, and the conventions that
keep rebasing on upstream cheap. What follows is what matters when writing code here.

## Build and check

```sh
dotnet build src/SourceGit.csproj
dotnet format --verify-no-changes src/SourceGit.csproj
```

The formatting check is enforced by CI on every pull request, upstream's and ours. A
diff that fails it will be rejected. There is no test project.

Running the app needs care: SourceGit is single-instance. Launching a build while
another instance runs hands the request to that instance and exits, so you end up
looking at a binary you did not build. Check `(Get-Process SourceGit).Path` before
concluding anything from what you see on screen, and never close an instance you did
not start — it may be the user's own session, with work in progress.

## The one invariant

Everything this fork adds is **off by default**, and the compact graph placement must
stay byte-for-byte identical to upstream's. That is what makes any of it defensible.

A change to `CommitGraph.Generate` is not verified by a green build. Dump its output —
paths, points, dots, links, per-commit margins — over a real history before and after,
and compare. `git log --format="%H%x00%P%x00%D"` gives a usable input.

## Where code goes

Upstream lands ~28 commits a week, a quarter of which touch a file this fork modifies.
So: **new code goes in files of our own**, `*.Fork.cs`, using partial classes. Only
unavoidable changes to existing methods stay in upstream files. Handlers wired from
XAML work from a `.Fork.cs` — same class.

Already split: `Histories`, `CommitRefsPresenter`, `CommitGraph`, `Preferences`,
`CommitSubjectPresenter`, `Repository`, `About`.

Never reformat or reorder upstream code. Additions rarely conflict; deletions do.

Locale strings go in `src/Resources/Locales/Fork.*.axaml`, never in `en_US.axaml`
(~170 upstream commits a year). Build settings go in `Directory.Build.props`.

## How this codebase actually works

Things that cost time to discover:

- **The graph is not a control.** It is a `DataGrid` whose cells hold the subject, with
  a transparent `Views/CommitGraph` painted on top, kept in sync by `StartX`/`StartY`
  recomputed on every `LayoutUpdated`.
- **Column resizing is hand-rolled**, not Avalonia's — a transparent `Border` over the
  header. `CanUserResizeColumns` is false.
- **A binding on `DataGridColumn.Width` does not survive the grid's sizing pass.**
  Stored widths must be applied from code. This is why the author column always was.
- **`Commit.LeftMargin` is not observable.** Changing it needs a full reload, not just
  a graph regeneration.
- **The lane palette is rebuilt on theme change** (`SetPens`). Resolve brushes in
  `Render`, never through a binding, or a repaint leaves the previous theme's colours.
- **Git records no branch for a commit.** `BranchOwnership` derives a display-only
  owner by walking first-parent chains. Trunks come first, remote ones included, or a
  feature branch swallows the shared history it forked from.

## Conventions

Commit messages follow upstream: `feature:`, `fix:`, `refactor:`, `doc:`, `ux:`,
lowercase after the prefix. Say what was wrong and why the fix is shaped that way. No
`Co-Authored-By` trailer.

C# files carry a UTF-8 BOM and CRLF endings — preserve them. Constants are
`SCREAMING_SNAKE_CASE`, private fields `_camelCase`.

Pull requests to upstream target `develop`, never `master`.

## Upstream relations

Issue [#2649](https://github.com/sourcegit-scm/sourcegit/issues/2649) proposes the lane
drift fix upstream. The maintainer's only reply so far rejected the comparison
screenshot rather than the substance. Assume this fork stays a fork; treat upstream
adoption as a bonus, not a design goal.

Do not push, tag, comment on an issue or open a pull request without being asked.
