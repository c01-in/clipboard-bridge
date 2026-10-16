"""Isolated native Web Push probe. No Firebase account or relay server.

pip install cryptography
python probe.py init | install | register | collect | send [--text TEXT] | results
ADB is used only to install, provision a public key, and collect evidence.
The send command never invokes ADB and POSTs directly to Google.
Private state is DPAPI protected in the repository's ignored local directory.
"""
from __future__ import annotations
import argparse, base64, ctypes, json, os, pathlib, secrets, subprocess, time, urllib.error, urllib.request
from ctypes import wintypes
from cryptography.hazmat.primitives import hashes, serialization
from cryptography.hazmat.primitives.asymmetric import ec, utils
from cryptography.hazmat.primitives.ciphers.aead import AESGCM
from cryptography.hazmat.primitives.kdf.hkdf import HKDF

ROOT = pathlib.Path(__file__).resolve().parents[2]
DATA = ROOT / "local" / "webpush-probe"
PACKAGE = "com.colin.clipbridge.webpushprobe"
DEFAULT_ADB = pathlib.Path.home()/".codex/tools/clipboard-bridge-build/sdk/platform-tools/adb.exe"

def b64(data): return base64.urlsafe_b64encode(data).rstrip(b"=").decode("ascii")
def unb64(data): return base64.urlsafe_b64decode(data+"="*((-len(data))%4))
def compact(data): return json.dumps(data, separators=(",", ":"), ensure_ascii=False).encode("utf-8")
def public(key): return key.public_key().public_bytes(serialization.Encoding.X962,serialization.PublicFormat.UncompressedPoint)

class Blob(ctypes.Structure):
    _fields_=[("cbData",wintypes.DWORD),("pbData",ctypes.POINTER(ctypes.c_ubyte))]

def dpapi(data: bytes, decrypt=False):
    memory=ctypes.create_string_buffer(data); source=Blob(len(data),ctypes.cast(memory,ctypes.POINTER(ctypes.c_ubyte))); target=Blob()
    function=ctypes.windll.crypt32.CryptUnprotectData if decrypt else ctypes.windll.crypt32.CryptProtectData
    function.argtypes=[ctypes.POINTER(Blob),ctypes.c_void_p,ctypes.c_void_p,ctypes.c_void_p,ctypes.c_void_p,wintypes.DWORD,ctypes.POINTER(Blob)]
    function.restype=wintypes.BOOL
    if not function(ctypes.byref(source),None,None,None,None,1,ctypes.byref(target)):raise ctypes.WinError()
    try:return ctypes.string_at(target.pbData,target.cbData)
    finally:
        ctypes.windll.kernel32.LocalFree.argtypes=[ctypes.c_void_p]
        ctypes.windll.kernel32.LocalFree(target.pbData)

def load():return json.loads(dpapi((DATA/"state.dpapi").read_bytes(),True))
def save(state):
    DATA.mkdir(parents=True,exist_ok=True)
    (DATA/"state.dpapi").write_bytes(dpapi(compact(state)))
def key_from(state):return serialization.load_pem_private_key(state["privateKey"].encode(),None)

def init():
    if (DATA/"state.dpapi").exists():state=load()
    else:
        key=ec.generate_private_key(ec.SECP256R1())
        state={"privateKey":key.private_bytes(serialization.Encoding.PEM,serialization.PrivateFormat.PKCS8,serialization.NoEncryption()).decode(),"publicKey":b64(public(key))}
        save(state)
    return state

def seal(payload,subscription):
    receiver=unb64(subscription["keys"]["p256dh"]); auth=unb64(subscription["keys"]["auth"])
    remote=ec.EllipticCurvePublicKey.from_encoded_point(ec.SECP256R1(),receiver)
    sender=ec.generate_private_key(ec.SECP256R1()); sender_public=public(sender)
    shared=sender.exchange(ec.ECDH(),remote)
    ikm=HKDF(algorithm=hashes.SHA256(),length=32,salt=auth,info=b"WebPush: info\x00"+receiver+sender_public).derive(shared)
    salt=secrets.token_bytes(16)
    cek=HKDF(algorithm=hashes.SHA256(),length=16,salt=salt,info=b"Content-Encoding: aes128gcm\x00").derive(ikm)
    nonce=HKDF(algorithm=hashes.SHA256(),length=12,salt=salt,info=b"Content-Encoding: nonce\x00").derive(ikm)
    if len(payload)>3000:raise ValueError("Probe payload too large for single-record Web Push")
    body=AESGCM(cek).encrypt(nonce,payload+b"\x02",None)
    return salt+(4096).to_bytes(4,"big")+bytes([65])+sender_public+body

