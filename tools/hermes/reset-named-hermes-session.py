"""Refresh or replace one named Hermes session while preserving chat."""

from __future__ import annotations

import argparse
import json
import uuid

from hermes_state import SessionDB


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--title", required=True)
    parser.add_argument("--preserve-title", required=True)
    parser.add_argument("--apply", action="store_true")
    parser.add_argument(
        "--refresh-prompt",
        action="store_true",
        help="Invalidate only the cached system prompt; preserve the session transcript and id.",
    )
    args = parser.parse_args()

    if args.title != "trading" or args.preserve_title != "chat":
        raise SystemExit("This helper may only replace 'trading' while preserving 'chat'.")

    db = SessionDB()
    try:
        old_id = db.resolve_session_by_title(args.title)
        preserved_id = db.resolve_session_by_title(args.preserve_title)
        if not old_id:
            raise RuntimeError("Named trading session is missing.")
        if not preserved_id:
            raise RuntimeError("Named chat session is missing.")

        if args.apply and args.refresh_prompt:
            raise RuntimeError("Choose either session replacement or prompt refresh, not both.")

        result = {
            "mode": "refresh_prompt" if args.refresh_prompt else ("apply" if args.apply else "preview"),
            "old_session_id": old_id,
            "old_message_count": db.message_count(db.resolve_resume_session_id(old_id)),
            "preserved_session_id": preserved_id,
            "new_session_id": None,
            "transcript_preserved": not args.apply,
        }

        if args.refresh_prompt:
            # Hermes natively treats a NULL cached prompt as an instruction to
            # rebuild it on the next turn. This keeps the continuous trading
            # transcript while loading the current SOUL, memory and preloaded
            # skills supplied by the runner.
            db.update_system_prompt(db.resolve_resume_session_id(old_id), None)
            if db.resolve_session_by_title(args.title) != old_id:
                raise RuntimeError("Trading session identity changed during prompt refresh.")
            if db.resolve_session_by_title(args.preserve_title) != preserved_id:
                raise RuntimeError("Chat session identity changed during prompt refresh.")
        elif args.apply:
            if not db.delete_session(old_id):
                raise RuntimeError("Hermes refused to delete the named trading session.")
            new_id = str(uuid.uuid4())
            db.create_session(new_id, "cli")
            if not db.set_session_title(new_id, args.title):
                raise RuntimeError("Could not title the replacement trading session.")
            if db.resolve_session_by_title(args.preserve_title) != preserved_id:
                raise RuntimeError("Chat session identity changed during trading reset.")
            if db.resolve_session_by_title(args.title) != new_id:
                raise RuntimeError("Replacement trading session could not be resolved.")
            result["new_session_id"] = new_id

        print(json.dumps(result, sort_keys=True))
        return 0
    finally:
        db.close()


if __name__ == "__main__":
    raise SystemExit(main())
