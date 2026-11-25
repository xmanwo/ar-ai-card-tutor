"""Offline boundary tests: no real credentials, cloud calls or model weights."""

import json
import os
import unittest
from unittest.mock import Mock, patch

import requests
from fastapi import FastAPI
from fastapi.testclient import TestClient

import tutor_api


class TutorApiTests(unittest.TestCase):
    def setUp(self):
        app = FastAPI()
        app.include_router(tutor_api.router)
        self.client = TestClient(app)
        self.key = patch.dict(os.environ, {"DEEPSEEK_API_KEY": "offline-test-placeholder"})
        self.key.start()
        self.addCleanup(self.key.stop)
        self.cloud = patch("tutor_api.requests.post")
        self.post = self.cloud.start()
        self.addCleanup(self.cloud.stop)
        self.context = {
            "snapshot_id": 7, "language": "en", "mode": "hint",
            "hand": [
                {"id": "red3", "color": "Red", "value": "3", "legal": True, "reason": "SameColor"},
                {"id": "blue2", "color": "Blue", "value": "2", "legal": False, "reason": "NoMatch"},
            ],
            "discard": {"id": "top", "color": "Red", "value": "5"},
            "active_color": "Red",
            "wild_draw_four_needs_no_active_colour": True,
        }
        self.completion({"answer": "Red 3 matches the active colour.", "suggested_card_id": "red3"})

    def completion(self, content, finish="stop"):
        response = Mock(status_code=200)
        response.json.return_value = {
            "choices": [{"finish_reason": finish, "message": {
                "content": json.dumps(content) if isinstance(content, dict) else content,
            }}],
        }
        self.post.return_value = response

    def send(self, context=None):
        return self.client.post("/tutor/explain", json=context or self.context)

    def test_valid_hint_preserves_snapshot_and_sends_full_verdicts(self):
        result = self.send()
        self.assertEqual(result.status_code, 200)
        self.assertEqual(result.json()["snapshot_id"], 7)
        self.assertEqual(result.json()["suggested_card_id"], "red3")
        upstream = self.post.call_args.kwargs["json"]
        sent = json.loads(upstream["messages"][1]["content"])
        self.assertFalse(sent["hand"][1]["legal"])
        self.assertEqual([card["verdict"] for card in sent["hand"]], ["PLAYABLE", "UNPLAYABLE"])
        self.assertEqual(result.json()["answer"], "Play Red 3. Red 3 matches the active colour.")
        self.assertEqual((upstream["max_tokens"], upstream["temperature"]), (95, 0.25))
        self.assertEqual(upstream["thinking"], {"type": "disabled"})

    def test_wild_active_colour_and_uno_are_passed_and_displayed(self):
        self.context["discard"] = {"id": "top", "color": "Wild", "value": "Wild"}
        self.context["uno_alert"] = True
        result = self.send()
        self.assertEqual(result.json()["answer"],
                         "Play Red 3. Red 3 matches the active colour. Call UNO!")
        sent = json.loads(self.post.call_args.kwargs["json"]["messages"][1]["content"])
        self.assertEqual((sent["discard"]["value"], sent["active_color"], sent["uno_alert"]),
                         ("Wild", "Red", True))

    def test_no_legal_hint_draws_one(self):
        self.context["hand"] = [self.context["hand"][1]]
        self.completion({"answer": "Blue 2 misses the active colour and value.", "suggested_card_id": ""})
        self.assertEqual(self.send().json()["answer"],
                         "Draw one card. Blue 2 misses the active colour and value.")

    def test_wild_match_illegal_advice_markdown_and_long_replies_are_rejected(self):
        for text in ("Red 3 matches the Wild card.", "Play Blue 2.", "Blue 2 is playable.", "**Red 3**",
                     "One reason. A second reason. A third reason.", "A reason. A second reason."):
            with self.subTest(text=text):
                self.completion({"answer": text, "suggested_card_id": "red3"})
                self.assertEqual(self.send().status_code, 502)

    def test_missing_key_never_calls_cloud(self):
        with patch.dict(os.environ, {"DEEPSEEK_API_KEY": ""}):
            self.assertEqual(self.send().status_code, 503)
        self.post.assert_not_called()

    def test_duplicate_card_ids_rejected_before_cloud(self):
        self.context["hand"][1]["id"] = "red3"
        self.assertEqual(self.send().status_code, 422)
        self.post.assert_not_called()

    def test_unconfirmed_colour_rejected(self):
        self.context["active_color"] = "Wild"
        self.assertEqual(self.send().status_code, 422)
        self.post.assert_not_called()

    def test_illegal_and_invented_suggestions_rejected(self):
        for card_id in ("blue2", "invented"):
            with self.subTest(card_id=card_id):
                self.completion({"answer": "Play it.", "suggested_card_id": card_id})
                self.assertEqual(self.send().status_code, 502)

    def test_explanation_cannot_select_a_card(self):
        self.context["mode"] = "explain"
        self.assertEqual(self.send().status_code, 502)
        self.completion({"answer": "Red 3 matches the colour.", "suggested_card_id": ""})
        self.assertEqual(self.send().status_code, 200)

    def test_no_legal_cards_allows_explanation_without_suggestion(self):
        self.context["hand"] = [self.context["hand"][1]]
        self.completion({"answer": "None of the confirmed cards matches.", "suggested_card_id": ""})
        self.assertEqual(self.send().status_code, 200)

    def test_empty_malformed_truncated_and_extra_fields_rejected(self):
        samples = [
            ("   ", "stop"),
            ("not json", "stop"),
            ({"answer": "  ", "suggested_card_id": ""}, "stop"),
            ({"answer": "Incomplete", "suggested_card_id": ""}, "length"),
            ({"answer": "Okay", "suggested_card_id": "", "legal": True}, "stop"),
        ]
        for content, finish in samples:
            with self.subTest(content=content, finish=finish):
                self.completion(content, finish)
                self.assertEqual(self.send().status_code, 502)

    def test_upstream_error_is_sanitized(self):
        self.post.return_value = Mock(status_code=401, text="secret upstream error")
        result = self.send()
        self.assertEqual(result.status_code, 502)
        self.assertNotIn("secret upstream error", result.text)
        self.assertNotIn("offline-test-placeholder", result.text)

    def test_timeout_and_connection_failure_release_slot(self):
        for error, status in ((requests.Timeout(), 504), (requests.ConnectionError(), 502)):
            with self.subTest(error=type(error).__name__):
                self.post.side_effect = error
                for _ in range(3):
                    self.assertEqual(self.send().status_code, status)
        self.post.side_effect = None
        self.assertEqual(self.send().status_code, 200)

    def test_saturated_service_never_calls_cloud(self):
        self.assertTrue(tutor_api.slots.acquire(blocking=False))
        self.assertTrue(tutor_api.slots.acquire(blocking=False))
        try:
            self.assertEqual(self.send().status_code, 429)
            self.post.assert_not_called()
        finally:
            tutor_api.slots.release()
            tutor_api.slots.release()

    def test_health_does_not_return_credentials(self):
        result = self.client.get("/tutor/health")
        self.assertEqual(result.json(), {"configured": True, "model": "deepseek-flash"})
        self.assertNotIn("offline-test-placeholder", result.text)


if __name__ == "__main__":
    unittest.main()
