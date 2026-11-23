"""
UNO 卡牌检测 —— v2（在原版基础上加了「角标去重」）

模型：Stormynova/Uno_Card_detection 的 YOLOv8 权重
      （54 类 = 4 色 x (0-9 + Draw Two + reverse + skip) + Wild + Wild+4）

★ v2 与 detect.py 的唯一区别
   这个权重检测的是「牌角上的符号」，不是整张牌。
   一张完整可见的 UNO 牌有两个角标（左上 + 右下）→ 会输出两个框。
   v2 在 detect() 后加了一步去重：
     ① 同一位置的重复框（IoU 高）→ 只留置信度最高的
     ② 同一张牌的两个角标（对角 + 归一化距离合适）→ 只留「靠左上」的那个
   参数在 85 张数据集图上实测标定（r 中位 3.20，范围 2.58–4.45）。
   想对比原始输出，加 --no-dedupe。

用法：
    python detect_v2.py                     # 电脑摄像头
    python detect_v2.py --diag              # 自检（有问题先跑这个）
    python detect_v2.py --source 图.jpg     # 单张图
    python detect_v2.py --source 视频.mp4   # 视频文件
    python detect_v2.py --conf 0.4          # 提高置信度阈值
    python detect_v2.py --camera 1          # 换一个摄像头序号
    python detect_v2.py --list-classes      # 打印模型能认的 54 个类
    python detect_v2.py --no-dedupe         # 关掉去重，看模型原始输出

窗口里的按键：
    q 退出    s 保存当前带框的画面    p 暂停/继续

注意：画面上的标签用英文（OpenCV 画不了中文），控制台里是中文。
"""
from __future__ import annotations

import argparse
import os
import sys
import time
from pathlib import Path

try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    sys.stderr.reconfigure(encoding="utf-8", errors="replace")
except Exception:
    pass

ROOT = Path(__file__).resolve().parent
os.environ.setdefault("YOLO_CONFIG_DIR", str(ROOT / ".ultralytics"))

import cv2
import math
import numpy as np
from ultralytics import YOLO

MODEL_PATH = Path(os.environ.get("UNO_MODEL", "")) if os.environ.get("UNO_MODEL") \
    else ROOT / "models" / "best.pt"
SNAP_DIR = ROOT / "snapshots"
GRID_N = 3          # 分区诊断：3x3 网格

# ---- 角标去重参数 ----
#   一轮标定 2026-10-15 在 85 张数据集图上：同一张牌两角标的 r 中位 3.20，范围 2.58–4.45
#   二轮放宽 2026-10-15：实测发现"两个角标被分到不同类别"的情况，故增加宽松配对
DUP_IOU         = 0.60   # ① 同位置重复框：IoU 超过此值 → 判为同一个角标

# 【严格配对】要求两个框类别相同
PAIR_R_MIN      = 2.00   # 归一化中心距下限
PAIR_R_MAX      = 5.50   # 上限
PAIR_R_CENTER   = 3.20   # 贪心排序目标值（离它近的优先配）
PAIR_SIZE_RATIO = 2.00   # 两个框的线性尺寸比上限
PAIR_DIAG_MIN   = 0.50   # |dx|、|dy| 相对 msize 的最小值（确保呈对角）
PAIR_Y_EPS      = 0.30   # 「y 接近」判定（相对框高）

# 【宽松配对】类别不同也允许（应对小角标误分类），但几何要求更严
LOOSE_R_MIN       = 2.60
LOOSE_R_MAX       = 4.20
LOOSE_SIZE_RATIO  = 1.40

# 【按牌形配对】2026-11-04 新增，默认开启
#   旧的 r 窗口（中心距 / 平均边长）在近距会失效：牌占满画面时角标框还是那么大，
#   两个角标的间距却随距离不变地折算成"几十个框宽"，直接冲出 [2.0, 5.5]，
#   于是一张牌留下两个框 —— 实测近距 5 张牌出 9 条结果就是这么来的。
#   换成看"两框外接矩形的长宽比"：同一张牌的两个角标，外接矩形就是那张牌，
#   长宽比恒等于 0.645（UNO 牌 56×87mm），与距离、与框的绝对大小都无关。
#   两张并排的牌外接框会接近 2:1，长宽比掉到 0.3 左右，自然被拒。
PAIR_BY_CARD_SHAPE = os.environ.get("UNO_PAIR_BY_SHAPE", "1") != "0"
CARD_ASPECT        = 0.645   # 短边 / 长边
CARD_ASPECT_TOL    = 0.18    # 允许 0.465–0.825

# 【宽松配对开关】2026-11-04 默认关闭
#   关闭的理由：它允许"类别不同也配对"，是两张相邻/相邻叠放的牌被合成一张的主要嫌疑。
#   代价：同一张牌的两个角标若被判成不同类，会留下两个框（宁可多一个，不要少一张）。
#   要开回来：设置环境变量 UNO_LOOSE_PAIRING=1。
LOOSE_PAIRING = os.environ.get("UNO_LOOSE_PAIRING", "0") == "1"

