# The changes page

A static page listing every section of `tools/play-branches.txt` merged into the fork's `main`, newest first, each with
what changed and its before/after pictures and clips. It is published from the `gh-pages` branch with GitHub Pages.

## Where the pictures come from

Nothing in the page's media is committed to `main`: no game art or captures go into the source branches. Every change is
made with evidence (side-by-side sheets, crops, short clips) kept in an evidence folder outside the repository, one
folder per piece of work (`r<N>-<name>/...`), and the commit's `Evidence:` line names it. The build reads the commit
messages of each merged branch, finds the files they name in that folder, and adds the ones matched by hand in
`overrides.json` (`files`, `lane_hint`) for sections whose commits carry no Evidence line.

Pictures are shrunk to WebP (at most 4096 px on a side and 9 megapixels, so the dense sheets stay legible) and clips to
H.264 at 1720 px wide without sound. The page uses relative links only, so it works from any sub-path and on a phone.

## Rebuilding after a merge

```
git worktree add ../gh-pages gh-pages            # once; the branch holds only the built site
python3 tools/changes-page/build.py --evidence /path/to/evidence --out ../gh-pages
cd ../gh-pages && git add -A && git commit -m "Changes page: main <sha>"
git push <your fork remote> gh-pages
```

`--evidence` and `--out` can also come from `I26_EVIDENCE_ROOT` and `I26_CHANGES_OUT`. Shrunk media are cached (default
`<evidence>/.changes-page-cache`, or `--cache`), so a rebuild only converts new files; `--rescan` re-reads the evidence
folder after new work lands. Needs Python 3 with Pillow (WebP support) and ffmpeg on `PATH`.

## What the public page leaves out

- Pictures listed in `overrides.json` `public_exclude` (with the reason): anything that shows more than the game or a
  labelled comparison sheet, such as a desktop, another program's window or a file path.
- Working notes in commit messages: evidence paths and who made a change. Section titles come from the manifest headers
  (dates and remarks in brackets dropped) or from `overrides.json` `titles`.
- The build fails, and prints what it found, if the page's text holds a local path, a private network address or an
  e-mail address. `--deny FILE` (or `I26_CHANGES_DENY`) adds patterns of your own, one regular expression a line,
  from a file kept outside the repository.

Pictures that set the original 1997 game beside I26 are listed in `overrides.json` `original_game` and captioned so.

## Publishing

In the fork's GitHub settings: Pages, Source "Deploy from a branch", branch `gh-pages`, folder `/ (root)`. The branch
carries a `.nojekyll` file so the files are served as they are.
