#!/usr/bin/env python3
"""Durable reconciliation for the scheduled Reddit investigator; never submits posts.

scan is read-only. observe consumes a public JSON observation on stdin, made by the
agent after inspecting Reddit/IFTTT through the supported signed-in browser. A read
failure is 'inaccessible', never 'removed'. ack consumes eventId/notificationKey
after the user has been notified. No command reads credentials or contacts Reddit.
Run as the service account against the existing production database.
"""
import argparse
import datetime as dt
import hashlib
import html
import json
import re
import sqlite3
import sys
import unicodedata
from pathlib import Path
from urllib.parse import urlsplit

UTC = dt.timezone.utc


def timestamp(value):
    return dt.datetime.fromisoformat(value.replace("Z", "+00:00")).astimezone(UTC)


def normalize(value):
    return " ".join(unicodedata.normalize("NFC", value).split())


def post_id(url):
    parsed = urlsplit(url or "")
    if (parsed.scheme != "https" or parsed.hostname not in ("reddit.com", "www.reddit.com", "old.reddit.com")
            or parsed.username or parsed.password or parsed.port not in (None, 443) or parsed.query or parsed.fragment):
        raise ValueError("Expected a canonical public Reddit post URL")
    match = re.fullmatch(r"/r/LongevityWorldCup/comments/([a-z0-9]{1,32})(?:/[^/]+)?/?", parsed.path, re.I)
    if not match:
        raise ValueError("Post is outside r/LongevityWorldCup")
    return "t3_" + match[1].lower()


def schema(db):
    db.execute("""CREATE TABLE IF NOT EXISTS RedditDeliveryChecks (
        EventId TEXT PRIMARY KEY, CheckedAtUtc TEXT NOT NULL, Outcome TEXT NOT NULL,
        Reason TEXT NOT NULL, EvidenceUrl TEXT NOT NULL, PostUrl TEXT)""")
    db.execute("""CREATE TABLE IF NOT EXISTS RedditDeliveryNotices (
        NotificationKey TEXT PRIMARY KEY, EventId TEXT NOT NULL, Outcome TEXT NOT NULL,
        Reason TEXT NOT NULL, EvidenceUrl TEXT NOT NULL, PostUrl TEXT, CreatedAtUtc TEXT NOT NULL,
        NotifiedAtUtc TEXT)""")
    db.execute("""CREATE TABLE IF NOT EXISTS RedditBlockedLinks (
        Link TEXT PRIMARY KEY, EvidenceUrl TEXT NOT NULL, Reason TEXT NOT NULL, ObservedAtUtc TEXT NOT NULL)""")


def scan(db, now, limit=12):
    tables = {r[0] for r in db.execute("SELECT name FROM sqlite_schema WHERE type='table'")}
    checks = {r["EventId"]: dict(r) for r in db.execute("SELECT * FROM RedditDeliveryChecks")} if "RedditDeliveryChecks" in tables else {}
    candidates = []
    notices = [dict(r) for r in db.execute("SELECT * FROM RedditDeliveryNotices WHERE NotifiedAtUtc IS NULL ORDER BY CreatedAtUtc")] if "RedditDeliveryNotices" in tables else []
    for row in db.execute("""SELECT d.EventId,d.RecordKey,d.Status,d.LastErrorCode,d.RemoteUri,d.RemoteCid,
            d.FirstAttemptAtUtc,d.UpdatedAtUtc,d.RecordJson,e.Text,e.Type,e.OccurredAt
            FROM SocialDeliveries d JOIN Events e ON e.Id=d.EventId WHERE d.Platform='reddit'
            AND (d.FirstAttemptAtUtc IS NOT NULL OR d.LastErrorCode='BlockedLink')"""):
        item = dict(row)
        check = checks.get(item["EventId"])
        age = now - timestamp(item["FirstAttemptAtUtc"] or item["OccurredAt"])
        # Keep observing sent posts: removal can happen after a successful receipt.
        interval = dt.timedelta(minutes=30) if item["Status"] in ("pending", "review") or age < dt.timedelta(days=1) else dt.timedelta(hours=6) if age < dt.timedelta(days=7) else dt.timedelta(days=1)
        if check and check["Outcome"] in ("removed", "rejected", "blocked"):
            interval = dt.timedelta(days=7)
        due = timestamp(check["CheckedAtUtc"]) + interval if check else dt.datetime.min.replace(tzinfo=UTC)
        if due > now:
            continue
        item["lastCheck"] = check
        item["preparedPost"] = json.loads(item.pop("RecordJson")) if item["RecordJson"] else None
        # Old unverified posts must not starve behind new posts.
        candidates.append((due, item["FirstAttemptAtUtc"] or item["OccurredAt"], item))
    candidates.sort(key=lambda value: (value[0], value[1]))
    return {"checkedAtUtc": now.isoformat(), "dueCount": len(candidates),
            "candidates": [item[2] for item in candidates[:limit]], "notifications": notices}


