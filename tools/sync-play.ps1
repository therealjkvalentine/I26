<#
.SYNOPSIS
  Merges the patch stack's new topic branches onto the fork's `main` integration branch (called `play` until pass 19).

.DESCRIPTION
  Run from a worktree whose checked-out branch is `main`. The stack is the branch list in tools/play-branches.txt on
  `play` itself (one branch a line, merge order, # comments; since pass 19 the bookkeeping lives on play, the old
  `play-meta` branch is history). For each branch the script:
    1. drops it from the stack when its pull request upstream is merged (gh, --head <user>:<branch>) or its commits are
       already in origin/master;
    2. rebases it onto origin/master in a temporary detached worktree, unless it is checked out in another worktree
       (then it is merged as it stands and listed as "rebase skipped");
    3. merges the branch onto `play` with --no-ff (upstream moves are merged into play by hand, pass 19 onward). Conflicts in
       CHANGELOG.md, docs/deviations.md and docs/dev-hooks.md are resolved by keeping both sides' lines (duplicates
       removed), everything else aborts that merge and the branch is listed as a conflict;
    4. pushes `play` and every topic branch to the `play` remote (--force-with-lease), unless -NoPush.
  Nothing is ever pushed to origin or fork. The upstream repository and the fork's owner are read from the remotes.

.PARAMETER NoPush   Build only.
.PARAMETER NoRebase Merge the branches as they stand.
.PARAMETER Rebuild  Reset play to origin/master and merge everything again (after upstream moved and the topics were
                    rebased). Hand fixes made on play outside conflict hunks are lost and must be redone: the script
                    prints them from docs/play-tracker.md's "Integration notes". Without it the run is incremental:
                    branches already in play are left alone and only the new ones are merged on top.
.PARAMETER Only     Restrict the run to these branches (the stack order is kept).
#>
[CmdletBinding()]
param(
  [switch]$NoPush,
  [switch]$NoRebase,
  [switch]$Rebuild,
  [string[]]$Only
)

$ErrorActionPreference = 'Continue'   # native git writes progress to stderr; exit codes are checked by hand
$root = (git.exe rev-parse --show-toplevel).Trim()
if (-not $root) { throw 'not inside a git worktree' }
Set-Location $root

function Git { param([Parameter(ValueFromRemainingArguments = $true)][string[]]$a)
  $out = & git.exe @a 2>&1
  $script:gitExit = $LASTEXITCODE
  return ($out | ForEach-Object { "$_" })
}
function Fail($msg) { Write-Host "ERROR: $msg" -ForegroundColor Red; exit 1 }

$cur = (Git rev-parse --abbrev-ref HEAD) -join ''
if ($cur -ne 'main') { Fail "run this from the worktree that has 'main' checked out (this one has '$cur')" }
if ((Git status --porcelain) -ne $null -and (Git status --porcelain).Count -gt 0) { Fail 'the main worktree is not clean' }

$remotes = Git remote
foreach ($r in 'origin', 'play') { if ($remotes -notcontains $r) { Fail "remote '$r' missing" } }
$originUrl = (Git remote get-url origin) -join ''
$upstream = $null
if ($originUrl -match 'github\.com[:/]([^/]+/[^/.]+)') { $upstream = $Matches[1] }
$forkOwner = $null
if ($remotes -contains 'fork') {
  $forkUrl = (Git remote get-url fork) -join ''
  if ($forkUrl -match 'github\.com[:/]([^/]+)/') { $forkOwner = $Matches[1] }
}

Write-Host '== fetch'
Git fetch origin --prune | Out-Null
Git fetch play --prune | Out-Null
Git config rerere.enabled true | Out-Null

$manifestText = Get-Content -LiteralPath (Join-Path $root 'tools/play-branches.txt') -Encoding UTF8
if (-not $manifestText) { Fail 'tools/play-branches.txt not found on play' }
$stack = @()
foreach ($line in $manifestText) {
  $t = ($line -split '#')[0].Trim()
  if ($t) { $stack += $t }
}
if ($Only) { $stack = $stack | Where-Object { $Only -contains $_ } }

# Other worktrees' checked-out branches: those branches are not rebased here.
$checkedOut = @{}
$wtPath = $null
foreach ($l in (Git worktree list --porcelain)) {
  if ($l -like 'worktree *') { $wtPath = $l.Substring(9) }
  elseif ($l -like 'branch refs/heads/*') { $checkedOut[$l.Substring(18)] = $wtPath }
}

$dropped = @(); $conflicts = @(); $rebaseSkipped = @(); $merged = @(); $missing = @()
$keep = @()

Write-Host '== drop merged branches'
foreach ($b in $stack) {
  $exists = (Git rev-parse --verify --quiet "refs/heads/$b") -join ''
  if ($gitExit -ne 0) { $missing += $b; continue }
  Git merge-base --is-ancestor $b origin/master | Out-Null
  if ($gitExit -eq 0) { $dropped += "$b (already in origin/master)"; continue }
  $prMerged = $false
  if ($upstream -and (Get-Command gh -ErrorAction SilentlyContinue)) {
    $heads = @($b)
    if ($forkOwner) { $heads = @("${forkOwner}:$b", $b) }
    foreach ($h in $heads) {
      $json = & gh pr list -R $upstream --head $h --state merged --json number 2>$null
      if ($LASTEXITCODE -eq 0 -and $json -and $json -ne '[]') { $prMerged = $true; break }
    }
  }
  if ($prMerged) { $dropped += "$b (pull request merged upstream)"; continue }
  $keep += $b
}
$stack = $keep