PRETTY = {
    "lblue-0": "blue 0", "lblue-1": "blue 1", "lblue-2": "blue 2", "lblue-3": "blue 3",
    "lblue-4": "blue 4", "lblue-5": "blue 5", "lblue-6": "blue 6", "lblue-7": "blue 7",
    "lblue-8": "blue 8", "lblue-9": "blue 9",
    "lblue-20": "blue Draw two", "lbluerevers-20": "blue reverse", "lblueskip-20": "blue skip",

    "lgreen-0": "green 0", "lgreen-1": "green 1", "lgreen-2": "green 2", "lgreen-3": "green 3",
    "lgreen-4": "green 4", "lgreen-5": "green 5", "lgreen-6": "green 6", "lgreen-7": "green 7",
    "lgreen-8": "green 8", "lgreen-9": "green 9",
    "lgreen-20": "green Draw two", "lgreenrevers-20": "green reverse", "lgreenskip-20": "green skip",

    "lred-0": "red 0", "lred-1": "red 1", "lred-2": "red 2", "lred-3": "red 3",
    "lred-4": "red 4", "lred-5": "red 5", "lred-6": "red 6", "lred-7": "red 7",
    "lred-8": "red 8", "lred-9": "red 9",
    "lred-20": "red Draw two", "lredrevers-20": "red reverse", "lredskip-20": "red skip",

    "lyellow-0": "yellow 0", "lyellow-1": "yellow 1", "lyellow-2": "yellow 2",
    "lyellow-3": "yellow 3", "lyellow-4": "yellow 4", "lyellow-5": "yellow 5",
    "lyellow-6": "yellow 6", "lyellow-7": "yellow 7", "lyellow-8": "yellow 8",
    "lyellow-9": "yellow 9",
    "lyellow-20": "yellow Draw two", "lyellowrevers-20": "yellow reverse",
    "lyellowskip-20": "yellow skip",

    "lcolor-40": "Wild", "lcolor-400": "Wild draw 4",
}

COLOUR_BGR = {
    "red": (0, 0, 255), "green": (0, 190, 0), "blue": (255, 120, 0),
    "yellow": (0, 215, 255), "wild": (210, 0, 210),
}


def say(*a):
    print(*a, flush=True)


def pretty_name(raw: str) -> str:
    if raw in PRETTY:
        return PRETTY[raw]
    low = raw.lower()
    for c in ("red", "green", "blue", "yellow"):
        if low.startswith("l" + c):
            return c + low[1 + len(c):].lstrip("-_")
    return raw


def card_colour(pretty: str) -> str:
    p = pretty.lower()
    for c in ("red", "green", "blue", "yellow"):
        if p.startswith(c):
            return c
    return "wild"


def _iou(a, b):
    ax1, ay1, ax2, ay2 = a["xyxy"]
    bx1, by1, bx2, by2 = b["xyxy"]
    ix1, iy1 = max(ax1, bx1), max(ay1, by1)
    ix2, iy2 = min(ax2, bx2), min(ay2, by2)
    inter = max(0, ix2 - ix1) * max(0, iy2 - iy1)
    ua = (ax2 - ax1) * (ay2 - ay1) + (bx2 - bx1) * (by2 - by1) - inter
    return inter / ua if ua > 0 else 0.0


