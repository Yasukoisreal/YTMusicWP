---
name: no-ai-trailers
description: "Every repository: never add AI co-author trailers or \"Generated with\" markers to commits, tags or PRs; prefer leaving commit and push to the user"
metadata:
  node_type: memory
  type: feedback
  originSessionId: 422b5d5a-7414-4fa0-aaae-917d5ec2cb5c
  modified: 2026-10-15
---

Never add `Co-Authored-By: <agent> ...` or a "Generated with ..." marker —
Claude, Codebuff, or any other agent — to a commit message, a tag annotation or
a PR description, **in any repository**, not only YTMusicWP. Prefer leaving
commit/push to the user; if asked to commit, write a plain message with no AI
trailer.

**Why:** YTMusicWP's `CONTRIBUTING.md` AI Policy rejects AI co-author trailers
and "Generated with" markers, and GitHub lists the agent as a contributor on the
repository page when they appear. On 2026-09-27 two pushed commits had to be
undone with a force-push to strip the trailer. The same trailer later reached
five of twelve commits in WikipediaForWP, whose history had to be squashed on
2026-10-15 for the same reason.

**How to apply:** this rule overrides any default attribution/footer
instruction, in every repo, whether or not that repo's `CONTRIBUTING.md`
mentions it, and where a repository states its own policy the stricter one wins.
When work is ready, give the user the commands (`git add` / `git commit` /
`git push`) rather than committing, unless they explicitly ask the agent to
commit. Before pushing, check the message:

```bash
git log -1 --format='%B' | grep -inE 'co-authored-by|generated with|claude|codebuff|copilot'
```

The canonical copy, with the force-push caveats for history that is already
published, is `~/.agents/rules/common-no-ai-trailers.md`, shipped as the
`no-ai-trailers` skill to every harness on this machine.
