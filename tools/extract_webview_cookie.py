# -*- coding: utf-8 -*-
"""从 WebView2 登录窗口的 Cookie 库里恢复会话"""
import base64
import json
import os
import shutil
import sqlite3
import sys

import win32crypt
from cryptography.hazmat.primitives.ciphers.aead import AESGCM

ROOT = os.path.join(os.path.expandvars(r"%LOCALAPPDATA%"), "NcmLite", "WebView2", "EBWebView")
PREFS = os.path.join(ROOT, "Local State")
COOKIES = os.path.join(ROOT, "Default", "Network", "Cookies")

prefs = json.load(open(PREFS, "r", encoding="utf-8"))
blob = base64.b64decode(prefs["os_crypt"]["encrypted_key"])
assert blob[:5] == b"DPAPI"
key = win32crypt.CryptUnprotectData(blob[5:], None, None, None, 0)[1]
print("AES key len:", len(key))

aes = AESGCM(key)


def dec(ev):
    if not ev or ev[:3] != b"v10":
        return None
    return aes.decrypt(ev[3:15], ev[15:], None)


tmp = os.path.join(os.environ["TEMP"], "wv_cookies.db")
shutil.copyfile(COOKIES, tmp)
con = sqlite3.connect(tmp)
cur = con.cursor()
cur.execute("SELECT host_key, name, encrypted_value, value FROM cookies")
jar = {}
for host, name, ev, plain in cur.fetchall():
    try:
        v = plain if plain else dec(ev)
        if v:
            jar[name] = v.decode("utf-8", "replace")
    except Exception:
        pass
con.close()

print("cookie 数:", len(jar))
for k in sorted(jar):
    if k.startswith("MUSIC"):
        print(f"  {k:12} len={len(jar[k])}")
print("有 MUSIC_U:", "MUSIC_U" in jar and bool(jar["MUSIC_U"]))

if jar.get("MUSIC_U"):
    out = os.path.join(os.environ["TEMP"], "wv_cookie.txt")
    open(out, "w", encoding="utf-8").write("; ".join(f"{k}={v}" for k, v in jar.items() if v))
    print("written:", out)
else:
    print("没有 MUSIC_U")