if (-not $NoRebase) {
  Write-Host '== rebase topic branches onto origin/master'
  $tmp = Join-Path ([IO.Path]::GetTempPath()) ("i26-sync-" + [Guid]::NewGuid().ToString('n').Substring(0, 8))
  foreach ($b in $stack) {
    Git merge-base --is-ancestor origin/master $b | Out-Null
    if ($gitExit -eq 0) { continue }   # already on top of master
    if ($checkedOut.ContainsKey($b)) { $rebaseSkipped += "$b (checked out in $($checkedOut[$b]))"; continue }
    Git worktree add --detach $tmp $b | Out-Null
    if ($gitExit -ne 0) { $rebaseSkipped += "$b (could not make a scratch worktree)"; continue }
    $out = & git.exe -C $tmp rebase origin/master 2>&1
    if ($LASTEXITCODE -eq 0) {
      $sha = (& git.exe -C $tmp rev-parse HEAD).Trim()
      Git branch -f $b $sha | Out-Null
      Write-Host "  rebased $b -> $($sha.Substring(0,7))"
    } else {
      & git.exe -C $tmp rebase --abort 2>&1 | Out-Null
      $conflicts += "$b (rebase onto origin/master)"
    }
    Git worktree remove --force $tmp | Out-Null
  }
}

function Resolve-UnionConflicts([string]$path) {
  # Keep both sides of every conflict hunk, ours first, without repeating lines; used for the changelog and docs.
  $lines = Get-Content -LiteralPath $path -Encoding UTF8
  $outL = New-Object System.Collections.Generic.List[string]
  $state = 0; $ours = @(); $theirs = @()
  foreach ($l in $lines) {
    if ($state -eq 0 -and $l -like '<<<<<<<*') { $state = 1; $ours = @(); $theirs = @(); continue }
    if ($state -eq 1 -and $l -like '|||||||*') { $state = 3; continue }
    if (($state -eq 1 -or $state -eq 3) -and $l -like '=======*') { $state = 2; continue }
    if ($state -eq 2 -and $l -like '>>>>>>>*') {
      $seen = New-Object System.Collections.Generic.HashSet[string]
      foreach ($x in $ours) { if ($seen.Add($x) -or $x -eq '') { $outL.Add($x) } }
      foreach ($x in $theirs) { if ($seen.Add($x)) { $outL.Add($x) } }
      $state = 0; continue
    }
    switch ($state) { 1 { $ours += $l } 2 { $theirs += $l } 3 { } default { $outL.Add($l) } }
  }
  if ($state -ne 0) { return $false }
  [IO.File]::WriteAllLines($path, $outL, (New-Object System.Text.UTF8Encoding($false)))
  return $true
}
$unionFiles = @('CHANGELOG.md', 'docs/deviations.md', 'docs/dev-hooks.md')

$already = @()
if ($Rebuild) {
  Write-Host '== rebuild play from origin/master'
  Git reset --hard origin/master | Out-Null
  Fail '-Rebuild is retired since pass 19: play carries its own bookkeeping and 100+ branches are stacked on it; merge origin/master into play by hand instead'
} else {
  Write-Host '== merge the new branches onto play'
}

foreach ($b in $stack) {
  if (-not $Rebuild) { Git merge-base --is-ancestor $b main | Out-Null; if ($gitExit -eq 0) { $already += $b; continue } }
  $out = Git merge --no-ff --no-edit -m "Merge $b into main" $b
  if ($gitExit -eq 0) { $merged += $b; Write-Host "  merged $b"; continue }
  $unmerged = @(Git diff --name-only --diff-filter=U)
  $ok = $true
  foreach ($f in $unmerged) {
    if ($unionFiles -contains $f) {
      if (-not (Resolve-UnionConflicts (Join-Path $root $f))) { $ok = $false; break }
      Git add -- $f | Out-Null
    } elseif ((Git diff --name-only --diff-filter=U) -contains $f) {
      # rerere may have resolved it already; a file still holding markers is a real conflict
      $txt = Get-Content -LiteralPath (Join-Path $root $f) -Raw -ErrorAction SilentlyContinue
      if ($txt -match '(?m)^<<<<<<< ') { $ok = $false; break }
      Git add -- $f | Out-Null
    }
  }
  if ($ok) {
    $out = Git commit --no-edit -m "Merge $b into main"
    if ($gitExit -eq 0) { $merged += $b; Write-Host "  merged $b (changelog/docs union)"; continue }
  }
  Git merge --abort | Out-Null
  $conflicts += "$b (merge into play: $($unmerged -join ', '))"
  Write-Host "  CONFLICT $b : $($unmerged -join ', ')" -ForegroundColor Yellow
}

if (-not $NoPush) {
  Write-Host '== push to play'
  Git push --force-with-lease play main | Out-Null
  foreach ($b in $stack) { Git push --force-with-lease play $b | Out-Null }
}

Write-Host ''
Write-Host "merged into main now ($($merged.Count)):"; $merged | ForEach-Object { "  $_" }
if ($already) { Write-Host "already in play: $($already.Count)" }
if ($Rebuild) { Write-Host 'full rebuild: redo the hand fixes listed under "Integration notes" in docs/play-tracker.md, then dotnet build I76.sln' }
if ($rebaseSkipped) { Write-Host 'rebase skipped (merged as they stand):'; $rebaseSkipped | ForEach-Object { "  $_" } }
if ($dropped) { Write-Host 'dropped from the stack (edit tools/play-branches.txt on play):'; $dropped | ForEach-Object { "  $_" } }
if ($missing) { Write-Host 'listed but no such local branch:'; $missing | ForEach-Object { "  $_" } }
if ($conflicts) { Write-Host 'CONFLICTS (not in play):' -ForegroundColor Yellow; $conflicts | ForEach-Object { "  $_" } }
