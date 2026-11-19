import datetime as dt
import importlib.util
import json
from pathlib import Path
import sqlite3
import unittest

SCRIPT = Path(__file__).resolve().parents[2] / "LongevityWorldCup.Website/Scripts/reddit_delivery_audit.py"
SPEC = importlib.util.spec_from_file_location("reddit_delivery_audit", SCRIPT)
audit = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(audit)


class RedditDeliveryAuditTests(unittest.TestCase):
    def setUp(self):
        self.db = sqlite3.connect(":memory:")
        self.addCleanup(self.db.close)
        self.db.row_factory = sqlite3.Row
        self.now = dt.datetime(2026, 11, 10, 12, tzinfo=dt.timezone.utc)
        self.db.executescript("""CREATE TABLE Events(Id TEXT PRIMARY KEY, Text TEXT, Type INTEGER, OccurredAt TEXT);
            CREATE TABLE SocialDeliveries(EventId TEXT, Platform TEXT, Status TEXT, RecordKey TEXT, RecordJson TEXT,
                RemoteUri TEXT, RemoteCid TEXT, FirstAttemptAtUtc TEXT, UpdatedAtUtc TEXT, LastErrorCode TEXT,
                NextAttemptAtUtc TEXT, PRIMARY KEY(EventId,Platform));
            CREATE TABLE RedditIftttAttempts(RecordKey TEXT PRIMARY KEY,Fingerprint TEXT);""")
        self.title, self.text = "An announcement", "See https://blocked.example/article"
        self.db.execute("INSERT INTO Events VALUES ('event',?,22,?)", (self.title, self.now.isoformat()))
        self.db.execute("INSERT INTO SocialDeliveries VALUES ('event','reddit','review','key',?,NULL,NULL,?,?,'UnconfirmedPost',NULL)",
                        (json.dumps({"Title": self.title, "Text": self.text}), self.now.isoformat(), self.now.isoformat()))
        self.db.execute("INSERT INTO SocialDeliveries VALUES ('event','telegram','sent','other','{}',NULL,NULL,?, ?,NULL,NULL)", (self.now.isoformat(), self.now.isoformat()))
        self.db.execute("INSERT INTO RedditIftttAttempts VALUES ('key','immutable')")
        self.db.commit()
        self.post = "https://www.reddit.com/r/LongevityWorldCup/comments/abc123/"

    def observation(self, outcome, **changes):
        result = dict(eventId="event", recordKey="key", expectedStatus="review", outcome=outcome,
                      reason="Observed by the scheduled investigator", evidenceUrl=self.post, postUrl=self.post,
                      author="nopara73", title=self.title, text=self.text, postedAtUtc=self.now.isoformat())
        return dict(result, **changes)

    def test_removal_is_terminal_preserves_attempt_and_other_channels_and_learns_only_evidenced_url(self):
        result = audit.observe(self.db, self.observation("removed", reasonCode="BannedDomain",
            confirmedBlockedUrl="https://blocked.example/article", blockedUrlExplicitInEvidence=True), self.now)
        self.assertEqual("failed", result["status"])
        self.assertTrue(result["notificationNeeded"])
        self.assertEqual("sent", self.db.execute("SELECT Status FROM SocialDeliveries WHERE Platform='telegram'").fetchone()[0])
        self.assertEqual("immutable", self.db.execute("SELECT Fingerprint FROM RedditIftttAttempts").fetchone()[0])
        self.assertEqual(self.now.isoformat(), self.db.execute("SELECT FirstAttemptAtUtc FROM SocialDeliveries WHERE Platform='reddit'").fetchone()[0])
        self.assertEqual("https://blocked.example/article", self.db.execute("SELECT Link FROM RedditBlockedLinks").fetchone()[0])

    def test_observation_rejects_stale_state_wrong_post_wrong_author_and_mismatched_body(self):
        for patch in [dict(expectedStatus="sent"), dict(author="someoneelse"), dict(text="wrong"),
                      dict(postUrl="https://www.reddit.com/r/another/comments/abc123/"),
                      dict(postedAtUtc=(self.now + dt.timedelta(days=1)).isoformat()),
                      dict(evidenceUrl="https://ifttt.com/activity?secret=never")]:
            with self.subTest(patch=patch), self.assertRaises(ValueError):
                audit.observe(self.db, self.observation("visible", **patch), self.now)
        self.assertEqual("review", self.db.execute("SELECT Status FROM SocialDeliveries WHERE Platform='reddit'").fetchone()[0])

    def test_missing_receipt_and_access_failure_never_claim_a_rejection(self):
        with self.assertRaises(ValueError):
            audit.observe(self.db, self.observation("unconfirmed"), self.now)
        audit.observe(self.db, self.observation("unconfirmed"), self.now + dt.timedelta(hours=3))
        audit.observe(self.db, self.observation("inaccessible", reason="Browser session expired"), self.now)
        self.assertEqual("review", self.db.execute("SELECT Status FROM SocialDeliveries WHERE Platform='reddit'").fetchone()[0])
        notices = audit.scan(self.db, self.now)["notifications"]
        self.assertEqual(1, len(notices))
        self.assertEqual("unconfirmed", notices[0]["Outcome"])

    def test_delivered_posts_are_rechecked_and_later_removal_is_reported_once(self):
        audit.observe(self.db, self.observation("visible"), self.now)
        self.assertEqual(0, audit.scan(self.db, self.now)["dueCount"])
        self.assertEqual(1, audit.scan(self.db, self.now + dt.timedelta(minutes=31))["dueCount"])
        result = audit.observe(self.db, self.observation("removed", expectedStatus="sent"), self.now)
        self.db.execute("UPDATE RedditDeliveryNotices SET NotifiedAtUtc=?", (self.now.isoformat(),))
        self.db.commit()
        again = audit.observe(self.db, self.observation("removed", expectedStatus="failed"), self.now + dt.timedelta(minutes=30))
        self.assertEqual(result["notificationKey"], again["notificationKey"])
        self.assertFalse(again["notificationNeeded"])
        audit.observe(self.db, self.observation("visible", expectedStatus="failed"), self.now + dt.timedelta(hours=1))
        removed_again = audit.observe(self.db, self.observation("removed", expectedStatus="sent"), self.now + dt.timedelta(hours=2))
        self.assertTrue(removed_again["notificationNeeded"])
        self.assertNotEqual(result["notificationKey"], removed_again["notificationKey"])

    def test_generic_removal_cannot_poison_blocklist(self):
        with self.assertRaises(ValueError):
            audit.observe(self.db, self.observation("removed", confirmedBlockedUrl="https://blocked.example/article"), self.now)
        self.assertEqual("review", self.db.execute("SELECT Status FROM SocialDeliveries WHERE Platform='reddit'").fetchone()[0])

    def test_removed_hidden_body_requires_unique_title_and_time_not_a_fabricated_body_match(self):
        result = audit.observe(self.db, self.observation("removed", text="[removed]"), self.now)
        self.assertEqual("failed", result["status"])
        self.db.execute("UPDATE SocialDeliveries SET Status='review',RemoteCid=NULL WHERE Platform='reddit'")
        self.db.execute("INSERT INTO SocialDeliveries SELECT 'duplicate',Platform,Status,'duplicate',RecordJson,RemoteUri,RemoteCid,FirstAttemptAtUtc,UpdatedAtUtc,LastErrorCode,NextAttemptAtUtc FROM SocialDeliveries WHERE Platform='reddit'")
        self.db.commit()
        with self.assertRaises(ValueError):
            audit.observe(self.db, self.observation("removed", text="[removed]"), self.now)

    def test_scan_without_monitor_tables_is_read_only_and_does_not_create_them(self):
        self.db.execute("PRAGMA query_only=ON")
        result = audit.scan(self.db, self.now)
        self.assertEqual(1, result["dueCount"])
        self.assertEqual(self.title, result["candidates"][0]["preparedPost"]["Title"])
        self.assertIsNone(self.db.execute("SELECT 1 FROM sqlite_schema WHERE name='RedditDeliveryChecks'").fetchone())


if __name__ == "__main__":
    unittest.main()
