#Requires -Version 7.0
<#
.SYNOPSIS
    Rebases this fork on top of sourcegit-scm/sourcegit and checks nothing broke.

.DESCRIPTION
    Upstream lands roughly 28 commits a week. Rebasing often is what keeps this fork
    cheap to maintain: four commits behind is a thirty-second job, two hundred is a
    project you keep postponing. Run this weekly.

    The rebase itself stays in your hands — it rewrites history and can need
    judgement — so this script stops and tells you where it hurts rather than
    guessing. Everything before the rebase is read-only.

.PARAMETER Check
    Report how far behind we are and stop. Changes nothing.

.PARAMETER Force
    Rebase even if upstream moved a lot. Without it, the script asks first.

.EXAMPLE
    ./tools/sync-upstream.ps1 -Check
    ./tools/sync-upstream.ps1
#>

[CmdletBinding()]
param(
    [switch]$Check,
    [switch]$Force,
    [string]$Upstream = "upstream",
    [string]$UpstreamBranch = "develop"
)

$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
Set-Location $repo

function Say([string]$text, [string]$colour = "Gray") { Write-Host $text -ForegroundColor $colour }
function Die([string]$text) { Say "`n  $text`n" "Red"; exit 1 }

# ---------------------------------------------------------------- preconditions
if (git status --porcelain) {
    Die "Working tree is not clean. Commit or stash first, a rebase would refuse to start anyway."
}

$branch = git rev-parse --abbrev-ref HEAD
if ($branch -eq "HEAD") { Die "Detached HEAD. Check out the fork branch first." }

if (-not (git remote | Where-Object { $_ -eq $Upstream })) {
    Die "No '$Upstream' remote. Add it with:`n    git remote add $Upstream https://github.com/sourcegit-scm/sourcegit.git"
}

# ---------------------------------------------------------------- how far behind
Say "`n  Fetching $Upstream/$UpstreamBranch ..." "Cyan"
git fetch $Upstream $UpstreamBranch --quiet

$target = "$Upstream/$UpstreamBranch"
$behind = [int](git rev-list --count "HEAD..$target")
$ahead = [int](git rev-list --count "$target..HEAD")

Say ""
Say "  branch        $branch"
Say "  our commits   $ahead"
Say "  behind        $behind commit(s)"

if ($behind -eq 0) {
    Say "`n  Already up to date.`n" "Green"
    exit 0
}

Say "`n  What landed upstream:" "Cyan"
git log --oneline --reverse "HEAD..$target" | ForEach-Object { Say "    $_" }

# Files touched on both sides are where a conflict can occur. Same file is not the
# same lines, but it is the only cheap warning we can give before trying.
$base = git merge-base HEAD $target
$theirs = git diff --name-only "$base..$target"
$ours = git diff --name-only "$base..HEAD"
$both = $theirs | Where-Object { $ours -contains $_ }

if ($both) {
    Say "`n  Files touched on both sides:" "Yellow"
    $both | ForEach-Object { Say "    $_" "Yellow" }
} else {
    Say "`n  No file touched on both sides." "Green"
}

if ($Check) { Say ""; exit 0 }

# ---------------------------------------------------------------- rebase
if (-not $Force -and $behind -gt 50) {
    Say "`n  $behind commits behind. Rebasing that much at once is where conflicts pile up." "Yellow"
    $answer = Read-Host "  Continue anyway? (y/N)"
    if ($answer -ne "y") { Say "  Stopped.`n"; exit 0 }
}

$before = git rev-parse HEAD
Say "`n  Rebasing onto $target ..." "Cyan"

git rebase $target
if ($LASTEXITCODE -ne 0) {
    Say "`n  Rebase stopped on a conflict. Nothing is lost." "Yellow"
    Say "  Resolve it, then:      git rebase --continue"
    Say "  Or start over:         git rebase --abort"
    Say "  Your commit before:    $before`n"
    exit 1
}

Say "  Rebase clean." "Green"

# ---------------------------------------------------------------- did it survive
Say "`n  Building ..." "Cyan"
dotnet build src/SourceGit.csproj -v q --nologo | Out-Null
if ($LASTEXITCODE -ne 0) { Die "Build failed after the rebase. Fix it before pushing; 'git reset --hard $before' takes you back." }
Say "  Build ok." "Green"

Say "  Checking formatting ..." "Cyan"
dotnet format --verify-no-changes src/SourceGit.csproj | Out-Null
if ($LASTEXITCODE -ne 0) {
    Say "  Formatting drifted — upstream CI would reject this. Run:  dotnet format src/SourceGit.csproj" "Yellow"
} else {
    Say "  Formatting ok." "Green"
}

# ---------------------------------------------------------------- what to do next
Say "`n  Done. $behind upstream commit(s) integrated." "Green"
Say "  Nothing was pushed. When you are happy with it:"
Say "    git push --force-with-lease origin $branch`n"