def vapid(state):
    header=b64(compact({"typ":"JWT","alg":"ES256"}))
    claims=b64(compact({"aud":"https://fcm.googleapis.com","exp":int(time.time())+3600,"sub":"mailto:webpush-probe@example.invalid"}))
    signing=(header+"."+claims).encode()
    der=key_from(state).sign(signing,ec.ECDSA(hashes.SHA256()))
    r,s=utils.decode_dss_signature(der)
    jwt=signing.decode()+"."+b64(r.to_bytes(32,"big")+s.to_bytes(32,"big"))
    return "vapid t="+jwt+", k="+state["publicKey"]

def record(event):
    DATA.mkdir(parents=True,exist_ok=True)
    with (DATA/"sends.jsonl").open("a",encoding="utf-8") as stream:stream.write(json.dumps(event,ensure_ascii=False)+"\n")

def send(text):
    state=load(); subscription=state["subscription"]
    endpoint=subscription["endpoint"]
    from urllib.parse import urlparse
    parsed=urlparse(endpoint)
    if parsed.scheme!="https" or parsed.netloc!="fcm.googleapis.com" or not parsed.path.startswith("/fcm/send/") or parsed.fragment:raise ValueError("Not a direct Google Web Push endpoint")
    payload={"id":secrets.token_hex(16),"sentAt":int(time.time()*1000),"text":text}
    headers={"Authorization":vapid(state),"TTL":"120","Urgency":"high","Content-Encoding":"aes128gcm","Content-Type":"application/octet-stream"}
    request=urllib.request.Request(endpoint,data=seal(compact(payload),subscription),headers=headers,method="POST")
    # No redirects, proxy relay, or ADB. The destination is always fcm.googleapis.com.
    class NoRedirect(urllib.request.HTTPRedirectHandler):
        def redirect_request(self,*args):return None
    opener=urllib.request.build_opener(urllib.request.ProxyHandler({}),NoRedirect)
    try:
        with opener.open(request,timeout=30) as response:
            event={**payload,"googleStatus":response.status,"responseBytes":len(response.read()),"completedAt":int(time.time()*1000)}
    except urllib.error.HTTPError as error:
        # Do not print response bodies; they might include subscription capability tokens.
        event={**payload,"googleStatus":error.code,"responseBytes":len(error.read()),"completedAt":int(time.time()*1000)}
    record(event); print(json.dumps(event,ensure_ascii=False))
    return 0 if event["googleStatus"] in (200,201,202) else 1

def adb_run(args,command):
    prefix=[args.adb]
    if args.serial:prefix.extend(["-s",args.serial])
    return subprocess.run(prefix+command,check=True,stdout=subprocess.PIPE,stderr=subprocess.PIPE).stdout

def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument("command",choices=["init","install","register","collect","send","results"])
    parser.add_argument("--adb",default=str(DEFAULT_ADB));parser.add_argument("--serial")
    parser.add_argument("--text",default="Windows direct Web Push test")
    args=parser.parse_args()
    if args.command=="init":print(json.dumps({"package":PACKAGE,"publicKey":init()["publicKey"],"privateState":"local/webpush-probe/state.dpapi"}))
    elif args.command=="install":
        apk=pathlib.Path(__file__).parent/"app/build/outputs/apk/debug/app-debug.apk"
        print(adb_run(args,["install","-r",str(apk)]).decode())
    elif args.command=="register":
        state=init()
        output=adb_run(args,["shell","am","start","-n",PACKAGE+"/.MainActivity","--es","vapid",state["publicKey"]])
        print("Registration Activity opened with local VAPID public key; awaiting a real registration callback.")
    elif args.command=="collect":
        state=load(); raw=adb_run(args,["exec-out","run-as",PACKAGE,"cat","files/subscription.json"])
        state["subscription"]=json.loads(raw);save(state)
        print("Native subscription collected into DPAPI-protected local state; endpoint and keys withheld.")
    elif args.command=="send":raise SystemExit(send(args.text))
    elif args.command=="results":
        raw=adb_run(args,["exec-out","run-as",PACKAGE,"cat","files/events.jsonl"])
        (DATA/"events.jsonl").write_bytes(raw);print(raw.decode("utf-8"))

if __name__=="__main__":main()