def observe(db, observation, now):
    outcome = observation["outcome"]
    if outcome not in ("visible", "removed", "rejected", "blocked", "unconfirmed", "inaccessible"):
        raise ValueError("Unknown outcome")
    reason = observation.get("reason", "").strip()
    evidence = observation.get("evidenceUrl", "")
    parsed = urlsplit(evidence)
    if (not reason or len(reason) > 1500 or parsed.scheme != "https" or parsed.username or parsed.password
            or parsed.query or parsed.fragment or parsed.port not in (None, 443)
            or parsed.hostname not in ("www.reddit.com", "reddit.com", "old.reddit.com", "ifttt.com")):
        raise ValueError("A concise reason and public Reddit/IFTTT evidence URL are required")
    if parsed.hostname == "ifttt.com" and not parsed.path.startswith("/activity"):
        raise ValueError("Only nonsecret IFTTT activity URLs are permitted")
    event_id = observation["eventId"]
    db.execute("BEGIN IMMEDIATE")
    try:
        row = db.execute("SELECT * FROM SocialDeliveries WHERE EventId=? AND Platform='reddit'", (event_id,)).fetchone()
        if not row or row["RecordKey"] != observation.get("recordKey") or row["Status"] != observation["expectedStatus"]:
            raise ValueError("Delivery changed; rescan before recording an observation")
        if not row["FirstAttemptAtUtc"] and not (outcome == "blocked" and row["LastErrorCode"] == "BlockedLink"):
            raise ValueError("No submission attempt or blocked-link skip exists")
        if outcome == "unconfirmed" and now - timestamp(row["FirstAttemptAtUtc"]) < dt.timedelta(hours=2):
            raise ValueError("The normal receipt window has not elapsed")
        post_url = observation.get("postUrl")
        cid = post_id(post_url) if post_url else None
        if row["RemoteCid"] and cid and cid != row["RemoteCid"]:
            raise ValueError("Post ID conflicts with the existing receipt")
        if outcome in ("visible", "removed", "rejected"):
            prepared = json.loads(row["RecordJson"])
            if (observation.get("author", "").casefold() != "nopara73"
                    or normalize(observation.get("title", "")) != normalize(prepared["Title"])):
                raise ValueError("Observation does not match the author and immutable title")
            if outcome in ("visible", "removed") and not cid:
                raise ValueError("Visible/removed observations require the actual post URL")
            if outcome == "visible" or (outcome == "removed" and not row["RemoteCid"]):
                text = observation.get("text", "")
                decoded = html.unescape(re.sub(r"<br\s*/?>", "\n", text, flags=re.I))
                if outcome == "visible" and normalize(prepared["Text"]) not in (normalize(text), normalize(decoded)):
                    raise ValueError("Post body does not match the immutable attempt")
                posted_at = timestamp(observation["postedAtUtc"])
                if not -60 <= (posted_at - timestamp(row["FirstAttemptAtUtc"])).total_seconds() <= 7200:
                    raise ValueError("Post time is outside this attempt's receipt window")
                if outcome == "removed":
                    # Removed bodies can be hidden or changed by IFTTT's link shortener.
                    # Require a unique title/author/time match instead of assuming body equality.
                    matches = 0
                    for other in db.execute("SELECT RecordJson,FirstAttemptAtUtc FROM SocialDeliveries WHERE Platform='reddit' AND FirstAttemptAtUtc IS NOT NULL AND RecordJson IS NOT NULL"):
                        if (-60 <= (posted_at - timestamp(other["FirstAttemptAtUtc"])).total_seconds() <= 7200
                                and normalize(json.loads(other["RecordJson"])["Title"]) == normalize(prepared["Title"])):
                            matches += 1
                    if matches != 1:
                        raise ValueError("Removed post cannot be matched uniquely to an attempt")
        if outcome == "rejected" and parsed.hostname != "ifttt.com":
            raise ValueError("A submission rejection needs matching IFTTT activity evidence")
        if outcome == "blocked" and (row["FirstAttemptAtUtc"] or row["LastErrorCode"] != "BlockedLink"):
            raise ValueError("Cannot classify a submitted attempt as a preflight skip")
        schema(db)
        previous = db.execute("SELECT * FROM RedditDeliveryChecks WHERE EventId=?", (event_id,)).fetchone()
        status = row["Status"]
        if outcome in ("removed", "rejected"):
            code = "BannedDomain" if observation.get("reasonCode") == "BannedDomain" else "RemovedByReddit" if outcome == "removed" else "RejectedByIfttt"
            status = "failed"
            db.execute("""UPDATE SocialDeliveries SET Status='failed',LastErrorCode=?,RemoteUri=COALESCE(?,RemoteUri),
                RemoteCid=COALESCE(?,RemoteCid),NextAttemptAtUtc=NULL,UpdatedAtUtc=? WHERE EventId=? AND Platform='reddit'""",
                       (code, post_url, cid, now.isoformat(), event_id))
        elif outcome == "visible" and status != "sent":
            if db.execute("SELECT 1 FROM SocialDeliveries WHERE Platform='reddit' AND RemoteCid=? AND EventId<>?", (cid, event_id)).fetchone():
                raise ValueError("Post already belongs to another Event")
            status = "sent"
            db.execute("""UPDATE SocialDeliveries SET Status='sent',LastErrorCode=NULL,RemoteUri=?,RemoteCid=?,
                NextAttemptAtUtc=NULL,UpdatedAtUtc=? WHERE EventId=? AND Platform='reddit'""", (post_url, cid, now.isoformat(), event_id))
        # Learn exact URLs only when the inspected evidence explicitly identifies them.
        # Never infer a domain-wide ban from a generic removal, and never fetch/expand links.
        link = observation.get("confirmedBlockedUrl")
        if link:
            if outcome != "removed" or observation.get("reasonCode") != "BannedDomain" or observation.get("blockedUrlExplicitInEvidence") is not True:
                raise ValueError("Learning a link requires explicit banned-link evidence")
            url = urlsplit(link)
            prepared = json.loads(row["RecordJson"])
            if url.scheme not in ("https", "http") or url.username or url.password or link not in prepared["Text"]:
                raise ValueError("Blocked link is not part of the submitted public body")
            db.execute("INSERT OR IGNORE INTO RedditBlockedLinks VALUES (?,?,?,?)", (link, evidence, reason, now.isoformat()))
        notify = outcome in ("removed", "rejected", "blocked", "unconfirmed") or (outcome == "visible" and
            (row["Status"] == "failed" or (previous and previous["Outcome"] == "unconfirmed")))
        # Notification identity excludes changing check timestamps and prose, preventing repeat alerts.
        notice = hashlib.sha256(f"{event_id}|{outcome}|{post_url or ''}|{observation.get('reasonCode','')}".encode()).hexdigest() if notify else None
        if notice:
            db.execute("INSERT OR IGNORE INTO RedditDeliveryNotices VALUES (?,?,?,?,?,?,?,NULL)",
                       (notice, event_id, outcome, reason, evidence, post_url, now.isoformat()))
        db.execute("""INSERT INTO RedditDeliveryChecks(EventId,CheckedAtUtc,Outcome,Reason,EvidenceUrl,PostUrl)
            VALUES (?,?,?,?,?,?) ON CONFLICT(EventId) DO UPDATE SET CheckedAtUtc=excluded.CheckedAtUtc,
            Outcome=excluded.Outcome,Reason=excluded.Reason,EvidenceUrl=excluded.EvidenceUrl,
            PostUrl=excluded.PostUrl""", (event_id, now.isoformat(), outcome, reason, evidence, post_url))
        notification_needed = bool(notice and db.execute("SELECT 1 FROM RedditDeliveryNotices WHERE NotificationKey=? AND NotifiedAtUtc IS NULL", (notice,)).fetchone())
        db.commit()
        return {"eventId": event_id, "status": status, "outcome": outcome, "notificationKey": notice,
                "notificationNeeded": notification_needed}
    except Exception:
        db.rollback()
        raise


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("database")
    parser.add_argument("command", choices=("scan", "observe", "ack"))
    parser.add_argument("--limit", type=int, default=12)
    args = parser.parse_args()
    path = Path(args.database).resolve(strict=True)
    with sqlite3.connect(path.as_uri() + ("?mode=ro" if args.command == "scan" else "?mode=rw"), uri=True, timeout=15) as db:
        db.row_factory = sqlite3.Row
        now = dt.datetime.now(UTC)
        if args.command == "scan":
            db.execute("PRAGMA query_only=ON")
            result = scan(db, now, min(max(args.limit, 1), 50))
        else:
            data = json.loads(sys.stdin.read(65537))
            if args.command == "observe":
                result = observe(db, data, now)
            else:
                changed = db.execute("UPDATE RedditDeliveryNotices SET NotifiedAtUtc=? WHERE EventId=? AND NotificationKey=?",
                                     (now.isoformat(), data["eventId"], data["notificationKey"])).rowcount
                if changed != 1:
                    raise ValueError("Notification changed; rescan before acknowledging")
                db.commit()
                result = {"acknowledged": True}
        print(json.dumps(result, ensure_ascii=True))


if __name__ == "__main__":
    try:
        main()
    except (ValueError, KeyError, sqlite3.Error) as error:
        print(str(error), file=sys.stderr)
        raise SystemExit(1)
