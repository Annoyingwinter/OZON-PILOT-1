#!/usr/bin/env bash
set -euo pipefail

IMAGE_DIR="${IMAGE_DIR:-/var/www/ozon-images}"
PUBLIC_BASE_URL="${PUBLIC_BASE_URL:-http://47.76.248.181/ozon-images/}"
UPLOAD_TOKEN="${UPLOAD_TOKEN:-change-this-token}"
INTERNAL_PORT="${INTERNAL_PORT:-18088}"
NGINX_SITE="${NGINX_SITE:-/etc/nginx/sites-available/ozon-pilot}"

mkdir -p "$IMAGE_DIR"
chown -R www-data:www-data "$IMAGE_DIR"

python3 - "$NGINX_SITE" <<'PY'
import sys
from pathlib import Path

path = Path(sys.argv[1])
text = path.read_text()
block = """

    location /ozon-images/ {
        alias /var/www/ozon-images/;
        add_header Cache-Control "public, max-age=31536000, immutable";
    }

    location /ozon-upload {
        client_max_body_size 10m;
        proxy_pass http://127.0.0.1:18088/upload;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
    }

    location /ozon-image-health {
        proxy_pass http://127.0.0.1:18088/health;
        access_log off;
    }
"""
if "/ozon-upload" not in text:
    text = text.replace("\n}", block + "\n}")
path.write_text(text)
PY

mkdir -p /etc/systemd/system/ozon-image-server.service.d
cat > /etc/systemd/system/ozon-image-server.service.d/override.conf <<EOF
[Service]
Environment=PUBLIC_BASE_URL=$PUBLIC_BASE_URL
Environment=UPLOAD_TOKEN=$UPLOAD_TOKEN
Environment=INTERNAL_PORT=$INTERNAL_PORT
Environment=IMAGE_DIR=$IMAGE_DIR
EOF

systemctl daemon-reload
systemctl restart ozon-image-server
nginx -t
systemctl reload nginx

echo "Port-80 image paths configured."
echo "Health: http://47.76.248.181/ozon-image-health"
echo "Upload: http://47.76.248.181/ozon-upload"
echo "Images: $PUBLIC_BASE_URL"
