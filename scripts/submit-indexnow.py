#!/usr/bin/env python3
"""Notify IndexNow of the published site's URLs, after live ownership checks.

Uses the public sitemap and a deliberately public ownership key. No search
account, password, OAuth credential, or third-party package is required.
"""

import argparse
import json
import re
import urllib.error
import urllib.request
from pathlib import Path
from urllib.parse import urlsplit
from xml.etree import ElementTree

ROOT = Path(__file__).resolve().parents[1]
ENDPOINT = 'https://api.indexnow.org/indexnow'
USER_AGENT = 'NimCap-Screenshot-IndexNow/1.0 (+https://github.com/nimcap/nimcap-screenshot)'


def fetch(url):
    request = urllib.request.Request(url, headers={'User-Agent': USER_AGENT})
    with urllib.request.urlopen(request, timeout=30) as response:
        return response.read()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--base-url', default='https://jietu.nimcap.com')
    parser.add_argument('--dry-run', action='store_true', help='Read sitemap and verify the public key without submitting')
    parser.add_argument('--report', type=Path, help='Optional JSON receipt file')
    args = parser.parse_args()
    base = args.base_url.rstrip('/')
    parts = urlsplit(base)
    if parts.scheme != 'https' or not parts.netloc or parts.query or parts.fragment or parts.username or parts.password or parts.port:
        parser.error('--base-url must be an HTTPS website URL without credentials, query, fragment, or custom port')
    key = (ROOT / 'website/indexnow-key.txt').read_text(encoding='utf-8').strip()
    if not re.fullmatch(r'[A-Za-z0-9-]{8,128}', key):
        parser.error('Invalid public IndexNow key')
    key_url = f'{base}/{key}.txt'
    if fetch(key_url).decode('utf-8').strip() != key:
        raise SystemExit('Live ownership file does not match; deploy the site before submitting.')
    sitemap = ElementTree.fromstring(fetch(base + '/sitemap.xml'))
    urls = list(dict.fromkeys(element.text for element in sitemap.findall('{http://www.sitemaps.org/schemas/sitemap/0.9}url/{http://www.sitemaps.org/schemas/sitemap/0.9}loc')))
    if not urls or len(urls) > 10000:
        raise SystemExit('IndexNow requires 1 to 10,000 URLs per request.')
    for url in urls:
        if not url or not url.startswith(base + '/') or urlsplit(url).hostname != parts.hostname:
            raise SystemExit(f'Sitemap URL is outside the ownership prefix: {url}')
    payload = {'host': parts.hostname, 'key': key, 'keyLocation': key_url, 'urlList': urls}
    if args.dry_run:
        print(f'PASS: live ownership file verified; {len(urls)} published sitemap URLs are eligible. No notification sent.')
        return
    request = urllib.request.Request(
        ENDPOINT,
        data=json.dumps(payload).encode('utf-8'),
        headers={'Content-Type': 'application/json; charset=utf-8', 'User-Agent': USER_AGENT},
        method='POST',
    )
    try:
        with urllib.request.urlopen(request, timeout=30) as response:
            status = response.status
            body = response.read().decode('utf-8', errors='replace')
    except urllib.error.HTTPError as error:
        raise SystemExit(f'IndexNow rejected the submission: HTTP {error.code}: {error.read().decode("utf-8", errors="replace")}') from error
    if status not in (200, 202):
        raise SystemExit(f'Unexpected IndexNow response: HTTP {status}: {body}')
    result = {
        'endpoint': ENDPOINT, 'http_status': status,
        'meaning': 'received' if status == 200 else 'received; key validation pending',
        'base_url': base, 'url_count': len(urls), 'urls': urls,
        'note': 'Receipt is not proof of crawling, indexing, ranking, or recommendation. Google does not use IndexNow.',
    }
    if args.report:
        args.report.parent.mkdir(parents=True, exist_ok=True)
        args.report.write_text(json.dumps(result, indent=2) + '\n', encoding='utf-8')
    print(f'IndexNow HTTP {status}: {len(urls)} URLs received' + ('; key validation pending.' if status == 202 else '.'))
    print('Google Search Console submission remains separate. Receipt does not guarantee indexing.')


if __name__ == '__main__':
    main()