def dedupe_corners(dets, debug=False):
    """把「一张牌的两个角标」去重成一个结果 —— 只留靠上那个。

    第 ① 轮：同一位置的重复框（IoU 高）→ 只留置信度最高的
    第 ② 轮：同一张牌的两个角标 → 只留「靠上」的
             · 严格配对：两个框类别相同
             · 宽松配对：类别不同也允许（小角标容易误分类），但几何要求更严
    没配上的框原样保留（牌被遮挡、只露一个角标时就是这种）

    debug=True 时打印每一对的分析，含「为什么没配上」。
    """
    if debug:
        say(f"  [去重] 输入 {len(dets)} 个框：")
        for k, d in enumerate(dets):
            x1, y1, x2, y2 = d["xyxy"]
            say(f"      #{k} {d['pretty']:18s} conf={d['conf']:.2f} "
                f"({x1},{y1})-({x2},{y2}) w={x2-x1} h={y2-y1}")

    if len(dets) < 2:
        return dets

    # ---- ① 同位置重复 ----
    keep = []
    for d in sorted(dets, key=lambda x: -x["conf"]):
        if all(_iou(d, k) <= DUP_IOU for k in keep):
            keep.append(d)
    if debug and len(keep) != len(dets):
        say(f"  [去重①] 同位置重复框：{len(dets)} → {len(keep)}")
    dets = keep
    if len(dets) < 2:
        return dets

    # ---- ② 对角配对 ----
    n = len(dets)
    cand = []
    for i in range(n):
        for j in range(i + 1, n):
            a, b = dets[i], dets[j]
            ax1, ay1, ax2, ay2 = a["xyxy"]
            bx1, by1, bx2, by2 = b["xyxy"]
            aw, ah = ax2 - ax1, ay2 - ay1
            bw, bh = bx2 - bx1, by2 - by1
            if min(aw, ah, bw, bh) <= 0:
                continue
            sa, sb = (aw + ah) / 2, (bw + bh) / 2
            ratio = max(sa, sb) / min(sa, sb)
            acx, acy = (ax1 + ax2) / 2, (ay1 + ay2) / 2
            bcx, bcy = (bx1 + bx2) / 2, (by1 + by2) / 2
            dx, dy = bcx - acx, bcy - acy
            ms = (aw + ah + bw + bh) / 4
            r = math.hypot(dx, dy) / ms
            diag_ok = (abs(dx) >= PAIR_DIAG_MIN * ms and abs(dy) >= PAIR_DIAG_MIN * ms)
            same = (a["raw"] == b["raw"])

            # 两框的外接矩形：若它们是同一张牌的两个角标，这个矩形就是那张牌本身。
            # 长宽比因此恒等于 CARD_ASPECT，与距离、与框的绝对大小无关 —— 近距不再失灵。
            ux1, uy1 = min(ax1, bx1), min(ay1, by1)
            ux2, uy2 = max(ax2, bx2), max(ay2, by2)
            uw, uh = ux2 - ux1, uy2 - uy1
            long_side = max(uw, uh)
            aspect = min(uw, uh) / long_side if long_side > 0 else 0.0
            shape_ok = abs(aspect - CARD_ASPECT) <= CARD_ASPECT_TOL

            if PAIR_BY_CARD_SHAPE:
                geo_ok = shape_ok
                rank = abs(aspect - CARD_ASPECT)
            else:
                geo_ok = PAIR_R_MIN <= r <= PAIR_R_MAX
                rank = abs(r - PAIR_R_CENTER)

            pri = None
            if same and geo_ok and ratio <= PAIR_SIZE_RATIO and diag_ok:
                pri = 0                                   # 严格配对
            elif LOOSE_PAIRING and (not same) and geo_ok \
                    and ratio <= LOOSE_SIZE_RATIO and diag_ok:
                pri = 1                                   # 宽松配对（默认关闭）

            if pri is not None:
                cand.append((pri, rank, i, j))
            elif debug:
                why = []
                if not same:
                    why.append("类别不同")
                if PAIR_BY_CARD_SHAPE:
                    if not shape_ok:
                        why.append(f"外接框比例 {aspect:.2f} 偏离 {CARD_ASPECT}±{CARD_ASPECT_TOL}")
                elif not (PAIR_R_MIN <= r <= PAIR_R_MAX):
                    why.append(f"r={r:.2f} 超出 [{PAIR_R_MIN},{PAIR_R_MAX}]")
                if ratio > PAIR_SIZE_RATIO:
                    why.append(f"尺寸比 {ratio:.2f} > {PAIR_SIZE_RATIO}")
                if not diag_ok:
                    why.append(f"非对角 (dx={dx:.0f} dy={dy:.0f} ms={ms:.0f})")
                say(f"  [去重②] 不配: #{i} {a['pretty']}({a['conf']:.2f}) <-> "
                    f"#{j} {b['pretty']}({b['conf']:.2f})   r={r:.2f} aspect={aspect:.2f}"
                    f"  -> {'; '.join(why) if why else '几何不符'}")

    cand.sort(key=lambda t: (t[0], t[1]))
    drop = set()
    for pri, _, i, j in cand:
        if i in drop or j in drop:             # 一个框只能配一次
            continue
        ax1, ay1, ax2, ay2 = dets[i]["xyxy"]
        bx1, by1, bx2, by2 = dets[j]["xyxy"]
        acy, bcy = (ay1 + ay2) / 2, (by1 + by2) / 2
        eps = PAIR_Y_EPS * ((ay2 - ay1) + (by2 - by1)) / 2
        if acy < bcy - eps:
            kept = i                           # i 更靠上 → 留 i
            drop.add(j)
        elif bcy < acy - eps:
            kept = j                           # j 更靠上 → 留 j
            drop.add(i)
        else:                                  # 竖直方向接近 → 比 x，留靠左的
            acx, bcx = (ax1 + ax2) / 2, (bx1 + bx2) / 2
            kept = i if acx <= bcx else j
            drop.add(j if acx <= bcx else i)
        if debug:
            how = ("y 更小 = 画面上更靠上" if abs(acy - bcy) > eps
                   else "y 接近 → 比 x，保留更靠左")
            say(f"  [去重②] 配对 #{i}+#{j}（{'严格' if pri == 0 else '宽松'}）"
                f"  中心 y：#{i}={acy:.0f}  #{j}={bcy:.0f}"
                f"  → 保留 #{kept}（{how}）")

    out = [d for k, d in enumerate(dets) if k not in drop]
    if debug:
        say(f"  [去重] 结果：{len(dets)} → {len(out)}")
        for d in out:
            x1, y1, x2, y2 = d["xyxy"]
            say(f"      保留 {d['pretty']:18s} conf={d['conf']:.2f}  "
                f"框=({x1},{y1})-({x2},{y2})  中心=({(x1+x2)//2},{(y1+y2)//2})")
    return out


