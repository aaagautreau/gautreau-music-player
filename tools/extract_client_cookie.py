# -*- coding: utf-8 -*-
"""从网易云客户端本地 Cookie 库解密出会话凭证（仅用于本机自己的账号）"""
import base64
import json
import os
import shutil
import sqlite3
import sys

import win32crypt
from cryptography.hazmat.primitives.ciphers.aead import AESGCM

BASE = os.path.expandvars(r"%LOCALAPPDATA%\Netease\CloudMusic")
PROFILE = os.path.join(BASE, "webapp91x64")
PREFS = os.path.join(PROFILE, "LocalPrefs.json")
COOKIES = os.path.join(PROFILE, "Cookies")

prefs = json.load(open(PREFS, "r", encoding="utf-8"))
enc_key_b64 = prefs["os_crypt"]["encrypted_key"]
enc_key = base64.b64decode(enc_key_b64)
print("key blob len:", len(enc_key), "prefix:", enc_key[:5])
assert enc_key[:5] == b"DPAPI", "不是 DPAPI 保护的密钥"

key = win32crypt.CryptUnprotectData(enc_key[5:], None, None, None, 0)[1]
print("AES key len:", len(key))

aes = AESGCM(key)


def decrypt(ev: bytes):
    if not ev or ev[:3] != b"v10":
        return None
    nonce, ct = ev[3:15], ev[15:]
    return aes.decrypt(nonce, ct, None)


tmp = os.path.join(os.environ["TEMP"], "ncm_cookies_read.db")
shutil.copyfile(COOKIES, tmp)
con = sqlite3.connect(tmp)
cur = con.cursor()
cur.execute("SELECT host_key, name, encrypted_value, expires_utc FROM cookies")

jar = {}
failed = 0
for host, name, ev, exp in cur.fetchall():
    try:
        v = decrypt(ev)
        if v:
            jar[name] = v.decode("utf-8", "replace")
    except Exception:
        failed += 1
con.close()

print(f"解出 {len(jar)} 个 cookie，失败 {failed} 个")
for k, v in jar.items():
    if k in ("MUSIC_U", "MUSIC_R_U", "MUSIC_A_T", "MUSIC_R_T"):
        print(f"  {k:12} len={len(v)}")
    else:
        print(f"  {k:12} = {v[:40]}")

music_u = jar.get("MUSIC_U")
if not music_u:
    print("!! 没有 MUSIC_U")
    sys.exit(1)

cookie_str = "; ".join(f"{k}={v}" for k, v in jar.items() if v)
out = os.path.join(os.environ["TEMP"], "ncm_cookie_extracted.txt")
open(out, "w", encoding="utf-8").write(cookie_str)
print("已写入:", out, "总长度:", len(cookie_str))
