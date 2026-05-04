#!/usr/bin/env bash
set -euo pipefail

APP_DIR="/opt/ozon-image-server"
IMAGE_DIR="/var/www/ozon-images"
PORT="${PORT:-8088}"
INTERNAL_PORT="${INTERNAL_PORT:-18088}"
UPLOAD_TOKEN="${UPLOAD_TOKEN:-change-this-token}"

apt-get update
apt-get install -y python3 python3-venv nginx

mkdir -p "$APP_DIR" "$IMAGE_DIR"
chown -R www-data:www-data "$IMAGE_DIR"

cat > "$APP_DIR/server.py" <<'PY'
import json
import os
import re
import secrets
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.parse import quote
import cgi

IMAGE_DIR = os.environ.get("IMAGE_DIR", "/var/www/ozon-images")
PUBLIC_BASE_URL = os.environ.get("PUBLIC_BASE_URL", "http://47.76.248.181:8088/images/")
UPLOAD_TOKEN = os.environ.get("UPLOAD_TOKEN", "")
MAX_BYTES = int(os.environ.get("MAX_BYTES", str(10 * 1024 * 1024)))

def safe_ext(filename):
    ext = os.path.splitext(filename or "")[1].lower()
    if ext in [".jpg", ".jpeg", ".png", ".webp"]:
        return ext
    return ".jpg"

class Handler(BaseHTTPRequestHandler):
    def do_GET(self):
        if self.path == "/health":
            self.send_response(200)
            self.end_headers()
            self.wfile.write(b"ok")
            return
        self.send_error(404)

    def do_POST(self):
        if self.path != "/upload":
            self.send_error(404)
            return
        if UPLOAD_TOKEN and self.headers.get("X-Upload-Token") != UPLOAD_TOKEN:
            self.send_error(401)
            return
        length = int(self.headers.get("Content-Length", "0"))
        if length <= 0 or length > MAX_BYTES:
            self.send_error(413)
            return
        form = cgi.FieldStorage(fp=self.rfile, headers=self.headers, environ={
            "REQUEST_METHOD": "POST",
            "CONTENT_TYPE": self.headers.get("Content-Type", ""),
        })
        item = form["file"] if "file" in form else None
        if item is None or not item.filename:
            self.send_error(400)
            return
        data = item.file.read()
        if not data:
            self.send_error(400)
            return
        filename = secrets.token_hex(16) + safe_ext(item.filename)
        path = os.path.join(IMAGE_DIR, filename)
        with open(path, "wb") as f:
            f.write(data)
        url = PUBLIC_BASE_URL.rstrip("/") + "/" + quote(filename)
        body = json.dumps({"url": url, "file": filename}).encode("utf-8")
        self.send_response(200)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

if __name__ == "__main__":
    os.makedirs(IMAGE_DIR, exist_ok=True)
    server = ThreadingHTTPServer(("127.0.0.1", int(os.environ.get("INTERNAL_PORT", "18088"))), Handler)
    server.serve_forever()
PY

cat > /etc/systemd/system/ozon-image-server.service <<EOF
[Unit]
Description=OZON image upload server
After=network.target

[Service]
Type=simple
Environment=PORT=$PORT
Environment=INTERNAL_PORT=$INTERNAL_PORT
Environment=IMAGE_DIR=$IMAGE_DIR
Environment=PUBLIC_BASE_URL=http://47.76.248.181:$PORT/images/
Environment=UPLOAD_TOKEN=$UPLOAD_TOKEN
WorkingDirectory=$APP_DIR
ExecStart=/usr/bin/python3 $APP_DIR/server.py
Restart=always
RestartSec=3
User=www-data
Group=www-data

[Install]
WantedBy=multi-user.target
EOF

cat > /etc/nginx/sites-available/ozon-images.conf <<EOF
server {
    listen $PORT;
    server_name _;

    client_max_body_size 10m;

    location /images/ {
        alias $IMAGE_DIR/;
        add_header Cache-Control "public, max-age=31536000, immutable";
    }

    location /upload {
        proxy_pass http://127.0.0.1:$INTERNAL_PORT;
        proxy_set_header Host \$host;
        proxy_set_header X-Real-IP \$remote_addr;
        proxy_set_header X-Forwarded-For \$proxy_add_x_forwarded_for;
    }

    location /health {
        proxy_pass http://127.0.0.1:$INTERNAL_PORT;
    }
}
EOF

rm -f /etc/nginx/sites-enabled/ozon-images.conf
ln -s /etc/nginx/sites-available/ozon-images.conf /etc/nginx/sites-enabled/ozon-images.conf

systemctl daemon-reload
systemctl enable --now ozon-image-server
nginx -t
systemctl reload nginx

echo "OZON image server installed."
echo "Health: http://47.76.248.181:$PORT/health"
echo "Upload: http://47.76.248.181:$PORT/upload"
echo "Images: http://47.76.248.181:$PORT/images/"
echo "Token: $UPLOAD_TOKEN"