def _boxes_at(model, frame, imgsz, conf, device, iou=None, agnostic=None):
    """单尺度推理，返回原始框（不做去重）。坐标已由 ultralytics 映射回原图。

    iou / agnostic 走模块默认值（UNO_NMS_IOU / UNO_AGNOSTIC_NMS），也可逐次覆盖。
    """
    res = model.predict(
        frame,
        imgsz=imgsz,
        conf=conf,
        iou=NMS_IOU if iou is None else iou,
        agnostic_nms=AGNOSTIC_NMS if agnostic is None else agnostic,
        device=device,
        verbose=False,
    )[0]
    out = []
    for b in res.boxes:
        x1, y1, x2, y2 = [int(v) for v in b.xyxy[0].tolist()]
        raw = model.names[int(b.cls[0])]
        out.append({"xyxy": (x1, y1, x2, y2), "raw": raw,
                    "pretty": pretty_name(raw), "conf": float(b.conf[0])})
    return out


def detect(model, frame, imgsz, conf, device, dedupe=True, debug=False,
           iou=None, agnostic=None):
    out = _boxes_at(model, frame, imgsz, conf, device, iou=iou, agnostic=agnostic)
    return dedupe_corners(out, debug=debug) if dedupe else out


# ---------------------------------------------------------------- 多尺度并集
#   这个权重的致命点：同一个物体只有在"输入里 40–85 px"时才稳定可检。
#   牌在画面里占比一变（走近/走远/牌多牌少），就会整体掉出这个窗口。
#   单靠调 imgsz 是零和的：照顾了近处，远处就死。多尺度并集是唯一同时覆盖两端的做法。
DEFAULT_SCALES = (256, 416, 512, 640)

# NMS 参数。agnostic_nms=True 会把不同类别之间的重叠框也抑制掉 —— 实测同一张万能牌
# 在不同尺度会被判成 Wild 和 Wild draw 4 两个类别，各自成框，几何去重管不着。
# 打开后按 IoU 只留一个，等于在源头解决"同类不同名的重复"。
NMS_IOU      = float(os.environ.get("UNO_NMS_IOU", "0.7"))
AGNOSTIC_NMS = os.environ.get("UNO_AGNOSTIC_NMS", "0") != "0"

#   不同尺度会把同一张牌框成不同大小的东西（实测 22×24 的角标 vs 415×465 的整牌面），
#   IoU 低到 0.05，过不了第 ① 轮的 0.60 去重 —— 所以并集之后要先做一次「包含关系」合并：
#   小框有 85% 以上面积落在大框里，就认为它们是同一张牌，留置信度高的。
CONTAIN_RATIO = 0.85

# 同标签近距离合并。微调后的权重会把同一张牌的一个角标同时输出成大框和小框，
# 两者既不互相包含（一个在左一个在右）、IoU 也低，前面的规则都拦不住。
# 这里补一条：同类别 + 中心距 < 1 倍框边长 → 判为同一个目标。
# 为什么不会误合并相邻的两张同名牌：相邻牌的中心距约等于一个牌宽（~200px），
# 而框只有 60–80px，比值 2.5 以上，够不到阈值。
SAME_LABEL_MERGE_K = 1.0


def _center_size(d):
    x1, y1, x2, y2 = d["xyxy"]
    return (x1 + x2) / 2, (y1 + y2) / 2, max(x2 - x1, y2 - y1)


def merge_same_label(dets, k=SAME_LABEL_MERGE_K, debug=False):
    keep = []
    for d in sorted(dets, key=lambda x: -x["conf"]):
        cx, cy, sz = _center_size(d)
        dup = False
        for e in keep:
            ex, ey, esz = _center_size(e)
            if d["raw"] == e["raw"] and math.hypot(cx - ex, cy - ey) < k * max(sz, esz):
                dup = True
                break
        if dup:
            if debug:
                say(f"  [同标签合并] 丢掉 {d['pretty']} {d['conf']:.2f}")
            continue
        keep.append(d)
    return keep


def _area(box):
    x1, y1, x2, y2 = box
    return max(0, x2 - x1) * max(0, y2 - y1)


def _contained_ratio(small, big):
    """small 有多少面积落在 big 里（0–1）。"""
    ax1, ay1, ax2, ay2 = small
    bx1, by1, bx2, by2 = big
    ix1, iy1 = max(ax1, bx1), max(ay1, by1)
    ix2, iy2 = min(ax2, bx2), min(ay2, by2)
    inter = max(0, ix2 - ix1) * max(0, iy2 - iy1)
    a = _area(small)
    return inter / a if a > 0 else 0.0


