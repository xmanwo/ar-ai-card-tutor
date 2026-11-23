"""Offline regression checks for hand geometry, fusion and API compatibility."""

import io
import sys
import tempfile
import unittest
from pathlib import Path
from unittest.mock import Mock, patch

import cv2
import numpy as np
from fastapi.testclient import TestClient

PACKAGE = Path(__file__).resolve().parents[1] / "yolo" / "uno-recognition-2026-11-20"
sys.path.insert(0, str(PACKAGE))
import hand_detector as hand
import server


def detection(label, box, confidence=0.8):
    return {"raw": label, "pretty": label, "xyxy": box, "conf": confidence}


class HandFusionTests(unittest.TestCase):
    def test_repeated_names_are_independent_cards(self):
        base = [detection("yellow 7", (0,0,20,20)), detection("yellow 7", (35,0,55,20))]
        other = [detection("yellow 7", (36,1,56,21)), detection("yellow 7", (1,1,21,21))]
        self.assertEqual(len(hand.fuse_views([base, other])), 2)

    def test_one_group_cannot_consume_two_corners_in_one_view(self):
        base = [detection("red 3", (0,0,20,20))]
        other = [detection("red 3", (1,0,21,20)), detection("red 3", (5,0,25,20))]
        self.assertEqual(len(hand.fuse_views([base, other])), 2)

    def test_weak_rotated_conflict_does_not_replace_baseline(self):
        base = [detection("red 9", (10,10,30,30), .8)]
        other = [detection("red 6", (10,10,30,30), .4)]
        self.assertEqual(hand.fuse_views([base, other])[0]["raw"], "red 9")

    def test_low_confidence_addition_does_not_remove_baseline(self):
        base = [detection("blue 5", (0,0,20,20), .26)]
        other = [detection("green 4", (100,100,120,120), .29)]
        result = hand.fuse_views([base, other])
        self.assertEqual([item["raw"] for item in result], ["blue 5"])

    def test_restored_boxes_use_original_pixel_coordinates(self):
        box = detection("red 3", (5,10,25,30))
        inverse = np.array([[1,0,0],[0,1,0]], dtype=float)
        result = hand.restore_boxes([box], inverse, (100,200), (500,500,3))
        self.assertEqual(result[0]["xyxy"], (105,210,125,230))

    def test_rotated_view_preserves_center_after_inverse_transform(self):
        image = np.zeros((100,200,3), np.uint8)
        rotated, inverse = hand.rotate_view(image, 30)
        height,width = rotated.shape[:2]
        box = detection("red 3", (width/2-5,height/2-5,width/2+5,height/2+5))
        restored = hand.restore_boxes([box], inverse, (0,0), image.shape)[0]["xyxy"]
        self.assertAlmostEqual((restored[0]+restored[2])/2, 100, delta=1)
        self.assertAlmostEqual((restored[1]+restored[3])/2, 50, delta=1)

    def test_roi_is_bounded_and_single_card_has_no_extra_views(self):
        boxes = [detection("red 3", (10,30,40,60)), detection("red 4", (70,40,100,70))]
        roi = hand.hand_roi(boxes, (200,200,3))
        self.assertIsNotNone(roi)
        self.assertTrue(0 <= roi[0] < roi[2] <= 200 and 0 <= roi[1] < roi[3] <= 200)
        self.assertIsNone(hand.hand_roi(boxes[:1], (200,200,3)))


class RecognitionApiTests(unittest.TestCase):
    def setUp(self):
        server.app.state.model = object()
        server.app.state.device = "cpu"
        self.client = TestClient(server.app)
        ok, jpeg = cv2.imencode(".jpg", np.zeros((100,100,3), np.uint8))
        assert ok
        self.jpeg = jpeg.tobytes()
        self.item = detection("red 3", (5,5,25,25))
        self.legacy = patch.object(server.detector, "detect_multiscale", return_value=[self.item])
        self.old = self.legacy.start()
        self.addCleanup(self.legacy.stop)
        self.enhancer = patch.object(server, "detect_hand", return_value=([self.item], {"profile":"hand_roi_rotation_v1","views":4,"baseline_count":1}))
        self.enhanced = self.enhancer.start()
        self.addCleanup(self.enhancer.stop)
        self.capture = patch.object(server, "CAPTURE_DIR", None)
        self.capture.start()
        self.addCleanup(self.capture.stop)

    def send(self, data=None):
        return self.client.post("/detect", data=data or {}, files={"file":("frame.jpg",self.jpeg,"image/jpeg")}, headers={"X-Frame-Id":"42"})

    def test_old_client_keeps_legacy_response_and_frame_id(self):
        with patch.object(server, "HAND_ENHANCE", True):
            result = self.send()
        self.assertEqual(result.status_code, 200)
        self.assertEqual(result.json()["frame_id"], 42)
        self.assertEqual(result.json()["profile"], "legacy")
        self.enhanced.assert_not_called()

    def test_discard_is_never_sent_to_hand_enhancer(self):
        with patch.object(server, "HAND_ENHANCE", True):
            result = self.send({"scan_target":"discard"})
        self.assertEqual(result.status_code, 200)
        self.enhanced.assert_not_called()

    def test_hand_profile_requires_explicit_trial_setting(self):
        with patch.object(server, "HAND_ENHANCE", False):
            self.assertEqual(self.send({"scan_target":"hand"}).json()["profile"], "legacy")
        self.enhanced.assert_not_called()
        with patch.object(server, "HAND_ENHANCE", True):
            self.assertEqual(self.send({"scan_target":"hand"}).json()["profile"], "hand_roi_rotation_v1")
        self.enhanced.assert_called_once()

    def test_raw_diagnostic_request_bypasses_enhancement(self):
        with patch.object(server, "HAND_ENHANCE", True):
            self.assertEqual(self.send({"scan_target":"hand","dedupe":"false"}).status_code, 200)
        self.enhanced.assert_not_called()

    def test_invalid_target_and_image_rejected_before_detection(self):
        self.assertEqual(self.send({"scan_target":"other"}).status_code, 422)
        bad = self.client.post("/detect", files={"file":("bad.jpg",b"not jpeg","image/jpeg")}, headers={"X-Frame-Id":"42"})
        self.assertEqual(bad.status_code, 400)
        self.old.assert_not_called()

    def test_capture_is_local_bounded_and_does_not_save_discard(self):
        workspace = Path(__file__).resolve().parents[1]
        debug_root = workspace / ".debug"
        debug_root.mkdir(parents=True, exist_ok=True)
        with tempfile.TemporaryDirectory(prefix="capture_check_", dir=debug_root) as folder:
            self.assertTrue(Path(folder).resolve().is_relative_to(debug_root.resolve()))
            with patch.object(server,"CAPTURE_DIR",Path(folder)), patch.object(server,"CAPTURE_MAX",2), patch.object(server,"capture_seen",0), patch.object(server,"capture_saved",0):
                self.send({"scan_target":"discard"})
                self.assertEqual(len(list(Path(folder).glob("*.jpg"))), 0)
                for _ in range(10):
                    self.send({"scan_target":"hand"})
                self.assertEqual(len(list(Path(folder).glob("*.jpg"))), 2)
                self.assertEqual(len(list(Path(folder).glob("*.json"))), 2)


if __name__ == "__main__":
    unittest.main()
