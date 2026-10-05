# What this fork adds

This is a fork of [SourceGit](https://github.com/sourcegit-scm/sourcegit), rebased onto
every upstream release. Its versions follow upstream's and add a suffix:
`v2026.21-3b.3` is the third release of this fork built on upstream `v2026.21`.

**Download it from [this fork's releases](https://github.com/74nu5/sourcegit/releases/latest).**
The installation channels in the [README](README.md) — scoop, Homebrew, the Linux
repositories, AppImage Hub — install upstream SourceGit, without anything below.

Two kinds of additions, and the difference matters:

- **Options that change what you see are off by default.** Until you turn one on, that
  part of the application looks and behaves exactly as upstream's.
- **Everything else is an extra menu entry or button** that does nothing until used.

Everything is organised below by where you find it.

---

## The history graph

Most of these live in the **Advanced Options** menu of the history toolbar.

**Stable lanes.** *Advanced Options → Branch placement → Stable Lanes.* Upstream places a
branch at whatever rank it holds among the live ones, so it drifts one column left each
time a branch on its left ends. With stable lanes, a branch keeps the column it was given
until it ends. The graph has a budget of twenty lanes; beyond that, branches share the
last column, and a tooltip on the graph says so.

**Keep a branch in the leftmost lane.** *Right-click a branch → Keep in Leftmost Lane.*
Typically for `main`: it stays in the first column whatever is checked out. Works with
both placements.

**Show only one branch.** *Right-click a branch, a folder of branches or a tag → Show Only
This in Graph*, and *Show Everything in Graph Again* to come back. The same filter upstream
offers behind an icon that only appears on hover, one branch at a time.

**A branch column.** *Advanced Options → Columns → Branch / Tag Column.* Moves branch and
tag labels out of the commit message into a column of their own. Either on the rows that
carry a reference (*Refs Only*, the default) or on every row, naming the branch each
commit belongs to (*All Rows*). Related options in the same menu: *Separate Graph Column*,
*Colorize Rows By Branch*, *Remote As Icon Instead Of Name*.

**Stashes in the graph.** *Advanced Options → Show stashes in the graph.* Each stash hangs
off the commit it was taken from. Right-clicking one gives the stash actions — apply,
checkout a new branch, drop, save as patch, copy the message — not the commit menu.

**Uncommitted changes in the graph.** *Advanced Options → Show uncommitted changes in the
graph.* A row at the top stands for the work in progress. Selecting it fills the detail
panel with the changes, staged and unstaged together, and shows the diff of the file you
pick.

---

## Stashes

On the stashes page and in the graph, a stash's menu adds:

- **Go To Where It Was Taken** — selects the commit the stash was taken from. Disabled,
  with the reason, when that commit is not in the loaded history.
- **Select In Graph** (from the stashes page) and **Show In Stashes** (from the graph).
- **Show The Message As The Label** — the stash's message on the top line, with its branch
  and `stash@{N}` below. Off by default.

---

## The sidebar

**Hide a section.** *Right-click a section header → Hide this section.* Hidden sections
come back from the chips at the foot of the panel, and their room goes to the others.

**Pull requests.** A section listing the repository's open pull requests, with the state
of their checks and a filter for *Only the ones I opened*. It appears only for a
repository hosted on a forge you have an account for, set up in *Preferences → Forges*:
**Azure DevOps, GitHub** (Enterprise included), **GitLab, Gitea, Bitbucket**. The token
can be read from an environment variable instead of being typed; a typed one is stored in
plain text with the rest of the preferences. Optionally, branches with an open pull
request get a marker (*Mark branches that have an open pull request*, off by default).

**Remove dead local branches.** A button on the *Local Branches* header. It prunes stale
remote references first, then lists the local branches whose upstream is gone, with their
age and whether they are merged, and deletes only the ones you tick. Branches never pushed
and branches not merged are listed but left unticked unless you ask.

---

## Tabs

**Group worktrees under their repository.** *Right-click a tab → Group Worktrees Under
Their Repository.* A repository keeps one tab, and its worktrees become a row underneath,
each named after its branch. A worktree is opened the first time you select it, the row
follows `git worktree list`, and a removed worktree's tab closes. Turning the option on
groups the tabs already open; turning it off gives each open worktree its tab back.

Also: a tab title too long for its width ends with an ellipsis instead of being clipped
on both sides.

---

## Local changes

**Expand or collapse every folder at once.** *Right-click the unstaged or staged list →
Expand All Folders / Collapse All Folders*, in tree mode. Collapsing includes the folders
nested inside others, and they stay collapsed when the list refreshes — upstream forgot
a collapsed folder as soon as its parent was collapsed too, and showed it expanded again
after the next file saved.

---

## Merge conflicts

**Edit the result.** Once every block of a conflict is resolved, the *Result* panel
accepts typing, and saving writes exactly what you see — for the fix that neither side
describes. *Back to Blocks* returns to block-by-block resolution, after confirming that
what you typed will be discarded. This one is not an option: it only switches on once
nothing is left to resolve, and changes nothing until you type.

---

## Preferences and configuration

**Duplicate an entry** instead of retyping it: an AI service (with its key), a forge
account (with its token), a workspace, a custom action, a commit template, an issue
tracker.

---

## Updates

The update check reads **this fork's** releases, not upstream's. *Update now* downloads
the package for your platform, checks it against the `SHA256SUMS` file published with
every release, replaces the installed files and offers to restart. That works for the
Windows zip, the macOS app and the AppImage. A copy installed from a `.deb` or `.rpm`
belongs to your package manager and is left alone: download the new package from the
releases page and install it the same way.

The checksum proves a package arrived intact. It does not prove who built it — the sums
are published next to the files they cover, and this fork signs nothing.

---

## Settings, and running upstream alongside

This fork uses the **same settings folder as upstream SourceGit** (`%APPDATA%\SourceGit`
on Windows, see the README for the others), so the settings you share — theme, fonts,
workspaces, AI services — are the same in both.

What only this fork has lives in files of its own, next to upstream's:
`preference.fork.json` for the application — the graph options, the forge accounts and
their tokens — and `sourcegit.fork.uistates` in each repository's `.git` folder for what
the repository remembers — the pinned branch, the hidden sidebar sections, the stashes
shown in the graph, the column widths. Upstream never opens these files, so running it
does not erase them. Earlier versions of this fork kept them in upstream's files, which
upstream rewrote without them; they move across on their own the first time this version
starts.

A token typed rather than read from an environment variable is still stored in plain
text, now in `preference.fork.json`.

Going back to a version of this fork that predates these files loses this fork's settings
in that version: it looks for them where they no longer are.

---

## Working on this fork

See [DEVELOPMENT.md](DEVELOPMENT.md) for the branch layout, the release process, and the
conventions that keep rebasing on upstream cheap.