def merge_contained(dets, ratio=CONTAIN_RATIO, debug=False):
    """跨尺度合并：把「同一张牌的小框 + 大框」并成一个。"""
    keep = []
    dropped = 0
    for d in sorted(dets, key=lambda x: -x["conf"]):
        dup = None
        for k in keep:
            if _contained_ratio(d["xyxy"], k["xyxy"]) >= ratio or \
                    _contained_ratio(k["xyxy"], d["xyxy"]) >= ratio:
                dup = k
                break
        if dup is None:
            keep.append(d)
        else:
            dropped += 1
            if debug:
                say(f"  [并集] 吞掉 {d['pretty']} conf={d['conf']:.2f} "
                    f"{d['xyxy']} ←→ {dup['pretty']} conf={dup['conf']:.2f} {dup['xyxy']}")
    if debug and dropped:
        say(f"  [并集] 跨尺度重复 {dropped} 个：{len(dets)} → {len(keep)}")
    return keep


def detect_multiscale(model, frame, scales=DEFAULT_SCALES, conf=0.25, device=None,
                      dedupe=True, debug=False, iou=None, agnostic=None):
    """多尺度并集 → 包含关系合并 → 角标去重。

    顺序不能换：先去重会把不同尺度的框当成两件事，越去越多。
    """
    merged = []
    for s in scales:
        got = _boxes_at(model, frame, s, conf, device, iou=iou, agnostic=agnostic)
        if debug:
            say(f"  [尺度 {s}] {len(got)} 个框")
        for d in got:
            d["scale"] = s
        merged.extend(got)

    merged = merge_contained(merged, debug=debug)
    merged = merge_same_label(merged, debug=debug)
    return dedupe_corners(merged, debug=debug) if dedupe else merged


# ---------------------------------------------------------------- 自适应两档
#   实测定下来的：这个模型的召回对"框在输入里多大"极其敏感，峰值在 60–70px。
#   与其把 256/416/512/640/896/1024/1280 全跑一遍，不如：
#     第一档 896 先看框有多大 → 反算"要让框变成 65px 该用多大 imgsz" → 只补跑那一档。
#   40 张真机帧上：两档 896+1152 = 25/40 正好五张、39ms；
#   旧的四档 256,416,512,640 = 18/40、68ms。少一半算力还更准。
ADAPTIVE_SEED      = 896
ADAPTIVE_TARGET_PX = 65


