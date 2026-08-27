# Working on this fork

This is a fork of [sourcegit-scm/sourcegit](https://github.com/sourcegit-scm/sourcegit)
carrying history-view features the upstream project does not have. Upstream lands
around 28 commits a week, so everything below exists for one reason: to keep taking
their work without fighting for it.

## Branches

```
upstream/develop          the project we forked
       │  rebase
       ▼
   develop                integration: upstream lands here, features merge here
       │  fast-forward
       ▼
     main                 published state, default branch, carries release tags
       ▲
 features/*               work in progress
```

`main` never has commits of its own. It is a label that follows `develop`, moved
forward when something is worth publishing.

## Day-to-day

Start a feature from `develop`:

```sh
git checkout develop
git checkout -b features/my-thing
```

Integrate it, in fast-forward only. The `--ff-only` is not a style preference: it
fails loudly when the feature is not up to date, instead of quietly creating a merge
commit that makes the next rebase painful.

```sh
git checkout features/my-thing && git rebase develop
git checkout develop && git merge --ff-only features/my-thing
git branch -d features/my-thing
```

A hook refuses a commit on `main` or `develop`, so that the flow above is the only way
in. It lives in the repository and is turned on once per clone:

```sh
git config core.hooksPath tools/hooks
```

It stands aside for anything already under way — a rebase resolving a conflict, a
revert, a bisect — so the weekly sync below is unaffected. `git commit --no-verify`
goes ahead regardless, for the day you mean it.

## Taking upstream's work

Weekly, from `develop`:

```sh
pwsh -File tools/sync-upstream.ps1
```

It reports how far behind we are, names the files touched on both sides, rebases,
then builds and checks formatting. The rebase stays interactive on purpose — it
rewrites history and a conflict can need judgement — so on failure it stops and
prints the commit to reset to. It never pushes.

`-Check` reports and changes nothing.

A scheduled job replays the same rebase every Monday on a throwaway branch and opens
an issue when it stops working. You learn about a conflict while it still costs two
commits.

Because `develop` is rewritten, both branches are force-pushed afterwards:

```sh
git push --force-with-lease origin develop
git checkout main && git merge --ff-only develop
git push --force-with-lease origin main
```

Release tags are unaffected: a tag keeps its commit alive, so a published release
still points at exactly the code that produced its binaries.

## Releasing

Tag `main`, annotated — `gh release create --notes-from-tag` takes the release notes
from the tag message, and a lightweight tag would publish an empty release:

```sh
git checkout main
git tag -a v2026.18-3b.1 -m "..."
git push origin v2026.18-3b.1
```

The tag triggers `Release`, which builds and attaches ten packages: zips for Windows
and macOS, AppImage, deb and rpm for Linux, each in x64 and arm64. No secrets, no
signing. Twenty to forty minutes.

Version numbers keep upstream's `YYYY.MM` and add a suffix: `v2026.18-3b.1`. The
`VERSION` file must stay purely numeric — it feeds `AssemblyVersion`, which rejects
suffixes. The suffix lives in the string `git describe` produces, which is what the
About window shows.

### When a release fails, bump the number

**A tag consumed by a release is spent for good.** Deleting the release does not free
it, and neither does turning immutable releases off: the ref can never be created
again. Pushing the same tag a second time is refused with

    remote: Cannot create ref due to creations being restricted.

which names neither the tag nor the reason. So when a run fails, fix the cause and tag
`v2026.18-3b.2`. Do not try to reuse the number.

**A release is all or nothing.** One failing package cancels the others in its matrix
and skips the `Release` job entirely — a run whose Windows and macOS packages all
succeeded can still publish nothing. Read the job list, not the summary.

Two traps already dealt with, worth knowing before touching the packaging:

- **RPM rejects a dash in `Version:`**, where it separates version from release.
  `package.linux.sh` folds `2026.18-3b.1` into `2026.18.3b.1` for that format alone,
  which is why the rpm filename differs from the deb's.
- **A published release is immutable**, so assets cannot be added afterwards.
  `release.yml` downloads the artifacts first and hands them to `gh release create`
  in one call.

## Writing code that survives a rebase

A quarter of upstream commits touch at least one file this fork modifies. Two habits
keep that from turning into work.

**Put new code in files of your own.** C# partial classes let a class live in several
files. Anything purely additive belongs in a `*.Fork.cs`, which upstream will never
touch; only unavoidable changes to existing methods stay in their file. Handlers wired
from XAML work from a `.Fork.cs` just as well — they remain members of the same class.

Classes already split this way: `Histories`, `CommitRefsPresenter`, `CommitGraph`,
`Preferences`, `CommitSubjectPresenter`, `Repository`, `About`.

**Never reformat or reorder existing code.** Additions rarely conflict; modifications
and deletions do. Every line you remove from an upstream file should be there because
the feature demanded it, never because you were tidying up.

Two more places worth knowing:

- **Locale strings** go in `src/Resources/Locales/Fork.*.axaml`, stacked on top of the
  active locale in `App.SetLocale`. `en_US.axaml` alone receives about 170 commits a
  year upstream; staying out of it removes a whole class of conflicts.
- **Build settings** go in `Directory.Build.props`. MSBuild picks it up on its own, so
  nothing in the upstream project file has to know about it.

What cannot move: the column definitions in `Histories.axaml` — XAML has no partial —
and the branches inside `Generate` and `Render` that choose between upstream behaviour
and ours.

## Before pushing

```sh
dotnet build src/SourceGit.csproj
dotnet format --verify-no-changes src/SourceGit.csproj
```

The formatting check is not advisory: upstream CI rejects a diff that fails it, and so
does ours.

Graph changes deserve more. The compact placement must stay byte-for-byte identical to
upstream's, and that is the argument any of this rests on. Dump `CommitGraph.Generate`
over a real history before and after your change and compare the two.

## Things that would break if you forgot them

**The update check is off**, through `DisableUpdateDetection` in `Directory.Build.props`.
It has to stay off. This build carries the version number of the release it branched
from, so the day upstream publishes a newer one, the check would offer an update whose
installer replaces this fork with the stock application — quietly dropping everything
here.

**This fork and an official install cannot run side by side.** They share
`%APPDATA%\SourceGit` and the same process lock, so launching one while the other runs
hands the request over and exits. Nothing is broken; it is simply not two applications.
