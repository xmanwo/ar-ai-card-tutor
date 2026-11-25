"""DeepSeek tutoring router; independent of the recognition inference lock."""

import json
import os
import re
from threading import BoundedSemaphore
from typing import Literal

import requests
from fastapi import APIRouter, HTTPException
from pydantic import BaseModel, ConfigDict, Field, model_validator


router = APIRouter(prefix="/tutor")
slots = BoundedSemaphore(2)
MODEL = "deepseek-flash"


class Card(BaseModel):
    model_config = ConfigDict(extra="forbid")
    id: str = Field(min_length=1, max_length=64)
    color: str = Field(max_length=20)
    value: str = Field(min_length=1, max_length=30)


class CheckedCard(Card):
    legal: bool
    reason: Literal[
        "SameColor", "SameValue", "WildCard", "WildDrawFour",
        "WildDrawFourBlocked", "NoMatch", "UnknownCard", "NoTopCard", "NotReady",
    ]


class TutorRequest(BaseModel):
    model_config = ConfigDict(extra="forbid")
    snapshot_id: int = Field(ge=0)
    language: Literal["en", "zh"] = "en"
    mode: Literal["explain", "hint"] = "explain"
    hand: list[CheckedCard] = Field(min_length=1, max_length=128)
    discard: Card
    active_color: Literal["Red", "Yellow", "Green", "Blue"]
    wild_draw_four_needs_no_active_colour: bool = True
    uno_alert: bool = False

    @model_validator(mode="after")
    def unique_ids(self):
        ids = [card.id for card in self.hand]
        if len(ids) != len(set(ids)):
            raise ValueError("Hand card IDs must be unique")
        return self


class TutorResponse(BaseModel):
    snapshot_id: int
    answer: str = Field(min_length=1, max_length=1200)
    suggested_card_id: str = Field(max_length=64)


SYSTEM_PROMPT = """You explain confirmed UNO positions to beginners.
Use only the confirmed top card, active_color, and the engine's PLAYABLE/UNPLAYABLE
verdicts and reason codes in the JSON data. Never judge cards yourself or infer
turn history, penalties, challenges, opponents' cards or strategy.
After a Wild, the selected active colour governs ordinary colour matching.
Say "matches the active colour"; never say "matches the Wild card".
Honour the supplied Wild Draw Four rule and the app's verdicts for Wild cards.
For explain: teach one rule behind the result instead of repeating the card list.
For hint: select a PLAYABLE card ID and give its reason in one short sentence.
If no card is PLAYABLE, return an empty ID and explain why. The app adds the draw-one action.
The app displays the recommended action and adds "Call UNO!" when uno_alert is true.
Write one short sentence in the requested language (en or zh), plain text with no Markdown.
Return only JSON: {"answer":"short rule reason", "suggested_card_id":""}.
The suggested ID must be empty for explain.
"""


def card_name(card: Card) -> str:
    return card.value if card.color in ("", "Wild") else f"{card.color} {card.value}"


def format_answer(context: TutorRequest, reason: str, card_id: str) -> str:
    legal = {card.id: card for card in context.hand if card.legal}
    hint = context.mode == "hint"
    if (card_id and (not hint or card_id not in legal)) or (hint and legal and not card_id):
        raise ValueError("Selected card disagrees with the legal set")

    reason = reason.strip()
    sentences = re.findall(r"[^.!?。！？]+", reason)
    if (not reason or len(reason) > 240 or len(sentences) > (1 if hint or context.uno_alert else 2)
            or re.search(r"[\r\n`*_#<>\[\]]|^\s*[-+]", reason)
            or re.search(r"\bmatch(?:es|ed|ing)?\s+(?:the\s+)?wild(?:\s+card)?\b", reason, re.I)):
        raise ValueError("Invalid rule explanation")
    for card in context.hand:
        if card.legal:
            continue
        name = re.escape(card_name(card))
        if re.search(rf"\b(?:play\s+(?:the\s+)?{name}\b|"
                     rf"{name}\s+(?:can be played|is (?:legal|playable)))", reason, re.I):
            raise ValueError("Explanation recommends an unplayable card")

    zh = context.language == "zh"
    parts = []
    if hint:
        if card_id:
            name = card_name(legal[card_id])
            parts.append(f"出 {name}。" if zh else f"Play {name}.")
        else:
            parts.append("摸一张牌。" if zh else "Draw one card.")
    parts.append(reason if reason[-1] in ".!?。！？" else reason + ("。" if zh else "."))
    if context.uno_alert:
        parts.append("喊 UNO！" if zh else "Call UNO!")
    return ("" if zh else " ").join(parts)


@router.get("/health")
def health():
    return {"configured": bool(os.environ.get("DEEPSEEK_API_KEY", "").strip()), "model": MODEL}


@router.post("/explain", response_model=TutorResponse)
def explain(context: TutorRequest):
    key = os.environ.get("DEEPSEEK_API_KEY", "").strip()
    if not key:
        raise HTTPException(503, "AI key is not configured on the PC. Use run_ai_server.bat.")
    if not slots.acquire(blocking=False):
        raise HTTPException(429, "AI is busy. Please wait before trying again.")
    try:
        prompt_data = context.model_dump()
        for card in prompt_data["hand"]:
            card["verdict"] = "PLAYABLE" if card["legal"] else "UNPLAYABLE"
        try:
            response = requests.post(
                "https://api.deepseek.com/chat/completions",
                headers={"Authorization": "Bearer " + key},
                json={
                    "model": MODEL,
                    "messages": [
                        {"role": "system", "content": SYSTEM_PROMPT},
                        {"role": "user", "content": json.dumps(prompt_data, ensure_ascii=False)},
                    ],
                    "thinking": {"type": "disabled"},
                    "response_format": {"type": "json_object"},
                    "max_tokens": 95,
                    "temperature": 0.25,
                    "stream": False,
                },
                timeout=(5, 20),
            )
        except requests.Timeout:
            raise HTTPException(504, "AI timed out. The local rule advice is still available.") from None
        except requests.RequestException:
            raise HTTPException(502, "The PC could not connect to DeepSeek.") from None
        if response.status_code != 200:
            detail = {
                401: "The PC API key was rejected.",
                402: "The DeepSeek account has insufficient balance.",
                429: "DeepSeek is rate limiting requests. Please try later.",
            }.get(response.status_code, "DeepSeek is temporarily unavailable.")
            raise HTTPException(502, detail)
        try:
            choice = response.json()["choices"][0]
            if choice.get("finish_reason") != "stop":
                raise ValueError("Incomplete completion")
            value = json.loads(choice["message"]["content"])
            if not isinstance(value, dict) or set(value) != {"answer", "suggested_card_id"}:
                raise ValueError("Unexpected response fields")
            if not isinstance(value["answer"], str) or not isinstance(value["suggested_card_id"], str):
                raise ValueError("Unexpected response types")
            answer = format_answer(context, value["answer"], value["suggested_card_id"])
            return TutorResponse(snapshot_id=context.snapshot_id, answer=answer,
                                 suggested_card_id=value["suggested_card_id"])
        except (ValueError, KeyError, TypeError, IndexError):
            raise HTTPException(502, "AI returned an incomplete or invalid answer. Use the local rule advice.") from None
    finally:
        slots.release()