def detect_adaptive(model, frame, conf=0.25, device=None, dedupe=True, debug=False,
                    seed_imgsz=ADAPTIVE_SEED, target_box_px=ADAPTIVE_TARGET_PX,
                    fallback_scales=DEFAULT_SCALES, iou=None, agnostic=None):
    """先一档拿锚点，按框大小反算第二档，只跑两次。零检出时退回多尺度。"""
    first = _boxes_at(model, frame, seed_imgsz, conf, device, iou=iou, agnostic=agnostic)

    if not first:
        if debug:
            say(f"  [自适应] 首档 {seed_imgsz} 零检出 -> 退回多尺度 {list(fallback_scales)}")
        return detect_multiscale(model, frame, fallback_scales, conf, device,
                                 dedupe, debug, iou, agnostic)

    sizes = sorted(max(d["xyxy"][2] - d["xyxy"][0], d["xyxy"][3] - d["xyxy"][1]) for d in first)
    median = sizes[len(sizes) // 2]
    long_side = max(frame.shape[0], frame.shape[1])

    second_imgsz = seed_imgsz
    if median > 0:
        want = int(round(target_box_px * long_side / float(median)))
        second_imgsz = max(256, min(1536, int(round(want / 32.0)) * 32))

    if debug:
        say(f"  [自适应] 首档 {seed_imgsz}: 框中位 {median}px -> 第二档 {second_imgsz}")

    second = []
    if second_imgsz != seed_imgsz:
        second = _boxes_at(model, frame, second_imgsz, conf, device, iou=iou, agnostic=agnostic)

    merged = merge_contained(first + second, debug=debug)
    merged = merge_same_label(merged, debug=debug)
    return dedupe_corners(merged, debug=debug) if dedupe else merged


def draw(img, dets, fps=None, paused=False, grid=False, live=None):
    h, w = img.shape[:2]

    if grid and live is not None:
        cw, ch = w / GRID_N, h / GRID_N
        for i in range(1, GRID_N):
            cv2.line(img, (int(cw * i), 0), (int(cw * i), h), (110, 110, 110), 1)
            cv2.line(img, (0, int(ch * i)), (w, int(ch * i)), (110, 110, 110), 1)
        for gy in range(GRID_N):
            for gx in range(GRID_N):
                n = int(live[gy, gx])
                cv2.putText(img, str(n), (int(gx * cw) + 8, int(gy * ch) + 28),
                            cv2.FONT_HERSHEY_SIMPLEX, 0.75,
                            (0, 230, 0) if n else (150, 150, 150), 2, cv2.LINE_AA)

    for d in dets:
        x1, y1, x2, y2 = d["xyxy"]
        col = COLOUR_BGR[card_colour(d["pretty"])]
        cv2.rectangle(img, (x1, y1), (x2, y2), col, 2)
        label = f'{d["pretty"]} {d["conf"]:.2f}'
        (tw, th), _ = cv2.getTextSize(label, cv2.FONT_HERSHEY_SIMPLEX, 0.55, 2)
        cv2.rectangle(img, (x1, max(0, y1 - th - 8)), (x1 + tw + 8, y1), col, -1)
        cv2.putText(img, label, (x1 + 4, max(12, y1 - 6)),
                    cv2.FONT_HERSHEY_SIMPLEX, 0.55, (255, 255, 255), 2, cv2.LINE_AA)

    bar = f"cards: {len(dets)}"
    if fps:
        bar += f"   {fps:.1f} FPS"
    if paused:
        bar += "   [PAUSED]"
    cv2.rectangle(img, (0, 0), (w, 30), (32, 32, 32), -1)
    cv2.putText(img, bar, (10, 21), cv2.FONT_HERSHEY_SIMPLEX, 0.6, (230, 230, 230), 2, cv2.LINE_AA)

    if not dets:
        msg = "No card detected - hold cards facing the camera"
        (tw, _), _ = cv2.getTextSize(msg, cv2.FONT_HERSHEY_SIMPLEX, 0.6, 2)
        cv2.putText(img, msg, ((w - tw) // 2, h // 2),
                    cv2.FONT_HERSHEY_SIMPLEX, 0.6, (60, 220, 255), 2, cv2.LINE_AA)

    cv2.putText(img, "q=quit  s=save  p=pause", (10, h - 12),
                cv2.FONT_HERSHEY_SIMPLEX, 0.5, (200, 200, 200), 1, cv2.LINE_AA)
    return img


def snapshot(img, tag="snap"):
    SNAP_DIR.mkdir(exist_ok=True)
    p = SNAP_DIR / f"{tag}_{time.strftime('%Y%m%d_%H%M%S')}.jpg"
    cv2.imwrite(str(p), img)
    say(f"  画面已保存 -> {p}")


def open_camera(idx):
    """依次尝试几个后端，返回能读帧的 VideoCapture。"""
    for backend, name in ((cv2.CAP_DSHOW, "DSHOW"), (cv2.CAP_MSMF, "MSMF"), (cv2.CAP_ANY, "ANY")):
        try:
            cap = cv2.VideoCapture(idx, backend)
        except Exception:
            continue
        if cap.isOpened():
            ok, frame = cap.read()
            if ok and frame is not None:
                say(f"  摄像头 {idx} 打开成功（后端 {name}，原生尺寸 {frame.shape[1]}x{frame.shape[0]}）")
                return cap, name, frame.shape
            cap.release()
        else:
            cap.release()
    return None, None, None


def run_diag(model, names):
    say("")
    say("=" * 56)
    say("  自检报告")
    say("=" * 56)
    say(f"OpenCV        {cv2.__version__}   （GUI 后端：{cv2.getBuildInformation().split('GUI:')[1].splitlines()[0].strip() if 'GUI:' in cv2.getBuildInformation() else '?'}）")
    try:
        import torch
        say(f"PyTorch       {torch.__version__}")
        say(f"CUDA 可用     {torch.cuda.is_available()}")
        if torch.cuda.is_available():
            say(f"GPU           {torch.cuda.get_device_name(0)}")
    except Exception as e:
        say(f"PyTorch 检查失败：{e}")
    say(f"模型          {MODEL_PATH.name}   （{len(names)} 类）")
    say(f"权重路径存在  {MODEL_PATH.exists()}")
    say("")
    say("摄像头扫描：")
    found = 0
    for i in range(4):
        cap, name, shape = open_camera(i)
        if cap:
            found += 1
            cap.release()
        else:
            say(f"  摄像头 {i}：打不开")
    if not found:
        say("")
        say("  ⚠ 一个摄像头都打不开。检查：")
        say("     ① Windows 设置 → 隐私 → 相机 → 允许桌面应用访问")
        say("     ② 摄像头是否被其它程序（会议软件等）占用")
        say("     ③ 台式机是否真的装了摄像头")
    say("")
    say("窗口测试：")
    try:
        test = cv2.imread(str(ROOT / "samples" / "test_uno.jpg"))
        if test is None:
            test = (__import__("numpy").zeros((240, 320, 3), dtype="uint8") + 60)
        cv2.namedWindow("diag", cv2.WINDOW_AUTOSIZE)
        cv2.imshow("diag", test)
        cv2.moveWindow("diag", 120, 120)
        cv2.waitKey(1500)
        cv2.destroyAllWindows()
        say("  ✓ 窗口创建正常（刚才应该闪过一个窗口）")
    except Exception as e:
        say(f"  ✗ 窗口创建失败：{e}")
    say("=" * 56)


def main():
    ap = argparse.ArgumentParser(description="UNO 卡牌检测（本地测试）")
    ap.add_argument("--source", default=None, help="图片/视频路径；不给则用摄像头")
    ap.add_argument("--camera", type=int, default=0, help="摄像头序号（默认 0）")
    ap.add_argument("--imgsz", type=int, default=640, help="推理输入尺寸（默认 640）")
    ap.add_argument("--conf", type=float, default=0.25, help="置信度阈值（默认 0.25）")
    ap.add_argument("--device", default=None, help="cpu / 0 / 留空自动")
    ap.add_argument("--no-show", action="store_true", help="不开窗口（跑批用）")
    ap.add_argument("--grid", action="store_true", help="叠加 3x3 分区统计，用于定位哪些画面区域能识别")
    ap.add_argument("--no-dedupe", action="store_true", help="关闭角标去重（看模型原始输出，用于对比）")
    ap.add_argument("--debug-dedupe", action="store_true", help="打印去重过程：每对框的 r / 尺寸比 / 没配上的原因")
    ap.add_argument("--diag", action="store_true", help="只做自检然后退出")
    ap.add_argument("--list-classes", action="store_true", help="打印模型的类别表后退出")
    args = ap.parse_args()

    say("=" * 56)
    say("  UNO 卡牌检测  |  q 退出  s 存图  p 暂停")
    say("=" * 56)

    if not MODEL_PATH.exists():
        say(f"[错误] 找不到权重：{MODEL_PATH}")
        return 2

    say(f"加载模型 {MODEL_PATH.name} ...")
    model = YOLO(str(MODEL_PATH))
    names = list(model.names.values()) if isinstance(model.names, dict) else list(model.names)
    say(f"模型类别数：{len(names)}")
    if args.no_dedupe:
        say("角标去重：关闭（显示模型原始输出）")
    else:
        say(f"角标去重：开启（一张牌只留靠左上的角标；r ∈ [{PAIR_R_MIN}, {PAIR_R_MAX}]）")

    if args.list_classes:
        for i, n in enumerate(names):
            say(f"  {i:3d}  {n:20s} -> {pretty_name(n)}")
        return 0

    if args.diag:
        run_diag(model, names)
        return 0

    unknown = [n for n in names if n not in PRETTY]
    if unknown:
        say(f"[提示] 有 {len(unknown)} 个类名不在内置对照表里：{unknown[:6]}")

    device = args.device
    if device is None:
        try:
            import torch
            device = "0" if torch.cuda.is_available() else "cpu"
            say(f"设备：{'GPU ' + torch.cuda.get_device_name(0) if device == '0' else 'CPU（会慢一些）'}")
        except Exception:
            device = "cpu"
    else:
        say(f"设备（指定）：{device}")

    still, cap, frame0 = None, None, None

    if args.source:
        p = Path(args.source)
        if not p.exists():
            say(f"[错误] 找不到文件：{p}")
            return 1
        if p.suffix.lower() in (".jpg", ".jpeg", ".png", ".bmp", ".webp"):
            still = cv2.imread(str(p))
            if still is None:
                say(f"[错误] 读不了这张图：{p}")
                return 1
            say(f"图片已载入：{still.shape[1]}x{still.shape[0]}")
        else:
            cap = cv2.VideoCapture(str(p))
            if not cap.isOpened():
                say(f"[错误] 打不开视频：{p}")
                return 1
            say("视频已打开。")
    else:
        say(f"正在打开摄像头 {args.camera} ...")
        cap, backend, shape = open_camera(args.camera)
        if cap is None:
            say(f"[错误] 打不开摄像头 {args.camera}。")
            say("       先跑  python detect.py --diag  看自检报告。")
            return 1
        say("摄像头就绪，按 q 退出。")

    # ---------- 单张图片 ----------
    if still is not None:
        t0 = time.time()
        dets = detect(model, still, args.imgsz, args.conf, device,
                      dedupe=not args.no_dedupe, debug=args.debug_dedupe)
        say(f"\n检出 {len(dets)} 张牌，耗时 {(time.time() - t0) * 1000:.0f} ms")
        for d in sorted(dets, key=lambda x: -x["conf"]):
            say(f"   {d['pretty']:18s} conf={d['conf']:.2f}")

        live1 = None
        if args.grid:
            hh, ww = still.shape[:2]
            live1 = np.zeros((GRID_N, GRID_N), dtype=float)
            for d in dets:
                x1, y1, x2, y2 = d["xyxy"]
                gx = min(int(((x1 + x2) / 2) / ww * GRID_N), GRID_N - 1)
                gy = min(int(((y1 + y2) / 2) / hh * GRID_N), GRID_N - 1)
                live1[gy, gx] += 1
            say("\n分区（行 = 上/中/下，列 = 左/中/右）：")
            for i, r in enumerate(["上", "中", "下"]):
                say(f"   {r}  " + "".join(f"{int(live1[i][j]):6d}" for j in range(GRID_N)))

        img = draw(still, dets, None, False, args.grid, live1)
        out = ROOT / "result_image.jpg"
        cv2.imwrite(str(out), img)
        say(f"结果已保存 -> {out}")
        if not args.no_show:
            cv2.imshow("UNO detection", img)
            cv2.waitKey(0)
            cv2.destroyAllWindows()
        return 0

    # ---------- 摄像头 / 视频 ----------
    win = "UNO detection"
    show = not args.no_show
    frozen = False
    if show:
        try:
            cv2.namedWindow(win, cv2.WINDOW_AUTOSIZE)
            cv2.moveWindow(win, 80, 80)   # 防止窗口跑到屏幕外
        except Exception as e:
            say(f"[警告] 建窗口失败：{e}；改成不开窗口运行。")
            show = False

    fps, paused = 0.0, False
    frame, dets = None, []
    n_frames, n_cards, n_empty = 0, 0, 0
    t_last_report = time.time()
    grid_hits = np.zeros((GRID_N, GRID_N), dtype=float)
    grid_lum = np.zeros((GRID_N, GRID_N), dtype=float)
    lum_frames = 0
    live = np.zeros((GRID_N, GRID_N), dtype=float) if args.grid else None

    while True:
        if not paused:
            ok, frame = cap.read()
            if not ok or frame is None:
                if args.source:
                    say("读帧结束。")
                    break
                n_empty += 1
                if n_empty == 5:
                    say("[警告] 摄像头连读 5 帧失败。可能被别的程序占用了。")
                if n_empty > 100:
                    say("[错误] 摄像头持续读不到画面，退出。")
                    break
                time.sleep(0.05)
                continue
            n_empty = 0
            if n_frames == 0 and show:
                try:
                    h, w = frame.shape[:2]
                    cv2.resizeWindow(win, min(w, 1280), min(h, 800))
                    cv2.moveWindow(win, 80, 80)
                except Exception:
                    pass
            t0 = time.time()
            _dbg = args.debug_dedupe and (n_frames < 3)   # 实时模式只印前 3 帧，别刷屏
            dets = detect(model, frame, args.imgsz, args.conf, device,
                          dedupe=not args.no_dedupe, debug=_dbg)
            dt = max(time.time() - t0, 1e-6)
            fps = 0.9 * fps + 0.1 * (1.0 / dt) if fps else 1.0 / dt
            n_frames += 1
            n_cards += len(dets)

            if args.grid:
                hh, ww = frame.shape[:2]
                live = np.zeros((GRID_N, GRID_N), dtype=float)
                for d in dets:
                    x1, y1, x2, y2 = d["xyxy"]
                    gx = min(int(((x1 + x2) / 2) / ww * GRID_N), GRID_N - 1)
                    gy = min(int(((y1 + y2) / 2) / hh * GRID_N), GRID_N - 1)
                    live[gy, gx] += 1
                    grid_hits[gy, gx] += 1
                gray = cv2.cvtColor(frame, cv2.COLOR_BGR2GRAY)
                for gy in range(GRID_N):
                    for gx in range(GRID_N):
                        cell = gray[gy * hh // GRID_N:(gy + 1) * hh // GRID_N,
                                    gx * ww // GRID_N:(gx + 1) * ww // GRID_N]
                        if cell.size:
                            grid_lum[gy, gx] += float(cell.mean())
                lum_frames += 1

        if show:
            try:
                cv2.imshow(win, draw(frame.copy(), dets, fps, paused, args.grid, live))
                k = cv2.waitKey(1) & 0xFF
            except Exception as e:
                say(f"[错误] 显示失败：{e}")
                break
            if k == ord("q"):
                break
            elif k == ord("s"):
                snapshot(draw(frame.copy(), dets, fps, paused, args.grid, live))
                for d in dets:
                    say(f"   {d['pretty']:18s} conf={d['conf']:.2f}")
            elif k == ord("p"):
                paused = not paused
                say("暂停" if paused else "继续")
        else:
            if time.time() - t_last_report > 3:
                say(f"  帧 {n_frames}  检出 {len(dets)} 张  {fps:.1f} FPS")
                t_last_report = time.time()
            if n_frames >= 300:
                break

    cap.release()
    if show:
        cv2.destroyAllWindows()
    if n_frames:
        say(f"\n共处理 {n_frames} 帧，平均 {n_cards / n_frames:.1f} 张/帧，{fps:.1f} FPS")

    if args.grid and n_frames:
        rows = ["上", "中", "下"]
        say("")
        say("分区统计（行 = 上/中/下，列 = 左/中/右）")
        say("  ① 累计检测到牌的次数")
        say("         左      中      右")
        for i, r in enumerate(rows):
            say(f"   {r}  " + "".join(f"{int(grid_hits[i][j]):7d}" for j in range(GRID_N)))
        if lum_frames:
            avg = [[grid_lum[i][j] / lum_frames for j in range(GRID_N)] for i in range(GRID_N)]
            say("  ② 各区平均亮度（0-255，越高越亮；>200 基本可判过曝）")
            say("         左      中      右")
            for i, r in enumerate(rows):
                say(f"   {r}  " + "".join(f"{avg[i][j]:7.0f}" for j in range(GRID_N)))
            top, bottom = avg[0], avg[2]
            tmax = max(top)
            if tmax > 200:
                say("")
                say(f"  [!] 画面上方平均亮度 {tmax:.0f}，接近/超过过曝线。")
                say("      过曝会丢掉牌的颜色和边缘，检测率会明显下降。先解决光照，再谈模型。")
        say("")
        say("  用法：把同一张牌依次放到九个格子里，各停 2-3 秒，")
        say("        然后看哪些格子有计数。只在上/下某一半有反应 = 位置相关的问题。")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
