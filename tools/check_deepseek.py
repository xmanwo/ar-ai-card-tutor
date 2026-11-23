"""One-request API connectivity check. The key is never saved to disk."""

import getpass
import json
import os
import re
import socket
import sys
import urllib.error
import urllib.request


def main():
    print("DeepSeek API 连接测试（一次简短请求，会产生少量 API 费用）")
    print("需要完整 API Key，不是 Tracking ID；输入时不显示字符。")
    key = os.environ.get("DEEPSEEK_API_KEY", "").strip()
    if not key:
        key = getpass.getpass("请粘贴 API Key，然后按 Enter：").strip()
    if not key:
        print("未输入 Key，已取消。")
        return 1
    if re.fullmatch(r"[0-9a-fA-F]{8}(?:-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12}", key):
        print("这是 Tracking ID。请使用创建时显示的完整 API Key。")
        return 1

    payload = {
        "model": "deepseek-flash",
        "messages": [
            {
                "role": "system",
                "content": "你是 UNO 教学助手。根据给定的规则判断，用两句中文向初学者解释原因，不扩展其他规则。",
            },
            {
                "role": "user",
                "content": "桌顶是红色5，手牌是红色3和蓝色5。规则引擎已确认两张都能出。请解释原因。",
            },
        ],
        "thinking": {"type": "disabled"},
        "max_tokens": 500,
        "stream": False,
    }
    request = urllib.request.Request(
        "https://api.deepseek.com/chat/completions",
        data=json.dumps(payload, ensure_ascii=False).encode("utf-8"),
        headers={
            "Content-Type": "application/json",
            "Authorization": "Bearer " + key,
        },
        method="POST",
    )
    print("正在请求 DeepSeek，请稍候……")
    try:
        with urllib.request.urlopen(request, timeout=30) as response:
            result = json.load(response)
    except urllib.error.HTTPError as error:
        hints = {
            400: "请求参数被拒绝，请检查当前模型配置。",
            401: "认证失败，请检查是否复制了完整、有效的 API Key。",
            402: "账户余额不足，请在 DeepSeek 开放平台检查余额。",
            429: "请求受限，请稍后重试。",
            500: "DeepSeek 服务暂时出现错误，请稍后重试。",
            503: "DeepSeek 服务繁忙，请稍后重试。",
        }
        print(f"连接失败（HTTP {error.code}）：" + hints.get(error.code, "请检查平台状态和账户权限。"))
        return 1
    except (urllib.error.URLError, TimeoutError, socket.timeout):
        print("网络连接失败或超时。请检查电脑网络、代理设置，稍后再试。")
        return 1
    except (ValueError, UnicodeError):
        print("服务器返回了无法解析的内容。")
        return 1

    try:
        choice = result["choices"][0]
        answer = choice["message"]["content"]
        if not isinstance(answer, str) or not answer.strip():
            raise ValueError("Empty response")
    except (KeyError, IndexError, TypeError, ValueError):
        print("请求已返回，但没有有效回答；请稍后重试。")
        return 1
    print("\n连接成功。模型回答：\n" + answer.strip())
    if choice.get("finish_reason") == "length":
        print("提示：本次回答达到输出上限，内容可能不完整。")
    usage = result.get("usage", {})
    print(f"\nToken 用量：输入 {usage.get('prompt_tokens', '?')}，输出 {usage.get('completion_tokens', '?')}")
    print("本脚本仅测试 API，尚未接入 Unity；Key 未保存到文件。")
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except (KeyboardInterrupt, EOFError):
        print("\n已取消。")
        raise SystemExit(1)
