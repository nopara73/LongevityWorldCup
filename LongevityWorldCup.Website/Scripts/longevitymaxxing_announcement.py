#!/usr/bin/env python3
"""Add a published custom event to Challenge Discussion without redispatching it."""
import argparse
import json
import sqlite3
from contextlib import closing
from datetime import datetime, timezone
from pathlib import Path


def publish(database, event_id, participant_id):
    path = Path(database).resolve(strict=True)
    with closing(sqlite3.connect(f"{path.as_uri()}?mode=rw", uri=True, timeout=30)) as db, db:
        db.execute("PRAGMA foreign_keys = ON")
        db.execute("BEGIN IMMEDIATE")
        event = db.execute(
            "SELECT Text, OccurredAt FROM Events WHERE Id = ? AND Type = 6 AND VisibleOnWebsite = 1",
            (event_id,),
        ).fetchone()
        if not event:
            raise ValueError("The source must be a published custom event.")
        if not db.execute(
            "SELECT 1 FROM LongevitymaxxingParticipants WHERE Id = ? AND ConfirmedAtUtc IS NOT NULL",
            (participant_id,),
        ).fetchone():
            raise ValueError("The discussion owner must be a confirmed participant.")
        text, occurred_at = event
        title, separator, content = text.replace("\r\n", "\n").partition("\n\n")
        if not title.strip() or not separator or not content.strip():
            raise ValueError("The announcement needs a title and content.")
        occurred = datetime.fromisoformat(occurred_at.replace("Z", "+00:00"))
        if occurred.tzinfo is None:
            occurred = occurred.replace(tzinfo=timezone.utc)
        occurred = occurred.astimezone(timezone.utc)
        existing = db.execute(
            "SELECT Kind, ParticipantId, SourceEventId, AnnouncementText "
            "FROM LongevitymaxxingDiscussionSystemPosts WHERE Id = ?", (event_id,),
        ).fetchone()
        expected = ("announcement", participant_id, event_id, text)
        if existing and existing != expected:
            raise ValueError("This discussion ID already belongs to different content or an owner.")
        if not existing:
            db.execute(
                "INSERT INTO LongevitymaxxingDiscussionSystemPosts "
                "(Id, Kind, ParticipantId, OccurredDate, OccurredAtUtc, SourceEventId, AnnouncementText) "
                "VALUES (?, 'announcement', ?, ?, ?, ?, ?)",
                (event_id, participant_id, occurred.date().isoformat(), occurred.isoformat(), event_id, text),
            )
    return {"id": event_id, "created": existing is None,
            "url": f"https://longevityworldcup.com/longevitymaxxing#discussion/system/{event_id}"}


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("database")
    parser.add_argument("event_id")
    parser.add_argument("participant_id", help="Confirmed participant responsible for replies")
    args = parser.parse_args()
    print(json.dumps(publish(args.database, args.event_id, args.participant_id)))
