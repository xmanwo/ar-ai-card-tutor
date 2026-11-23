"""Experimental hand-only views; original weights and discard detection stay unchanged."""

import math
from collections import defaultdict

import cv2
import numpy as np

import detect_v2 as detector


def hand_roi(detections, shape):
    """Use observed corners to crop background, with room for unseen fan end cards."""
    if len(detections) < 2:
        return None
    height, width = shape[:2]
    boxes = np.asarray([item["xyxy"] for item in detections], dtype=float)
    left, top = boxes[:, :2].min(axis=0)
    right, bottom = boxes[:, 2:].max(axis=0)
    span = max(right - left, bottom - top)
    if span <= 0:
        return None
    roi = (
        max(0, int(left - 0.30 * span)),
        max(0, int(top - 0.15 * span)),
        min(width, int(math.ceil(right + 0.30 * span))),
        min(height, int(math.ceil(bottom + 0.50 * span))),
    )
    x1, y1, x2, y2 = roi
    if x2 - x1 < 64 or y2 - y1 < 64 or (x2-x1)*(y2-y1) >= width*height*0.95:
        return None
    return roi


def rotate_view(frame, angle):
    height, width = frame.shape[:2]
    matrix = cv2.getRotationMatrix2D((width / 2, height / 2), angle, 1.0)
    out_width = int(math.ceil(abs(matrix[0, 0])*width + abs(matrix[0, 1])*height))
    out_height = int(math.ceil(abs(matrix[0, 1])*width + abs(matrix[0, 0])*height))
    matrix[0, 2] += (out_width-width) / 2
    matrix[1, 2] += (out_height-height) / 2
    return cv2.warpAffine(frame, matrix, (out_width, out_height)), cv2.invertAffineTransform(matrix)


def restore_boxes(detections, inverse, offset, shape):
    height, width = shape[:2]
    result = []
    for item in detections:
        x1, y1, x2, y2 = item["xyxy"]
        points = np.array([[x1,y1,1], [x2,y1,1], [x2,y2,1], [x1,y2,1]], dtype=float)
        mapped = points @ inverse.T
        mapped += np.asarray(offset)
        left, top = mapped.min(axis=0)
        right, bottom = mapped.max(axis=0)
        box = (max(0, int(math.floor(left))), max(0, int(math.floor(top))),
               min(width, int(math.ceil(right))), min(height, int(math.ceil(bottom))))
        if box[2] > box[0] and box[3] > box[1]:
            result.append({**item, "xyxy": box})
    return result


def fuse_views(views, supplemental_conf=0.30):
    """Associate one-to-one within each view; never deduplicate by card name alone."""
    groups = []
    for view_index, detections in enumerate(views):
        candidates = [item for item in detections if view_index == 0 or item["conf"] >= supplemental_conf]
        edges = []
        for d, item in enumerate(candidates):
            x1,y1,x2,y2 = item["xyxy"]
            size = max(x2-x1, y2-y1)
            for g, group in enumerate(groups):
                anchor = group[0][1]
                ax1,ay1,ax2,ay2 = anchor["xyxy"]
                distance = math.hypot((x1+x2-ax1-ax2)/2, (y1+y2-ay1-ay2)/2)
                limit = 0.55 * min(size, max(ax2-ax1,ay2-ay1))
                overlap = detector._iou(item, anchor)
                if overlap >= 0.30 and distance <= limit:
                    edges.append((-overlap, distance, d, g))
        used_detections, used_groups = set(), set()
        for _, _, d, g in sorted(edges):
            if d in used_detections or g in used_groups:
                continue
            groups[g].append((view_index, candidates[d]))
            used_detections.add(d)
            used_groups.add(g)
        for d, item in enumerate(candidates):
            if d not in used_detections:
                groups.append([(view_index, item)])

    result = []
    for group in groups:
        scores = defaultdict(float)
        for view_index, item in group:
            # An isolated rotated classification must not outweigh a sound baseline verdict.
            scores[item["raw"]] += item["conf"] * (1.25 if view_index == 0 else 1.0)
        label = max(scores, key=scores.get)
        winner = max((item for _, item in group if item["raw"] == label), key=lambda item: item["conf"])
        # Keep the unrotated anchor box to avoid enlarged rotated boxes crossing adjacent corners.
        result.append({**winner, "xyxy": group[0][1]["xyxy"]})
    return sorted(result, key=lambda item: (item["xyxy"][0]+item["xyxy"][2])/2)


def detect_hand(model, frame, scales, conf, device, angles=(-30, 30)):
    baseline = detector.detect_multiscale(model, frame, scales=scales, conf=conf, device=device)
    roi = hand_roi(baseline, frame.shape)
    if roi is None:
        return baseline, {"profile": "hand_baseline", "baseline_count": len(baseline), "views": 1}
    x1,y1,x2,y2 = roi
    crop = frame[y1:y2, x1:x2]
    identity = np.array([[1,0,0], [0,1,0]], dtype=float)
    views = [baseline]
    detected = detector.detect_multiscale(model, crop, scales=scales, conf=conf, device=device)
    views.append(restore_boxes(detected, identity, (x1,y1), frame.shape))
    for angle in angles:
        rotated, inverse = rotate_view(crop, angle)
        detected = detector.detect_multiscale(model, rotated, scales=scales, conf=conf, device=device)
        views.append(restore_boxes(detected, inverse, (x1,y1), frame.shape))
    result = fuse_views(views)
    return result, {"profile": "hand_roi_rotation_v1", "baseline_count": len(baseline),
                    "views": len(views), "roi_xyxy": list(roi)}
