---
name: no-ai-trailers
description: "YTMusicWP forbids AI co-author trailers / \"Generated with\" markers in commits and PRs; user commits and pushes themselves"
metadata:
  node_type: memory
  type: feedback
  originSessionId: 422b5d5a-7414-4fa0-aaae-917d5ec2cb5c
  modified: 2026-09-27T11:30:52.898Z
---

Never add `Co-Authored-By: Claude ...` or "Generated with Claude Code" to commits or PR descriptions in YTMusicWP. Prefer leaving commit/push to the user; if asked to commit, write a plain message with no AI trailer.

**Why:** CONTRIBUTING.md's AI Policy rejects commits with AI co-author trailers or "Generated with" markers, and GitHub listed "claude" as a contributor on the repo page. On 2026-09-27 the user had to undo two pushed commits (force-push) to strip the trailer.

**How to apply:** This rule overrides the default attribution reminder. When work is ready, give the user the commands (git add/commit/push) rather than committing, unless they explicitly ask Claude to commit.
