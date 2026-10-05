"""隔离集成测试的 Odoo 协议模拟器，不用于线上服务。"""
import hmac
import http.server
import json
import os
import threading
import time

STATE = {"seen": {}, "counts": {}, "failures": [], "delays": {}}
LOCK = threading.Lock()
KEY = os.environ["ODOO_INTEGRATION_KEY"]


class Handler(http.server.BaseHTTPRequestHandler):
    def log_message(self, *args):
        pass

    def respond(self, status, data):
        encoded = json.dumps(data).encode()
        self.send_response(status)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(encoded)))
        self.end_headers()
        try:
            self.wfile.write(encoded)
        except (BrokenPipeError, ConnectionResetError):
            pass

    def authorized(self):
        if not hmac.compare_digest(self.headers.get("Authorization", ""), "Bearer " + KEY):
            self.respond(401, {"code": 401})
            return False
        return True

    def do_GET(self):
        if self.authorized():
            with LOCK:
                self.respond(200, STATE)

    def do_POST(self):
        if not self.authorized():
            return
        if self.headers.get("Transfer-Encoding", "").lower() == "chunked":
            chunks = []
            while True:
                size = int(self.rfile.readline().split(b";", 1)[0], 16)
                if not size:
                    while self.rfile.readline() != b"\r\n":
                        pass
                    break
                chunks.append(self.rfile.read(size))
                self.rfile.read(2)
            raw = b"".join(chunks)
        else:
            raw = self.rfile.read(int(self.headers["Content-Length"]))
        body = json.loads(raw)
        if self.path == "/control":
            with LOCK:
                if "failureAdd" in body:
                    STATE["failures"].append(body["failureAdd"])
                if "failureRemove" in body:
                    STATE["failures"].remove(body["failureRemove"])
                STATE["seen"].update(body.get("seen", {}))
                STATE["delays"].update(body.get("delays", {}))
            self.respond(200, {"ok": True})
            return
        event = body["eventId"]
        with LOCK:
            STATE["counts"][event] = STATE["counts"].get(event, 0) + 1
            failure = event in STATE["failures"]
            duplicate = event in STATE["seen"]
            identifier = STATE["seen"].setdefault(event, 1000 + len(STATE["seen"])) if not failure else None
            delay = STATE["delays"].get(event, 0)
        time.sleep(delay)
        if failure:
            self.respond(503, {"code": 503})
        else:
            self.respond(200, {"code": 0, "data": {"model": "crm.lead", "id": identifier, "duplicate": duplicate}})


http.server.ThreadingHTTPServer(("0.0.0.0", 8080), Handler).serve_forever()
