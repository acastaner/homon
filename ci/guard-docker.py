#!/usr/bin/env python3
"""Homon — the docker guard.

A PreToolUse hook on Bash, registered in .claude/settings.json. It reads the hook payload
on stdin and exits 2 (block) when the command would destroy, stop or otherwise disturb a
container, volume, network or image that does not belong to the `homon-ci` Compose project.

Why a hook and not permission rules alone. Permission rules match the *start* of a command,
so `Bash(docker compose down:*)` never sees `docker compose -f x.yaml down -v` — the flags
come first and the prefix no longer matches. The deny list in settings.json catches the
literal forms and documents the rule; this is the layer that actually holds.

What is at stake. The development machine runs containers for other projects, and one of
them — a shared PostgreSQL — holds Homon's own development database. See
docs/postgres-setup-dev.md §0.

Python, not bash, for one boring reason: the payload arrives on stdin, and a shell script
that pipes its own heredoc into an interpreter replaces that stdin with the heredoc and
silently approves everything. This file is the whole hook.

This blocks; it does not decide. If a destructive command outside the CI project really is
the right thing, the maintainer runs it — agents ask.
"""

import json
import re
import sys

# The escape hatch, and the only one: the CI Compose project. Either addressed by project
# name (`-p homon-ci`) or by a container/volume/network the project owns, which Compose
# names `homon-ci[-_]…`.
CI_SCOPED = re.compile(r"(?:-p|--project-name)[=\s]+homon-ci\b|\bhomon-ci[-_]")

# Read-only verbs. If the docker call's first non-flag word is one of these, it cannot
# disturb anything and the destructive scan is skipped — `docker logs x` and
# `docker inspect y` stay available to agents, which is most of what they need.
READ_ONLY = {
    "ps", "images", "image", "info", "version", "inspect", "logs", "stats", "top",
    "port", "diff", "events", "search", "history", "context", "login", "logout",
    "manifest", "system",
}

# Verbs that remove, stop or disturb. `exec` is here on purpose: a shell inside the shared
# PostgreSQL can drop the development database, which is worse than losing a container.
DESTRUCTIVE = re.compile(
    r"\b(?:rm|rmi|kill|stop|restart|pause|unpause|prune|down|exec|update|cp|rename)\b"
)

# `docker system prune` and `docker image rm` reach READ_ONLY's first word but are not
# read-only at all, so the read-only shortcut only holds when nothing destructive follows.
SEPARATORS = re.compile(r"&&|\|\||;|\n|\|")


def is_blocked(segment: str) -> bool:
    if not re.search(r"(?:^|[\s/])docker\b", segment):
        return False
    if CI_SCOPED.search(segment):
        return False
    if not DESTRUCTIVE.search(segment):
        return False

    words = [w for w in segment.split() if not w.startswith("-")]
    if "docker" in words:
        after = words[words.index("docker") + 1:]
        if after and after[0] in READ_ONLY and not DESTRUCTIVE.search(" ".join(after)):
            return False

    return True


def main() -> int:
    try:
        payload = json.load(sys.stdin)
    except Exception:
        # A payload we cannot read is not a reason to block every Bash call.
        return 0

    if payload.get("tool_name") != "Bash":
        return 0

    command = (payload.get("tool_input") or {}).get("command") or ""

    # Split on shell separators so `docker ps && docker rm x` is judged per command, and a
    # CI-scoped call earlier in the line cannot launder a destructive one later.
    for segment in SEPARATORS.split(command):
        segment = segment.strip()
        if not is_blocked(segment):
            continue

        sys.stderr.write(
            "BLOCKED by ci/guard-docker.py: this docker command removes, stops or enters a\n"
            "container outside the `homon-ci` Compose project, and agents may not do that on\n"
            "this machine.\n"
            f"\n  {segment}\n\n"
            "The development machine runs containers belonging to other projects, and the\n"
            "shared PostgreSQL holds Homon's development database. See\n"
            "docs/postgres-setup-dev.md §0.\n"
            "\n"
            "Everything an agent may do to a database here is in ./ci/run-ci.sh, which\n"
            "addresses `-p homon-ci` and nothing else. If this command really is the right\n"
            "thing, ask the maintainer to run it.\n"
        )
        return 2

    return 0


if __name__ == "__main__":
    sys.exit(main())
